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

## FlightPolicyBenchmarks

The price of cancellation per async-memo flight. Each iteration writes a trigger the body reads, then reads the memo, which launches one flight. The body ignores its token, so the cancel runs no callbacks. `Flight` is `Completed` when the body returns a completed task, and `Superseded` when it returns a task that never completes, so every launch supersedes a flight in progress.

| Method | What it measures |
| --- | --- |
| `KeepLatest` | Baseline. A launch under `FlightPolicy.KeepLatest`, where every flight shares the token source allocated at the first flight. |
| `CancelPrevious` | A launch under `FlightPolicy.CancelPrevious`, which cancels and disposes the previous flight's token source and allocates a new one. |

Not yet recorded.

## FlightBenchmarks

Repeated flight launches under each `FlightPolicy`, set by `Policy`. `Writes` applies to `WritesDuringFlight` only.

| Method | What it measures |
| --- | --- |
| `RelaunchSettled` | Baseline. Writes a trigger the body reads and reads the memo. The body returns a completed task, so every flight settles on launch. |
| `WritesDuringFlight` | Starts a flight whose task stays open, writes and reads `Writes` more times, then completes every open task, reading after each round. Under `FinishCurrent` the body runs twice whatever `Writes` is; under the other policies it runs `Writes + 1` times. |

The counter scenario `flight` runs the same shape at one write during a flight, once per policy: two writes and reads, then a settle of every flight the writes started. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Policy | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| `CancelPrevious` | 7,520 | 1,720 | 0 |
| `KeepLatest` | 7,348 | 1,672 | 0 |
| `Queue` | 15,065 | 3,184 | 0 |
| `FinishCurrent` | 7,655 | 1,744 | 0 |

`CancelPrevious`, `KeepLatest` and `FinishCurrent` cost the same within about 4 %. `Queue` costs about twice as much in instructions and bytes. The timing cases are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*FlightBenchmarks*"`) and have no published figure yet. The full report is [`e13f159.md`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## FailureBenchmarks

The error channel's cost on a recomputation. Each iteration writes a trigger the memo reads, so every read re-runs the body.

| Method | What it measures |
| --- | --- |
| `SucceedingRecompute` | Baseline. Re-running a memo whose body succeeds. |
| `FailingRecompute` | Re-running a memo whose body throws a fresh exception. The failure records the memo as its origin. |

The counter scenario `fail-recompute` measures the same recomputation with one reader, then reads the reader's `ErrorOrigin`. Figures are retired instructions per operation at commit `e13f159`, .NET 10 with tiered compilation and PGO off (see [Instruction counts](counters.md)):

| Variant | instr/op | bytes/op | Library counters/op |
| --- | ---: | ---: | --- |
| Succeeding | 1,131 | 0 | MemoRecomputes 2 |
| Failing | 62,686 | 1,576 | MemoRecomputes 2 |

A failing recompute costs about 55 times a succeeding one, and the figure includes throwing a fresh .NET exception. The timing cases are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*FailureBenchmarks*"`) and have no published figure yet. The full report is [`e13f159.md`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## FailureChainBenchmarks

A failure's cost per reader: a memo whose body throws a fresh exception, read through `Depth` memos (1, 4 or 16) that each read the one before with `Value`. Each reader adopts the upstream failure and its origin.

| Method | What it measures |
| --- | --- |
| `FailingRecomputeOneHop` | Re-running the failing memo and every reader on the path, then reading the last reader's failure. |

The failing row of `fail-recompute` above measures `Depth` 1. `Depth` 4 and 16 are in the suite for local runs (`dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*FailureChainBenchmarks*"`) and have no published figure yet.

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
