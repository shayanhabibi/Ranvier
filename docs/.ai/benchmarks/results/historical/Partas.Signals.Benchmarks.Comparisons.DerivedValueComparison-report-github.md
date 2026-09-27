```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Comparison  

```
| Method        | Mean        | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------- |------------:|----------:|----------:|----------------:|--------------:|--------:|-------:|----------:|------------:|
| PartasSignals |  15.0255 ns | 0.2832 ns | 0.2510 ns |    66,553,646.4 |      baseline |         |      - |         - |          NA |
| Adaptive      | 151.3019 ns | 2.9911 ns | 3.8892 ns |     6,609,301.8 | 10.07x slower |   0.30x | 0.0277 |     464 B |          NA |
| R3            |   5.7667 ns | 0.0846 ns | 0.0707 ns |   173,410,337.0 |  2.61x faster |   0.05x |      - |         - |          NA |
| Rx            |  10.5569 ns | 0.0888 ns | 0.0787 ns |    94,725,171.9 |  1.42x faster |   0.03x |      - |         - |          NA |
| Manual        |   0.5418 ns | 0.0033 ns | 0.0027 ns | 1,845,637,085.3 | 27.73x faster |   0.47x |      - |         - |          NA |
