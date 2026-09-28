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
| **RecomputeSettledChain** | **1**     |    **18.30 ns** |  **0.232 ns** |  **0.217 ns** | **54,656,258.3** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 1     | 1,460.60 ns | 11.381 ns | 10.089 ns |    684,650.1 | 79.84x slower |   1.06x | 0.0286 |     496 B |          NA |
|                       |       |             |           |           |              |               |         |        |           |             |
| **RecomputeSettledChain** | **4**     |    **64.20 ns** |  **0.850 ns** |  **0.795 ns** | **15,577,452.1** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 4     | 1,517.24 ns | 14.803 ns | 13.847 ns |    659,091.4 | 23.64x slower |   0.35x | 0.0286 |     496 B |          NA |
|                       |       |             |           |           |              |               |         |        |           |             |
| **RecomputeSettledChain** | **16**    |   **235.95 ns** |  **2.367 ns** |  **1.977 ns** |  **4,238,127.7** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| ThrowThroughChain     | 16    | 1,637.23 ns | 17.495 ns | 16.365 ns |    610,786.5 |  6.94x slower |   0.09x | 0.0286 |     496 B |          NA |
