using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Serialization;
using MessagePack;
using MessagePack.Resolvers;
using System.Text.Json;

namespace AsbtCore.Broker.Serialization.MessagePack;

public sealed class MessagePackRpcSerializer : IRpcSerializer
{
    public string ContentType { get; } = "application/x-msgpack";

    private readonly MessagePackSerializerOptions options;

    public MessagePackRpcSerializer()
        : this(MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance))
    {
    }

    public MessagePackRpcSerializer(MessagePackSerializerOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public byte[] Serialize<T>(T value)
        => MessagePackSerializer.Serialize(value, options);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => MessagePackSerializer.Deserialize<T>(payload, options);

    public RpcPayload PackPayload(object? value, Type type)
        => new()
        {
            Binary = MessagePackSerializer.Serialize(type, value, options)
        };

    public object? UnpackPayload(RpcPayload payload, Type type)
    {
        if (payload.Binary is { } binary)
            return MessagePackSerializer.Deserialize(type, binary, options);

        if (payload.HasJson)
            return payload.Json!.Value.Deserialize(type, RpcJson.Options);

        throw new InvalidOperationException("MessagePack payload is missing.");
    }
}
