```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Comparison  

```
| Method        | Depth | Mean         | Error      | StdDev     | Op/s          | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------- |------ |-------------:|-----------:|-----------:|--------------:|--------------:|--------:|-------:|----------:|------------:|
| **PartasSignals** | **1**     |    **13.596 ns** |  **0.1362 ns** |  **0.1274 ns** |  **73,549,195.4** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| Adaptive      | 1     |   149.964 ns |  3.0004 ns |  3.5718 ns |   6,668,246.2 | 11.03x slower |   0.28x | 0.0277 |     464 B |          NA |
| R3            | 1     |     5.875 ns |  0.1164 ns |  0.1143 ns | 170,224,151.1 |  2.32x faster |   0.05x |      - |         - |          NA |
|               |       |              |            |            |               |               |         |        |           |             |
| **PartasSignals** | **4**     |    **61.475 ns** |  **0.8645 ns** |  **0.8086 ns** |  **16,266,869.9** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| Adaptive      | 4     |   348.087 ns |  5.5205 ns |  5.1639 ns |   2,872,842.7 |  5.66x slower |   0.11x | 0.0277 |     464 B |          NA |
| R3            | 4     |    12.432 ns |  0.1565 ns |  0.1387 ns |  80,436,929.4 |  4.95x faster |   0.08x |      - |         - |          NA |
|               |       |              |            |            |               |               |         |        |           |             |
| **PartasSignals** | **16**    |   **240.551 ns** |  **3.1142 ns** |  **2.9130 ns** |   **4,157,121.4** |      **baseline** |        **** |      **-** |         **-** |          **NA** |
| Adaptive      | 16    | 1,111.926 ns | 20.0620 ns | 18.7660 ns |     899,340.3 |  4.62x slower |   0.09x | 0.0267 |     464 B |          NA |
| R3            | 16    |    39.167 ns |  0.7512 ns |  0.7027 ns |  25,531,872.7 |  6.14x faster |   0.13x |      - |         - |          NA |
