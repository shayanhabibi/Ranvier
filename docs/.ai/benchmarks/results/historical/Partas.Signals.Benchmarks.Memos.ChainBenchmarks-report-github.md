```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  

```
| Method            | Categories    | Depth | Mean        | Error     | StdDev    | Op/s            | Allocated |
|------------------ |-------------- |------ |------------:|----------:|----------:|----------------:|----------:|
| **ReadTailClean**     | **Memo**          | **1**     |   **0.4579 ns** | **0.0126 ns** | **0.0118 ns** | **2,184,046,686.2** |         **-** |
| **ReadTailClean**     | **Memo**          | **4**     |   **0.4567 ns** | **0.0275 ns** | **0.0258 ns** | **2,189,440,152.4** |         **-** |
| **ReadTailClean**     | **Memo**          | **16**    |   **0.4257 ns** | **0.0080 ns** | **0.0075 ns** | **2,349,219,111.8** |         **-** |
| **ReadTailClean**     | **Memo**          | **64**    |   **0.4318 ns** | **0.0055 ns** | **0.0052 ns** | **2,315,634,863.0** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **1**     |  **13.8481 ns** | **0.0794 ns** | **0.0743 ns** |    **72,211,955.8** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **4**     |  **55.4999 ns** | **0.3941 ns** | **0.3687 ns** |    **18,018,042.1** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **16**    | **220.3286 ns** | **1.0517 ns** | **0.9837 ns** |     **4,538,674.7** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **64**    | **933.3284 ns** | **5.0128 ns** | **4.6890 ns** |     **1,071,434.3** |         **-** |
