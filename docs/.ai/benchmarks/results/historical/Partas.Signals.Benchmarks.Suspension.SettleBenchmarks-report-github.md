```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Suspension  

```
| Method        | Mean      | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------- |----------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| SettleInline  | 11.153 ns | 0.2184 ns | 0.3335 ns |  89,664,815.4 | 1.84x slower |   0.10x | 0.0076 |     128 B |  1.33x more |
| CreateAndRead |  6.073 ns | 0.1401 ns | 0.2699 ns | 164,668,476.9 |     baseline |         | 0.0057 |      96 B |             |
