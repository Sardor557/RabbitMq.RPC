using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Serialization.SystemTextJson;
using AsbtCore.Broker.Serialization.XPacketRpc;
using BenchmarkDotNet.Attributes;

namespace AsbtCore.Broker.Benchmarks;

/// <summary>
/// Head-to-head comparison of the two v4.0 <see cref="IRpcSerializer"/>
/// adapters (<see cref="JsonRpcSerializer"/> and <see cref="XPacketRpcSerializer"/>)
/// on the three hot paths used by the broker:
/// <list type="bullet">
///   <item>Envelope <c>Serialize&lt;RpcRequest&gt;</c> — 3 mixed-type args.</item>
///   <item>Envelope <c>Deserialize&lt;RpcRequest&gt;</c> — same payload.</item>
///   <item>Fragment <c>SerializeFragment</c> — representative DTO (<see cref="OrderDto"/> with 10 lines).</item>
/// </list>
/// <see cref="MemoryDiagnoserAttribute"/> tracks allocation per op.
/// </summary>
[MemoryDiagnoser]
public class SerializerComparisonBench
{
    public sealed record OrderLineDto(int Sku, string Name, int Quantity, decimal UnitPrice);
    public sealed record OrderDto(
        Guid Id,
        string CustomerName,
        DateTime PlacedAt,
        decimal Total,
        OrderLineDto[] Lines);

    public enum SerializerKind { Json, XPacket }

    private IRpcSerializer serializer = null!;
    private OrderDto representativeOrder = null!;
    private RpcRequest representativeRequest = null!;
    private ReadOnlyMemory<byte> serializedRequest;

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

        representativeOrder = BuildOrder();

        // 3 mixed-type args: int, string, OrderDto.
        var customerId = 12345;
        var note = "rush order";
        var fragInt = serializer.SerializeFragment(customerId, typeof(int));
        var fragString = serializer.SerializeFragment(note, typeof(string));
        var fragOrder = serializer.SerializeFragment(representativeOrder, typeof(OrderDto));

        representativeRequest = new RpcRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            InterfaceName = "AsbtCore.Broker.Benchmarks.IOrderService",
            MethodName = "PlaceAsync",
            Arguments = new List<RpcArgument>
            {
                new() { TypeName = StableTypeName.From(typeof(int)), Payload = fragInt },
                new() { TypeName = StableTypeName.From(typeof(string)), Payload = fragString },
                new() { TypeName = StableTypeName.From(typeof(OrderDto)), Payload = fragOrder }
            }
        };

        serializedRequest = serializer.Serialize(representativeRequest);

        // Warm fragment codec for OrderDto so first-call build doesn't pollute the measurement.
        _ = serializer.SerializeFragment(representativeOrder, typeof(OrderDto));
    }

    [Benchmark]
    public ReadOnlyMemory<byte> Envelope_Serialize_RpcRequest()
        => serializer.Serialize(representativeRequest);

    [Benchmark]
    public RpcRequest? Envelope_Deserialize_RpcRequest()
        => serializer.Deserialize<RpcRequest>(serializedRequest);

    [Benchmark]
    public ReadOnlyMemory<byte> Fragment_OrderDto()
        => serializer.SerializeFragment(representativeOrder, typeof(OrderDto));

    private static OrderDto BuildOrder()
    {
        var lines = new OrderLineDto[10];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = new OrderLineDto(
                Sku: 1000 + i,
                Name: $"item-{i:D3}",
                Quantity: i + 1,
                UnitPrice: 9.99m + i);
        }

        return new OrderDto(
            Id: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            CustomerName: "Alice Example",
            PlacedAt: new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc),
            Total: 199.99m,
            Lines: lines);
    }
}
