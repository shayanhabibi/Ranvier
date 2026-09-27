```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Projection  

```
| Method         | Mean      | Error    | StdDev   | Op/s         | Gen0   | Allocated |
|--------------- |----------:|---------:|---------:|-------------:|-------:|----------:|
| RemountKey     | 101.36 ns | 1.476 ns | 1.380 ns |  9,865,396.3 | 0.0257 |     432 B |
| ReadAheadOfGet |  55.62 ns | 0.795 ns | 0.743 ns | 17,979,513.9 | 0.0014 |      24 B |
| UntrackedGet   |  21.23 ns | 0.358 ns | 0.335 ns | 47,107,140.7 | 0.0014 |      24 B |
