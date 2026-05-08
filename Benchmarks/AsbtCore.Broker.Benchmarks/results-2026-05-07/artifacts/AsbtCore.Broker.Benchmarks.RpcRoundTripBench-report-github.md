```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
Unknown processor
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method       | Mean       | Error       | StdDev    | Gen0   | Allocated |
|------------- |-----------:|------------:|----------:|-------:|----------:|
| PingAsync    |   201.5 ns |    45.23 ns |   2.48 ns | 0.0257 |     704 B |
| SumAsync     | 1,168.8 ns | 2,769.22 ns | 151.79 ns | 0.0420 |    2432 B |
| GetByIdAsync | 1,102.5 ns |   345.40 ns |  18.93 ns | 0.0324 |    1944 B |
