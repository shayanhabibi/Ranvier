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
| CreateSignal           |  8.931 ns | 0.2132 ns | 0.3443 ns | 111,971,160.8 |     baseline |         | 0.0053 |      88 B |             |
| CreateAndDisposeMemo   | 26.369 ns | 0.5240 ns | 0.5147 ns |  37,923,903.8 | 2.96x slower |   0.12x | 0.0148 |     248 B |  2.82x more |
| CreateAndDisposeEffect | 35.086 ns | 0.7057 ns | 0.8127 ns |  28,501,082.4 | 3.93x slower |   0.17x | 0.0114 |     192 B |  2.18x more |
