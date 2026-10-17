# Keyed list region passes — 2026-10-15

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open; every timing and profile ran one at a time, with no build alongside. A game process (`deadlock.exe`) used about six cores until shortly before the runs; the runs started after it exited and the CPU load read 2–15%. Of three timing runs, two had one disturbed process (remove10k 4.50 and 5.22 ms against about 3.1 ms in the others); their cause is unknown and they were discarded. The third discarded run read swap1k at 2.321× and remove1k at 2.792×, with Solid 2 in its slow mode in two of its processes for each operation.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release), Vite 8.3.2. Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Production code: `e447e21` (`poc/fable-ranvier-dom`). Against `1f1f1df` it adds:
  - `506e04b`, `86a476b`: each keyed pass validates and looks up only the rows around the changed window; unchanged items are found by reference against the region's own copy of the previous items;
  - `d426435`, `7f49dde`: `Row.Layout` moves the two end rows of an end swap, and one row when the window has two rows;
  - `d5bb6ce`, `e447e21`: row node removal is deferred to the end of a pass, so an emptied list is cleared with one `textContent` or `Range` operation;
  - `5a97087`, `995f3e9`, `51987e8`: docs and tests.
- Harness: local branch `measure/poc-rerun-2026-10-15` at `1f3d94d` (never pushed). `7add5b7` applies the `1f1f1df..e447e21` diff outside `docs/`, so `src/` and `experiments/` match `e447e21`; `1f3d94d` ignores the profile bundle directory. The harness files are otherwise those of the [item identity note](2026-10-15-list-item-identity.md).
- The production and profile bundles do not define `RANVIER_VIEW_COUNTERS` (only the PoC's `test:dom` script does): neither `validatedRows` nor `increasingRuns` appears in the compiled `Mounting.js` or the unminified bundle.
- Baseline: the `1f1f1df` figures of the item identity note. The protocol is unchanged, so `1f1f1df` was not re-measured. The function totals in the `1f1f1df` column of [Per-task attribution](#per-task-attribution) are recomputed from that note's raw CPU profiles with the script used for the new ones.

Absolute times are not comparable across days on this machine; ratios between frameworks measured in the same processes are. The absolute-time deltas against `1f1f1df` below are indicative only. Ratios below 1 mean Ranvier is faster.

Raw data in [`framework-performance/investigation/list-region-passes/`](framework-performance/investigation/list-region-passes/):

- `timings-solid.json`: the timing protocol, all samples;
- `heap.json`: heap snapshot diff, two processes;
- `protocol-check-1.json`, `protocol-check-2.json`: `keyed-gc-check.mjs`, two processes;
- `profile-loop.json`: loop-protocol CPU profile buckets, the top 40 functions by self time and the top 50 by total time;
- `profile-functions.json`, `profile-functions-1f1f1df.json`: total time per function, keyed by name and generated source line, for the new profiles and the item identity note's profiles.

## Keyed list against Solid 2

[`timings-solid.json`](framework-performance/investigation/list-region-passes/timings-solid.json). The protocol of the rerun note: five processes × (3 warmups + 21 shuffled rounds), a forced GC before every sample. Median ms, then the median same-round ratio `[per-process medians]`, and the ratio at `1f1f1df`:

| Operation | Solid 2 (ms) | Production (ms) | Ratio | Ratio at `1f1f1df` | Target | |
|---|---|---|---|---|---|---|
| create1k | 3.720 | 6.085 | 1.666 [1.89, 1.60, 1.63, 1.75, 1.66] | 1.628 | ≤ 2.0× | met |
| append1k | 2.865 | 4.585 | 1.506 [1.47, 1.62, 1.63, 1.48, 1.50] | 1.473 | ≤ 2.0× | met |
| clear1k | 0.575 | 1.115 | 1.920 [1.68, 1.97, 1.90, 2.00, 1.65] | 2.160 | ≤ 2.0× | met |
| swap1k | 0.120 | 0.355 | 3.040 [2.96, 3.46, 3.27, 2.92, 2.71] | 4.333 | ≤ 3× | not met |
| remove1k | 0.110 | 0.360 | 3.286 [3.19, 3.43, 3.29, 3.04, 3.14] | 3.333 | ≤ 3× | not met |
| clear10k / clear1k | — | 10.440 / 1.115 | 9.36× | 10.56× | within 12× | met |
| retained heap, 1,000 rows | 905.4 / 1,050.1 | 1,179.9 / 1,405.1 | snapshot diff / `getHeapUsage` (bytes per row) | 1,171.9 / 1,400.0 | ≤ 1,300 bytes | met by snapshot diff; not met by `getHeapUsage` |
| armsSwitch1k | 3.320 | 5.495 | 1.669 [1.67, 1.73, 1.60, 1.69, 1.63] | 1.668 | ≤ 2× | met |
| create10k | 34.785 | 52.765 | 1.529 [1.54, 1.52, 1.47, 1.51, 1.56] | 1.492 | — | |
| replace1k | 3.415 | 5.385 | 1.534 [1.43, 1.58, 1.65, 1.49, 1.47] | 1.474 | — | |
| update10th1k | 0.140 | 0.065 | 0.458 [0.15, 0.48, 0.48, 0.16, 0.16] | 0.462 | — | |
| remove10k | 0.665 | 3.060 | 4.674 [4.67, 4.94, 5.50, 4.59, 4.75] | 4.844 | — | |
| clear10k | 5.155 | 10.440 | 2.087 [2.06, 2.07, 2.16, 2.05, 2.06] | 2.441 | — | |
| armsCreate1k | 6.415 | 11.220 | 1.759 [1.78, 1.72, 1.76, 1.76, 1.76] | 1.707 | — | |
| armsPayload1k | 1.680 | 0.905 | 0.546 [0.54, 0.55, 0.52, 0.55, 0.53] | 0.531 | — | |
| armsClear1k | 1.085 | 2.830 | 2.595 [2.69, 2.37, 2.66, 2.70, 2.66] | 2.344 | — | |

Per-process medians, ms (Production, then Solid 2), with the `1f1f1df` production range:

| Operation | Production | Solid 2 | Production at `1f1f1df` |
|---|---|---|---|
| swap1k | 0.36, 0.41, 0.34, 0.34, 0.35 | 0.13, 0.13, 0.11, 0.12, 0.14 | 0.46–0.47 |
| remove1k | 0.36, 0.38, 0.35, 0.35, 0.36 | 0.12, 0.11, 0.11, 0.12, 0.11 | 0.35–0.37 |
| remove10k | 3.10, 3.06, 3.09, 3.04, 3.02 | 0.69, 0.74, 0.64, 0.67, 0.66 | 2.84–2.95 |
| clear1k | 1.13, 1.12, 1.06, 1.11, 1.13 | 0.66, 0.58, 0.56, 0.55, 0.68 | 1.17–1.21 |

- Production's per-process medians lie within 3% of each other for remove10k and within 9% for remove1k and clear1k. swap1k is 0.34–0.36 ms in four processes and 0.41 ms in the second, whose samples have a wider upper tail (0.33–0.72 ms against 0.31–0.53 ms).
- Two other runs of the same protocol each had one process with production remove10k at 4.50 and 5.22 ms, against about 3.1 ms in the others. They were discarded and are not reported here.
- Solid 2's `swap1k` ran in its fast mode (0.11–0.14 ms) in all five processes, as at `1f1f1df` (0.10–0.14 ms). Against the fast mode, swap1k moved from 3.25–4.67× to 2.71–3.46×.
- Solid 2's `remove1k` ran its fast mode (0.11–0.12 ms) in all five processes; at `1f1f1df` the fifth process ran its slow mode.
- Heap snapshot diff ([`heap.json`](framework-performance/investigation/list-region-passes/heap.json), unminified, two processes): 1,179.9 bytes per row at 1,000 rows in both processes; 1,184.0 and 1,163.2 at 10,000 rows. Solid 2: 905.4 and 936.0, as before. The 8 bytes per row added at 1,000 rows match two retained 1,000-slot arrays (4-byte compressed slots): the region's copy of the previous items and the previous pass's rows. In `heap.json` the `array (object elements)` count rose from 4,008 to 4,010.

## Steady-state loop

`keyed-gc-check.mjs`, the minified bundle, two processes, median of 40 per protocol, ms. "Loop" prepares a fresh list and runs the operation once per iteration inside one `evaluate`; "After CDP GC" is the timing protocol's step. The `1f1f1df` columns are the item identity note's two processes.

| Operation | Mode | Loop at `1f1f1df` | Loop, processes 1 / 2 | After CDP GC at `1f1f1df` | After CDP GC, processes 1 / 2 |
|---|---|---|---|---|---|
| remove1k | Ranvier | 0.100 / 0.100 | 0.075 / 0.075 | 0.355 / 0.345 | 0.335 / 0.325 |
| remove1k | Solid 2 | 0.030 / 0.035 | 0.030 / 0.035 | 0.090 / 0.090 | 0.090 / 0.095 |
| swap1k | Ranvier | 0.190 / 0.195 | 0.090 / 0.100 | 0.500 / 0.500 | 0.320 / 0.325 |
| swap1k | Solid 2 | 0.055 / 0.045 | 0.045 / 0.045 | 0.090 / 0.115 | 0.115 / 0.080 |
| clear1k | Ranvier | 0.525 / 0.530 | 0.385 / 0.360 | 1.190 / 1.220 | 1.065 / 1.025 |
| clear1k | Solid 2 | 0.255 / 0.265 | 0.265 / 0.265 | 0.535 / 0.520 | 0.590 / 0.535 |
| remove10k | Ranvier | 0.990 / 1.140 | 0.685 / 0.645 | 2.970 / 3.015 | 3.250 / 3.280 |
| remove10k | Solid 2 | 0.225 / 0.245 | 0.245 / 0.360 | 0.945 / 0.855 | 0.815 / 0.915 |

In the loop, Ranvier ÷ Solid 2 is 2.1–2.5× for remove1k (2.9–3.3× at `1f1f1df`), 2.0–2.2× for swap1k (3.5–4.3×), 1.4–1.5× for clear1k (2.0–2.1×) and 1.8–2.8× for remove10k (4.4–4.7×). Solid 2's remove10k loop is 0.360 ms in the second process against 0.225–0.245 ms in the other three runs, which gives the 1.8×.

## Where the time goes now

[`profile-loop.json`](framework-performance/investigation/list-region-passes/profile-loop.json). The method of the profile note: unminified bundle, 50 µs sampling, loop protocol (2,000 iterations for remove1k and swap1k, 1,000 for clear1k, 200 for remove10k), samples inside `measuredRun` only, the same buckets. Sampled ms per operation (share):

| Bucket | remove1k (0.078; 0.119 at `1f1f1df`) | remove10k (0.992; 1.638) | swap1k (0.119; 0.203) | clear1k (0.398; 0.660) |
|---|---|---|---|---|
| View list region | 0.042 (54.0%) | 0.515 (51.9%) | 0.047 (39.8%) | 0.251 (63.0%) |
| Core projection pass | 0.013 (16.6%) | 0.095 (9.6%) | 0.031 (25.7%) | 0.031 (7.7%) |
| Core graph | 0.013 (16.1%) | 0.191 (19.3%) | 0.024 (20.2%) | 0.021 (5.4%) |
| GC | 0.007 (9.4%) | 0.165 (16.7%) | 0.012 (10.3%) | 0.031 (7.7%) |
| DOM operations | 0.002 (2.4%) | 0.005 (0.5%) | 0.004 (3.7%) | < 0.001 |
| Row teardown | 0.001 (1.1%) | 0.014 (1.4%) | < 0.001 | 0.065 (16.3%) |

Solid 2 in the same profiles: remove1k 0.030 ms, swap1k 0.058 ms, remove10k 0.348 ms, clear1k 0.299 ms.

- clear1k's list region bucket now holds the clear itself: `parent.textContent = ""` in `Row.ClearBetween` is 0.216 ms of self time, since a property setter has no native frame of its own. The benchmark's `tbody` holds only the region, so the `textContent` branch runs.
- The bucket "Core projection pass" fell for remove10k (0.344 → 0.095 ms) because `Projection.Get` now runs for the changed window only; in the `1f1f1df` profiles the rebuild's `Projection.Get` calls counted there.
- The view list region bucket includes the benchmark's own `Array.removeAt` for remove (0.010 ms of remove1k), as at `1f1f1df`.

### Per-task attribution

Total time per function, ms per operation (share of the operation), from [`profile-functions.json`](framework-performance/investigation/list-region-passes/profile-functions.json) and [`profile-functions-1f1f1df.json`](framework-performance/investigation/list-region-passes/profile-functions-1f1f1df.json). "Row rebuild" is the layout callback's total minus `Row.Layout`: `keys |> Array.map projection.Get` at `1f1f1df`; at `e447e21`, the loop that takes unchanged rows from the previous pass and calls `Projection.Get` for the window.

| Change | Function | remove1k, `1f1f1df` → `e447e21` | remove10k | swap1k | clear1k |
|---|---|---|---|---|---|
| Window validate and lookup (`506e04b`, `86a476b`) | `Row.Validate` → `Row.ValidateWindow` | 0.020 → 0.001 | 0.449 → 0.005 | 0.019 → 0.027 | 0.018 → 0.024 |
| | Row rebuild | 0.040 → 0.012 | 0.540 → 0.182 | 0.034 → 0.040 | < 0.001 → 0.001 |
| Swap ends (`d426435`, `7f49dde`) | `Row.Layout` | 0.010 → 0.012 | 0.164 → 0.123 | 0.114 → 0.012 | 0.001 → 0.003 |
| | `increasingRun` | — | — | 0.054 → no samples | — |
| Deferred removal (`d5bb6ce`, `e447e21`) | native `remove` | 0.005 → 0.002 | 0.010 → 0.005 | — | 0.381 → no samples |
| | `Row.Remove` under `RegionSite.ReleaseRow` → `RegionSite.FlushReleased` | 0.005 → 0.003 | 0.012 → 0.013 | — | 0.415 → 0.236, of which `Row.ClearBetween` 0.235 |
| | row teardown bucket | 0.002 → 0.001 | 0.004 → 0.014 | < 0.001 | 0.156 → 0.065 |

- swap1k exchanges rows 1 and 998, so its changed window spans 998 of 1,000 rows: the window validation and the window's `Projection.Get` calls cover almost the whole list, and swap1k's saving is the swap-ends branch of `Row.Layout`.
- remove1k and remove10k gain from the window: validation and lookups fell to one or two rows; the row rebuild still copies every row reference.
- clear1k gains from the deferred removal: one `textContent` write (0.216 ms self) replaces 1,000 native `remove` calls (0.381 ms), and the row teardown bucket fell from 0.156 to 0.065 ms.
- `SettleAdditions` is 0.010 ms of remove1k (0.013 at `1f1f1df`), 0.150 of remove10k (0.152) and 0.015 of swap1k (0.013). `KeyedProjection.Trim` is 0.006, 0.056 and 0.008 ms (0.008, 0.048, 0.010).

## Targets

- **swap1k ≤ 3×: not met.** 3.040× at the median, 2.71–3.46× per process, with Solid 2 in its fast mode (0.11–0.14 ms) in every process. The median lies within the run-to-run spread of the 3× target. Production fell 0.46 → 0.355 ms. In the loop, 2.0–2.2×.
- **remove1k ≤ 3×: not met.** 3.286× at the median, 3.04–3.43× per process, with Solid 2 in its fast mode in every process. Production is 0.36 ms in both runs. In the loop, 2.1–2.5×.
- **clear1k ≤ 2×: met.** 1.920× at the median, 1.65–2.00× per process; one process is at 2.00. Solid 2's clear1k rose from 0.540 to 0.575 ms, and against the `1f1f1df` Solid 2 median production's 1.115 ms would be 2.06×, so about half of the drop from 2.160× comes from Solid 2. The loop ratio of 1.4–1.5×, with Solid 2 unchanged at 0.265 ms, is the stronger evidence of the gain. Production fell 1.18 → 1.115 ms.

## What moved

- **swap1k:** 4.333× → 3.040×; production 0.46 → 0.355 ms. In the loop, 0.19 → 0.09–0.10 ms.
- **remove1k:** 3.333× → 3.286×; production 0.36 ms in both runs. In the loop, 0.100 → 0.075 ms.
- **remove10k:** 4.844× → 4.674×; production 2.87 → 3.06 ms, with all five processes (3.02–3.10 ms) above the `1f1f1df` range of 2.84–2.95 ms. In the loop, 0.99–1.14 → 0.645–0.685 ms.
- **clear1k:** 2.160× → 1.920×; production 1.18 → 1.115 ms. In the loop, 0.525–0.530 → 0.360–0.385 ms. clear10k 2.441× → 2.087×.
- Under the timing protocol, remove1k and remove10k did not fall with the loop: after a CDP GC, remove1k is 0.33 ms against 0.075 ms in the loop, and remove10k 3.25 ms against 0.67 ms.
- **Create, append, replace, create10k, update10th1k, armsSwitch1k, armsCreate1k, armsPayload1k:** each ratio within 0.06 of `1f1f1df`. armsClear1k moved 2.344× → 2.595× (2.37–2.70 per process, 2.29–2.44 at `1f1f1df`); it was not profiled.
- **Retained heap:** 1,171.9 → 1,179.9 bytes per row by snapshot diff at 1,000 rows; 1,400.0 → 1,405.1 by `getHeapUsage`.

## Open

- remove1k and swap1k remain above 3× under the timing protocol and at 2.0–2.5× in the loop. The after-GC penalty is the larger share of the timing figure: remove1k is 0.33 ms after a CDP GC and 0.075 ms in the loop; Solid 2 is 0.09 and 0.03 ms.
- The row rebuild still copies every row reference on each pass: 0.012 ms of remove1k, 0.182 ms (18%) of remove10k.
- `Row.Layout` is 12% of remove10k and `RegionSite.Settle` 32% of remove and 46% of swap, of which `SettleAdditions` is 12–15%.
- armsClear1k rose to 2.60× while clear1k fell; it has not been profiled.
- Behaviour changes recorded by the region passes:
  - rows outside the changed window are not validated on each pass, so a foreign node inserted among them raises no error on a pass whose window excludes them;
  - an item keeps its key while it is in the list;
  - removed rows leave the document after the pass's projection and its row cleanups, before layout;
  - a node inserted inside the region by a cleanup during a clear is deleted with the region's rows, as in Solid.
- Key functions must be pure, and `KeyedItems` has internal fields, as recorded by the item identity note.
