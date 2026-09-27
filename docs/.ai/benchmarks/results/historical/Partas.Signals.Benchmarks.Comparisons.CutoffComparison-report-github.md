```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Comparison  

```
| Method        | Mean       | Error     | StdDev    | Op/s            | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------- |-----------:|----------:|----------:|----------------:|--------------:|--------:|-------:|----------:|------------:|
| PartasSignals |  2.5931 ns | 0.0343 ns | 0.0321 ns |   385,638,888.8 |      baseline |         |      - |         - |          NA |
| Adaptive      | 38.8853 ns | 0.7613 ns | 1.3132 ns |    25,716,686.6 | 15.00x slower |   0.53x | 0.0210 |     352 B |          NA |
| R3            |  0.2653 ns | 0.0248 ns | 0.0232 ns | 3,769,107,630.9 |  9.85x faster |   0.89x |      - |         - |          NA |
