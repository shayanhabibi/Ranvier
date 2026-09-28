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
| **EffectsOnOneSource** | **1**     |     **68.62 ns** |     **1.392 ns** |     **1.547 ns** | **14,573,344.5** |  **0.0257** |      **-** |     **432 B** |
| MemosOnOneSource   | 1     |     67.41 ns |     0.959 ns |     0.850 ns | 14,833,915.8 |  0.0291 |      - |     488 B |
| **EffectsOnOneSource** | **64**    |  **4,008.59 ns** |    **53.988 ns** |    **47.859 ns** |    **249,464.3** |  **0.9232** | **0.0458** |   **15552 B** |
| MemosOnOneSource   | 64    |  3,992.81 ns |    77.870 ns |    72.840 ns |    250,450.3 |  1.1406 | 0.0687 |   19136 B |
| **EffectsOnOneSource** | **1024**  | **69,184.00 ns** | **1,100.919 ns** | **1,029.800 ns** |     **14,454.2** | **14.6484** | **6.7139** |  **245952 B** |
| MemosOnOneSource   | 1024  | 68,837.85 ns | 1,084.798 ns |   961.645 ns |     14,526.9 | 18.0664 | 9.2773 |  303296 B |
