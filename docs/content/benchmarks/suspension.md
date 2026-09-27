---
title: Suspension
order: 7
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

The pending channel is what lets a computation read an async source that has no value yet. On .NET, a transparent read of a Pending source aborts the reading body by throwing, and an exception is the most expensive single operation in the design. These benchmarks measure what that costs and how a boundary limits it. See [Benchmarks](index.md) for configuration, environment and caveats.

## SuspensionBenchmarks

A chain of `Depth` frames (1, 4 or 16) between the suspending read and the reader. Each iteration writes a trigger that the chain's first memo reads, so every read re-runs the chain.

| Method | What it measures |
| --- | --- |
| `RecomputeSettledChain` | Baseline. Re-running the chain to completion over a settled source. |
| `ThrowThroughChain` | Re-running the chain and aborting it on a source that never settles. The difference from the baseline is the price of the throw. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Suspension.SuspensionBenchmarks` at commit `915f139`.

| Method                | Depth | Mean        | Error     | StdDev    | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |------ |------------:|----------:|----------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| **RecomputeSettledChain** | **1**     |    **17.59 ns** |  **0.360 ns** |  **0.505 ns** | **56,848,034.8** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 1     | 1,507.32 ns | 29.524 ns | 26.172 ns |    663,431.3 | 85.76x slower |   2.82x | 0.0286 |     496 B |          NA |
|                       |       |             |           |           |              |               |         |        |           |             |
| **RecomputeSettledChain** | **4**     |    **65.86 ns** |  **1.333 ns** |  **2.631 ns** | **15,183,107.9** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 4     | 1,577.05 ns | 21.353 ns | 18.929 ns |    634,095.5 | 23.98x slower |   0.97x | 0.0286 |     496 B |          NA |
|                       |       |             |           |           |              |               |         |        |           |             |
| **RecomputeSettledChain** | **16**    |   **245.70 ns** |  **2.858 ns** |  **2.534 ns** |  **4,070,071.6** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 16    | 1,694.59 ns | 33.276 ns | 36.987 ns |    590,111.8 |  6.90x slower |   0.16x | 0.0286 |     496 B |          NA |

## BoundaryBenchmarks

A boundary catches the pending channel instead of letting it propagate, so the cost of a suspended subtree is bounded by where the boundary sits rather than by the depth of the whole graph. Each iteration writes a trigger both bodies read, so every read re-runs the body.

| Method | What it measures |
| --- | --- |
| `CatchingPending` | Re-running a body that is still waiting, with the boundary catching its throw. |
| `CleanBoundary` | Baseline. Re-running the body over settled sources: the price of a boundary when nothing is in flight. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Suspension.BoundaryBenchmarks` at commit `915f139`.

| Method          | Mean      | Error     | StdDev   | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |----------:|----------:|---------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| CatchingPending | 977.97 ns | 11.622 ns | 9.705 ns |  1,022,521.6 | 41.19x slower |   1.30x | 0.0143 |     248 B | 10.33x more |
| CleanBoundary   |  23.77 ns |  0.486 ns | 0.727 ns | 42,076,656.2 |      baseline |         | 0.0014 |      24 B |             |

## SettleBenchmarks

Settling a source is where a pending subtree becomes live, and it is the only path in the library that can arrive from another thread.

| Method | What it measures |
| --- | --- |
| `CreateAndRead` | Baseline. Constructing an `AsyncSource` and reading it: the floor for `SettleInline`. |
| `SettleInline` | Constructing a source and settling it on the graph's own thread, which takes the inline path and never touches the dispatcher. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Suspension.SettleBenchmarks` at commit `915f139`.

| Method        | Mean      | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------- |----------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| SettleInline  | 11.153 ns | 0.2184 ns | 0.3335 ns |  89,664,815.4 | 1.84x slower |   0.10x | 0.0076 |     128 B |  1.33x more |
| CreateAndRead |  6.073 ns | 0.1401 ns | 0.2699 ns | 164,668,476.9 |     baseline |         | 0.0057 |      96 B |             |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
