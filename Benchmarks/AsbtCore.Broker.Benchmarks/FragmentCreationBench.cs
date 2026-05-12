using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.SystemTextJson;
using AsbtCore.Broker.Serialization.XPacketRpc;
using BenchmarkDotNet.Attributes;

namespace AsbtCore.Broker.Benchmarks;

/// <summary>
/// Measures fragment-level <see cref="IRpcSerializer.SerializeFragment"/>
/// allocations and throughput across all v4.0 serializers and across
/// representative argument shapes (single primitive, single record,
/// collection of records). Replaces the obsolete <c>JsonElementCreationBench</c>
/// from the pre-v4.0 <c>JsonElement</c>-based API.
/// </summary>
[MemoryDiagnoser]
public class FragmentCreationBench
{
    public sealed record Small(int Id, string Name);
    public sealed record Nested(int Id, Small Inner, string[] Tags);

    public enum SerializerKind { Json, XPacket }

    private IRpcSerializer serializer = null!;
    private Small smallValue = null!;
    private Nested nestedValue = null!;
    private List<Small> listValue = null!;

    [Params(SerializerKind.Json, SerializerKind.XPacket)]
    public SerializerKind Kind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        serializer = Kind switch
        {
            SerializerKind.Json => new JsonRpcSerializer(),
            SerializerKind.XPacket => new XPacketRpcSerializer(),
            _ => throw new InvalidOperationException()
        };

        smallValue = new Small(42, "x");
        nestedValue = new Nested(1, new Small(2, "n"), new[] { "a", "b", "c" });
        listValue = Enumerable.Range(0, 50).Select(i => new Small(i, $"n{i}")).ToList();

        // Touch each shape once so first-call codec build is excluded from measurements.
        _ = serializer.SerializeFragment(smallValue, typeof(Small));
        _ = serializer.SerializeFragment(nestedValue, typeof(Nested));
        _ = serializer.SerializeFragment(listValue, typeof(List<Small>));
    }

    [Benchmark]
    public ReadOnlyMemory<byte> Small_Fragment()
        => serializer.SerializeFragment(smallValue, typeof(Small));

    [Benchmark]
    public ReadOnlyMemory<byte> Nested_Fragment()
        => serializer.SerializeFragment(nestedValue, typeof(Nested));

    [Benchmark]
    public ReadOnlyMemory<byte> List_Fragment()
        => serializer.SerializeFragment(listValue, typeof(List<Small>));
}
