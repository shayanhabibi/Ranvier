```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Suspension  

```
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
