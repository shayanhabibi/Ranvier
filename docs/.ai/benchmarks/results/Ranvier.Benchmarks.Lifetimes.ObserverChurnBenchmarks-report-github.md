```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]          : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime,ObserverRemoval  

```
| Method    | Nodes | Mean         | Error        | StdDev     | Op/s         | Allocated |
|---------- |------ |-------------:|-------------:|-----------:|-------------:|----------:|
| **RelinkAll** | **8**     |    **377.97 ns** |     **7.269 ns** |   **6.799 ns** |  **2,645,714.6** |         **-** |
| RelinkOne | 8     |     60.95 ns |     1.073 ns |   1.004 ns | 16,408,004.0 |         - |
| WriteWide | 8     |    132.87 ns |     2.535 ns |   2.371 ns |  7,525,884.3 |         - |
| **RelinkAll** | **64**    |  **3,927.65 ns** |    **65.521 ns** |  **54.713 ns** |    **254,605.5** |         **-** |
| RelinkOne | 64    |     72.26 ns |     1.143 ns |   1.014 ns | 13,839,487.3 |         - |
| WriteWide | 64    |  1,008.66 ns |    20.087 ns |  18.789 ns |    991,415.2 |         - |
| **RelinkAll** | **1024**  | **74,813.00 ns** | **1,026.699 ns** | **910.142 ns** |     **13,366.7** |         **-** |
| RelinkOne | 1024  |     83.41 ns |     1.297 ns |   1.083 ns | 11,989,332.0 |         - |
| WriteWide | 1024  | 17,975.11 ns |   222.052 ns | 196.844 ns |     55,632.5 |         - |
