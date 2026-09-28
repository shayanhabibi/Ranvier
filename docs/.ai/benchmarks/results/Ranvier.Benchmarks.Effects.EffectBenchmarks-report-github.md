```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Effect  

```
| Method           | Effects | Mean        | Error     | StdDev    | Op/s         | Allocated |
|----------------- |-------- |------------:|----------:|----------:|-------------:|----------:|
| **WriteAndFlush**    | **1**       |    **17.81 ns** |  **0.184 ns** |  **0.154 ns** | **56,133,012.1** |         **-** |
| BatchOfTenWrites | 1       |    36.89 ns |  0.311 ns |  0.276 ns | 27,104,757.4 |         - |
| **WriteAndFlush**    | **8**       |   **114.76 ns** |  **1.007 ns** |  **0.841 ns** |  **8,713,660.1** |         **-** |
| BatchOfTenWrites | 8       |   179.76 ns |  2.292 ns |  2.032 ns |  5,562,912.3 |         - |
| **WriteAndFlush**    | **64**      |   **835.32 ns** |  **6.463 ns** |  **5.729 ns** |  **1,197,153.0** |         **-** |
| BatchOfTenWrites | 64      | 1,298.42 ns | 13.692 ns | 12.138 ns |    770,167.6 |         - |
