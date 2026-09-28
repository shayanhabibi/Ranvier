```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Probe  

```
| Method            | Sources | Mean       | Error     | StdDev    | Op/s            | Allocated |
|------------------ |-------- |-----------:|----------:|----------:|----------------:|----------:|
| **DetachAndRelink**   | **1**       | **13.6226 ns** | **0.1833 ns** | **0.1714 ns** |    **73,407,352.9** |         **-** |
| PositionalCompare | 1       |  0.2129 ns | 0.0136 ns | 0.0113 ns | 4,697,314,070.6 |         - |
| **DetachAndRelink**   | **2**       | **23.7882 ns** | **0.2436 ns** | **0.2278 ns** |    **42,037,734.9** |         **-** |
| PositionalCompare | 2       |  0.4728 ns | 0.0283 ns | 0.0423 ns | 2,115,033,117.9 |         - |
| **DetachAndRelink**   | **4**       | **45.4998 ns** | **0.5870 ns** | **0.5203 ns** |    **21,978,120.5** |         **-** |
| PositionalCompare | 4       |  1.0704 ns | 0.0343 ns | 0.0321 ns |   934,218,615.5 |         - |
| **DetachAndRelink**   | **8**       | **96.3792 ns** | **1.0222 ns** | **0.9061 ns** |    **10,375,678.3** |         **-** |
| PositionalCompare | 8       |  2.3728 ns | 0.0314 ns | 0.0294 ns |   421,436,446.3 |         - |
