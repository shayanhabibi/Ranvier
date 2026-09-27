---
title: Effects
order: 4
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

Effects are the push side of the graph: nothing reads them, so the scheduler runs them. A write that reaches an effect therefore pays for queueing and flushing on top of notification. See [Benchmarks](index.md) for configuration, environment and caveats.

## EffectBenchmarks

One signal observed by `Effects` effects (1, 8 or 64).

| Method | What it measures |
| --- | --- |
| `WriteAndFlush` | One write, then the flush that runs every effect the write invalidated. |
| `BatchOfTenWrites` | Ten writes inside one `batch`, followed by a single flush that runs each effect once. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Effects.EffectBenchmarks` at commit `915f139`.

| Method           | Effects | Mean        | Error     | StdDev    | Op/s         | Allocated |
|----------------- |-------- |------------:|----------:|----------:|-------------:|----------:|
| **WriteAndFlush**    | **1**       |    **17.54 ns** |  **0.363 ns** |  **0.388 ns** | **57,026,644.5** |         **-** |
| BatchOfTenWrites | 1       |    39.58 ns |  0.643 ns |  0.602 ns | 25,262,805.0 |         - |
| **WriteAndFlush**    | **8**       |   **115.85 ns** |  **0.915 ns** |  **0.856 ns** |  **8,631,846.5** |         **-** |
| BatchOfTenWrites | 8       |   193.82 ns |  3.861 ns |  3.612 ns |  5,159,450.9 |         - |
| **WriteAndFlush**    | **64**      |   **863.59 ns** | **14.831 ns** | **13.148 ns** |  **1,157,963.5** |         **-** |
| BatchOfTenWrites | 64      | 1,345.23 ns | 26.842 ns | 37.629 ns |    743,367.6 |         - |

`BatchOfTenWrites` performs ten writes per operation where `WriteAndFlush` performs one, so the rows are not comparable per operation. The batch row shows the cost of ten writes that share one flush.
