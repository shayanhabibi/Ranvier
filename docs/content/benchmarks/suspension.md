---
title: Suspension
order: 7
---

> **Preview** — Ranvier is pre-release; its APIs may change.

The pending channel is what lets a computation read an async source that has no value yet. On .NET, a transparent read of a Pending source aborts the reading body by throwing, and an exception is the most expensive single operation in the design. These benchmarks measure what that costs and how a boundary limits it. See [Benchmarks](index.md) for configuration, environment and caveats.

## SuspensionBenchmarks

A chain of `Depth` frames (1, 4 or 16) between the suspending read and the reader. Each iteration writes a trigger that the chain's first memo reads, so every read re-runs the chain.

| Method | What it measures |
| --- | --- |
| `RecomputeSettledChain` | Baseline. Re-running the chain to completion over a settled source. |
| `ThrowThroughChain` | Re-running the chain and aborting it on a source that never settles. The difference from the baseline is the price of the throw. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Suspension.SuspensionBenchmarks` at commit `d87920f`.

| Method                | Depth | Mean        | Error     | StdDev    | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |------ |------------:|----------:|----------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| **RecomputeSettledChain** | **1**     |    **18.30 ns** |  **0.232 ns** |  **0.217 ns** | **54,656,258.3** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 1     | 1,460.60 ns | 11.381 ns | 10.089 ns |    684,650.1 | 79.84x slower |   1.06x | 0.0286 |     496 B |          NA |
|                       |       |             |           |           |              |               |         |        |           |             |
| **RecomputeSettledChain** | **4**     |    **64.20 ns** |  **0.850 ns** |  **0.795 ns** | **15,577,452.1** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 4     | 1,517.24 ns | 14.803 ns | 13.847 ns |    659,091.4 | 23.64x slower |   0.35x | 0.0286 |     496 B |          NA |
|                       |       |             |           |           |              |               |         |        |           |             |
| **RecomputeSettledChain** | **16**    |   **235.95 ns** |  **2.367 ns** |  **1.977 ns** |  **4,238,127.7** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 16    | 1,637.23 ns | 17.495 ns | 16.365 ns |    610,786.5 |  6.94x slower |   0.09x | 0.0286 |     496 B |          NA |

## BoundaryBenchmarks

A boundary catches the pending channel instead of letting it propagate, so the cost of a suspended subtree is bounded by where the boundary sits rather than by the depth of the whole graph. Each iteration writes a trigger both bodies read, so every read re-runs the body.

| Method | What it measures |
| --- | --- |
| `CatchingPending` | Re-running a body that is still waiting, with the boundary catching its throw. |
| `CleanBoundary` | Baseline. Re-running the body over settled sources: the price of a boundary when nothing is in flight. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Suspension.BoundaryBenchmarks` at commit `d87920f`.

| Method          | Mean      | Error     | StdDev    | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |----------:|----------:|----------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| CatchingPending | 979.54 ns | 15.940 ns | 14.910 ns |  1,020,886.0 | 40.76x slower |   0.95x | 0.0153 |     256 B |  8.00x more |
| CleanBoundary   |  24.04 ns |  0.450 ns |  0.442 ns | 41,600,534.4 |      baseline |         | 0.0019 |      32 B |             |

## SettleBenchmarks

Settling a source is where a pending subtree becomes live, and it is the only path in the library that can arrive from another thread.

| Method | What it measures |
| --- | --- |
| `CreateAndRead` | Baseline. Constructing an `AsyncSource` and reading it: the floor for `SettleInline`. |
| `SettleInline` | Constructing a source and settling it on the graph's own thread, which takes the inline path and never touches the dispatcher. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Suspension.SettleBenchmarks` at commit `d87920f`.

| Method        | Mean      | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------- |----------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| SettleInline  | 10.240 ns | 0.2031 ns | 0.1696 ns |  97,652,054.4 | 1.90x slower |   0.04x | 0.0076 |     128 B |  1.33x more |
| CreateAndRead |  5.402 ns | 0.0920 ns | 0.0816 ns | 185,130,337.5 |     baseline |         | 0.0057 |      96 B |             |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
