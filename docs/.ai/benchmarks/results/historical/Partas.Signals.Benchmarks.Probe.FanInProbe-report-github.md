```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Probe  

```
| Method        | Sources | Mean       | Error     | StdDev    | Op/s             | Ratio             | RatioSD | Allocated | Alloc Ratio |
|-------------- |-------- |-----------:|----------:|----------:|-----------------:|------------------:|--------:|----------:|------------:|
| **WriteThenRead** | **1**       | **15.7036 ns** | **0.3306 ns** | **0.4181 ns** |     **63,679,614.3** |          **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 1       |  2.6220 ns | 0.0444 ns | 0.0371 ns |    381,393,319.9 |     5.990x faster |   0.18x |         - |          NA |
| BodyOnly      | 1       |  0.0170 ns | 0.0088 ns | 0.0083 ns | 58,727,774,378.6 | 1,141.958x faster | 509.84x |         - |          NA |
|               |         |            |           |           |                  |                   |         |           |             |
| **WriteThenRead** | **2**       | **16.4773 ns** | **0.3476 ns** | **0.4519 ns** |     **60,689,553.0** |          **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 2       |  2.5370 ns | 0.0443 ns | 0.0415 ns |    394,164,473.7 |     6.496x faster |   0.20x |         - |          NA |
| BodyOnly      | 2       |  0.0492 ns | 0.0184 ns | 0.0163 ns | 20,308,024,430.6 |   373.085x faster | 126.45x |         - |          NA |
|               |         |            |           |           |                  |                   |         |           |             |
| **WriteThenRead** | **4**       | **19.5313 ns** | **0.4046 ns** | **0.4969 ns** |     **51,199,971.9** |          **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 4       |  2.5798 ns | 0.0577 ns | 0.0512 ns |    387,625,506.6 |      7.57x faster |   0.24x |         - |          NA |
| BodyOnly      | 4       |  0.4670 ns | 0.0288 ns | 0.0374 ns |  2,141,442,780.7 |     42.10x faster |   3.69x |         - |          NA |
|               |         |            |           |           |                  |                   |         |           |             |
| **WriteThenRead** | **8**       | **24.5831 ns** | **0.3012 ns** | **0.2670 ns** |     **40,678,383.2** |          **baseline** |        **** |         **-** |          **NA** |
| WriteOnly     | 8       |  2.7110 ns | 0.0320 ns | 0.0284 ns |    368,872,551.1 |      9.07x faster |   0.13x |         - |          NA |
| BodyOnly      | 8       |  1.1324 ns | 0.0424 ns | 0.0505 ns |    883,080,137.7 |     21.75x faster |   0.95x |         - |          NA |
