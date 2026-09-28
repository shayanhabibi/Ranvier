```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Probe  

```
| Method        | Sources | Mean       | Error     | StdDev    | Op/s             | Ratio           | RatioSD | Allocated | Alloc Ratio |
|-------------- |-------- |-----------:|----------:|----------:|-----------------:|----------------:|--------:|----------:|------------:|
| **WriteThenRead** | **1**       | **16.7912 ns** | **0.1476 ns** | **0.1308 ns** |     **59,555,113.7** |        **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 1       |  2.5251 ns | 0.0505 ns | 0.0473 ns |    396,030,929.1 |   6.652x faster |   0.13x |         - |          NA |
| BodyOnly      | 1       |  0.0304 ns | 0.0112 ns | 0.0099 ns | 32,916,037,214.0 | 622.250x faster | 246.67x |         - |          NA |
|               |         |            |           |           |                  |                 |         |           |             |
| **WriteThenRead** | **2**       | **18.1450 ns** | **0.2166 ns** | **0.1920 ns** |     **55,111,647.9** |        **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 2       |  2.4887 ns | 0.0249 ns | 0.0208 ns |    401,816,105.2 |    7.29x faster |   0.10x |         - |          NA |
| BodyOnly      | 2       |  0.2075 ns | 0.0115 ns | 0.0102 ns |  4,819,327,474.8 |   87.63x faster |   4.06x |         - |          NA |
|               |         |            |           |           |                  |                 |         |           |             |
| **WriteThenRead** | **4**       | **20.7692 ns** | **0.2599 ns** | **0.2171 ns** |     **48,148,125.5** |        **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 4       |  2.5080 ns | 0.0346 ns | 0.0324 ns |    398,725,869.7 |    8.28x faster |   0.13x |         - |          NA |
| BodyOnly      | 4       |  0.5600 ns | 0.0183 ns | 0.0163 ns |  1,785,849,739.1 |   37.12x faster |   1.11x |         - |          NA |
|               |         |            |           |           |                  |                 |         |           |             |
| **WriteThenRead** | **8**       | **25.2827 ns** | **0.1827 ns** | **0.1426 ns** |     **39,552,785.3** |        **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 8       |  2.5023 ns | 0.0240 ns | 0.0212 ns |    399,627,633.2 |   10.10x faster |   0.10x |         - |          NA |
| BodyOnly      | 8       |  1.0108 ns | 0.0280 ns | 0.0262 ns |    989,289,928.5 |   25.03x faster |   0.64x |         - |          NA |
