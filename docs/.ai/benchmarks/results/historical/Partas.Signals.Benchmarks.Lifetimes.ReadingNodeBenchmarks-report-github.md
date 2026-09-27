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
| CreateAndDisposeReadingEffect | 42.33 ns | 0.830 ns | 0.853 ns | 23,626,268.4 | 0.0129 |     216 B |
| CreateAndDisposeReadingMemo   | 41.52 ns | 0.817 ns | 1.342 ns | 24,083,945.7 | 0.0162 |     272 B |
