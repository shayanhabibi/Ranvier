```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.402
  [Host]          : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime,ObserverRemoval  

```
| Method                 | Nodes | Mean         | Error       | StdDev      | Op/s        | Gen0    | Gen1    | Allocated |
|----------------------- |------ |-------------:|------------:|------------:|------------:|--------:|--------:|----------:|
| **DisposeRoot**            | **8**     |     **438.6 ns** |     **8.58 ns** |     **7.61 ns** | **2,279,748.2** |  **0.1745** |  **0.0014** |   **2.85 KB** |
| DisposeInCreationOrder | 8     |     649.8 ns |    12.75 ns |    15.66 ns | 1,538,905.7 |  0.1612 |  0.0010 |   2.65 KB |
| **DisposeRoot**            | **64**    |   **6,159.5 ns** |   **118.09 ns** |   **126.36 ns** |   **162,350.9** |  **1.2665** |  **0.0763** |  **20.79 KB** |
| DisposeInCreationOrder | 64    |   6,976.0 ns |   135.20 ns |   185.06 ns |   143,348.6 |  1.2817 |  0.0763 |  21.02 KB |
| **DisposeRoot**            | **1024**  | **101,567.4 ns** | **1,895.25 ns** | **1,861.39 ns** |     **9,845.7** | **20.0195** | **10.3760** | **328.29 KB** |
| DisposeInCreationOrder | 1024  | 118,983.9 ns | 2,369.69 ns | 2,820.95 ns |     8,404.5 | 20.5078 | 11.4746 | 336.02 KB |
