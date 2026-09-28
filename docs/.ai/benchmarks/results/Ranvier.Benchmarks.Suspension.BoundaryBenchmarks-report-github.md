```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Suspension  

```
| Method          | Mean      | Error     | StdDev    | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |----------:|----------:|----------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| CatchingPending | 979.54 ns | 15.940 ns | 14.910 ns |  1,020,886.0 | 40.76x slower |   0.95x | 0.0153 |     256 B |  8.00x more |
| CleanBoundary   |  24.04 ns |  0.450 ns |  0.442 ns | 41,600,534.4 |      baseline |         | 0.0019 |      32 B |             |
