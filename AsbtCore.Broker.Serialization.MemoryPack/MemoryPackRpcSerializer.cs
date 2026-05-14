using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.MemoryPack.Formatters;
using AsbtCore.Broker.Serialization.MemoryPack.Reflection;
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

    private readonly ReflectionMemoryPackRegistry registry;

    public MemoryPackRpcSerializer()
        : this(ReflectionMemoryPackRegistry.Shared) { }

    internal MemoryPackRpcSerializer(ReflectionMemoryPackRegistry registry)
    {
        this.registry = registry;
    }

    public string ContentType => "application/x-memorypack-rpc";

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        this.registry.EnsureRegistered(typeof(T));
        return MemoryPackSerializer.Serialize(value);
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        this.registry.EnsureRegistered(typeof(T));
        return MemoryPackSerializer.Deserialize<T>(payload.Span);
    }

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
    {
        this.registry.EnsureRegistered(type);
        return MemoryPackSerializer.Serialize(type, value);
    }

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
    {
        this.registry.EnsureRegistered(type);
        return MemoryPackSerializer.Deserialize(type, payload.Span);
    }
}
