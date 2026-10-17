# Keyed rows on the core: measurement — 2026-10-14

- Machine: AMD Ryzen 9 9900X (12 cores, 62 GB), Windows 11 10.0.26200.
- Chromium 153.0.8010.12 (Playwright 1.63.0), Node 26.7.0, Fable 5.18.0, .NET SDK 10.0.402, Vite 8.3.2. Solid 2: `solid-js`, `@solidjs/web` and `@solidjs/compiler` 2.0.0-rc.14, native compiler.
- Date: 2026-10-14.
- Commit: production code at `ab68bde` (`poc/fable-ranvier-dom`, Tasks 1–9). Every run used the throwaway tree committed afterwards as `4353219` on `measure/keyed-rows-on-core`, which is never merged. Each JSON records `commit: ab68bde` and a `dirty` list of the throwaway files. The timing files' `sourceHashes` match `4353219`.

Raw data, in [`framework-performance/investigation/keyed-rows-on-core/`](framework-performance/investigation/keyed-rows-on-core/): `counters.json`, `heap.json`, `timings.json`, `timings-ab-direct.json`, `timings-ab-swapped.json`, `timings-solid.json`, `core.json`, `bundle.json`.

The harness is the one from Keyed-row cost (`docs/research/2026-10-14-keyed-row-cost.md` on `research/keyed-row-cost`, `aa5b811`). The settings match it unless stated: shuffled paired rounds, a forced GC before every sample, and `flush()` inside each timed write.

## Variants

| | Core | Rows (`Mounting.rowForm`) |
|---|---|---|
| **A** | option code compiled out (`RANVIER_ROWS_BASELINE`) | 0: `createProjectionWith`, each row wrapped as `fun item -> let row = buildRow item in fun () -> row` |
| **B** | options compiled in | 1: `createProjectionWithOptions ProjectionOptions.None`, same wrapper |
| **C1** | options compiled in | 2: `createProjectionWithOptions ProjectionOptions.CreateFirst`, same wrapper |
| **C2** | options compiled in | 3: `createProjectionOnce ProjectionOptions.None` |
| **C3** | options compiled in | 4: `createProjectionOnce ProjectionOptions.CreateFirst` (production) |

`RANVIER_ROWS_BASELINE` compiles out the following: `OnceRow`, `CreateFirst`, `OnceValue`, `RequireRowMemos` and its nine combinator call sites and two `Projection` call sites, `IsDisposedInternal`, `Discard`, `UseOnce`, `CreateOnce`, the create-first branch of `ApplyDiff`, the held-error test in `EnsureCurrent`, the deferred-dirty re-mark in `Abandon`, the create-first raises in `FactoryFailure`, the guarded creation loop in `CreateAdded`, the null tests of `entry.Row` in `rowObserved`, `Retire`, `TrackRow`, `ReadRow` and `Snapshot`, and `createProjectionWithOptions` and `createProjectionOnce`. `ItemRow` is `Sealed` under the define. The rest of the list region is the same in every form; `rowForm` selects only the projection constructor.

## Core options off and on

### Op counters

[`counters.json`](framework-performance/investigation/keyed-rows-on-core/counters.json). `RanvierCounters=true` builds, one run of each operation under jsdom after its untimed setup on a fresh list.

| Operation | Variant | Signals | Memos | Effects | Owners | Edges + / − | Observers + / − | Memo recomputes | Effect runs | Flushes | Factory rows | Memo-free rows |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| create1k | A, B, C1 | 2,000 | 1,000 | 1,000 | 1,000 | 1,000 / 0 | 1,000 / 0 | 1,000 | 1,001 | 1 | 1,000 | 0 |
| create1k | C2, C3 | 2,000 | 0 | 1,000 | 1,000 | 1,000 / 0 | 1,000 / 0 | 0 | 1,001 | 1 | 0 | 1,000 |
| create10k | A, B, C1 | 20,000 | 10,000 | 10,000 | 10,000 | 10,000 / 0 | 10,000 / 0 | 10,000 | 10,001 | 1 | 10,000 | 0 |
| create10k | C2, C3 | 20,000 | 0 | 10,000 | 10,000 | 10,000 / 0 | 10,000 / 0 | 0 | 10,001 | 1 | 0 | 10,000 |
| append1k | A, B, C1 | 2,000 | 1,000 | 1,000 | 1,000 | 1,000 / 0 | 1,000 / 0 | 1,000 | 1,001 | 1 | 1,000 | 0 |
| append1k | C2, C3 | 2,000 | 0 | 1,000 | 1,000 | 1,000 / 0 | 1,000 / 0 | 0 | 1,001 | 1 | 0 | 1,000 |
| update10th1k | all | 0 | 0 | 0 | 0 | 0 / 0 | 0 / 0 | 0 | 100 | 1 | 0 | 0 |
| swap1k | all | 0 | 0 | 0 | 0 | 0 / 0 | 0 / 0 | 0 | 1 | 1 | 0 | 0 |
| remove1k | all | 0 | 0 | 0 | 0 | 0 / 1 | 0 / 1 | 0 | 1 | 1 | 0 | 0 |
| clear1k | all | 0 | 0 | 0 | 0 | 0 / 1,000 | 0 / 1,000 | 0 | 1 | 1 | 0 | 0 |

- B equals A in every tally of every operation; the file's `differences` list holds no B entry.
- C1 equals B, because no measured operation fails.
- C2 and C3 have zero `FactoryRowsCreated` and one `OnceRowsCreated` per created row. They create one memo fewer per row and run one memo recompute fewer per row.

### Retained heap by constructor

[`heap.json`](framework-performance/investigation/keyed-rows-on-core/heap.json). Each figure is a CDP heap-snapshot diff of one page: an empty mounted list, then the same list after `create(rows)`. Unminified, two processes. Each bundle holds one core build: A runs in the baseline bundle, B to C3 in the option bundle. Bytes per row (objects per row), 1,000 rows, first process:

| Constructor | A | B | C1 | C2 | C3 | Solid 2 |
|---|---|---|---|---|---|---|
| `(object elements)` backing stores | 275.6 (5) | 275.6 (5) | 275.6 (5) | 275.6 (5) | 275.6 (5) | 21.2 (0) |
| closure contexts | 184 (6) | 184 (6) | 184 (6) | 140 (4) | 140 (4) | 108 (3) |
| closures | 168.1 (6) | 168.1 (6) | 168.1 (6) | 112.1 (4) | 112.1 (4) | 272 (10) |
| `Array` | 144 (9) | 144 (9) | 144 (9) | 112 (7) | 112 (7) | 0.1 (0) |
| `OwnerLink` | 108 (3) | 108 (3) | 108 (3) | 72 (2) | 72 (2) | — |
| strings | 95.2 | 95.2 | 95.2 | 95.2 | 95.2 | 20 |
| `Memo$1` | 84 (1) | 84 (1) | 84 (1) | — | — | — |
| `Effect$1` | 72 (1) | 72 (1) | 72 (1) | 72 (1) | 72 (1) | — |
| `ObserverSet` | 72 (3) | 72 (3) | 72 (3) | 48 (2) | 48 (2) | — |
| `Signal$1` | 64 (2) | 64 (2) | 64 (2) | 64 (2) | 64 (2) | — |
| `SourceList` | 56 (2) | 56 (2) | 56 (2) | 28 (1) | 28 (1) | — |
| `Owner` | 52 (1) | 52 (1) | 52 (1) | 52 (1) | 52 (1) | — |
| `ItemRow$3` / `OnceRow$3` | 44 (1) | 44 (1) | 44 (1) | 48 (1) | 48 (1) | — |
| `Row$2`, `ObserverSlot`, `KeyedRow` | 84 (4) | 84 (4) | 84 (4) | 84 (4) | 84 (4) | — |
| per-list arrays | 24.8 | 24.8 | 24.8 | 24.8 | 24.8 | — |
| `Object`, bound-argument arrays | — | — | — | — | — | 484 (8) |
| **total, 1,000 rows** | **1,527.8** | **1,527.8** | **1,527.8** | **1,227.8** | **1,227.8** | **905.4** |
| total, 10,000 rows (two processes) | 1,532.0 / 1,511.2 | 1,511.2 / 1,511.2 | 1,511.2 / 1,511.2 | 1,211.2 / 1,211.2 | 1,211.2 / 1,211.2 | 936 / 936 |

- A and B are identical in every constructor's count and bytes, at both row counts and in both processes. The one exception is strings: A's first 10,000-row process holds 20.8 bytes per row more string bytes (see [Unexplained results](#unexplained-results)). String object counts differ by 68–83 in total, not per row, with equal bytes.
- C1 equals B.
- C2 and C3 have no `Memo$1` per row. The memo-field attribution (`memo`) is empty for both. They retain 300 bytes per row less than A and B at both counts: the row memo (256 bytes) and the `fun () -> row` reader with its context (44).

`Runtime.getHeapUsage`, median of five processes, from `timings.json`. It includes everything the list allocates:

| Rows | A | B | C1 | C2 | C3 | Solid 2 |
|---|---|---|---|---|---|---|
| 1,000 | 1,791 | 1,791 | 1,794 | 1,460 | 1,469 | 1,062 |
| 10,000 | 1,624 | 1,626 | 1,627 | 1,318 | 1,319 | 1,051 |

### Timings

[`timings.json`](framework-performance/investigation/keyed-rows-on-core/timings.json). Five Chromium processes × (3 warmups + 21 rounds). A, B, C1, C2, C3 and Solid 2 alternate in one page, in a seeded shuffled order per round. Minified production bundle. Median milliseconds, then the median same-round ratio to Solid 2:

| Operation | Solid 2 (ms) | A | B | C1 | C2 | C3 |
|---|---|---|---|---|---|---|
| create1k | 5.215 | 9.200 (1.708×) | 9.085 (1.699×) | 9.230 (1.695×) | 8.490 (1.563×) | 8.515 (1.557×) |
| create10k | 36.225 | 64.110 (1.738×) | 63.895 (1.764×) | 64.235 (1.739×) | 56.800 (1.548×) | 56.480 (1.545×) |
| replace1k | 3.695 | 7.580 (1.961×) | 7.775 (2.031×) | 7.825 (2.051×) | 6.390 (1.676×) | 6.135 (1.674×) |
| update10th1k | 0.160 | 0.070 (0.464×) | 0.075 (0.464×) | 0.070 (0.444×) | 0.070 (0.447×) | 0.070 (0.414×) |
| swap1k | 0.120 | 0.705 (6.000×) | 0.730 (6.409×) | 0.775 (6.302×) | 0.680 (5.818×) | 0.680 (5.857×) |
| remove1k | 0.125 | 0.695 (5.952×) | 0.775 (6.261×) | 0.795 (6.182×) | 0.680 (5.619×) | 0.685 (5.591×) |
| remove10k | 0.655 | 5.390 (9.427×) | 5.500 (9.530×) | 5.425 (9.339×) | 7.075 (11.043×) | 7.360 (12.153×) |
| clear1k | 0.630 | 1.450 (2.367×) | 1.525 (2.400×) | 1.475 (2.396×) | 1.430 (2.279×) | 1.390 (2.228×) |
| clear10k | 4.735 | 12.215 (2.879×) | 12.335 (2.918×) | 12.585 (2.896×) | 13.115 (2.701×) | 13.115 (2.628×) |
| append1k | 3.005 | 5.710 (1.948×) | 5.870 (2.056×) | 5.755 (2.008×) | 5.660 (1.889×) | 5.500 (1.898×) |

Same-round ratios between variants, median `[per-process medians]`:

| Operation | B / A | C1 / B | C2 / B | C3 / B | C3 / C2 | C3 / A |
|---|---|---|---|---|---|---|
| create1k | 0.988 [0.997, 0.985, 1.020, 0.991, 0.977] | 1.003 [1.007, 0.984, 1.022, 0.977, 1.012] | 0.918 [0.903, 0.923, 0.959, 0.919, 0.910] | 0.916 [0.932, 0.907, 0.938, 0.903, 0.919] | 1.003 | 0.908 [0.905, 0.892, 0.921, 0.918, 0.894] |
| create10k | 1.005 [1.010, 1.005, 0.983, 1.002, 1.035] | 0.991 [1.015, 0.997, 0.990, 0.835, 0.991] | 0.870 | 0.876 | 1.007 | 0.879 [0.888, 0.879, 0.879, 0.860, 0.926] |
| replace1k | 1.004 [1.028, 0.988, 1.061, 0.988, 0.998] | 1.040 | 0.827 | 0.813 | 0.999 | 0.837 [0.861, 0.824, 0.868, 0.836, 0.816] |
| update10th1k | 1.000 [0.806, 1.000, 1.000, 0.917, 1.000] | 1.000 | 0.950 | 0.917 | 1.000 | 0.923 |
| swap1k | 1.050 [1.073, 1.029, 1.048, 1.068, 1.023] | 0.993 | 0.933 | 0.938 | 0.992 | 0.984 [0.913, 0.979, 0.969, 1.013, 0.984] |
| remove1k | 1.047 [1.094, 1.021, 1.024, 1.099, 1.043] | 0.984 | 0.903 | 0.899 | 1.008 | 0.944 [0.906, 0.945, 0.976, 0.944, 0.933] |
| remove10k | 1.027 [1.015, 1.024, 1.011, 1.071, 1.066] | 0.995 | 1.263 [1.185, 1.199, 1.304, 1.312, 1.292] | 1.284 [1.201, 1.227, 1.302, 1.342, 1.338] | 1.012 | 1.321 [1.224, 1.254, 1.343, 1.406, 1.404] |
| clear1k | 1.012 [1.178, 0.990, 1.020, 1.011, 0.989] | 1.008 | 0.942 | 0.912 | 0.987 | 0.939 [1.071, 1.008, 0.940, 0.924, 0.929] |
| clear10k | 1.004 [1.001, 0.992, 1.033, 1.008, 0.965] | 1.006 | 0.919 | 0.924 | 0.999 | 0.908 [0.925, 0.858, 0.930, 0.907, 0.903] |
| append1k | 1.041 [1.017, 1.057, 1.091, 1.025, 1.011] | 0.983 | 0.928 | 0.940 | 1.003 | 0.964 [0.929, 0.970, 0.974, 0.986, 0.962] |

The arm operations need an arm adapter for every mode in the page, so they do not run here. They run in [Production against Solid 2](#production-against-solid-2).

**Bundle-position control.** In the six-mode page, B's core is the first module graph in the bundle and A's is the second. Two further runs time A and B alone, each with 5 processes × 31 shuffled rounds. [`timings-ab-direct.json`](framework-performance/investigation/keyed-rows-on-core/timings-ab-direct.json) runs the bundle as built. [`timings-ab-swapped.json`](framework-performance/investigation/keyed-rows-on-core/timings-ab-swapped.json) uses `RANVIER_ROWS_SWAPPED=true`, which exchanges the two builds' import positions. Each variant picks its fixture by core build, so A stays on the baseline core in both runs and neither column is inverted. B / A, median `[per-process medians]`:

| Operation | As built | Exchanged | Geometric mean |
|---|---|---|---|
| create1k | 1.004 [0.999, 1.028, 0.994, 1.009, 0.992] | 1.008 [1.012, 1.025, 1.020, 1.000, 0.995] | 1.006 |
| create10k | 1.001 [0.973, 1.000, 1.006, 1.011, 1.003] | 1.003 [1.010, 1.001, 1.004, 1.011, 0.991] | 1.002 |
| replace1k | 1.002 [0.999, 1.020, 1.002, 0.988, 1.002] | 1.005 [1.006, 1.017, 1.039, 0.977, 0.993] | 1.003 |
| update10th1k | 1.000 [1.000, 1.071, 1.000, 1.077, 1.000] | 1.000 [1.000, 1.059, 1.000, 1.000, 1.000] | 1.000 |
| swap1k | 1.000 [1.000, 1.000, 0.985, 1.067, 0.977] | 1.000 [0.993, 0.993, 1.036, 1.007, 0.992] | 1.000 |
| remove1k | 1.013 [1.045, 1.000, 1.000, 1.008, 1.013] | 1.020 [0.993, 1.030, 1.020, 1.014, 1.008] | 1.016 |
| remove10k | 1.016 [1.014, 1.004, 1.014, 1.030, 1.031] | 1.024 [1.035, 1.028, 1.028, 1.021, 1.006] | 1.020 |
| clear1k | 0.997 [1.004, 0.997, 0.951, 1.012, 0.992] | 1.012 [1.031, 1.013, 1.016, 1.019, 0.992] | 1.004 |
| clear10k | 0.993 [0.991, 1.011, 0.970, 1.000, 0.976] | 1.023 [1.027, 0.994, 1.024, 1.035, 1.023] | 1.008 |
| append1k | 1.005 [0.996, 0.999, 1.007, 1.011, 1.006] | 1.006 [1.002, 1.026, 1.000, 1.021, 0.999] | 1.006 |

With bundle position controlled, B is within 1% of A on create, replace, update, swap, clear and append. `remove1k` is 1.6% slower and `remove10k` 2.0% slower. All ten per-process medians of `remove10k` are at or above 1.004.

### Pure-core workload

[`core.json`](framework-performance/investigation/keyed-rows-on-core/core.json), `bench/Ranvier.RowsBench`. Each row's factory creates one effect reading its label, and one effect reads `Keys` and every row untracked. There is no DOM. The bench runs 21 rounds, 10 warmups and 21 samples per process, with one process per variant per round in rotating order. .NET is Release net10.0; Node runs the Fable build with `--expose-gc`. Median ms of A, then the median `[interquartile range]` of the per-round ratios:

| Runtime | Operation | A (ms) | B / A | C1 / B | C2 / B | C3 / B | C3 / A |
|---|---|---|---|---|---|---|---|
| Node | create1k | 1.433 | 0.974 [0.931, 0.999] | 1.008 [0.939, 1.022] | 0.718 [0.699, 0.727] | 0.690 [0.668, 0.747] | 0.684 [0.639, 0.716] |
| Node | create10k | 11.695 | 0.979 [0.954, 0.997] | 1.014 [0.981, 1.033] | 0.669 [0.660, 0.691] | 0.690 [0.665, 0.720] | 0.671 [0.651, 0.695] |
| Node | append1k | 0.983 | 0.955 [0.912, 1.013] | 1.027 [0.944, 1.084] | 0.875 [0.842, 0.907] | 0.863 [0.837, 0.900] | 0.838 [0.789, 0.872] |
| Node | update10th1k | 0.025 | 0.980 [0.937, 1.008] | 1.017 [0.920, 1.050] | 1.028 [0.968, 1.072] | 1.036 [0.980, 1.102] | 1.012 [0.972, 1.089] |
| Node | swap1k | 0.241 | 0.999 [0.954, 1.016] | 0.997 [0.974, 1.031] | 0.966 [0.939, 0.991] | 0.966 [0.924, 0.987] | 0.946 [0.921, 0.986] |
| Node | remove1k | 0.255 | 1.011 [0.969, 1.061] | 1.004 [0.977, 1.057] | 0.974 [0.935, 1.002] | 0.986 [0.930, 1.004] | 0.967 [0.954, 1.010] |
| Node | clear1k | 0.257 | 0.985 [0.919, 1.055] | 0.995 [0.973, 1.081] | 0.778 [0.741, 0.844] | 0.776 [0.736, 0.821] | 0.782 [0.738, 0.812] |
| .NET | create1k | 0.528 | 0.979 [0.962, 0.985] | 1.002 [0.986, 1.018] | 0.762 [0.747, 0.772] | 0.765 [0.761, 0.776] | 0.748 [0.739, 0.763] |
| .NET | create10k | 4.754 | 1.183 [1.050, 1.233] | 1.020 [0.861, 1.268] | 0.622 [0.548, 0.667] | 0.616 [0.532, 0.655] | 0.686 [0.643, 0.704] |
| .NET | append1k | 0.849 | 1.004 [0.914, 1.142] | 0.972 [0.831, 1.148] | 0.788 [0.704, 0.936] | 0.827 [0.768, 0.887] | 0.861 [0.733, 0.977] |
| .NET | update10th1k | 0.017 | 1.034 [0.930, 1.244] | 0.973 [0.825, 1.129] | 0.990 [0.799, 1.153] | 1.022 [0.951, 1.150] | 1.158 [0.894, 1.299] |
| .NET | swap1k | 0.207 | 0.996 [0.905, 1.040] | 1.003 [0.991, 1.128] | 0.938 [0.828, 1.009] | 0.908 [0.871, 1.006] | 0.918 [0.860, 1.026] |
| .NET | remove1k | 0.205 | 0.992 [0.929, 1.025] | 1.030 [0.982, 1.147] | 0.911 [0.814, 0.977] | 0.894 [0.869, 1.001] | 0.906 [0.858, 1.011] |
| .NET | clear1k | 0.211 | 0.976 [0.826, 1.076] | 1.012 [0.923, 1.148] | 0.842 [0.814, 0.932] | 0.884 [0.804, 1.012] | 0.846 [0.801, 0.943] |

- Under Node, B is within 4.5% of A at the median on every operation. Its interquartile ranges reach 1 or lie below it. C2 and C3 save 28–33% of a create and 22% of a clear.
- On .NET, `create1k` shows B 2.1% faster than A, with an interquartile range of [0.962, 0.985]. `ItemRow` stays sealed under the baseline define, as it was in the research's off build. The research measured B 1–2% slower; this build has the reverse sign.
- C2 and C3 save 24% of `create1k` and 38% of `create10k` on .NET.

### Bundle size

[`bundle.json`](framework-performance/investigation/keyed-rows-on-core/bundle.json): minified Vite library builds of one entry each. Bytes, minified / gzip / Brotli:

| Entry | A (baseline) | B (options compiled in) | B − A |
|---|---|---|---|
| core, every `Api` export | 137,576 / 30,890 / 26,929 | 138,911 / 31,294 / 27,304 | +1,335 / +404 / +375 |
| core, `createProjectionWith` only | 87,848 / 20,577 / 18,336 | 88,882 / 20,887 / 18,587 | +1,034 / +310 / +251 |
| keyed fixture (view and core) | 173,724 / 41,937 / 36,536 | 175,330 / 42,304 / 36,881 | +1,606 / +367 / +345 |

The fixture row includes the measurement switch: the option build carries row forms 1–4, and the baseline build carries form 0 and an `invalidOp`. The two core rows contain only core code. A program using only `createProjectionWith` carries 1,034 bytes minified of option code, against 850 for the research's memo-free option alone.

## Production against Solid 2

[`timings-solid.json`](framework-performance/investigation/keyed-rows-on-core/timings-solid.json). C3 (production rows; arms through the production arm adapter) and Solid 2 alone in one page. Five processes × (3 warmups + 21 shuffled rounds). Median ms, then the median same-round ratio `[per-process medians]`:

| Operation | Solid 2 (ms) | Production (ms) | Ratio | Target | |
|---|---|---|---|---|---|
| create1k | 3.660 | 6.085 | 1.690 [1.76, 1.60, 1.67, 1.69, 1.63] | ≤ 2.0× | met |
| append1k | 3.165 | 4.855 | 1.625 [1.65, 1.54, 1.65, 1.65, 1.63] | ≤ 2.0× | met |
| clear1k | 0.845 | 1.340 | 1.607 [1.85, 1.79, 2.18, 1.45, 1.48] | ≤ 2.0× | met (six-mode page: 2.228×, not met) |
| swap1k | 0.165 | 0.625 | 4.556 [5.35, 5.64, 4.48, 1.59, 1.42] | ≤ 3× | not met |
| remove1k | 0.125 | 0.595 | 5.000 [4.91, 5.41, 4.54, 4.18, 5.43] | ≤ 3× | not met |
| clear10k / clear1k | — | 13.170 / 1.340 | 9.83× | within 12× | met (six-mode page: 9.44×) |
| retained heap, 1,000 rows | 905.4 / 1,057 | 1,227.8 / 1,448 | snapshot diff / `getHeapUsage` (bytes per row) | ≤ 1,300 bytes | met by snapshot diff; not met by `getHeapUsage` |
| armsSwitch1k | 3.065 | 5.355 | 1.768 [1.73, 1.82, 1.71, 1.75, 1.76] | ≤ 2× | met |
| create10k | 33.360 | 54.490 | 1.694 [1.75, 1.61, 1.72, 1.57, 1.55] | — | |
| replace1k | 3.545 | 5.385 | 1.505 [1.57, 1.45, 1.52, 1.53, 1.55] | — | |
| update10th1k | 0.465 | 0.060 | 0.210 [0.16, 0.46, 0.33, 0.12, 0.39] | — | |
| remove10k | 0.680 | 7.465 | 11.000 [10.66, 10.86, 10.42, 10.61, 16.29] | — | |
| clear10k | 5.050 | 13.170 | 2.561 [2.39, 3.03, 2.95, 2.55, 2.53] | — | |
| armsCreate1k | 6.475 | 10.920 | 1.700 [1.73, 1.73, 1.70, 1.62, 1.70] | — | |
| armsPayload1k | 1.490 | 0.805 | 0.541 [0.55, 0.56, 0.54, 0.54, 0.54] | — | |
| armsClear1k | 0.990 | 2.330 | 2.333 [2.30, 2.45, 2.39, 2.36, 2.27] | — | |

- The heap target does not name a measure. The snapshot diff (`heap.json`, unminified, objects new after `create`) gives 1,227.8 bytes per row. `Runtime.getHeapUsage` (`timings-solid.json`, minified, everything the list allocates) gives 1,448.
- Met: `create1k`, `append1k`, `clear1k` in this page, `clear10k` / `clear1k`, and `armsSwitch1k`. The heap target is met by the snapshot diff only.
- Not met: `swap1k`, `remove1k`, and `clear1k` in the six-mode page.

## Differences from the research prototype

The research's projection-row numbers (prototypes 6 and 7, `timings-memo-free.json`) come from a view that differs from production `ListRegion` in two ways:

1. **Region input.** The research projection read `read ()` as its tracked source. In production, the region effect tracks `read ()` once into a snapshot and bumps a region-local `version` signal untracked; the projection's source reads `version` and returns the snapshot. The effect pulls `projection.Keys` untracked inside `MountLifetime.Capture`.
2. **Row removal.** Production has no nested-row removal skip. Every template row removes its own nodes. Rows retired during their mount's teardown are queued in `releasedRows` and removed by the mount's release.

The harness also differs:

- Variants select the projection through `Mounting.rowForm` and pick their fixture by core build. Under `RANVIER_ROWS_SWAPPED` each variant therefore keeps its own core, and the exchanged column needs no inversion.
- Arm operations run only through the production arm adapter.

For comparison with the research, at 1,000 rows by snapshot diff: research A 1,516 and C 1,216; here A 1,527.8 and C3 1,227.8. Both are 11.8 bytes per row higher, and the gap is 300 bytes in both. `create1k`: research C / A 0.891; here C3 / A 0.908.

## Unexplained results

- **`remove10k` is slower with memo-free rows.** In the six-mode page, C2 / B is 1.263 and C3 / B 1.284, with every per-process median from 1.185 to 1.342. C3 / A is 1.321. C2 and C3 take 7.08 and 7.36 ms against A's 5.39 ms. `remove1k` is 10% faster under C2 and C3, and `clear10k` 8% faster. The pure-core bench has no `remove10k`.
- **B over A on removes.** Position-controlled, `remove10k` B / A is 1.016 as built and 1.024 exchanged, and `remove1k` is 1.013 and 1.020. All ten `remove10k` per-process medians are at or above 1.004.
- **B over A on `swap1k` and `remove1k` in the six-mode page.** The ratios are 1.050 and 1.047, with every per-process median above 1.02. The controls give 1.000 / 1.000 and 1.013 / 1.020.
- **Solid 2's own times differ between pages.** The six-mode and two-mode pages give `update10th1k` 0.160 against 0.465 ms, `swap1k` 0.120 against 0.165 ms, and `clear1k` 0.630 against 0.845 ms. Production's ratios differ with them, e.g. `clear1k` 2.228× against 1.607×. In the two-mode page, `swap1k` per-process ratios split into two groups: 5.35, 5.64 and 4.48 against 1.59 and 1.42.
- **One heap process.** In A's first 10,000-row process, strings retain 88.6 bytes per row against 67.8 in A's second process and in every other variant.
- **.NET pure core.** `create10k` B / A is 1.183 with an interquartile range of [1.050, 1.233], wider than any Node range. On .NET, C3 is slower than C2 on `append1k` (0.753 against 0.676 ms), `swap1k`, `remove1k` and `clear1k`; under Node the two are within 3%.
