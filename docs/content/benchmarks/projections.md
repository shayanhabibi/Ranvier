---
title: Projections
order: 6
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

Projections keep keyed, per-row reactive state over a collection source. Selectors (`Lookup`) track, per key, whether that key matches a source value. See [Benchmarks](index.md) for configuration, environment and caveats.

## ProjectionBenchmarks

A projection over `Items` rows (8, 64 or 512), observed by one effect so that the projection stays scheduled.

| Method | What it measures |
| --- | --- |
| `ReadRow` | A tracked read of one cached row. |
| `EditOneItem` | A write that changes the value of the first row, re-running that row's effect. |
| `Reorder` | A write of the same keys and values in reverse order: the key order changes and every row keeps its value. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Projections.ProjectionBenchmarks` at commit `915f139`.

| Method      | Items | Mean         | Error       | StdDev      | Op/s          | Gen0   | Gen1   | Allocated |
|------------ |------ |-------------:|------------:|------------:|--------------:|-------:|-------:|----------:|
| **ReadRow**     | **8**     |     **5.833 ns** |   **0.0843 ns** |   **0.0788 ns** | **171,439,099.5** |      **-** |      **-** |         **-** |
| EditOneItem | 8     |   216.145 ns |   4.3265 ns |   4.8089 ns |   4,626,527.6 | 0.0057 |      - |      96 B |
| Reorder     | 8     |   193.706 ns |   3.7413 ns |   4.0031 ns |   5,162,452.2 | 0.0210 |      - |     352 B |
| **ReadRow**     | **64**    |     **5.901 ns** |   **0.0867 ns** |   **0.0768 ns** | **169,460,750.1** |      **-** |      **-** |         **-** |
| EditOneItem | 64    |   959.434 ns |  17.1787 ns |  16.0690 ns |   1,042,281.2 | 0.0057 |      - |      96 B |
| Reorder     | 64    | 1,216.021 ns |  24.1097 ns |  37.5359 ns |     822,354.3 | 0.1411 |      - |    2368 B |
| **ReadRow**     | **512**   |     **5.803 ns** |   **0.1103 ns** |   **0.1031 ns** | **172,328,449.5** |      **-** |      **-** |         **-** |
| EditOneItem | 512   | 7,152.490 ns | 141.4320 ns | 145.2403 ns |     139,811.4 |      - |      - |      96 B |
| Reorder     | 512   | 8,983.265 ns | 174.9247 ns | 227.4516 ns |     111,318.1 | 1.0986 | 0.0916 |   18496 B |

## LookupBenchmarks

A selector created with `createSelector`, whose key 7 loses its last observer and is read again before the next transition. Each case should reuse the key's cell; a regression would rebuild it.

| Method | What it measures |
| --- | --- |
| `RemountKey` | Mounting and disposing the only reader of a key. |
| `ReadAheadOfGet` | Re-running a reader that inserts or drops a read ahead of its `Get`. |
| `UntrackedGet` | An untracked read of an unobserved key. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Projections.LookupBenchmarks` at commit `915f139`.

| Method         | Mean      | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|--------------- |----------:|---------:|---------:|-------------:|-------:|----------:|
| RemountKey     | 101.36 ns | 1.476 ns | 1.380 ns |  9,865,396.3 | 0.0257 |     432 B |
| ReadAheadOfGet |  55.62 ns | 0.795 ns | 0.743 ns | 17,979,513.9 | 0.0014 |      24 B |
| UntrackedGet   |  21.23 ns | 0.358 ns | 0.335 ns | 47,107,140.7 | 0.0014 |      24 B |
