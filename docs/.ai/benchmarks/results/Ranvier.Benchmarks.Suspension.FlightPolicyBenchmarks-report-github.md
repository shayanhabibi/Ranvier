```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  Dry             : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Categories=Suspension  

```
| Method         | Job             | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount | Flight     | Mean           | Error   | StdDev   | Op/s        | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------- |---------------- |--------------- |------------ |------------ |------------- |------------ |----------- |---------------:|--------:|---------:|------------:|-------------:|--------:|-------:|----------:|------------:|
| **KeepLatest**     | **Dry**             | **1**              | **1**           | **ColdStart**   | **1**            | **1**           | **Completed**  | **1,153,197.0 ns** |      **NA** |  **0.00 ns** |       **867.2** |     **baseline** |        **** |      **-** |     **792 B** |            **** |
| CancelPrevious | Dry             | 1              | 1           | ColdStart   | 1            | 1           | Completed  | 1,409,134.0 ns |      NA |  0.00 ns |       709.7 | 1.22x slower |   0.00x |      - |     840 B |  1.06x more |
|                |                 |                |             |             |              |             |            |                |         |          |             |              |         |        |           |             |
| KeepLatest     | UnrollFactor=16 | Default        | Default     | Default     | 16           | Default     | Completed  |       414.2 ns | 9.61 ns | 28.04 ns | 2,414,027.7 |     baseline |         | 0.0048 |     672 B |             |
| CancelPrevious | UnrollFactor=16 | Default        | Default     | Default     | 16           | Default     | Completed  |       431.7 ns | 8.50 ns | 18.30 ns | 2,316,343.5 | 1.05x slower |   0.08x | 0.0052 |     720 B |  1.07x more |
|                |                 |                |             |             |              |             |            |                |         |          |             |              |         |        |           |             |
| **KeepLatest**     | **Dry**             | **1**              | **1**           | **ColdStart**   | **1**            | **1**           | **Superseded** | **1,357,834.0 ns** |      **NA** |  **0.00 ns** |       **736.5** |     **baseline** |        **** |      **-** |     **784 B** |            **** |
| CancelPrevious | Dry             | 1              | 1           | ColdStart   | 1            | 1           | Superseded | 1,644,615.0 ns |      NA |  0.00 ns |       608.0 | 1.21x slower |   0.00x |      - |     832 B |  1.06x more |
|                |                 |                |             |             |              |             |            |                |         |          |             |              |         |        |           |             |
| KeepLatest     | UnrollFactor=16 | Default        | Default     | Default     | 16           | Default     | Superseded |       314.0 ns | 6.17 ns | 11.29 ns | 3,184,799.2 |     baseline |         | 0.0043 |     616 B |             |
| CancelPrevious | UnrollFactor=16 | Default        | Default     | Default     | 16           | Default     | Superseded |       324.5 ns | 6.51 ns | 13.44 ns | 3,082,099.7 | 1.03x slower |   0.06x | 0.0048 |     664 B |  1.08x more |
