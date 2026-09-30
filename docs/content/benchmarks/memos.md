---
title: Memos
order: 3
---

> **Preview** — Ranvier is pre-release; its APIs may change.

Memos are pull-based: a read runs them, nothing else does. Two costs therefore matter: the cache hit, which every read pays, and a recomputation, which only a read after invalidation pays. See [Benchmarks](index.md) for configuration, environment and caveats.

## MemoBenchmarks

| Method | What it measures |
| --- | --- |
| `CachedRead` | Baseline. An untracked read of a clean memo: close to a field read. |
| `CachedTrackedRead` | The same read, tracked. Nothing is listening, so this adds only the tracking check. |
| `Recompute` | Invalidate, then read: one body run plus re-collection of the memo's dependencies. Tagged `Sentinel`. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Memos.MemoBenchmarks` at commit `d87920f`.

| Method            | Categories    | Mean       | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Allocated | Alloc Ratio |
|------------------ |-------------- |-----------:|----------:|----------:|----------------:|--------------:|--------:|----------:|------------:|
| CachedRead        | Memo          |  0.2169 ns | 0.0152 ns | 0.0142 ns | 4,611,057,105.0 |      baseline |         |         - |          NA |
| CachedTrackedRead | Memo          |  0.4765 ns | 0.0180 ns | 0.0168 ns | 2,098,796,549.2 |  2.21x slower |   0.15x |         - |          NA |
| Recompute         | Sentinel,Memo | 15.5039 ns | 0.2091 ns | 0.1854 ns |    64,499,876.3 | 71.76x slower |   4.44x |         - |          NA |

`CachedRead` is at the resolution limit (see [Reading the results](index.md#reading-the-results)), which is why the ratios in this table carry a large `RatioSD`.

`MemoBenchmarks` also runs each case under every `ThreadAffinity`, set by `Affinity` (`Guarded`, `Unchecked`, `Serialised`), as [`SignalBenchmarks`](signals.md#thread-affinity) does. `Recompute` carries the check twice: on the write and on the stale read. The table above predates the parameter and shows `Guarded` only.

> **Figures pending.** The figures for these cases come with the next instruction-counter run ([`counters.ps1`](counters.md), on Windows, after the merge).

## ChainBenchmarks

A signal followed by a chain of `Depth` memos (1, 4, 16 or 64). This shows how propagation scales with depth.

| Method | What it measures |
| --- | --- |
| `WriteThenReadTail` | One write at the head and one read at the tail. Every memo in the chain is invalidated and recomputed exactly once. Tagged `Sentinel`. |
| `ReadTailClean` | The same chain read without an intervening write. Every memo is clean, so only the tail is touched. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Memos.ChainBenchmarks` at commit `d87920f`.

| Method            | Categories    | Depth | Mean        | Error      | StdDev     | Op/s            | Allocated |
|------------------ |-------------- |------ |------------:|-----------:|-----------:|----------------:|----------:|
| **ReadTailClean**     | **Memo**          | **1**     |   **0.5058 ns** |  **0.0283 ns** |  **0.0278 ns** | **1,977,109,044.1** |         **-** |
| **ReadTailClean**     | **Memo**          | **4**     |   **0.5149 ns** |  **0.0291 ns** |  **0.0272 ns** | **1,942,057,188.8** |         **-** |
| **ReadTailClean**     | **Memo**          | **16**    |   **0.5114 ns** |  **0.0285 ns** |  **0.0317 ns** | **1,955,597,348.4** |         **-** |
| **ReadTailClean**     | **Memo**          | **64**    |   **0.5106 ns** |  **0.0283 ns** |  **0.0303 ns** | **1,958,494,380.5** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **1**     |  **16.2286 ns** |  **0.3274 ns** |  **0.4021 ns** |    **61,619,470.0** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **4**     |  **64.5471 ns** |  **1.3097 ns** |  **2.2591 ns** |    **15,492,567.6** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **16**    | **253.4166 ns** |  **3.1584 ns** |  **2.9544 ns** |     **3,946,072.1** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **64**    | **998.7070 ns** | **17.5727 ns** | **16.4375 ns** |     **1,001,294.6** |         **-** |

## DiamondBenchmarks

Two paths from one source reconverging on one reader. The shape exists to catch an implementation that recomputes the joining node twice per write, or pairs a value from before the write with one from after it.

| Method | What it measures |
| --- | --- |
| `WriteThenRead` | One write to the source followed by one read of the joining node. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Memos.DiamondBenchmarks` at commit `d87920f`.

| Method        | Mean     | Error    | StdDev   | Op/s         | Allocated |
|-------------- |---------:|---------:|---------:|-------------:|----------:|
| WriteThenRead | 45.98 ns | 0.930 ns | 1.142 ns | 21,747,537.4 |         - |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
