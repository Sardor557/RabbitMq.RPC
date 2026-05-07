using AsbtCore.Broker.Core.Serialization;
using BenchmarkDotNet.Attributes;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
public class TypeResolutionBench
{
    private string stableName = null!;

    [Params(1, 10, 1000)]
    public int Calls { get; set; }

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup() => stableName = StableTypeName.From(typeof(Guid));

    [Benchmark]
    public Type Resolve()
    {
        Type t = typeof(object);
        for (int i = 0; i < Calls; i++)
        {
            t = Mode switch
            {
                LegacyOrNew.Legacy => Type.GetType(StableTypeName.From(typeof(Guid)), throwOnError: true)!,
                LegacyOrNew.New => StableTypeName.Resolve(stableName),
                _ => throw new InvalidOperationException()
            };
        }
        return t;
    }
}
