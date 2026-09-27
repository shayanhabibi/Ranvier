---
title: Lifetimes
order: 5
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

Construction and teardown are not the hottest path, but they are the path a UI takes on every mount and unmount, and the one where an owner that only grows would show up as a leak rather than as a slowdown. See [Benchmarks](index.md) for configuration, environment and caveats.

## ConstructionBenchmarks

| Method | What it measures |
| --- | --- |
| `CreateSignal` | Baseline. Constructing a signal. |
| `CreateAndDisposeMemo` | Constructing and disposing a memo that reads nothing. |
| `CreateAndDisposeEffect` | Constructing and disposing an effect that reads nothing. |

There is deliberately no create-without-dispose case for memos. A memo attaches itself to its enclosing owner, so one that is never disposed is retained for the life of the graph by design; measuring that would measure garbage collection over a growing child list rather than construction.

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Lifetimes.ConstructionBenchmarks` at commit `915f139`.

| Method                 | Mean      | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| CreateSignal           |  8.931 ns | 0.2132 ns | 0.3443 ns | 111,971,160.8 |     baseline |         | 0.0053 |      88 B |             |
| CreateAndDisposeMemo   | 26.369 ns | 0.5240 ns | 0.5147 ns |  37,923,903.8 | 2.96x slower |   0.12x | 0.0148 |     248 B |  2.82x more |
| CreateAndDisposeEffect | 35.086 ns | 0.7057 ns | 0.8127 ns |  28,501,082.4 | 3.93x slower |   0.17x | 0.0114 |     192 B |  2.18x more |

## ReadingNodeBenchmarks

The same lifecycle for nodes that read a source, so the dependency edge is part of the cost: linked on the first run, unlinked on disposal.

| Method | What it measures |
| --- | --- |
| `CreateAndDisposeReadingEffect` | An effect that reads one signal, created and disposed. |
| `CreateAndDisposeReadingMemo` | A memo that reads one signal, created and disposed. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Lifetimes.ReadingNodeBenchmarks` at commit `915f139`.

| Method                        | Mean     | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|------------------------------ |---------:|---------:|---------:|-------------:|-------:|----------:|
| CreateAndDisposeReadingEffect | 42.33 ns | 0.830 ns | 0.853 ns | 23,626,268.4 | 0.0129 |     216 B |
| CreateAndDisposeReadingMemo   | 41.52 ns | 0.817 ns | 1.342 ns | 24,083,945.7 | 0.0162 |     272 B |

## FanOutLifecycleBenchmarks

`Nodes` computations (1, 64 or 1024) on one source, created in a scope and torn down with it. The larger cases cross the observer list's indexing threshold, so they include building and draining that index. Divide by `Nodes` for the cost per node.

| Method | What it measures |
| --- | --- |
| `EffectsOnOneSource` | `Nodes` effects reading one signal, created and disposed as a unit. |
| `MemosOnOneSource` | `Nodes` memos reading one signal, created and disposed as a unit. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Lifetimes.FanOutLifecycleBenchmarks` at commit `915f139`.

| Method             | Nodes | Mean         | Error        | StdDev       | Op/s         | Gen0    | Gen1   | Allocated |
|------------------- |------ |-------------:|-------------:|-------------:|-------------:|--------:|-------:|----------:|
| **EffectsOnOneSource** | **1**     |     **71.68 ns** |     **1.402 ns** |     **1.920 ns** | **13,950,168.6** |  **0.0257** |      **-** |     **432 B** |
| MemosOnOneSource   | 1     |     71.63 ns |     1.430 ns |     2.428 ns | 13,961,543.6 |  0.0291 |      - |     488 B |
| **EffectsOnOneSource** | **64**    |  **4,275.07 ns** |    **84.716 ns** |   **192.941 ns** |    **233,914.4** |  **0.9232** | **0.0458** |   **15552 B** |
| MemosOnOneSource   | 64    |  4,055.30 ns |    65.971 ns |    58.482 ns |    246,591.0 |  1.1406 | 0.0687 |   19136 B |
| **EffectsOnOneSource** | **1024**  | **74,711.60 ns** | **1,500.925 ns** | **4,233.385 ns** |     **13,384.8** | **14.6484** | **6.7139** |  **245952 B** |
| MemosOnOneSource   | 1024  | 69,733.96 ns | 1,364.669 ns | 1,867.975 ns |     14,340.2 | 18.0664 | 9.2773 |  303296 B |

## ScopeBenchmarks

A scope with `Children` children (1, 8 or 64): the shape a component mount takes.

| Method | What it measures |
| --- | --- |
| `CreateAndDisposeScope` | The scope and its children, created and torn down as a unit. |
| `DisposeChildrenIndividually` | The same children, each disposed on its own rather than through the scope. This checks that an individually disposed child leaves its owner's child list. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Lifetimes.ScopeBenchmarks` at commit `915f139`.

| Method                      | Children | Mean        | Error     | StdDev     | Op/s         | Gen0   | Gen1   | Allocated |
|---------------------------- |--------- |------------:|----------:|-----------:|-------------:|-------:|-------:|----------:|
| **CreateAndDisposeScope**       | **1**        |    **68.78 ns** |  **1.386 ns** |   **2.198 ns** | **14,538,773.0** | **0.0291** |      **-** |     **488 B** |
| DisposeChildrenIndividually | 1        |    44.77 ns |  0.909 ns |   1.856 ns | 22,338,086.5 | 0.0196 |      - |     328 B |
| **CreateAndDisposeScope**       | **8**        |   **392.76 ns** |  **7.645 ns** |   **9.669 ns** |  **2,546,069.9** | **0.1526** | **0.0010** |    **2560 B** |
| DisposeChildrenIndividually | 8        |   414.40 ns |  8.111 ns |   7.587 ns |  2,413,135.7 | 0.1464 | 0.0010 |    2456 B |
| **CreateAndDisposeScope**       | **64**       | **4,117.77 ns** | **73.174 ns** |  **64.867 ns** |    **242,850.0** | **1.1406** | **0.0687** |   **19136 B** |
| DisposeChildrenIndividually | 64       | 4,886.42 ns | 96.535 ns | 122.086 ns |    204,648.7 | 1.1597 | 0.0610 |   19480 B |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
