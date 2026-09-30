```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  Short  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=Short  IterationCount=3  LaunchCount=1  
UnrollFactor=16  WarmupCount=3  Categories=Suspension  

```
| Method                 | Depth | Mean      | Error      | StdDev    | Op/s      | Gen0   | Allocated |
|----------------------- |------ |----------:|-----------:|----------:|----------:|-------:|----------:|
| **FailingRecomputeOneHop** | **1**     |  **9.952 μs** |  **10.586 μs** | **0.5802 μs** | **100,486.8** | **0.0458** |    **1008 B** |
| **FailingRecomputeOneHop** | **4**     | **23.792 μs** |   **4.445 μs** | **0.2436 μs** |  **42,030.3** | **0.1526** |    **2784 B** |
| **FailingRecomputeOneHop** | **16**    | **90.026 μs** | **122.516 μs** | **6.7155 μs** |  **11,107.8** | **0.4883** |    **9888 B** |
