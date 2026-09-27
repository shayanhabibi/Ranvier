```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime  

```
| Method                      | Children | Mean        | Error     | StdDev     | Op/s         | Gen0   | Gen1   | Allocated |
|---------------------------- |--------- |------------:|----------:|-----------:|-------------:|-------:|-------:|----------:|
| **CreateAndDisposeScope**       | **1**        |    **68.78 ns** |  **1.386 ns** |   **2.198 ns** | **14,538,773.0** | **0.0291** |      **-** |     **488 B** |
| DisposeChildrenIndividually | 1        |    44.77 ns |  0.909 ns |   1.856 ns | 22,338,086.5 | 0.0196 |      - |     328 B |
| **CreateAndDisposeScope**       | **8**        |   **392.76 ns** |  **7.645 ns** |   **9.669 ns** |  **2,546,069.9** | **0.1526** | **0.0010** |    **2560 B** |
| DisposeChildrenIndividually | 8        |   414.40 ns |  8.111 ns |   7.587 ns |  2,413,135.7 | 0.1464 | 0.0010 |    2456 B |
| **CreateAndDisposeScope**       | **64**       | **4,117.77 ns** | **73.174 ns** |  **64.867 ns** |    **242,850.0** | **1.1406** | **0.0687** |   **19136 B** |
| DisposeChildrenIndividually | 64       | 4,886.42 ns | 96.535 ns | 122.086 ns |    204,648.7 | 1.1597 | 0.0610 |   19480 B |
