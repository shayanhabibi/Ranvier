---
title: Signals
order: 2
---

> **Preview** — Ranvier is pre-release; its APIs may change.

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

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Signals.SignalBenchmarks` at commit `d87920f`.

| Method            | Observers | Mean       | Error     | StdDev    | Op/s            | Allocated |
|------------------ |---------- |-----------:|----------:|----------:|----------------:|----------:|
| **Read**              | **0**         |  **0.2294 ns** | **0.0173 ns** | **0.0153 ns** | **4,358,414,858.0** |         **-** |
| Peek              | 0         |  0.2074 ns | 0.0066 ns | 0.0058 ns | 4,822,342,533.3 |         - |
| Write             | 0         |  1.6874 ns | 0.0293 ns | 0.0245 ns |   592,630,946.9 |         - |
| WriteCutoff       | 0         |  1.0102 ns | 0.0246 ns | 0.0231 ns |   989,944,159.6 |         - |
| WriteAndPropagate | 0         |  1.8208 ns | 0.0289 ns | 0.0256 ns |   549,214,973.6 |         - |
| **Read**              | **1**         |  **0.2466 ns** | **0.0235 ns** | **0.0220 ns** | **4,054,727,400.7** |         **-** |
| Peek              | 1         |  0.2018 ns | 0.0052 ns | 0.0043 ns | 4,954,368,076.7 |         - |
| Write             | 1         |  2.1688 ns | 0.0399 ns | 0.0354 ns |   461,088,285.7 |         - |
| WriteCutoff       | 1         |  1.0125 ns | 0.0307 ns | 0.0272 ns |   987,618,863.1 |         - |
| WriteAndPropagate | 1         |  3.0830 ns | 0.0128 ns | 0.0107 ns |   324,360,362.2 |         - |
| **Read**              | **8**         |  **0.2270 ns** | **0.0080 ns** | **0.0075 ns** | **4,404,951,053.6** |         **-** |
| Peek              | 8         |  0.2131 ns | 0.0111 ns | 0.0104 ns | 4,692,999,479.0 |         - |
| Write             | 8         |  6.3612 ns | 0.0702 ns | 0.0657 ns |   157,203,455.5 |         - |
| WriteCutoff       | 8         |  1.0141 ns | 0.0253 ns | 0.0224 ns |   986,111,038.4 |         - |
| WriteAndPropagate | 8         |  7.9569 ns | 0.1068 ns | 0.0947 ns |   125,676,794.1 |         - |
| **Read**              | **64**        |  **0.2227 ns** | **0.0081 ns** | **0.0072 ns** | **4,490,411,024.6** |         **-** |
| Peek              | 64        |  0.2069 ns | 0.0068 ns | 0.0060 ns | 4,832,182,570.0 |         - |
| Write             | 64        | 47.8819 ns | 0.3217 ns | 0.2686 ns |    20,884,713.1 |         - |
| WriteCutoff       | 64        |  1.0090 ns | 0.0231 ns | 0.0193 ns |   991,112,303.3 |         - |
| WriteAndPropagate | 64        | 57.4765 ns | 0.3814 ns | 0.3381 ns |    17,398,409.8 |         - |

## Thread affinity

`SignalBenchmarks` runs each case under every `ThreadAffinity`, set by `Affinity`:

| `Affinity` | Check on a write's entry |
| --- | --- |
| `Guarded` | The default. A thread-id comparison against the graph's owner thread. |
| `Unchecked` | None. |
| `Serialised` | Entry into the graph by the calling thread, which must run on the synchronisation context the graph was constructed on. A second thread entering at once raises. |

The table above predates the parameter and shows `Guarded` only.

> **Figures pending.** The figures for these cases come with the next instruction-counter run ([`counters.ps1`](counters.md), on Windows, after the merge).

The counter scenario `chain-affinity` writes the head of a four-memo chain and reads the tail under each affinity.

## EqualityBenchmarks

A reference-typed signal written with a cutoff by identity (the default) or by structural comparison (`StructuralPolicy`). The gap between the rows is the cost of choosing structural equality.

| Method | What it measures |
| --- | --- |
| `IdentityCutoff` | Baseline. A write stopped by identity comparison. |
| `StructuralCutoff` | A write of a structurally equal value, stopped by a deep comparison. |
| `StructuralWrite` | A write of a structurally different value under `StructuralPolicy`. |

Recorded 2026-09-28. Source: `Ranvier.Benchmarks.Signals.EqualityBenchmarks` at commit `d87920f`.

| Method           | Mean     | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| IdentityCutoff   | 1.391 ns | 0.0202 ns | 0.0179 ns | 718,789,638.3 |     baseline |         |      - |         - |          NA |
| StructuralCutoff | 5.743 ns | 0.1000 ns | 0.0886 ns | 174,110,032.6 | 4.13x slower |   0.08x |      - |         - |          NA |
| StructuralWrite  | 6.208 ns | 0.0609 ns | 0.0540 ns | 161,091,273.4 | 4.46x slower |   0.07x | 0.0019 |      32 B |          NA |

The `Ratio` and `Alloc Ratio` columns, where present, compare each method with the baseline method *of the same class* on the same run. They describe the relative cost of two operations inside Ranvier, not a comparison with any other library.
