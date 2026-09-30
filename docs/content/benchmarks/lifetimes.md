---
title: Lifetimes
order: 5
---

> **Preview** — Ranvier is pre-release; its APIs may change.

Construction and teardown are not the hottest path, but they are the path a UI takes on every mount and unmount, and the one where an owner that only grows would show up as a leak rather than as a slowdown. See [Benchmarks](index.md) for configuration, environment and caveats.

## ConstructionBenchmarks

| Method | What it measures |
| --- | --- |
| `CreateSignal` | Baseline. Constructing a signal. |
| `CreateAndDisposeMemo` | Constructing and disposing a memo that reads nothing. |
| `CreateAndDisposeEffect` | Constructing and disposing an effect that reads nothing. |

`CreateAndDisposeSeededMemo` constructs and disposes a memo through the seeded constructor, `Memo (graph, seed, compute)`, which adds one adapter closure over the seed and the compute. It postdates the table below and has no published figure yet; it is in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*ConstructionBenchmarks*"`).

There is deliberately no create-without-dispose case for memos. A memo attaches itself to its enclosing owner, so one that is never disposed is retained for the life of the graph by design; measuring that would measure garbage collection over a growing child list rather than construction.

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Lifetimes.ConstructionBenchmarks` at commit `d87920f`.

| Method                 | Mean      | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| CreateSignal           |  8.552 ns | 0.1997 ns | 0.2219 ns | 116,926,213.1 |     baseline |         | 0.0053 |      88 B |             |
| CreateAndDisposeMemo   | 25.348 ns | 0.4896 ns | 0.4580 ns |  39,451,210.8 | 2.97x slower |   0.09x | 0.0148 |     248 B |  2.82x more |
| CreateAndDisposeEffect | 35.357 ns | 0.7312 ns | 0.8980 ns |  28,282,776.9 | 4.14x slower |   0.15x | 0.0114 |     192 B |  2.18x more |

## ReadingNodeBenchmarks

The same lifecycle for nodes that read a source, so the dependency edge is part of the cost: linked on the first run, unlinked on disposal.

| Method | What it measures |
| --- | --- |
| `CreateAndDisposeReadingEffect` | An effect that reads one signal, created and disposed. |
| `CreateAndDisposeReadingMemo` | A memo that reads one signal, created and disposed. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Lifetimes.ReadingNodeBenchmarks` at commit `d87920f`.

| Method                        | Mean     | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|------------------------------ |---------:|---------:|---------:|-------------:|-------:|----------:|
| CreateAndDisposeReadingEffect | 39.67 ns | 0.811 ns | 0.719 ns | 25,208,600.5 | 0.0129 |     216 B |
| CreateAndDisposeReadingMemo   | 40.46 ns | 0.800 ns | 0.821 ns | 24,716,778.6 | 0.0162 |     272 B |

## FanOutLifecycleBenchmarks

`Nodes` computations (1, 64 or 1024) on one source, created in a scope and torn down with it. The larger cases cross the observer list's indexing threshold, so they include building and draining that index. Divide by `Nodes` for the cost per node.

| Method | What it measures |
| --- | --- |
| `EffectsOnOneSource` | `Nodes` effects reading one signal, created and disposed as a unit. |
| `MemosOnOneSource` | `Nodes` memos reading one signal, created and disposed as a unit. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Lifetimes.FanOutLifecycleBenchmarks` at commit `d87920f`.

| Method             | Nodes | Mean         | Error        | StdDev       | Op/s         | Gen0    | Gen1   | Allocated |
|------------------- |------ |-------------:|-------------:|-------------:|-------------:|--------:|-------:|----------:|
| **EffectsOnOneSource** | **1**     |     **68.62 ns** |     **1.392 ns** |     **1.547 ns** | **14,573,344.5** |  **0.0257** |      **-** |     **432 B** |
| MemosOnOneSource   | 1     |     67.41 ns |     0.959 ns |     0.850 ns | 14,833,915.8 |  0.0291 |      - |     488 B |
| **EffectsOnOneSource** | **64**    |  **4,008.59 ns** |    **53.988 ns** |    **47.859 ns** |    **249,464.3** |  **0.9232** | **0.0458** |   **15552 B** |
| MemosOnOneSource   | 64    |  3,992.81 ns |    77.870 ns |    72.840 ns |    250,450.3 |  1.1406 | 0.0687 |   19136 B |
| **EffectsOnOneSource** | **1024**  | **69,184.00 ns** | **1,100.919 ns** | **1,029.800 ns** |     **14,454.2** | **14.6484** | **6.7139** |  **245952 B** |
| MemosOnOneSource   | 1024  | 68,837.85 ns | 1,084.798 ns |   961.645 ns |     14,526.9 | 18.0664 | 9.2773 |  303296 B |

## ScopeBenchmarks

A scope with `Children` children (1, 8 or 64): the shape a component mount takes.

| Method | What it measures |
| --- | --- |
| `CreateAndDisposeScope` | The scope and its children, created and torn down as a unit. |
| `DisposeChildrenIndividually` | The same children, each disposed on its own rather than through the scope. This checks that an individually disposed child leaves its owner's child list. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Lifetimes.ScopeBenchmarks` at commit `d87920f`.

| Method                      | Children | Mean        | Error     | StdDev    | Op/s         | Gen0   | Gen1   | Allocated |
|---------------------------- |--------- |------------:|----------:|----------:|-------------:|-------:|-------:|----------:|
| **CreateAndDisposeScope**       | **1**        |    **67.45 ns** |  **0.841 ns** |  **0.703 ns** | **14,826,300.9** | **0.0291** |      **-** |     **488 B** |
| DisposeChildrenIndividually | 1        |    42.37 ns |  0.829 ns |  0.987 ns | 23,602,124.6 | 0.0196 |      - |     328 B |
| **CreateAndDisposeScope**       | **8**        |   **380.10 ns** |  **6.883 ns** |  **6.438 ns** |  **2,630,910.6** | **0.1526** | **0.0010** |    **2560 B** |
| DisposeChildrenIndividually | 8        |   391.09 ns |  4.925 ns |  4.607 ns |  2,556,979.7 | 0.1464 | 0.0010 |    2456 B |
| **CreateAndDisposeScope**       | **64**       | **3,961.65 ns** | **38.597 ns** | **34.215 ns** |    **252,420.3** | **1.1406** | **0.0687** |   **19136 B** |
| DisposeChildrenIndividually | 64       | 4,581.74 ns | 40.383 ns | 35.799 ns |    218,257.8 | 1.1597 | 0.0610 |   19480 B |

## SizeOfProbe

The shallow size of each node type: the `Allocated` column of an uninitialised instance, which counts the object's own fields and none of the objects they reference. It tracks how a change to a node's fields moves its footprint.

| Method | Type |
| --- | --- |
| `MemoInt` | `Memo<int>` |
| `AsyncMemoInt` | `AsyncMemo<int>` |
| `BoundaryInt` | `Boundary<int>` |
| `Effect` | `Effect` |
| `AsyncSourceInt` | `AsyncSource<int>` |
| `Failure` | `Failure`, the record a failed node holds. |

`SizeOfProbe` has no counter scenario and no published figure yet. It is in the suite for local runs: `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*SizeOfProbe*"`.

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
