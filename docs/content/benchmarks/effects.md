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

> **Figures pending.** The figures for these cases come with the next instruction-counter run ([`counters.ps1`](counters.md), on Windows, after the merge).

The counter scenario `mvu-dispatch` compares a signal per field with `Mvu.Dispatch` at 64 fields.

## EditableBenchmarks

A writable derived value (`createEditable`) against a plain signal, each read by one effect.

| Method | What it measures |
| --- | --- |
| `PlainSignalWrite` | Baseline. One signal write, waking one effect. |
| `LocalEdit` | A local edit, waking the effect that reads the editable. |
| `UpstreamChange` | An upstream write propagating through the source, the seed, the editable and the effect. |

> **Figures pending.** The figures for these cases come with the next instruction-counter run ([`counters.ps1`](counters.md), on Windows, after the merge).

The counter scenarios `editable-edit` and `editable-upstream` measure the same two writes over 1000 editables.
