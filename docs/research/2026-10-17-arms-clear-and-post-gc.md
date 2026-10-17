# armsClear1k and the first operation after a forced GC — 2026-10-17

## Environment and commits

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200. Rider open and idle; timings ran one at a time, with no build running alongside.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0 (`--noCache`, Release). Solid 2: `solid-js`, `@solidjs/web`, `@solidjs/compiler` 2.0.0-rc.14.
- Production code: before is `3461496` (master merged into `poc/fable-ranvier-dom`), after is `db538a0` (the armsClear1k fix).
- Harness: local branch `measure/poc-rerun-2026-10-15` (never pushed). `3d33bb9` carries the `3461496` code and `e62b20c` the `db538a0` code. The bundles are `generated-keyed-before` (experiments from `3d33bb9`) and `generated-keyed-after` (`e62b20c`). Every timing JSON records `commit: e62b20c`, including `timings-before-*.json`: their bundle was built from the `3d33bb9` experiments checked out into the `e62b20c` tree.
- Uncommitted harness changes on the measure branch, present in the pinned/unpinned and GC runs (`dirty` in each JSON):
  - `keyed-page.mjs`: `prepared(mode, name, beforeSetup)` and `retain(mode, count)`.
  - `keyed-rows.mjs --pin`: for each mode and operation, keeps one list that ran the operation once mounted for the whole process.
  - `keyed-gc-trace.mjs`: times one operation under several GC protocols with Chrome tracing of the young and old generation collections inside the setup and the timed step.
  - `keyed-gc-decay.mjs`: times `k` consecutive prepared steps after one full collection.

Ratios are Ranvier over Solid 2 in the same round; below 1 means Ranvier is faster. Absolute times are not comparable across days.

Raw data in [`framework-performance/investigation/arms-clear-and-post-gc/`](framework-performance/investigation/arms-clear-and-post-gc/):

- `timings-before-1..5.json`, `timings-after-1..5.json`: the armsClear1k fix, one process each.
- `profile-buckets-before.json`, `profile-buckets-after.json`: CPU profile buckets for armsClear1k and clear1k, method of [the remove profile](2026-10-15-list-remove-profile.md#method).
- `gc-*.json`, `gc-decay.txt`: the post-GC investigation.
- `timings-unpinned-1..5.json`, `timings-pinned-1..5.json`: the full protocol with and without `--pin`.

## armsClear1k

Clearing a list whose rows each contain a `matchWith` region removed each arm's nodes one at a time while the region's owner was disposed, and then removed the rows themselves. `db538a0` changes `RegionSite.ReleaseRow`: a template row of a region whose owner is disposed inside a live mount is marked removed, and its nodes leave with the enclosing row. A removed row keeps its nested rows inside its detached element, as Solid 2 does.

Five processes per build, alternating, 3 warmups and 21 shuffled rounds, a forced GC before every sample. Median ratio `[per-process medians]`, then Ranvier's median ms:

| Operation | Ratio before | Ratio after | Ranvier ms before → after | Solid 2 ms before → after |
|---|---|---|---|---|
| armsClear1k | 2.348 [2.33, 2.38, 2.43, 2.35, 2.30] | 1.872 [1.82, 2.10, 1.87, 1.87, 1.88] | 2.145 → 1.695 | 0.915 → 0.900 |
| armsSwitch1k | 1.450 | 1.513 | 4.435 → 4.385 | 3.065 → 2.940 |
| armsCreate1k | 1.560 | 1.564 | 10.785 → 10.495 | 6.775 → 6.720 |
| clear1k | 1.757 | 1.835 | 1.020 → 1.035 | 0.570 → 0.540 |
| remove1k | 2.300 | 2.276 | 0.345 → 0.340 | 0.150 → 0.145 |

Profile buckets for Ranvier's armsClear1k, share of samples in the timed step:

| Bucket | Before | After |
|---|---|---|
| Row teardown | 51.8% | 55.2% |
| DOM operations | 17.3% | 0.1% |
| GC | 15.2% | 18.9% |
| View list region | 10.5% | 18.2% |
| Core projection pass | 3.2% | 4.3% |
| Core graph | 2.0% | 3.3% |

The native `remove` calls (17.3% of self time) are gone. The remaining time is mostly owner and effect disposal (`Owner.DisposeScope`, `Row.ClearBetween`).

## The first operation after a forced GC

The timing protocol forces a full GC through CDP (`HeapProfiler.collectGarbage`) before every sample. By then every instance of Ranvier's row, owner and effect shapes is dead. The measurements below are consistent with V8 discarding those maps together with the optimized code that depends on them: the next operation runs on unoptimized or reoptimizing code and recovers over about three operations. `--trace-deopt` during the protocol shows Maglev code deoptimized for insufficient type feedback and dependent field-type changes.

remove1k, median of 40 per row, ms (`gc-trace.json`, `gc-forced.json`, `gc-wait.json`, `gc-single.json`, `gc-semi16.json`, `gc-pin0.json`, `gc-pin10.json`, `gc-pinop.json`):

| Protocol | Ranvier | Solid 2 |
|---|---|---|
| CDP GC, then the operation (timing protocol) | 0.395 | 0.100 |
| Back-to-back prepared lists in one `evaluate` | 0.075 | 0.030 |
| Loop with one or two minor GCs before the timed step | 0.085 / 0.075 | 0.030 / 0.055 |
| Loop with an in-page full `gc()` before the timed step | 0.275 | 0.095 |
| In-page full `gc()`, then a timer of 0 / 20 / 200 / 1,000 ms | 0.345 / 0.345 / 0.365 / 0.370 | 0.075 / 0.070 / 0.090 / 0.090 |
| In-page full `gc()` with `--single-threaded-gc` | 0.335 | 0.080 |
| CDP GC with `--min-semi-space-size=16` | 0.380 | 0.080 |
| CDP GC, a created list kept mounted (`--pin=<rows>`) | 0.230 | 0.095 |
| CDP GC, a list that ran remove1k once kept mounted (`--pin-op`) | 0.195 | 0.115 |

- No remove1k sample in these runs has a collection inside the timed step, apart from 2.5% of Solid 2's `--pin-op` loop samples; the post-GC cost is not collection work.
- A full collection alone reproduces it; minor collections do not. Waiting up to one second after the collection does not remove it, so it is not concurrent sweeping or background compilation finishing.
- Keeping live instances of the shapes removes most of it.

`gc-decay.txt` (`keyed-gc-decay.mjs --k=30 --n=20`): remove1k times of 30 consecutive prepared steps after one full collection, median of 20.

- Ranvier: 0.170, 0.130, 0.115, then 0.055–0.070.
- Solid 2: 0.080, 0.060, 0.060, then 0.020–0.025.

The same comparison on the other large ratios, median of 30, ms, CDP GC protocol (`gc-other0.json` without pinning, `gc-other-pin.json` with `--pin-op`):

| Operation | Ranvier | Ranvier, `--pin-op` | Solid 2 | Solid 2, `--pin-op` |
|---|---|---|---|---|
| swap1k | 0.335 | 0.155 | 0.485 | 0.090 |
| clear1k | 1.035 | 0.440 | 0.575 | 0.500 |
| armsClear1k | 1.805 | 0.735 | 1.080 | 0.940 |

Solid 2's unpinned swap1k sample includes a young collection in the timed step in 97% of samples (0.115 ms without it); its pinned samples have none.

## The full protocol with and without `--pin`

`generated-keyed-after`, ten processes alternating unpinned and pinned, each 3 warmups and 21 shuffled rounds with a forced CDP GC before every sample. All ten processes completed. Median ms across the five process medians, then the median ratio `[per-process medians]`:

| Operation | Solid 2 | Ranvier | Ratio, unpinned | Solid 2, pinned | Ranvier, pinned | Ratio, pinned |
|---|---|---|---|---|---|---|
| create1k | 3.720 | 5.735 | 1.546 [1.52, 1.62, 1.53, 1.55, 1.67] | 3.350 | 4.185 | 1.276 [1.22, 1.23, 1.29, 1.28, 1.28] |
| create10k | 35.315 | 50.580 | 1.439 [1.44, 1.45, 1.43, 1.42, 1.50] | 24.700 | 34.885 | 1.404 [1.40, 1.39, 1.40, 1.44, 1.41] |
| replace1k | 3.350 | 5.225 | 1.488 [1.51, 1.47, 1.55, 1.49, 1.42] | 2.700 | 3.740 | 1.369 [1.46, 1.16, 1.37, 1.49, 1.36] |
| update10th1k | 0.125 | 0.055 | 0.440 [0.44, 0.26, 0.46, 0.46, 0.39] | 0.095 | 0.040 | 0.438 [0.47, 0.41, 0.38, 0.44, 0.47] |
| swap1k | 0.105 | 0.340 | 3.190 [3.38, 2.52, 3.19, 3.29, 1.79] | 0.120 | 0.160 | 1.250 [1.28, 1.25, 1.31, 1.22, 1.13] |
| remove1k | 0.105 | 0.340 | 3.150 [3.25, 3.09, 3.20, 3.15, 1.64] | 0.125 | 0.205 | 1.667 [1.77, 1.67, 1.74, 1.54, 1.59] |
| remove10k | 0.760 | 2.980 | 4.190 [5.15, 3.83, 4.14, 4.19, 4.19] | 0.655 | 2.340 | 3.620 [4.62, 3.62, 3.58, 1.76, 3.69] |
| clear1k | 0.535 | 1.050 | 1.935 [1.93, 1.91, 1.95, 1.94, 1.45] | 0.545 | 0.420 | 0.796 [0.81, 0.80, 0.75, 0.76, 0.87] |
| clear10k | 5.230 | 10.305 | 1.987 [2.03, 1.78, 1.99, 1.55, 2.00] | 3.690 | 5.835 | 1.579 [1.59, 1.58, 1.56, 1.18, 1.67] |
| append1k | 2.810 | 4.395 | 1.460 [1.50, 1.45, 1.46, 1.46, 1.49] | 1.995 | 3.360 | 1.734 [1.73, 1.69, 1.78, 1.84, 1.69] |
| armsCreate1k | 6.215 | 9.840 | 1.561 [1.53, 1.55, 1.62, 1.56, 1.60] | 5.860 | 7.655 | 1.306 [1.31, 1.29, 1.31, 1.34, 1.29] |
| armsSwitch1k | 2.975 | 4.600 | 1.551 [1.52, 1.56, 1.54, 1.55, 1.58] | 2.485 | 3.600 | 1.432 [1.41, 1.51, 1.40, 1.43, 1.46] |
| armsPayload1k | 1.485 | 0.730 | 0.485 [0.48, 0.49, 0.48, 0.48, 0.49] | 0.870 | 0.275 | 0.319 [0.30, 0.33, 0.33, 0.32, 0.32] |
| armsClear1k | 0.970 | 1.710 | 1.786 [1.84, 1.65, 1.76, 1.79, 1.86] | 1.055 | 0.740 | 0.674 [0.66, 0.66, 0.67, 0.71, 0.71] |

Geometric mean of the 14 ratios per process: unpinned 1.636, 1.484, 1.610, 1.579, 1.444 (median 1.579); pinned 1.177, 1.129, 1.142, 1.078, 1.160 (median 1.142).

- The removal and clearing operations move most: swap1k 3.19× → 1.25×, remove1k 3.15× → 1.67×, clear1k 1.94× → 0.80×, armsClear1k 1.79× → 0.67×. Solid 2's times on those four are within 0.09 ms either way.
- Pinning also speeds Solid 2 on the large creations (create10k 35.3 → 24.7 ms, append1k 2.81 → 2.00 ms), so those ratios move less, and append1k rises 1.46× → 1.73×.
- remove10k stays at 3.6× pinned; it is the largest remaining ratio.
- The unpinned run is the same protocol as earlier reports; its ratios are comparable to them. The unpinned armsClear1k (1.786×) agrees with the after build of the fix run (1.872×).

An application usually keeps live instances of its row, owner and effect shapes while it removes or clears rows, so the pinned figures are closer to that state. The unpinned protocol measures a first operation after every instance has died.

## Reproduction

From `examples/Fable.Ranvier.Playground` on the measure branch, one at a time:

```sh
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-after --processes=1 --rounds=21 --shuffle --no-alloc --pairs=ranvier:solid --label=unpinned-1 --out=<dir>
node benchmarks/frameworks/keyed-rows.mjs --dist=generated-keyed-after --processes=1 --rounds=21 --shuffle --no-alloc --pin --pairs=ranvier:solid --label=pinned-1 --out=<dir>
node benchmarks/frameworks/keyed-gc-trace.mjs --dist=generated-keyed-after --n=40 --protocols=cdp-gc,loop --pin-op --json=<file>
node benchmarks/frameworks/keyed-gc-decay.mjs --dist=generated-keyed-after --k=30 --n=20
```
