```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  Short  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=Short  IterationCount=3  LaunchCount=1  
UnrollFactor=16  WarmupCount=3  Categories=TimedPolicy  

```
| Method          | Inputs | Mean         | Error       | StdDev    | Op/s         | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |------- |-------------:|------------:|----------:|-------------:|-------------:|--------:|-------:|----------:|------------:|
| **RearmEveryInput** | **1**      |     **53.38 ns** |    **58.06 ns** |  **3.183 ns** | **18,732,188.3** |     **baseline** |        **** | **0.0277** |     **464 B** |            **** |
| LazyExtension   | 1      |     49.68 ns |    18.49 ns |  1.013 ns | 20,128,927.3 | 1.07x faster |   0.06x | 0.0277 |     464 B |  1.00x more |
|                 |        |              |             |           |              |              |         |        |           |             |
| **RearmEveryInput** | **64**     |    **216.79 ns** |    **27.73 ns** |  **1.520 ns** |  **4,612,848.3** |     **baseline** |        **** | **0.0277** |     **464 B** |            **** |
| LazyExtension   | 64     |    208.71 ns |    30.63 ns |  1.679 ns |  4,791,231.2 | 1.04x faster |   0.01x | 0.0277 |     464 B |  1.00x more |
|                 |        |              |             |           |              |              |         |        |           |             |
| **RearmEveryInput** | **4096**   | **10,915.46 ns** | **1,630.94 ns** | **89.398 ns** |     **91,613.2** |     **baseline** |        **** | **0.0153** |     **464 B** |            **** |
| LazyExtension   | 4096   | 10,545.89 ns | 1,431.43 ns | 78.462 ns |     94,823.7 | 1.04x faster |   0.01x | 0.0153 |     464 B |  1.00x more |
