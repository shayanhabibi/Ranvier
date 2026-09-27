```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Signal  

```
| Method           | Mean     | Error     | StdDev    | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------:|----------:|----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| IdentityCutoff   | 1.472 ns | 0.0469 ns | 0.0460 ns | 679,563,960.0 |     baseline |         |      - |         - |          NA |
| StructuralCutoff | 6.124 ns | 0.1253 ns | 0.1172 ns | 163,290,276.0 | 4.17x slower |   0.15x |      - |         - |          NA |
| StructuralWrite  | 6.580 ns | 0.1006 ns | 0.0941 ns | 151,980,317.1 | 4.48x slower |   0.15x | 0.0019 |      32 B |          NA |
