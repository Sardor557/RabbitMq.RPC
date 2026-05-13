using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.MemoryPack.Formatters;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public sealed class MemoryPackRpcSerializer : IRpcSerializer
{
    static MemoryPackRpcSerializer()
    {
        MemoryPackFormatterProvider.Register<RpcError>(new RpcErrorFormatter());
        MemoryPackFormatterProvider.Register<RpcArgument>(new RpcArgumentFormatter());
        MemoryPackFormatterProvider.Register<RpcRequest>(new RpcRequestFormatter());
        MemoryPackFormatterProvider.Register<RpcResponse>(new RpcResponseFormatter());
    }

    public string ContentType => "application/x-memorypack-rpc";

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => MemoryPackSerializer.Serialize(value);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => MemoryPackSerializer.Deserialize<T>(payload.Span);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
        => MemoryPackSerializer.Serialize(type, value);

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
        => MemoryPackSerializer.Deserialize(type, payload.Span);
}
