---
title: Memos
order: 3
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

Memos are pull-based: a read runs them, nothing else does. Two costs therefore matter: the cache hit, which every read pays, and a recomputation, which only a read after invalidation pays. See [Benchmarks](index.md) for configuration, environment and caveats.

## MemoBenchmarks

| Method | What it measures |
| --- | --- |
| `CachedRead` | Baseline. An untracked read of a clean memo: close to a field read. |
| `CachedTrackedRead` | The same read, tracked. Nothing is listening, so this adds only the tracking check. |
| `Recompute` | Invalidate, then read: one body run plus re-collection of the memo's dependencies. Tagged `Sentinel`. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Memos.MemoBenchmarks` at commit `915f139`.

| Method            | Categories    | Mean       | Error     | StdDev    | Median     | Op/s            | Ratio         | RatioSD | Allocated | Alloc Ratio |
|------------------ |-------------- |-----------:|----------:|----------:|-----------:|----------------:|--------------:|--------:|----------:|------------:|
| CachedRead        | Memo          |  0.3191 ns | 0.0779 ns | 0.2298 ns |  0.1839 ns | 3,134,023,334.9 |      baseline |         |         - |          NA |
| CachedTrackedRead | Memo          |  0.8832 ns | 0.0971 ns | 0.2864 ns |  0.8930 ns | 1,132,267,919.4 |  4.45x slower |   3.12x |         - |          NA |
| Recompute         | Sentinel,Memo | 15.5268 ns | 0.3181 ns | 0.8380 ns | 15.3624 ns |    64,404,836.9 | 78.23x slower |  46.60x |         - |          NA |

`CachedRead` is at the resolution limit (see [Reading the results](index.md#reading-the-results)), which is why the ratios in this table carry a large `RatioSD`.

## ChainBenchmarks

A signal followed by a chain of `Depth` memos (1, 4, 16 or 64). This shows how propagation scales with depth.

| Method | What it measures |
| --- | --- |
| `WriteThenReadTail` | One write at the head and one read at the tail. Every memo in the chain is invalidated and recomputed exactly once. Tagged `Sentinel`. |
| `ReadTailClean` | The same chain read without an intervening write. Every memo is clean, so only the tail is touched. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Memos.ChainBenchmarks` at commit `915f139`.

| Method            | Categories    | Depth | Mean        | Error     | StdDev    | Op/s            | Allocated |
|------------------ |-------------- |------ |------------:|----------:|----------:|----------------:|----------:|
| **ReadTailClean**     | **Memo**          | **1**     |   **0.4579 ns** | **0.0126 ns** | **0.0118 ns** | **2,184,046,686.2** |         **-** |
| **ReadTailClean**     | **Memo**          | **4**     |   **0.4567 ns** | **0.0275 ns** | **0.0258 ns** | **2,189,440,152.4** |         **-** |
| **ReadTailClean**     | **Memo**          | **16**    |   **0.4257 ns** | **0.0080 ns** | **0.0075 ns** | **2,349,219,111.8** |         **-** |
| **ReadTailClean**     | **Memo**          | **64**    |   **0.4318 ns** | **0.0055 ns** | **0.0052 ns** | **2,315,634,863.0** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **1**     |  **13.8481 ns** | **0.0794 ns** | **0.0743 ns** |    **72,211,955.8** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **4**     |  **55.4999 ns** | **0.3941 ns** | **0.3687 ns** |    **18,018,042.1** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **16**    | **220.3286 ns** | **1.0517 ns** | **0.9837 ns** |     **4,538,674.7** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **64**    | **933.3284 ns** | **5.0128 ns** | **4.6890 ns** |     **1,071,434.3** |         **-** |

## DiamondBenchmarks

Two paths from one source reconverging on one reader. The shape exists to catch an implementation that recomputes the joining node twice per write, or pairs a value from before the write with one from after it.

| Method | What it measures |
| --- | --- |
| `WriteThenRead` | One write to the source followed by one read of the joining node. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Memos.DiamondBenchmarks` at commit `915f139`.

| Method        | Mean     | Error    | StdDev   | Op/s         | Allocated |
|-------------- |---------:|---------:|---------:|-------------:|----------:|
| WriteThenRead | 47.02 ns | 0.930 ns | 1.209 ns | 21,268,150.4 |         - |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
