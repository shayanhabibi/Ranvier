# Profile of the keyed list remove and swap path — 2026-10-15

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; timings and profiles ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release), Vite 8.3.2. Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Production code: `85e0f8c` (`poc/fable-ranvier-dom`; identical to `7855dbe` in `src/` and `experiments/`).
- Harness: local branch `measure/poc-rerun-2026-10-15`, `26f633e` plus `fce8caa` (never pushed). `fce8caa` adds:
  - `keyed-rows.mjs --profile --save-cpu=<dir>`: writes each raw Chromium CPU profile; `--protocol=sample` profiles the timing protocol; `--dist=<dir>` serves a bundle built from another commit.
  - `keyed-profile-buckets.mjs`: maps every frame through the bundle's source map to its source file and sorts the profile's samples into buckets.
  - `keyed-gc-check.mjs`: times one operation under several protocols in one process.
- Builds of other commits: a throwaway worktree, since removed. Each build is the commit's `src/` and `experiments/` with the harness files of `26f633e` (`keyed-page.mjs`, `keyed-solid.jsx`, `vite.config.mjs`, `page.mjs`, `tests/DomTests.fsproj`, `tests/KeyedRowsPerf.fs`). The exception is `4353219`, built with its own harness and timed through its production mode `rows-C3`.

Raw data in [`framework-performance/investigation/list-remove-profile/`](framework-performance/investigation/list-remove-profile/):

- `profile-85e0f8c-loop.json`, `profile-85e0f8c-timing-protocol.json`, `profile-experiment-stable-tuples-loop.json`: buckets, the top 40 functions by self time and the top 50 by total time, per operation and framework.
- `commits-run1.json`, `commits-run2.json`: per-commit timings.
- `protocol-check.json`: the protocol comparison.
- `experiment-*.patch`: the two experiments.

## Method

The profiles use unminified bundles with a 50 µs sampling interval. Only samples whose stack contains `measuredRun` (the timed step) count, so list setup and disposal are excluded. A stackless garbage-collector sample counts with the sample before it. Each sample goes to one bucket:

- **DOM operations**: the leaf is a native function (`remove`, `insertBefore`).
- **GC**: garbage-collector samples.
- **Row teardown**: the stack contains owner or effect disposal (`Owner.Dispose`, `Owner.DisposeScope`, `Effect.Dispose`), or `RegionSite.ReleaseRow` or `Row.Remove`.
- **Core projection pass**: the nearest project frame is in `Projections.fs`.
- **Core graph**: the nearest project frame is in another core file: `Core.fs` (signal writes, tracking), `Platform.fs` (`KeySet`, `KeyMap`) or `ValueReaders.fs`. Most of this bucket is called from the projection pass.
- **View list region**: the nearest project frame is in `experiments/Ranvier.View.Foundation` (`Mounting.fs`, `Template.fs`, `TemplateDom.fs`) or the fixture.

Fable-library frames (`Array.map`, `item`, `Enumerator`) go to the bucket of their nearest project frame.

Profiles were taken under two protocols:

- **Loop**: 2,000 iterations (200 for `remove10k`, 1,000 for `clear1k`), each preparing a fresh list and running the operation once, inside one `evaluate`.
- **Timing protocol** (`--protocol=sample`): a CDP `HeapProfiler.collectGarbage`, then one `keyedPerf.sample` per `evaluate`, as `keyed-rows.mjs` times. 1,000 iterations (600 for `clear1k`, 200 for `remove10k`).

The profiler inflates absolute times under the timing protocol (remove1k is 1.26 ms profiled against 0.70–0.83 ms unprofiled), so only the shares are used from that protocol.

### The timing protocol measures the first operation after a forced GC

`protocol-check.json`, minified `85e0f8c` bundle, one process, median of 40, ms:

| Operation | Mode | After CDP GC (timing protocol) | Next sample, no GC | Loop | One step per `evaluate`, no GC | In-page `gc()`, then the operation | In-page `gc()`, one untimed operation, then the operation |
|---|---|---|---|---|---|---|---|
| remove1k | Ranvier | 0.735 | 1.215 | 0.285 | 0.255 | 0.395 | 0.330 |
| remove1k | Solid 2 | 0.095 | 0.060 | 0.035 | 0.030 | 0.060 | 0.055 |
| swap1k | Ranvier | 1.045 | 1.460 | 0.365 | 0.305 | 0.530 | 0.410 |
| swap1k | Solid 2 | 0.085 | 0.070 | 0.055 | 0.050 | 0.060 | 0.045 |
| clear1k | Ranvier | 1.385 | 0.965 | 0.655 | 0.550 | 0.955 | 0.620 |
| clear1k | Solid 2 | 0.595 | 0.505 | 0.275 | 0.270 | 0.525 | 0.485 |
| remove10k | Ranvier | 8.800 | 7.345 | 3.030 | 2.910 | 4.275 | 2.905 |
| remove10k | Solid 2 | 0.840 | 0.270 | 0.225 | 0.315 | 0.700 | 0.340 |

- Both frameworks run their reported operations 2–3× slower than in steady state. The difference comes from the CDP-forced GC before the sample, not from the `evaluate` boundary: one step per `evaluate` without the GC is as fast as the loop.
- Idle time is also ruled out. A 5 ms timer between iterations leaves the loop time unchanged (remove1k 0.265 ms).
- The no-GC sample that follows a timed one is slower again for remove1k and swap1k. With `--js-flags=--min-semi-space-size=32` it drops to 0.485 ms, so that sample includes a young-generation collection triggered by the previous list's garbage.
- `--no-flush-bytecode`, `--no-flush-baseline-code`, `--no-memory-reducer` and `--no-lazy-feedback-allocation` each leave the post-GC remove1k at 0.68–0.75 ms. The cause of the post-GC penalty was not identified further.
- Ranvier ÷ Solid 2 for remove1k is about 7.7× under the timing protocol and about 8–9× in the loop.

The bucket shares below are from the loop protocol. Under the timing protocol, the shares of remove1k and swap1k stay within a few points (next section), and the same functions lead.

## Q1: where the time goes

### Bucket split, `85e0f8c`, Ranvier

Loop protocol, sampled ms per operation (share):

| Bucket | remove1k (0.311) | remove10k (4.021) | swap1k (0.403) | clear1k (0.714) |
|---|---|---|---|---|
| Core projection pass | 0.129 (41.5%) | 1.462 (36.4%) | 0.149 (36.9%) | 0.072 (10.1%) |
| Core graph | 0.085 (27.2%) | 0.996 (24.8%) | 0.094 (23.4%) | 0.047 (6.6%) |
| View list region | 0.060 (19.4%) | 0.912 (22.7%) | 0.129 (32.1%) | 0.028 (3.9%) |
| Row teardown | 0.004 (1.1%) | 0.025 (0.6%) | 0.001 (0.2%) | 0.149 (20.9%) |
| DOM operations | 0.007 (2.2%) | 0.007 (0.2%) | 0.005 (1.2%) | 0.340 (47.6%) |
| GC | 0.027 (8.6%) | 0.618 (15.4%) | 0.025 (6.3%) | 0.078 (10.9%) |

Timing protocol, share of the profiled operation:

| Bucket | remove1k | remove10k | swap1k | clear1k |
|---|---|---|---|---|
| Core projection pass | 36.7% | 29.1% | 43.0% | 15.0% |
| Core graph | 33.2% | 27.4% | 18.1% | 14.0% |
| View list region | 27.1% | 26.5% | 37.8% | 6.1% |
| Row teardown | 2.6% | 0.7% | 0.1% | 45.8% |
| DOM operations | 0.2% | 0.1% | 0.5% | 17.3% |
| GC | 0.2% | 16.2% | 0.3% | 1.7% |

The same operations in Solid 2, loop protocol: remove1k 0.030 ms, swap1k 0.061 ms, remove10k 0.495 ms, clear1k 0.323 ms.

### Why remove and swap are O(n): the trimmed pass trims nothing on the view path

`Template.each` (`Template.fs`, `Items`) maps the row array to a new array of `(box key, box item)` tuples on every read. Under Fable the projection's comparer is reference identity (`JsIdentityPolicy`, `Types.fs`), so `KeyedProjection.Trim` (`Projections.fs`) matches no position in its common-prefix and common-suffix scans. It then treats the whole list as changed. For remove1k it visits all 999 items, and each visit makes these calls:

- `changed.Add`, `changedKeys.Add`, `previousChanged.Contains` and `HasRow`;
- `Stage`, which does an `Entries.Find` and a `writes.Add`.

Then 1,000 `previousChanged.Add` and `Remove` calls and 999 `changed.Remove` calls follow, and `CommitWrites` writes all 999 item signals. The `KeySet` calls are the `Platform.fs` time in the core graph bucket.

The loop-protocol total time inside `KeyedProjection.Trim` is 0.122 ms of remove1k's 0.311 ms (39%), 1.346 ms of remove10k's 4.021 ms (33%) and 0.175 ms of swap1k's 0.403 ms (43%). In addition:

- `SettleAdditions`, including `CommitWrites`, takes 0.025 ms of remove1k (8%) and 0.386 ms of remove10k (10%).
- The view's `read` (the tuple map in `Template.each`) takes 0.011 ms of remove1k (3.5%) and 0.171 ms of remove10k (4.2%).

### Top functions by self time, loop protocol, `85e0f8c`, Ranvier

remove1k (0.311 ms):

| Function | Bucket | Self ms | % |
|---|---|---|---|
| `RowsOf.Enumerate` (inlined `KeyedProjection.Trim` loop) | core projection pass | 0.0402 | 12.9 |
| `KeySet.Remove` | core graph | 0.0305 | 9.8 |
| `KeySet.Add` | core graph | 0.0228 | 7.3 |
| `Enumerator` (fable-library, under the pass) | core projection pass | 0.0203 | 6.5 |
| `Row.Validate` | view list region | 0.0167 | 5.4 |
| `visit` (in `Trim`) | core projection pass | 0.0129 | 4.1 |
| `Projection.Get` (from the view's `keys \|> Array.map projection.Get`) | core projection pass | 0.0112 | 3.6 |
| `item` (fable-library, under `Row.Layout`) | view list region | 0.0084 | 2.7 |
| `Row.Layout` | view list region | 0.0071 | 2.3 |
| `remove` (native) | DOM operations | 0.0067 | 2.2 |
| `RowsOf.Stage` | core projection pass | 0.0065 | 2.1 |
| `Array.filter` (under the region) | view list region | 0.0061 | 2.0 |

remove10k (4.021 ms):

| Function | Bucket | Self ms | % |
|---|---|---|---|
| `RowsOf.Enumerate` | core projection pass | 0.438 | 10.9 |
| `Row.Validate` | view list region | 0.373 | 9.3 |
| `KeySet.Remove` | core graph | 0.347 | 8.6 |
| GC under `Row.Validate` | GC | 0.279 | 6.9 |
| `KeySet.Add` | core graph | 0.255 | 6.3 |
| `Enumerator` | core projection pass | 0.241 | 6.0 |
| `visit` | core projection pass | 0.161 | 4.0 |
| `Row.Removed` getter (in `Row.Validate` and `Row.Layout`) | view list region | 0.127 | 3.2 |
| `ItemRow.Item` | core projection pass | 0.118 | 2.9 |
| `Projection.Get` | core projection pass | 0.114 | 2.8 |
| `Template.each` tuple map | view list region | 0.093 | 2.3 |

swap1k (0.403 ms):

| Function | Bucket | Self ms | % |
|---|---|---|---|
| `item` (fable-library, under `Row.Layout` and `increasingRun`) | view list region | 0.0719 | 17.8 |
| `KeySet.Remove` | core graph | 0.0309 | 7.7 |
| `KeySet.Add` | core graph | 0.0297 | 7.4 |
| `Enumerator` | core projection pass | 0.0237 | 5.9 |
| `Row.Layout` | view list region | 0.0216 | 5.4 |
| `KeyedProjection.Trim` | core projection pass | 0.0200 | 5.0 |
| `RowsOf.Enumerate` | core projection pass | 0.0192 | 4.8 |
| `visit` | core projection pass | 0.0149 | 3.7 |
| `Row.Validate` | view list region | 0.0145 | 3.6 |
| `Projection.Get` | core projection pass | 0.0112 | 2.8 |
| `setItem` (fable-library) | view list region | 0.0096 | 2.4 |
| `increasingRun` | view list region | 0.0084 | 2.1 |

`Row.Layout` takes 0.089 ms total (22%) of swap1k. Its prefix and suffix leave a 998-row middle, so it builds the 998-entry `sources` array and runs `increasingRun` over it (0.044 ms total, 11%) to move two rows.

clear1k (0.714 ms):

| Function | Bucket | Self ms | % |
|---|---|---|---|
| `remove` (native), from `Row.Remove` per row | DOM operations | 0.3404 | 47.6 |
| GC under `remove` | GC | 0.0540 | 7.6 |
| `clear` (fable-library, under owner disposal) | row teardown | 0.0294 | 4.1 |
| `Projection.ApplyDiff` | core projection pass | 0.0258 | 3.6 |
| `Owner.DisposeScope` | row teardown | 0.0236 | 3.3 |
| `Row.Remove` | row teardown | 0.0199 | 2.8 |
| `RowsOf.Enumerate` | core projection pass | 0.0179 | 2.5 |
| `Row.Validate` | view list region | 0.0167 | 2.3 |

In clear1k, `Owner.Dispose` takes 0.559 ms total (78%), of which `RegionSite.ReleaseRow` → `Row.Remove` takes 0.423 ms (59%). Under the timing protocol, row teardown is 45.8% and DOM operations 17.3%.

Under the timing protocol, the leaders of remove1k move to `SourceList.EndRun` (0.260 ms self, 20.7%), `RowsOf.Stage` (0.249 ms, 19.8%) and `item` under the region (0.118 ms, 9.4%). `KeyedProjection.Trim` still takes 28.9% total and `Row.Layout` 13.4%.

### Experiments (patches in the measure worktree only)

Both experiments were built from `85e0f8c` and timed with the harness of `run 2` (next section): six interleaved processes, 21 shuffled rounds, a forced GC before each sample.

- **E1, stable tuples** (`experiment-stable-tuples.patch`): `Template.each` keeps one tuple per object item in a `WeakMap` and returns the same tuple while the key is unchanged, so `Trim` matches unchanged positions. The row text matched Solid 2 for every operation.
- **E2, no trim** (`experiment-no-trim.patch`): `KeyedProjection.Enumerate` always takes the full visit.

| Operation | `85e0f8c` | E1 stable tuples | E2 no trim | Solid 2 |
|---|---|---|---|---|
| remove1k | 0.765 | 0.425 | 0.570 | 0.11–0.19 |
| swap1k | 0.890 | 0.495 | 0.620 | 0.15–0.28 |
| remove10k | 8.540 | 5.395 | 6.850 | 0.61–0.65 |
| clear1k | 1.300 | 1.305 | 1.160 | 0.61–0.77 |

Median ms per operation; the Solid 2 column is the range of its medians across the three runs.

Under E1 (loop-protocol profile, `profile-experiment-stable-tuples-loop.json`), remove1k drops from 0.311 to 0.137 ms and swap1k from 0.403 to 0.174 ms. remove10k drops from 4.02 to 2.27 ms. The view list region then becomes the largest bucket: 43% of remove1k, 65% of swap1k and 62% of remove10k. Its time goes to:

- `keys |> Array.map projection.Get` in `ListRegion.reconcile`: 0.054 ms total in remove1k (39%) and 1.10 ms in remove10k (49%);
- the `WeakMap` lookups in the patched `Template.each`: 0.018 ms in remove1k (13%) and 0.64 ms in remove10k (28%);
- `Row.Validate`: 0.014 ms (10.5%) and 0.63 ms (28%);
- `Row.Layout`: 0.011 ms in remove1k, and 0.072 ms in swap1k (41%), of which `increasingRun` is 0.033 ms.

## Q2: the remove1k rise is real and comes from #55

Interleaved processes on the same day. One Chromium process per build per round, with the builds' order rotated each round. Each process ran 3 warmups and 21 shuffled rounds of Ranvier and Solid 2 in the same page, with a forced GC before each sample. Medians over all samples in ms, with the per-process medians in brackets.

Run 1 (`commits-run1.json`), six processes per build:

| Build | remove1k | swap1k | remove10k | clear1k |
|---|---|---|---|---|
| `4353219` (old harness, `rows-C3`) | 0.655 [0.72 0.63 0.64 0.66 0.60 0.66] | 0.685 | 7.065 | 1.355 |
| `ab68bde` (the view at `4353219` without the benchmark hooks) | 0.650 [0.67 0.67 0.70 0.64 0.64 0.61] | 0.675 | 7.130 | 1.385 |
| `a7b20d5` (create-first stop and rollback off the default pass) | 0.680 [0.69 0.67 0.67 0.65 0.66 0.83] | 0.710 | 6.920 | 1.410 |
| `7908949` (the view commits up to the merge) | 0.710 [0.71 0.71 0.66 0.72 0.69 0.76] | 0.695 | 6.915 | 1.355 |
| `85e0f8c` (merge of master: #53 `e2e0f95` and #55 `bef1521`) | 0.830 [0.84 0.79 0.84 0.88 0.88 0.83] | 0.940 | 8.505 | 1.375 |

Solid 2 in the same processes: remove1k 0.120–0.170, swap1k 0.120–0.455, remove10k 0.635–0.655, clear1k 0.610–0.640.

Run 2 (`commits-run2.json`), six processes per build, splitting the merge:

| Build | remove1k | swap1k | remove10k | clear1k |
|---|---|---|---|---|
| `7908949` | 0.645 [0.59 0.75 0.61 0.60 0.61 0.67] | 0.680 | 6.910 | 1.240 |
| `7908949` + `e2e0f95` (#53 only) | 0.610 [0.67 0.78 0.60 0.58 0.60 0.58] | 0.640 | 6.880 | 1.185 |
| `85e0f8c` (adds #55) | 0.765 [0.79 0.78 0.79 0.75 0.75 0.76] | 0.890 | 8.540 | 1.300 |
| `85e0f8c` + E2 (the trim path disabled) | 0.570 [0.59 0.59 0.63 0.54 0.56 0.55] | 0.620 | 6.850 | 1.160 |

The rise is real. In run 1 every `85e0f8c` process (0.79–0.88 ms) is slower than every `4353219` process (0.60–0.72 ms). The step comes with #55 (`bef1521`):

- `7908949` + #53 is at 0.610 ms and `85e0f8c` at 0.765 ms; all six `85e0f8c` processes are above five of the six `7908949` + #53 processes.
- swap1k rises 0.640 → 0.890 ms and remove10k 6.88 → 8.54 ms at the same step.

The view commits between `ab68bde` and `7908949` (including `a7b20d5`) move remove1k by 0.03–0.06 ms, within the per-process spread.

#55 is one squashed commit, so the bisect inside it is by experiment: disabling the trim path alone (E2) brings `85e0f8c` to 0.570 ms, below `7908949`. The rise is therefore the trimmed pass running on input it cannot trim. When nothing matches, its per-item bookkeeping (`changed`, `changedKeys`, `previousChanged`, `changedAt`) costs more than the full visit it replaces. The rest of #55 makes remove1k faster: E2 is 0.04–0.07 ms below `7908949`.

## Q3: what Solid 2 does differently

1. **Solid compares items by identity before mapping anything** (`@solidjs/signals/dist/prod/map.js`, `updateKeyedMap`, from line 186). The common prefix and suffix are skipped by `===` on the user's items (lines 283–289). Only the changed window is entered into a `Map` (lines 322–329), and rows outside the window are copied by index (lines 362+). For remove1k the window is one item. This matches the largest hot spot: Ranvier's trim compares fresh tuples and visits all n items (`KeyedProjection.Trim`, 39% of remove1k).
2. **Solid writes no per-row signal for retained rows in keyed mode.** The row and index signals (`this.Mt`, `this.gt`) exist only for the non-keyed and index forms (lines 201–215 and 284). Ranvier stages and writes every survivor's item signal (`RowsOf.Stage`, `CommitWrites`), which is 8% of remove1k in the loop and the second-largest self time (`RowsOf.Stage`, 19.8%) under the timing protocol.
3. **Solid reconciles DOM nodes with one prefix and suffix pass and a two-node swap branch** (`@solidjs/web/dist/web.js`, `reconcileArrays`, lines 73–182). The swap-ends branch (lines 122–137) moves the two rows of swap1k with two `insertBefore` calls and builds no map. Before reconciling, `trySmallMove` (`map.js`, line 46) handles small moves in the mapper. Ranvier's `Row.Layout` builds a 998-entry `sources` array and runs `increasingRun` over the middle (22% of swap1k; 41% under E1).
4. **Solid validates nothing in a full walk.** `reconcileArrays` checks liveness (`isLive`: `parentNode` and the slot tag) only for the nodes it compares, and returns at the first mismatch from each end. Ranvier's `Row.Validate` walks every row's DOM boundary on every pass: 5–9% of remove in the loop, 10–28% under E1.
5. **Solid builds no per-pass key array or row array for the DOM step.** The mapped array feeds `reconcileArrays` directly. Ranvier rebuilds `keys |> Array.map projection.Get`, the `previous` list and `laidOut` in `ListRegion.reconcile` and `Row.Layout`: about 14% of remove1k at `85e0f8c`, and 39% under E1.
6. **Solid clears a list it owns with one DOM operation.** `cleanChildren` (`web.js`, line 1796) assigns `parent.textContent = ""` when the list owns every child (`ownsAllChildren`, line 1773). Ranvier removes each row's nodes through `Row.Remove` from each row owner's cleanup: 0.34 ms of native `remove` in clear1k (47.6%). Solid's whole `cleanChildren`, including the `textContent` assignment, takes 0.22 ms in its own profile. Solid's owner disposal (`disposeChildren`, `core/owner.js`, line 38) takes 0.064 ms for 1,000 rows, against Ranvier's 0.149 ms of row teardown excluding the DOM.

Points 1, 2, 4 and 5 match the remove hot spots; points 1, 3 and 4 match the swap hot spots; point 6 matches clear1k.

## Changes ranked by the saving the profiles show

1. **Give the projection item identities that survive a read.** E1 does this with stable tuples in `Template.each`. Alternatives are to project the user's items directly with `keyOf` applied to them, or to compare tuples by their components in the trim. Measured under the timing protocol (E1):
   - remove1k 0.765 → 0.425 ms (−0.34 ms), swap1k 0.890 → 0.495 ms (−0.40 ms), remove10k 8.54 → 5.40 ms (−3.1 ms);
   - in the loop, remove1k 0.311 → 0.137 ms and swap1k 0.403 → 0.174 ms.
   - The `WeakMap` in E1 costs 0.018 ms of remove1k and 0.64 ms of remove10k, so a version without a per-read lookup saves up to that much more.
2. **Make the trim cost no more than the full visit when it matches nothing.** E2 shows the trimmed pass is 0.195 ms slower than the full visit on unmatched input for remove1k (0.765 vs 0.570 ms); swap1k is 0.27 ms slower and remove10k 1.7 ms slower. This saving overlaps with change 1: once positions match, the trim no longer visits them.
3. **Drop the per-pass full walks in the view list region:**
   - `Row.Validate` on every pass: 0.025 ms total of remove1k at `85e0f8c` (8%), 0.785 ms of remove10k (20%), and 0.63 ms of remove10k under E1 (28%);
   - the `keys |> Array.map projection.Get` rebuild: 0.044 ms of remove1k (14%) and 0.60 ms of remove10k (15%); 0.054 ms of remove1k and 1.10 ms of remove10k under E1.
   - Reading rows only for the changed window, or validating only the rows touched, removes up to these amounts.
4. **Special-case small moves in `Row.Layout`** before building `sources` and running `increasingRun`, as Solid's swap-ends branch does: up to 0.089 ms of swap1k at `85e0f8c` (22%) and 0.072 ms under E1 (41%).
5. **Clear an owned list with one range or `textContent` removal** instead of one `Row.Remove` per row: the per-node native `remove` is 0.34 ms of clear1k in the loop. Solid's single assignment costs 0.22 ms in its profile, so the measured room is about 0.12 ms plus the 0.020 ms of `Row.Remove` loop overhead and the 0.054 ms of GC under `remove`.
6. **Stop writing retained rows' item signals when the item is unchanged:** `CommitWrites` is 0.011 ms of remove1k in the loop (3.6%) and 0.077 ms (6.1%) under the timing protocol; `SettleAdditions` in total is 8–9%. This saving overlaps with change 1, which stages no write for matched positions.

The profiles do not show where the timing protocol's post-GC penalty goes: under it remove1k is 0.70–0.83 ms against 0.25–0.30 ms in steady state, and Solid 2 is 0.09–0.12 ms against 0.03 ms. Those numbers bound what any of these changes can show in the reported timings, as distinct from the profile shares.
