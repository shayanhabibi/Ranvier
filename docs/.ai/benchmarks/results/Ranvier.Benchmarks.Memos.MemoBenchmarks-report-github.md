```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  

```
| Method            | Categories    | Mean       | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Allocated | Alloc Ratio |
|------------------ |-------------- |-----------:|----------:|----------:|----------------:|--------------:|--------:|----------:|------------:|
| CachedRead        | Memo          |  0.2169 ns | 0.0152 ns | 0.0142 ns | 4,611,057,105.0 |      baseline |         |         - |          NA |
| CachedTrackedRead | Memo          |  0.4765 ns | 0.0180 ns | 0.0168 ns | 2,098,796,549.2 |  2.21x slower |   0.15x |         - |          NA |
| Recompute         | Sentinel,Memo | 15.5039 ns | 0.2091 ns | 0.1854 ns |    64,499,876.3 | 71.76x slower |   4.44x |         - |          NA |
