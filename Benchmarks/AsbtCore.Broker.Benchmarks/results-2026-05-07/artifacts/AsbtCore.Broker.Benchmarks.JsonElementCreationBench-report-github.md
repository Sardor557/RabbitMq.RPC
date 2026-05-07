```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8037)
Unknown processor
.NET SDK 10.0.203
  [Host]   : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | Mode   | Mean       | Error       | StdDev    | Gen0   | Allocated |
|--------------- |------- |-----------:|------------:|----------:|-------:|----------:|
| **Small_Element**  | **Legacy** |   **240.0 ns** |    **32.23 ns** |   **1.77 ns** | **0.0057** |     **336 B** |
| Nested_Element | Legacy |   582.9 ns |   105.70 ns |   5.79 ns | 0.0143 |     864 B |
| List_Element   | Legacy | 6,201.8 ns | 3,362.27 ns | 184.30 ns | 0.1144 |    6424 B |
| **Small_Element**  | **New**    |   **199.0 ns** |    **52.55 ns** |   **2.88 ns** | **0.0036** |     **216 B** |
| Nested_Element | New    |   511.2 ns |   175.14 ns |   9.60 ns | 0.0119 |     704 B |
| List_Element   | New    | 5,639.9 ns |   533.28 ns |  29.23 ns | 0.0916 |    5192 B |
