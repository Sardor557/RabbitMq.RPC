```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
Unknown processor
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method          | Mode   | Mean      | Error      | StdDev    | Gen0   | Allocated |
|---------------- |------- |----------:|-----------:|----------:|-------:|----------:|
| **SumAsync_Invoke** | **Legacy** | **77.036 ns** | **10.3684 ns** | **0.5683 ns** | **0.0044** |     **256 B** |
| Add_Invoke      | Legacy | 68.462 ns | 40.8715 ns | 2.2403 ns | 0.0044 |     256 B |
| Ping_Invoke     | Legacy | 13.960 ns |  4.0043 ns | 0.2195 ns |      - |         - |
| **SumAsync_Invoke** | **New**    | **50.381 ns** | **19.3701 ns** | **1.0617 ns** | **0.0045** |     **256 B** |
| Add_Invoke      | New    | 44.759 ns | 10.6166 ns | 0.5819 ns | 0.0045 |     256 B |
| Ping_Invoke     | New    |  8.209 ns |  0.7506 ns | 0.0411 ns |      - |         - |
