```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Probe  

```
| Method            | Sources | Mean        | Error     | StdDev    | Op/s            | Allocated |
|------------------ |-------- |------------:|----------:|----------:|----------------:|----------:|
| **DetachAndRelink**   | **1**       |  **14.0805 ns** | **0.1536 ns** | **0.1361 ns** |    **71,020,061.6** |         **-** |
| PositionalCompare | 1       |   0.2305 ns | 0.0240 ns | 0.0200 ns | 4,338,975,724.4 |         - |
| **DetachAndRelink**   | **2**       |  **25.0839 ns** | **0.4093 ns** | **0.3628 ns** |    **39,866,156.3** |         **-** |
| PositionalCompare | 2       |   0.4750 ns | 0.0277 ns | 0.0330 ns | 2,105,328,593.4 |         - |
| **DetachAndRelink**   | **4**       |  **49.5408 ns** | **0.9653 ns** | **1.1855 ns** |    **20,185,370.1** |         **-** |
| PositionalCompare | 4       |   1.2144 ns | 0.0430 ns | 0.1021 ns |   823,420,287.0 |         - |
| **DetachAndRelink**   | **8**       | **102.6031 ns** | **1.6252 ns** | **2.6703 ns** |     **9,746,294.8** |         **-** |
| PositionalCompare | 8       |   2.4592 ns | 0.0508 ns | 0.0450 ns |   406,643,966.1 |         - |
