```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=Probe  

```
| Method        | Sources | Mean      | Error     | StdDev    | Op/s          | Allocated |
|-------------- |-------- |----------:|----------:|----------:|--------------:|----------:|
| **RemoveThenAdd** | **1**       |  **3.402 ns** | **0.0862 ns** | **0.0922 ns** | **293,935,926.1** |         **-** |
| **RemoveThenAdd** | **8**       | **21.305 ns** | **0.3494 ns** | **0.3268 ns** |  **46,936,983.3** |         **-** |
