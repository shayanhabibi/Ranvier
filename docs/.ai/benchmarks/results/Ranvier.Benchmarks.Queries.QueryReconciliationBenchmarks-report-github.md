```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Query  

```
| Method            | Previews | Mean        | Error       | StdDev      | Op/s        | Ratio        | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------ |--------- |------------:|------------:|------------:|------------:|-------------:|--------:|--------:|-------:|----------:|------------:|
| **HandWrittenUpdate** | **10**       |    **118.1 ns** |     **1.45 ns** |     **1.36 ns** | **8,464,607.5** |     **baseline** |        **** |  **0.0434** |      **-** |     **728 B** |            **** |
| QueryCommit       | 10       |    537.9 ns |    10.44 ns |    14.63 ns | 1,859,004.8 | 4.55x slower |   0.13x |  0.1535 | 0.0010 |    2584 B |  3.55x more |
|                   |          |             |             |             |             |              |         |         |        |           |             |
| **HandWrittenUpdate** | **1000**     |  **5,047.8 ns** |    **76.54 ns** |    **71.60 ns** |   **198,104.5** |     **baseline** |        **** |  **1.9302** | **0.3815** |   **32408 B** |            **** |
| QueryCommit       | 1000     |  5,578.4 ns |   109.71 ns |   208.74 ns |   179,263.2 | 1.11x slower |   0.04x |  2.0447 | 0.4349 |   34264 B |  1.06x more |
|                   |          |             |             |             |             |              |         |         |        |           |             |
| **HandWrittenUpdate** | **10000**    | **59,175.4 ns** | **1,151.44 ns** | **1,077.05 ns** |    **16,898.9** |     **baseline** |        **** | **19.0430** | **9.5215** |  **320408 B** |            **** |
| QueryCommit       | 10000    | 58,485.3 ns | 1,156.27 ns | 1,285.20 ns |    17,098.3 | 1.01x faster |   0.03x | 19.2261 | 9.5825 |  322264 B |  1.01x more |
