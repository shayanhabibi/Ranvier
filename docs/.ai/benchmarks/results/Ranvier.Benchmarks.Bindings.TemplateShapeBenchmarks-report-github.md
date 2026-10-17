```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=UnrollFactor=16  UnrollFactor=16  Categories=TemplateShape

```
| Method | Rows | Shape                | Mean      | Error     | StdDev    | Op/s      | Gen0   | Gen1   | Allocated |
|------- |----- |--------------------- |----------:|----------:|----------:|----------:|-------:|-------:|----------:|
| **Mount**  | **128**  | **effect-plus-bindings** | **52.168 μs** | **0.8695 μs** | **0.8134 μs** |  **19,168.7** | **7.6904** | **2.5024** |  **129344 B** |
| Update | 128  | effect-plus-bindings |  5.435 μs | 0.1068 μs | 0.1049 μs | 183,995.6 |      - |      - |      40 B |
| **Mount**  | **128**  | **render-effect**        | **24.167 μs** | **0.4659 μs** | **0.6682 μs** |  **41,379.2** | **4.1199** | **0.7935** |   **68928 B** |
| Update | 128  | render-effect        |  3.437 μs | 0.0625 μs | 0.0876 μs | 290,974.5 |      - |      - |      40 B |
