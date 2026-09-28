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

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Effects.EffectBenchmarks` at commit `d87920f`.

| Method           | Effects | Mean        | Error     | StdDev    | Op/s         | Allocated |
|----------------- |-------- |------------:|----------:|----------:|-------------:|----------:|
| **WriteAndFlush**    | **1**       |    **17.81 ns** |  **0.184 ns** |  **0.154 ns** | **56,133,012.1** |         **-** |
| BatchOfTenWrites | 1       |    36.89 ns |  0.311 ns |  0.276 ns | 27,104,757.4 |         - |
| **WriteAndFlush**    | **8**       |   **114.76 ns** |  **1.007 ns** |  **0.841 ns** |  **8,713,660.1** |         **-** |
| BatchOfTenWrites | 8       |   179.76 ns |  2.292 ns |  2.032 ns |  5,562,912.3 |         - |
| **WriteAndFlush**    | **64**      |   **835.32 ns** |  **6.463 ns** |  **5.729 ns** |  **1,197,153.0** |         **-** |
| BatchOfTenWrites | 64      | 1,298.42 ns | 13.692 ns | 12.138 ns |    770,167.6 |         - |

`BatchOfTenWrites` performs ten writes per operation where `WriteAndFlush` performs one, so the rows are not comparable per operation. The batch row shows the cost of ten writes that share one flush.
