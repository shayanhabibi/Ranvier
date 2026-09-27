```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Probe  

```
| Method            | Mean      | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Allocated | Alloc Ratio |
|------------------ |----------:|----------:|----------:|----------------:|--------------:|--------:|----------:|------------:|
| CopyWalkAndClear  | 3.4011 ns | 0.0778 ns | 0.1422 ns |   294,022,424.2 |      baseline |         |         - |          NA |
| EnumerateDirectly | 0.2504 ns | 0.0228 ns | 0.0224 ns | 3,994,259,285.9 | 13.69x faster |   1.31x |         - |          NA |
