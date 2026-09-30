```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  Short  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=Short  IterationCount=3  LaunchCount=1  
UnrollFactor=16  WarmupCount=3  Categories=Suspension  

```
| Method              | Mean        | Error       | StdDev    | Op/s         | Ratio         | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------- |------------:|------------:|----------:|-------------:|--------------:|--------:|-------:|----------:|------------:|
| SucceedingRecompute |    57.24 ns |    49.21 ns |  2.697 ns | 17,470,517.0 |      baseline |         |      - |         - |          NA |
| FailingRecompute    | 3,893.47 ns | 1,559.83 ns | 85.500 ns |    256,840.4 | 68.12x slower |   3.09x | 0.0114 |     256 B |          NA |
