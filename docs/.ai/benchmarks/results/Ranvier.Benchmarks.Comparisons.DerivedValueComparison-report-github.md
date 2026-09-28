```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Comparison  

```
| Method   | Mean        | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |------------:|----------:|----------:|----------------:|--------------:|--------:|-------:|----------:|------------:|
| Ranvier  |  16.3330 ns | 0.3341 ns | 0.4792 ns |    61,225,681.7 |      baseline |         |      - |         - |          NA |
| Adaptive | 145.2451 ns | 2.7992 ns | 3.4377 ns |     6,884,912.3 |  8.90x slower |   0.33x | 0.0277 |     464 B |          NA |
| R3       |   5.6991 ns | 0.0954 ns | 0.0846 ns |   175,465,602.9 |  2.87x faster |   0.09x |      - |         - |          NA |
| Rx       |  10.4783 ns | 0.1491 ns | 0.1395 ns |    95,435,219.2 |  1.56x faster |   0.05x |      - |         - |          NA |
| Manual   |   0.5619 ns | 0.0076 ns | 0.0072 ns | 1,779,721,715.4 | 29.07x faster |   0.91x |      - |         - |          NA |
