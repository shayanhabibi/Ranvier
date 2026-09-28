```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Comparison  

```
| Method   | Mean       | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |-----------:|----------:|----------:|----------------:|--------------:|--------:|-------:|----------:|------------:|
| Ranvier  |  2.6562 ns | 0.0662 ns | 0.0619 ns |   376,472,154.6 |      baseline |         |      - |         - |          NA |
| Adaptive | 39.2122 ns | 0.7878 ns | 0.9675 ns |    25,502,238.2 | 14.77x slower |   0.49x | 0.0210 |     352 B |          NA |
| R3       |  0.2793 ns | 0.0248 ns | 0.0295 ns | 3,580,177,323.8 |  9.62x faster |   1.07x |      - |         - |          NA |
