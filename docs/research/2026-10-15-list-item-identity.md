# Keyed lists after the item identity change — 2026-10-15

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; every timing and profile ran one at a time, with no build alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release), Vite 8.3.2. Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Production code: `1f1f1df` (`poc/fable-ranvier-dom`). Against `85e0f8c` it adds:
  - #56 (`d264171`, merged at `f0f27c3`): a keyed projection pass that cannot trim takes the full visit;
  - `8352d7a`, `4d3b25c`: view keyed regions give the core projection the caller's item objects and a key function, in place of a new `(key, item)` tuple per item per read;
  - `9c42ed2`, `1f1f1df`: tests only.
- Harness: local branch `measure/poc-rerun-2026-10-15` at `0a4870e` (never pushed). `ca4d504` applies the `85e0f8c..1f1f1df` diff outside `docs/`, so `src/` and `experiments/` match `1f1f1df`; `0a4870e` adds `--out` to `keyed-heap.mjs`. The harness files are those of the [rerun](2026-10-15-poc-rerun.md) and the [profile](2026-10-15-list-remove-profile.md), otherwise unchanged.
- Baseline: the `85e0f8c` figures of those two notes. The protocol is unchanged, so `85e0f8c` was not re-measured.

Absolute times are not comparable across days on this machine; ratios between frameworks measured in the same processes are. Ratios below 1 mean Ranvier is faster.

Raw data in [`framework-performance/investigation/list-item-identity/`](framework-performance/investigation/list-item-identity/):

- `timings-solid.json`: the timing protocol, all samples;
- `heap.json`: heap snapshot diff, two processes;
- `protocol-check-1.json`, `protocol-check-2.json`: `keyed-gc-check.mjs`, two processes;
- `profile-loop.json`: loop-protocol CPU profile buckets, the top 40 functions by self time and the top 50 by total time.

## Keyed list against Solid 2

[`timings-solid.json`](framework-performance/investigation/list-item-identity/timings-solid.json). The protocol of the rerun note: five processes × (3 warmups + 21 shuffled rounds), a forced GC before every sample. Median ms, then the median same-round ratio `[per-process medians]`, and the ratio at `85e0f8c`:

| Operation | Solid 2 (ms) | Production (ms) | Ratio | Ratio at `85e0f8c` | Target | |
|---|---|---|---|---|---|---|
| create1k | 3.585 | 5.765 | 1.628 [1.75, 1.63, 1.65, 1.54, 1.65] | 1.809 | ≤ 2.0× | met |
| append1k | 2.970 | 4.495 | 1.473 [1.38, 1.48, 1.44, 1.64, 1.46] | 1.622 | ≤ 2.0× | met |
| clear1k | 0.540 | 1.180 | 2.160 [2.25, 2.21, 2.06, 2.18, 1.46] | 2.273 | ≤ 2.0× | not met |
| swap1k | 0.105 | 0.460 | 4.333 [3.25, 4.17, 3.92, 4.67, 4.38] | 2.202 | ≤ 3× | not met; see below |
| remove1k | 0.110 | 0.360 | 3.333 [3.13, 3.14, 2.91, 3.67, 0.94] | 6.833 | ≤ 3× | not met |
| clear10k / clear1k | — | 12.460 / 1.180 | 10.56× | 10.31× | within 12× | met |
| retained heap, 1,000 rows | 905.4 / 1,057.5 | 1,171.9 / 1,400.0 | snapshot diff / `getHeapUsage` (bytes per row) | 1,224.6 / 1,454.6 | ≤ 1,300 bytes | met by snapshot diff; not met by `getHeapUsage` |
| armsSwitch1k | 2.950 | 4.945 | 1.668 [1.76, 1.65, 1.65, 1.65, 1.66] | 1.777 | ≤ 2× | met |
| create10k | 34.510 | 51.325 | 1.492 [1.49, 1.50, 1.50, 1.46, 1.49] | 1.672 | — | |
| replace1k | 3.420 | 5.015 | 1.474 [1.44, 1.51, 1.41, 1.58, 1.46] | 1.553 | — | |
| update10th1k | 0.130 | 0.060 | 0.462 [0.52, 0.46, 0.44, 0.48, 0.30] | 0.127 | — | |
| remove10k | 0.615 | 2.870 | 4.844 [5.12, 5.06, 4.84, 4.74, 4.53] | 13.299 | — | |
| clear10k | 5.190 | 12.460 | 2.441 [2.49, 2.39, 2.49, 2.49, 2.36] | 2.801 | — | |
| armsCreate1k | 6.375 | 10.920 | 1.707 [1.81, 1.66, 1.73, 1.73, 1.63] | 1.615 | — | |
| armsPayload1k | 1.495 | 0.785 | 0.531 [0.56, 0.52, 0.52, 0.54, 0.53] | 0.535 | — | |
| armsClear1k | 0.960 | 2.280 | 2.344 [2.34, 2.44, 2.29, 2.35, 2.32] | 2.489 | — | |

Per-process medians, ms (Production, then Solid 2), with the `85e0f8c` production range:

| Operation | Production | Solid 2 | Production at `85e0f8c` |
|---|---|---|---|
| swap1k | 0.46, 0.47, 0.47, 0.46, 0.46 | 0.14, 0.11, 0.12, 0.10, 0.10 | 0.89–0.97 |
| remove1k | 0.37, 0.35, 0.36, 0.36, 0.36 | 0.12, 0.11, 0.12, 0.10, 0.39 | 0.78–0.83 |
| remove10k | 2.95, 2.84, 2.88, 2.88, 2.85 | 0.61, 0.56, 0.62, 0.60, 0.68 | 8.60 (median) |
| clear1k | 1.19, 1.21, 1.19, 1.17, 1.17 | 0.53, 0.53, 0.55, 0.53, 0.81 | 1.29–1.46 |

- Production's per-process medians lie within 4% of each other for every operation above, so the run shows no disturbance.
- Solid 2's `swap1k` ran in its fast mode (0.10–0.14 ms) in all five processes; at `85e0f8c` three of five processes ran its slow mode (about 0.45 ms). Against the fast mode, swap1k moved from 6.37–6.88× to 3.25–4.67×.
- Solid 2's `remove1k` ran its slow mode (0.39 ms) in the fifth process, where the ratio is 0.94. In the other four processes it is 2.91–3.67.
- Heap snapshot diff ([`heap.json`](framework-performance/investigation/list-item-identity/heap.json), unminified, two processes): 1,171.9 bytes per row at 1,000 rows in both processes; 1,176.0 and 1,155.2 at 10,000 rows. Solid 2: 905.4 and 936.0, as before.

## Steady-state loop

`keyed-gc-check.mjs`, the minified bundle, two processes, median of 40 per protocol, ms. "Loop" prepares a fresh list and runs the operation once per iteration inside one `evaluate`; "After CDP GC" is the timing protocol's step. The `85e0f8c` column is the one process of [`protocol-check.json`](framework-performance/investigation/list-remove-profile/protocol-check.json).

| Operation | Mode | Loop at `85e0f8c` | Loop, processes 1 / 2 | After CDP GC at `85e0f8c` | After CDP GC, processes 1 / 2 |
|---|---|---|---|---|---|
| remove1k | Ranvier | 0.285 | 0.100 / 0.100 | 0.735 | 0.355 / 0.345 |
| remove1k | Solid 2 | 0.035 | 0.030 / 0.035 | 0.095 | 0.090 / 0.090 |
| swap1k | Ranvier | 0.365 | 0.190 / 0.195 | 1.045 | 0.500 / 0.500 |
| swap1k | Solid 2 | 0.055 | 0.055 / 0.045 | 0.085 | 0.090 / 0.115 |
| clear1k | Ranvier | 0.655 | 0.525 / 0.530 | 1.385 | 1.190 / 1.220 |
| clear1k | Solid 2 | 0.275 | 0.255 / 0.265 | 0.595 | 0.535 / 0.520 |
| remove10k | Ranvier | 3.030 | 0.990 / 1.140 | 8.800 | 2.970 / 3.015 |
| remove10k | Solid 2 | 0.225 | 0.225 / 0.245 | 0.840 | 0.945 / 0.855 |

In the loop, Ranvier ÷ Solid 2 is 2.9–3.3× for remove1k (8.1× at `85e0f8c`), 3.5–4.3× for swap1k (6.6×), 2.0–2.1× for clear1k (2.4×) and 4.4–4.7× for remove10k (13.5×).

## Where the time goes now

[`profile-loop.json`](framework-performance/investigation/list-item-identity/profile-loop.json). The method of the profile note: unminified bundle, 50 µs sampling, loop protocol (2,000 iterations for remove1k and swap1k, 1,000 for clear1k, 200 for remove10k), samples inside `measuredRun` only, the same buckets. Sampled ms per operation (share):

| Bucket | remove1k (0.119; 0.311 at `85e0f8c`) | remove10k (1.638; 4.021) | swap1k (0.203; 0.403) | clear1k (0.660; 0.714) |
|---|---|---|---|---|
| View list region | 0.053 (44.3%) | 0.752 (45.9%) | 0.131 (64.6%) | 0.024 (3.6%) |
| Core projection pass | 0.033 (28.1%) | 0.344 (21.0%) | 0.031 (15.2%) | 0.036 (5.5%) |
| Core graph | 0.018 (15.1%) | 0.223 (13.6%) | 0.023 (11.2%) | 0.023 (3.4%) |
| GC | 0.009 (7.4%) | 0.306 (18.7%) | 0.015 (7.2%) | 0.101 (15.2%) |
| DOM operations | 0.004 (3.7%) | 0.010 (0.6%) | 0.003 (1.6%) | 0.320 (48.5%) |
| Row teardown | 0.002 (1.4%) | 0.004 (0.2%) | < 0.001 | 0.156 (23.6%) |

Solid 2 in the same profiles: remove1k 0.040 ms, swap1k 0.054 ms, remove10k 0.400 ms, clear1k 0.320 ms.

Total time of the functions named by the profile note's ranked changes, ms per operation (share):

| Function | remove1k | remove10k | swap1k | clear1k |
|---|---|---|---|---|
| `KeyedProjection.Trim` | 0.008 (7.0%) | 0.048 (2.9%) | 0.010 (4.7%) | — |
| `keys \|> Array.map projection.Get` in `ListRegion.reconcile` | 0.039 (32.4%) | 0.516 (31.5%) | 0.029 (14.2%) | — |
| `Row.Validate` | 0.020 (16.4%) | 0.449 (27.4%) | 0.019 (9.5%) | 0.018 (2.7%) |
| `Row.Layout` (of which `increasingRun`) | 0.010 (8.6%) | 0.164 (10.0%) | 0.114 (56.1%), `increasingRun` 0.054 (26.9%) | — |
| `SettleAdditions` | 0.013 (11.0%) | 0.152 (9.3%) | 0.013 (6.2%) | 0.008 (1.2%) |
| `Row.Remove` under `RegionSite.ReleaseRow` | 0.005 (4.1%) | — | — | 0.415 (62.8%) |

- `KeyedProjection.Trim` takes 0.008 ms of remove1k, against 0.122 ms at `85e0f8c`: the pass now trims the view's lists.
- The key-array rebuild runs outside the projection pass; no `Projection.Run` sample lies under it.
- In swap1k, `setItem` and `item` (fable-library, under `Row.Layout`) are the two largest self times, 19.6% and 16.1%.
- In clear1k, native `remove` is 0.320 ms (48.5%), as at `85e0f8c` (0.340 ms).

## Targets

- **swap1k ≤ 3×: not met.** 4.333× at the median, 3.25–4.67× per process, with Solid 2 in its fast mode in every process. Production halved, 0.91 → 0.46 ms.
- **remove1k ≤ 3×: not met.** 3.333× at the median; 2.91–3.67× in the four processes where Solid 2 ran its fast mode. Production fell 0.81 → 0.36 ms.
- **clear1k ≤ 2×: not met.** 2.160×; production 1.36 → 1.18 ms, Solid 2 0.61 → 0.54 ms.

## What moved

- **remove1k:** 6.833× → 3.333×; production 0.81 → 0.36 ms. In the loop, 0.285 → 0.100 ms.
- **remove10k:** 13.299× → 4.844×; production 8.60 → 2.87 ms. In the loop, 3.03 → 0.99–1.14 ms.
- **swap1k:** 2.202× → 4.333×. Production 0.91 → 0.46 ms; the median ratio rose because Solid 2 ran its fast mode in all five processes. Against Solid 2's fast mode the ratio fell from 6.37–6.88× to 3.25–4.67×.
- **clear1k:** 2.273× → 2.160×; production 1.36 → 1.18 ms.
- **update10th1k:** 0.127× → 0.462×. Production is 0.060 ms in both runs; Solid 2 moved 0.49 → 0.13 ms.
- **Create, append, replace, create10k, clear10k, armsSwitch1k:** each ratio fell by 0.08–0.36. armsCreate1k moved 1.615× → 1.707×; armsPayload1k and armsClear1k within 0.15.
- **Retained heap:** 1,224.6 → 1,171.9 bytes per row by snapshot diff at 1,000 rows; 1,454.6 → 1,400.0 by `getHeapUsage`.
- Against the profile note's stable-tuples experiment E1 (remove1k 0.425 ms, swap1k 0.495 ms, remove10k 5.40 ms under the timing protocol, on another day), production is at 0.36, 0.46 and 2.87 ms.

## Open

- The largest remaining costs of remove and swap are in the view list region: the key-array rebuild (32% of remove1k, 31% of remove10k), `Row.Validate` (16% and 27%) and, for swap1k, `Row.Layout` (56%, of which `increasingRun` 27%). These are the profile note's changes 3 and 4.
- clear1k is native `remove` per row (48.5%) and row teardown (23.6%), the profile note's change 5.
- `SettleAdditions`, including the retained rows' item signal writes, is 6–11% of remove and swap (change 6).
- Key functions must now be pure. A key function that reads a signal no longer re-runs the list region when that signal changes.
- `KeyedItems` has internal fields, so a backend outside `Ranvier.View.Foundation` cannot read the items or key of a keyed region.
- The timing protocol's post-GC penalty is unchanged in kind: remove1k is 0.35 ms after a CDP GC and 0.10 ms in the loop; Solid 2 is 0.09 and 0.03 ms.
