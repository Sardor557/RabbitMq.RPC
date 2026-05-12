```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
Unknown processor
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method            | Mode   | Mean     | Error     | StdDev  | Gen0   | Allocated |
|------------------ |------- |---------:|----------:|--------:|-------:|----------:|
| **SumAsync_Dispatch** | **Legacy** | **836.9 ns** | **165.40 ns** | **9.07 ns** | **0.0229** |   **1.33 KB** |
| **SumAsync_Dispatch** | **New**    | **510.5 ns** |  **29.23 ns** | **1.60 ns** | **0.0191** |   **1.08 KB** |
