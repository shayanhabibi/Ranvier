```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  

```
| Method            | Categories    | Depth | Mean        | Error      | StdDev     | Op/s            | Allocated |
|------------------ |-------------- |------ |------------:|-----------:|-----------:|----------------:|----------:|
| **ReadTailClean**     | **Memo**          | **1**     |   **0.5058 ns** |  **0.0283 ns** |  **0.0278 ns** | **1,977,109,044.1** |         **-** |
| **ReadTailClean**     | **Memo**          | **4**     |   **0.5149 ns** |  **0.0291 ns** |  **0.0272 ns** | **1,942,057,188.8** |         **-** |
| **ReadTailClean**     | **Memo**          | **16**    |   **0.5114 ns** |  **0.0285 ns** |  **0.0317 ns** | **1,955,597,348.4** |         **-** |
| **ReadTailClean**     | **Memo**          | **64**    |   **0.5106 ns** |  **0.0283 ns** |  **0.0303 ns** | **1,958,494,380.5** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **1**     |  **16.2286 ns** |  **0.3274 ns** |  **0.4021 ns** |    **61,619,470.0** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **4**     |  **64.5471 ns** |  **1.3097 ns** |  **2.2591 ns** |    **15,492,567.6** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **16**    | **253.4166 ns** |  **3.1584 ns** |  **2.9544 ns** |     **3,946,072.1** |         **-** |
| **WriteThenReadTail** | **Sentinel,Memo** | **64**    | **998.7070 ns** | **17.5727 ns** | **16.4375 ns** |     **1,001,294.6** |         **-** |
