```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime  

```
| Method                      | Children | Mean        | Error     | StdDev    | Op/s         | Gen0   | Gen1   | Allocated |
|---------------------------- |--------- |------------:|----------:|----------:|-------------:|-------:|-------:|----------:|
| **CreateAndDisposeScope**       | **1**        |    **67.45 ns** |  **0.841 ns** |  **0.703 ns** | **14,826,300.9** | **0.0291** |      **-** |     **488 B** |
| DisposeChildrenIndividually | 1        |    42.37 ns |  0.829 ns |  0.987 ns | 23,602,124.6 | 0.0196 |      - |     328 B |
| **CreateAndDisposeScope**       | **8**        |   **380.10 ns** |  **6.883 ns** |  **6.438 ns** |  **2,630,910.6** | **0.1526** | **0.0010** |    **2560 B** |
| DisposeChildrenIndividually | 8        |   391.09 ns |  4.925 ns |  4.607 ns |  2,556,979.7 | 0.1464 | 0.0010 |    2456 B |
| **CreateAndDisposeScope**       | **64**       | **3,961.65 ns** | **38.597 ns** | **34.215 ns** |    **252,420.3** | **1.1406** | **0.0687** |   **19136 B** |
| DisposeChildrenIndividually | 64       | 4,581.74 ns | 40.383 ns | 35.799 ns |    218,257.8 | 1.1597 | 0.0610 |   19480 B |
