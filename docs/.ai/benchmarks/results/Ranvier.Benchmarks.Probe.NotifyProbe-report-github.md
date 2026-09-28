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
| CopyWalkAndClear  | 3.1477 ns | 0.0401 ns | 0.0355 ns |   317,693,964.1 |      baseline |         |         - |          NA |
| EnumerateDirectly | 0.2327 ns | 0.0172 ns | 0.0161 ns | 4,297,001,209.9 | 13.59x faster |   0.94x |         - |          NA |
