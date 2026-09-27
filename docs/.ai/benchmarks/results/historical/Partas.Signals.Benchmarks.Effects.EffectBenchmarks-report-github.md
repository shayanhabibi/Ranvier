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
| **WriteAndFlush**    | **1**       |    **17.54 ns** |  **0.363 ns** |  **0.388 ns** | **57,026,644.5** |         **-** |
| BatchOfTenWrites | 1       |    39.58 ns |  0.643 ns |  0.602 ns | 25,262,805.0 |         - |
| **WriteAndFlush**    | **8**       |   **115.85 ns** |  **0.915 ns** |  **0.856 ns** |  **8,631,846.5** |         **-** |
| BatchOfTenWrites | 8       |   193.82 ns |  3.861 ns |  3.612 ns |  5,159,450.9 |         - |
| **WriteAndFlush**    | **64**      |   **863.59 ns** | **14.831 ns** | **13.148 ns** |  **1,157,963.5** |         **-** |
| BatchOfTenWrites | 64      | 1,345.23 ns | 26.842 ns | 37.629 ns |    743,367.6 |         - |
