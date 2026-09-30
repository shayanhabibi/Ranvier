---
title: Effects
order: 4
---

> **Preview** — Ranvier is pre-release; its APIs may change.

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

## FieldWriteBenchmarks

One field of a `Fields`-field model (8, 64 or 256) changed per write, with one effect per field. The model is an `int[]`, so a copy costs what copying a record of `Fields` fields costs. See [Elmish](../guide/elmish.md) for the patterns compared.

| Method | What it measures |
| --- | --- |
| `FieldSignalWrite` | Baseline. A write to one field's own signal, waking its one effect. Flat in `Fields`. |
| `SelectorMemoWrite` | A copy of a root signal's model with one field changed, re-running all `Fields` selector memos and waking one effect. |
| `MvuDispatch` | `SelectorMemoWrite` through `Mvu.Dispatch` and `Mvu.Select`. |
| `ModelCopyOnly` | The model copy alone: the part of `SelectorMemoWrite` spent outside the graph. |

The counter scenario `mvu-dispatch` compares a signal per field (`FieldSignalWrite`) with `Mvu.Dispatch` (`MvuDispatch`) at 64 fields. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Variant | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| A signal per field | 607 | 0 | EffectRuns 1, Flushes 1 |
| `Mvu.Dispatch` | 48,133 | 400 | MemoRecomputes 64, EffectRuns 1, Flushes 1 |

A dispatch costs about 80 times a field signal write: every observed selector re-runs, 64 recomputes per dispatch. `SelectorMemoWrite`, `ModelCopyOnly` and the 8- and 256-field cases are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*FieldWriteBenchmarks*"`) and have no published figure yet. The full report is [`e13f159.md`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## EditableBenchmarks

A writable derived value (`createEditable`) against a plain signal, each read by one effect.

| Method | What it measures |
| --- | --- |
| `PlainSignalWrite` | Baseline. One signal write, waking one effect. |
| `LocalEdit` | A local edit, waking the effect that reads the editable. |
| `UpstreamChange` | An upstream write propagating through the source, the seed, the editable and the effect. |

The counter scenarios `editable-edit` and `editable-upstream` measure the same two writes over 1000 editables, each read by one effect. One operation writes every 10th editable, 100 writes in all. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Scenario | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| `editable-edit` | 145,006 | 0 | MemoRecomputes 100, EffectRuns 100, Flushes 100 |
| `editable-upstream` | 194,449 | 2,400 | MemoRecomputes 200, EffectRuns 100, Flushes 100 |

A local edit costs about 1,450 instructions and allocates nothing; an upstream write costs about 1,940 instructions and 24 bytes, and recomputes two memos per write. `PlainSignalWrite` has no counter scenario of its own and no published figure yet; the timing cases are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*EditableBenchmarks*"`). The full report is [`e13f159.md`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).
