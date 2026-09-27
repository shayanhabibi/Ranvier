---
title: Signals
order: 2
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

The write path is the most frequently executed path in the library. These benchmarks measure reads, writes and the equality cutoff on a single signal. See [Benchmarks](index.md) for configuration, environment and caveats.

## SignalBenchmarks

One `int` signal observed by `Observers` memos (0, 1, 8 or 64).

| Method | What it measures |
| --- | --- |
| `Read` | A tracked read with no computation running: the read an outside caller makes. |
| `Peek` | An untracked read. Compared with `Read`, it shows the cost of the tracking check. |
| `Write` | A write that changes the value, so every observer is notified. With 0 observers it is the floor for a write. |
| `WriteCutoff` | A write of an equal value. The equality cutoff stops it, so nothing downstream is touched. |
| `WriteAndPropagate` | A write followed by reading every observer back, so it includes the recomputations the write caused. The difference from `Write` is the cost of propagation. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Signals.SignalBenchmarks` at commit `915f139`.

| Method            | Observers | Mean       | Error     | StdDev    | Op/s            | Allocated |
|------------------ |---------- |-----------:|----------:|----------:|----------------:|----------:|
| **Read**              | **0**         |  **0.2360 ns** | **0.0230 ns** | **0.0215 ns** | **4,236,740,472.4** |         **-** |
| Peek              | 0         |  0.2102 ns | 0.0232 ns | 0.0267 ns | 4,758,464,587.4 |         - |
| Write             | 0         |  1.7145 ns | 0.0415 ns | 0.0388 ns |   583,272,773.1 |         - |
| WriteCutoff       | 0         |  1.0782 ns | 0.0408 ns | 0.0671 ns |   927,483,605.9 |         - |
| WriteAndPropagate | 0         |  1.8739 ns | 0.0544 ns | 0.0668 ns |   533,642,645.7 |         - |
| **Read**              | **1**         |  **0.2542 ns** | **0.0199 ns** | **0.0176 ns** | **3,933,289,009.8** |         **-** |
| Peek              | 1         |  0.2157 ns | 0.0225 ns | 0.0211 ns | 4,636,744,316.1 |         - |
| Write             | 1         |  2.2538 ns | 0.0484 ns | 0.0453 ns |   443,700,542.4 |         - |
| WriteCutoff       | 1         |  1.1435 ns | 0.0421 ns | 0.0790 ns |   874,533,143.6 |         - |
| WriteAndPropagate | 1         |  2.6495 ns | 0.0721 ns | 0.0801 ns |   377,435,727.7 |         - |
| **Read**              | **8**         |  **0.2080 ns** | **0.0248 ns** | **0.0305 ns** | **4,808,508,483.3** |         **-** |
| Peek              | 8         |  0.2118 ns | 0.0224 ns | 0.0220 ns | 4,720,611,110.0 |         - |
| Write             | 8         |  6.2121 ns | 0.0741 ns | 0.0619 ns |   160,977,246.9 |         - |
| WriteCutoff       | 8         |  1.0842 ns | 0.0395 ns | 0.0514 ns |   922,379,131.8 |         - |
| WriteAndPropagate | 8         |  8.2701 ns | 0.1555 ns | 0.1454 ns |   120,917,858.3 |         - |
| **Read**              | **64**        |  **0.2541 ns** | **0.0217 ns** | **0.0203 ns** | **3,935,837,740.6** |         **-** |
| Peek              | 64        |  0.2207 ns | 0.0199 ns | 0.0187 ns | 4,531,427,579.2 |         - |
| Write             | 64        | 36.6354 ns | 0.7126 ns | 0.7920 ns |    27,295,984.2 |         - |
| WriteCutoff       | 64        |  1.0671 ns | 0.0394 ns | 0.0589 ns |   937,118,984.8 |         - |
| WriteAndPropagate | 64        | 51.8734 ns | 0.6198 ns | 0.5798 ns |    19,277,689.2 |         - |

## EqualityBenchmarks

A reference-typed signal written with a cutoff by identity (the default) or by structural comparison (`StructuralPolicy`). The gap between the rows is the cost of choosing structural equality.

| Method | What it measures |
| --- | --- |
| `IdentityCutoff` | Baseline. A write stopped by identity comparison. |
| `StructuralCutoff` | A write of a structurally equal value, stopped by a deep comparison. |
| `StructuralWrite` | A write of a structurally different value under `StructuralPolicy`. |

Recorded 2026-09-27. Source: `Partas.Signals.Benchmarks.Signals.EqualityBenchmarks` at commit `915f139`.

| Method           | Mean     | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| IdentityCutoff   | 1.472 ns | 0.0469 ns | 0.0460 ns | 679,563,960.0 |     baseline |         |      - |         - |          NA |
| StructuralCutoff | 6.124 ns | 0.1253 ns | 0.1172 ns | 163,290,276.0 | 4.17x slower |   0.15x |      - |         - |          NA |
| StructuralWrite  | 6.580 ns | 0.1006 ns | 0.0941 ns | 151,980,317.1 | 4.48x slower |   0.15x | 0.0019 |      32 B |          NA |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
