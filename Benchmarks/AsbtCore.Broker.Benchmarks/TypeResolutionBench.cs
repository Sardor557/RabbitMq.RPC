using AsbtCore.Broker.Core.Serialization;
using BenchmarkDotNet.Attributes;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
public class TypeResolutionBench
{
    private string aqn = null!;

    [Params(1, 10, 1000)]
    public int Calls { get; set; }

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup() => aqn = typeof(Guid).AssemblyQualifiedName!;

    [Benchmark]
    public Type Resolve()
    {
        Type t = typeof(object);
        for (int i = 0; i < Calls; i++)
        {
            t = Mode switch
            {
                LegacyOrNew.Legacy => Type.GetType(aqn, throwOnError: true)!,
                LegacyOrNew.New => StableTypeName.Resolve(aqn),
                _ => throw new InvalidOperationException()
            };
        }
        return t;
    }
}
