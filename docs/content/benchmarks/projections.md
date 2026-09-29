---
title: Projections
order: 6
---

> **Preview** — Ranvier is pre-release; its APIs may change.

Projections keep keyed, per-row reactive state over a collection source. Selectors (`Lookup`) track, per key, whether that key matches a source value. See [Benchmarks](index.md) for configuration, environment and caveats.

## ProjectionBenchmarks

A projection over `Items` rows (8, 64 or 512), observed by one effect so that the projection stays scheduled.

| Method | What it measures |
| --- | --- |
| `ReadRow` | A tracked read of one cached row. |
| `EditOneItem` | A write that changes the value of the first row, re-running that row's effect. |
| `Reorder` | A write of the same keys and values in reverse order: the key order changes and every row keeps its value. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Projections.ProjectionBenchmarks` at commit `d87920f`.

| Method      | Items | Mean         | Error       | StdDev     | Op/s          | Gen0   | Gen1   | Allocated |
|------------ |------ |-------------:|------------:|-----------:|--------------:|-------:|-------:|----------:|
| **ReadRow**     | **8**     |     **6.184 ns** |   **0.0715 ns** |  **0.0669 ns** | **161,707,225.4** |      **-** |      **-** |         **-** |
| EditOneItem | 8     |   214.629 ns |   2.3013 ns |  2.1527 ns |   4,659,197.7 | 0.0057 |      - |      96 B |
| Reorder     | 8     |   183.185 ns |   2.5630 ns |  2.1403 ns |   5,458,957.0 | 0.0210 |      - |     352 B |
| **ReadRow**     | **64**    |     **6.013 ns** |   **0.0744 ns** |  **0.0696 ns** | **166,315,149.2** |      **-** |      **-** |         **-** |
| EditOneItem | 64    |   925.895 ns |  11.0672 ns | 10.3522 ns |   1,080,035.8 | 0.0057 |      - |      96 B |
| Reorder     | 64    | 1,140.921 ns |  10.3918 ns |  9.2121 ns |     876,485.0 | 0.1411 |      - |    2368 B |
| **ReadRow**     | **512**   |     **6.005 ns** |   **0.0735 ns** |  **0.0688 ns** | **166,522,611.8** |      **-** |      **-** |         **-** |
| EditOneItem | 512   | 6,653.607 ns |  45.0946 ns | 35.2069 ns |     150,294.4 |      - |      - |      96 B |
| Reorder     | 512   | 8,307.363 ns | 104.3172 ns | 92.4745 ns |     120,375.1 | 1.0986 | 0.0916 |   18496 B |

## LookupBenchmarks

A selector created with `createSelector`, whose key 7 loses its last observer and is read again before the next transition. Each case should reuse the key's cell; a regression would rebuild it.

| Method | What it measures |
| --- | --- |
| `RemountKey` | Mounting and disposing the only reader of a key. |
| `ReadAheadOfGet` | Re-running a reader that inserts or drops a read ahead of its `Get`. |
| `UntrackedGet` | An untracked read of an unobserved key. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Projections.LookupBenchmarks` at commit `d87920f`.

| Method         | Mean      | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|--------------- |----------:|---------:|---------:|-------------:|-------:|----------:|
| RemountKey     | 101.88 ns | 1.721 ns | 1.525 ns |  9,815,568.7 | 0.0257 |     432 B |
| ReadAheadOfGet |  54.37 ns | 0.434 ns | 0.406 ns | 18,392,960.7 | 0.0014 |      24 B |
| UntrackedGet   |  21.15 ns | 0.170 ns | 0.159 ns | 47,272,722.8 | 0.0014 |      24 B |
