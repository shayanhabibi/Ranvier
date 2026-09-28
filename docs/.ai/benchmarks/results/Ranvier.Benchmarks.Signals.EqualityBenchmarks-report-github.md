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
| IdentityCutoff   | 1.391 ns | 0.0202 ns | 0.0179 ns | 718,789,638.3 |     baseline |         |      - |         - |          NA |
| StructuralCutoff | 5.743 ns | 0.1000 ns | 0.0886 ns | 174,110,032.6 | 4.13x slower |   0.08x |      - |         - |          NA |
| StructuralWrite  | 6.208 ns | 0.0609 ns | 0.0540 ns | 161,091,273.4 | 4.46x slower |   0.07x | 0.0019 |      32 B |          NA |
