```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.112
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  Short  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=Short  IterationCount=3  LaunchCount=1  
UnrollFactor=16  WarmupCount=3  Categories=Probe  

```
| Method         | Mean     | Error     | StdDev   | Op/s         | Gen0   | Allocated |
|--------------- |---------:|----------:|---------:|-------------:|-------:|----------:|
| MemoInt        | 33.85 ns |  32.24 ns | 1.767 ns | 29,543,188.9 | 0.0064 |     112 B |
| AsyncMemoInt   | 39.71 ns | 137.03 ns | 7.511 ns | 25,183,104.2 | 0.0092 |     160 B |
| BoundaryInt    | 37.88 ns |  78.03 ns | 4.277 ns | 26,397,513.5 | 0.0088 |     152 B |
| Effect         | 29.15 ns |  14.99 ns | 0.822 ns | 34,305,064.1 | 0.0055 |      96 B |
| AsyncSourceInt | 19.68 ns |  32.02 ns | 1.755 ns | 50,820,413.4 | 0.0032 |      56 B |
| Failure        | 18.88 ns |  17.90 ns | 0.981 ns | 52,958,820.4 | 0.0023 |      40 B |
