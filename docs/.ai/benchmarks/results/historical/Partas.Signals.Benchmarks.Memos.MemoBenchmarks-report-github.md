```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  

```
| Method            | Categories    | Mean       | Error     | StdDev    | Median     | Op/s            | Ratio         | RatioSD | Allocated | Alloc Ratio |
|------------------ |-------------- |-----------:|----------:|----------:|-----------:|----------------:|--------------:|--------:|----------:|------------:|
| CachedRead        | Memo          |  0.3191 ns | 0.0779 ns | 0.2298 ns |  0.1839 ns | 3,134,023,334.9 |      baseline |         |         - |          NA |
| CachedTrackedRead | Memo          |  0.8832 ns | 0.0971 ns | 0.2864 ns |  0.8930 ns | 1,132,267,919.4 |  4.45x slower |   3.12x |         - |          NA |
| Recompute         | Sentinel,Memo | 15.5268 ns | 0.3181 ns | 0.8380 ns | 15.3624 ns |    64,404,836.9 | 78.23x slower |  46.60x |         - |          NA |
