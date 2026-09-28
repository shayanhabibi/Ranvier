```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime  

```
| Method                 | Mean      | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |----------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| CreateSignal           |  8.552 ns | 0.1997 ns | 0.2219 ns | 116,926,213.1 |     baseline |         | 0.0053 |      88 B |             |
| CreateAndDisposeMemo   | 25.348 ns | 0.4896 ns | 0.4580 ns |  39,451,210.8 | 2.97x slower |   0.09x | 0.0148 |     248 B |  2.82x more |
| CreateAndDisposeEffect | 35.357 ns | 0.7312 ns | 0.8980 ns |  28,282,776.9 | 4.14x slower |   0.15x | 0.0114 |     192 B |  2.18x more |
