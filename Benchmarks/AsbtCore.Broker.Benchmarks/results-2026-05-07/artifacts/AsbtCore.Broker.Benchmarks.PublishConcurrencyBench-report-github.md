```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
Unknown processor
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method          | Concurrency | Mode   | Mean        | Error        | StdDev      | Gen0   | Allocated |
|---------------- |------------ |------- |------------:|-------------:|------------:|-------:|----------:|
| **ParallelPublish** | **1**           | **Legacy** |    **768.2 ns** |    **108.11 ns** |     **5.93 ns** | **0.0057** |     **331 B** |
| **ParallelPublish** | **1**           | **New**    |    **702.9 ns** |    **116.20 ns** |     **6.37 ns** | **0.0038** |     **220 B** |
| **ParallelPublish** | **4**           | **Legacy** |  **3,240.4 ns** |    **467.01 ns** |    **25.60 ns** | **0.0229** |    **1295 B** |
| **ParallelPublish** | **4**           | **New**    |  **1,305.0 ns** |     **55.07 ns** |     **3.02 ns** | **0.0095** |     **607 B** |
| **ParallelPublish** | **16**          | **Legacy** | **17,845.8 ns** |  **7,519.71 ns** |   **412.18 ns** | **0.1221** |    **4904 B** |
| **ParallelPublish** | **16**          | **New**    |  **4,136.3 ns** |    **935.79 ns** |    **51.29 ns** | **0.0305** |    **1830 B** |
| **ParallelPublish** | **64**          | **Legacy** | **60,478.4 ns** | **61,764.82 ns** | **3,385.54 ns** | **0.3052** |   **19118 B** |
| **ParallelPublish** | **64**          | **New**    | **18,252.9 ns** |  **1,592.29 ns** |    **87.28 ns** | **0.0916** |    **6802 B** |
