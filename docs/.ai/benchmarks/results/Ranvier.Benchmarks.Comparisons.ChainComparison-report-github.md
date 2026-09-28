```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Comparison  

```
| Method   | Depth | Mean         | Error      | StdDev     | Op/s          | Ratio        | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |------ |-------------:|-----------:|-----------:|--------------:|-------------:|--------:|-------:|----------:|------------:|
| **Ranvier**  | **1**     |    **16.233 ns** |  **0.3383 ns** |  **0.4278 ns** |  **61,604,599.1** |     **baseline** |        **** |      **-** |         **-** |          **NA** |
| Adaptive | 1     |   149.113 ns |  2.9144 ns |  3.8907 ns |   6,706,339.6 | 9.19x slower |   0.33x | 0.0277 |     464 B |          NA |
| R3       | 1     |     6.081 ns |  0.0999 ns |  0.0934 ns | 164,438,746.8 | 2.67x faster |   0.08x |      - |         - |          NA |
|          |       |              |            |            |               |              |         |        |           |             |
| **Ranvier**  | **4**     |    **64.183 ns** |  **1.2983 ns** |  **1.7331 ns** |  **15,580,405.0** |     **baseline** |        **** |      **-** |         **-** |          **NA** |
| Adaptive | 4     |   340.485 ns |  6.5760 ns |  6.1512 ns |   2,936,984.7 | 5.31x slower |   0.17x | 0.0277 |     464 B |          NA |
| R3       | 4     |    12.127 ns |  0.1944 ns |  0.1818 ns |  82,460,927.7 | 5.29x faster |   0.16x |      - |         - |          NA |
|          |       |              |            |            |               |              |         |        |           |             |
| **Ranvier**  | **16**    |   **240.468 ns** |  **3.3277 ns** |  **3.1127 ns** |   **4,158,560.2** |     **baseline** |        **** |      **-** |         **-** |          **NA** |
| Adaptive | 16    | 1,100.285 ns | 20.6948 ns | 22.1432 ns |     908,855.8 | 4.58x slower |   0.11x | 0.0267 |     464 B |          NA |
| R3       | 16    |    39.501 ns |  0.5789 ns |  0.5132 ns |  25,315,667.8 | 6.09x faster |   0.11x |      - |         - |          NA |
