```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Lifetime  

```
| Method                        | Mean     | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|------------------------------ |---------:|---------:|---------:|-------------:|-------:|----------:|
| CreateAndDisposeReadingEffect | 39.67 ns | 0.811 ns | 0.719 ns | 25,208,600.5 | 0.0129 |     216 B |
| CreateAndDisposeReadingMemo   | 40.46 ns | 0.800 ns | 0.821 ns | 24,716,778.6 | 0.0162 |     272 B |
