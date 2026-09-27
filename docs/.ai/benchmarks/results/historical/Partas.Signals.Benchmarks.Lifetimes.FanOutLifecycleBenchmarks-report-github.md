```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime  

```
| Method             | Nodes | Mean         | Error        | StdDev       | Op/s         | Gen0    | Gen1   | Allocated |
|------------------- |------ |-------------:|-------------:|-------------:|-------------:|--------:|-------:|----------:|
| **EffectsOnOneSource** | **1**     |     **71.68 ns** |     **1.402 ns** |     **1.920 ns** | **13,950,168.6** |  **0.0257** |      **-** |     **432 B** |
| MemosOnOneSource   | 1     |     71.63 ns |     1.430 ns |     2.428 ns | 13,961,543.6 |  0.0291 |      - |     488 B |
| **EffectsOnOneSource** | **64**    |  **4,275.07 ns** |    **84.716 ns** |   **192.941 ns** |    **233,914.4** |  **0.9232** | **0.0458** |   **15552 B** |
| MemosOnOneSource   | 64    |  4,055.30 ns |    65.971 ns |    58.482 ns |    246,591.0 |  1.1406 | 0.0687 |   19136 B |
| **EffectsOnOneSource** | **1024**  | **74,711.60 ns** | **1,500.925 ns** | **4,233.385 ns** |     **13,384.8** | **14.6484** | **6.7139** |  **245952 B** |
| MemosOnOneSource   | 1024  | 69,733.96 ns | 1,364.669 ns | 1,867.975 ns |     14,340.2 | 18.0664 | 9.2773 |  303296 B |
