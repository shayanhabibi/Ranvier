```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Suspension  

```
| Method          | Mean      | Error     | StdDev   | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |----------:|----------:|---------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| CatchingPending | 977.97 ns | 11.622 ns | 9.705 ns |  1,022,521.6 | 41.19x slower |   1.30x | 0.0143 |     248 B | 10.33x more |
| CleanBoundary   |  23.77 ns |  0.486 ns | 0.727 ns | 42,076,656.2 |      baseline |         | 0.0014 |      24 B |             |
