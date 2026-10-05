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
| **HandWrittenUpdate** | **10**       |    **114.5 ns** |     **2.07 ns** |     **1.93 ns** | **8,735,315.5** |     **baseline** |        **** |  **0.0434** |      **-** |     **728 B** |            **** |
| QueryCommit       | 10       |    590.5 ns |    11.47 ns |    11.26 ns | 1,693,406.0 | 5.16x slower |   0.13x |  0.1612 | 0.0010 |    2704 B |  3.71x more |
|                   |          |             |             |             |             |              |         |         |        |           |             |
| **HandWrittenUpdate** | **1000**     |  **5,135.7 ns** |    **53.99 ns** |    **45.09 ns** |   **194,715.9** |     **baseline** |        **** |  **1.9302** | **0.3815** |   **32408 B** |            **** |
| QueryCommit       | 1000     |  5,822.4 ns |    83.14 ns |    77.77 ns |   171,751.4 | 1.13x slower |   0.02x |  2.0523 | 0.4196 |   34384 B |  1.06x more |
|                   |          |             |             |             |             |              |         |         |        |           |             |
| **HandWrittenUpdate** | **10000**    | **56,776.6 ns** | **1,102.62 ns** | **1,394.46 ns** |    **17,612.9** |     **baseline** |        **** | **19.1040** | **9.5215** |  **320408 B** |            **** |
| QueryCommit       | 10000    | 57,361.5 ns | 1,131.61 ns | 1,693.74 ns |    17,433.3 | 1.01x slower |   0.04x | 19.2261 | 9.5825 |  322384 B |  1.01x more |
