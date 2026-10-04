```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  Short  : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=Short  IterationCount=3  LaunchCount=1
UnrollFactor=16  WarmupCount=3  Categories=Timed

```
| Method              | Nodes | Mode     | Mean          | Error         | StdDev       | Op/s         | Gen0   | Allocated |
|-------------------- |------ |--------- |--------------:|--------------:|-------------:|-------------:|-------:|----------:|
| **ChangedCapture**      | **1**     | **both**     |      **21.97 ns** |      **5.927 ns** |     **0.325 ns** | **45,510,841.6** |      **-** |         **-** |
| CaptureAndAdmission | 1     | both     |      26.47 ns |      4.811 ns |     0.264 ns | 37,772,180.7 |      - |         - |
| **ChangedCapture**      | **1**     | **debounce** |      **22.10 ns** |      **5.313 ns** |     **0.291 ns** | **45,247,016.8** |      **-** |         **-** |
| CaptureAndAdmission | 1     | debounce |      53.88 ns |      5.407 ns |     0.296 ns | 18,558,124.5 | 0.0014 |      24 B |
| **ChangedCapture**      | **1**     | **first**    |      **21.12 ns** |      **4.641 ns** |     **0.254 ns** | **47,341,147.9** |      **-** |         **-** |
| CaptureAndAdmission | 1     | first    |      26.92 ns |      5.016 ns |     0.275 ns | 37,145,800.0 |      - |         - |
| **ChangedCapture**      | **1**     | **last**     |      **24.05 ns** |      **3.995 ns** |     **0.219 ns** | **41,580,410.3** |      **-** |         **-** |
| CaptureAndAdmission | 1     | last     |      53.53 ns |      4.251 ns |     0.233 ns | 18,680,341.5 | 0.0014 |      24 B |
| **ChangedCapture**      | **64**    | **both**     |   **1,133.02 ns** |    **186.538 ns** |    **10.225 ns** |    **882,598.9** |      **-** |         **-** |
| CaptureAndAdmission | 64    | both     |   1,180.71 ns |    200.568 ns |    10.994 ns |    846,945.0 |      - |         - |
| **ChangedCapture**      | **64**    | **debounce** |   **1,134.03 ns** |    **270.639 ns** |    **14.835 ns** |    **881,807.7** |      **-** |         **-** |
| CaptureAndAdmission | 64    | debounce |   2,715.81 ns |    319.753 ns |    17.527 ns |    368,214.6 | 0.0916 |    1536 B |
| **ChangedCapture**      | **64**    | **first**    |   **1,030.12 ns** |    **393.518 ns** |    **21.570 ns** |    **970,759.9** |      **-** |         **-** |
| CaptureAndAdmission | 64    | first    |   1,137.70 ns |    127.582 ns |     6.993 ns |    878,964.6 |      - |         - |
| **ChangedCapture**      | **64**    | **last**     |   **1,062.62 ns** |     **72.199 ns** |     **3.957 ns** |    **941,074.2** |      **-** |         **-** |
| CaptureAndAdmission | 64    | last     |   2,705.32 ns |    386.188 ns |    21.168 ns |    369,641.6 | 0.0916 |    1536 B |
| **ChangedCapture**      | **4096**  | **both**     |  **80,400.52 ns** |  **4,159.090 ns** |   **227.974 ns** |     **12,437.7** |      **-** |         **-** |
| CaptureAndAdmission | 4096  | both     |  80,120.98 ns | 10,326.530 ns |   566.032 ns |     12,481.1 |      - |         - |
| **ChangedCapture**      | **4096**  | **debounce** |  **74,387.83 ns** |  **5,552.176 ns** |   **304.334 ns** |     **13,443.1** |      **-** |         **-** |
| CaptureAndAdmission | 4096  | debounce | 196,767.64 ns | 48,211.692 ns | 2,642.646 ns |      5,082.1 | 5.8594 |   98304 B |
| **ChangedCapture**      | **4096**  | **first**    |  **72,397.37 ns** | **21,675.033 ns** | **1,188.082 ns** |     **13,812.7** |      **-** |         **-** |
| CaptureAndAdmission | 4096  | first    |  83,081.23 ns | 13,719.142 ns |   751.992 ns |     12,036.4 |      - |         - |
| **ChangedCapture**      | **4096**  | **last**     |  **73,803.33 ns** | **14,886.602 ns** |   **815.985 ns** |     **13,549.5** |      **-** |         **-** |
| CaptureAndAdmission | 4096  | last     | 187,070.96 ns |  4,153.153 ns |   227.648 ns |      5,345.6 | 5.8594 |   98304 B |
