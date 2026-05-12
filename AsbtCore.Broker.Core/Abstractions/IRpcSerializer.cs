namespace AsbtCore.Broker.Core.Abstractions;

/// <summary>
/// RPC message serialization contract.
/// The byte boundary is <see cref="System.ReadOnlyMemory{T}"/> of <see cref="byte"/> at the transport (RabbitMQ body).
/// No intermediate <see cref="string"/> between layers.
/// </summary>
public interface IRpcSerializer
{
    /// <summary>Wire identifier — written to <c>BasicProperties.ContentType</c> on publish.</summary>
    string ContentType { get; }

    /// <summary>Serializes a whole envelope (RpcRequest / RpcResponse) directly to bytes for BasicPublish.</summary>
    ReadOnlyMemory<byte> Serialize<T>(T value);

    /// <summary>Deserializes a whole envelope from the message body without an intermediate string hop.</summary>
    T? Deserialize<T>(ReadOnlyMemory<byte> payload);

    /// <summary>
    /// Serializes a single typed RPC argument or method result.
    /// Called once per RpcArgument on the client and once per result on the server.
    /// </summary>
    ReadOnlyMemory<byte> SerializeFragment(object? value, Type type);

    /// <summary>Deserializes a single typed fragment back into a CLR value.</summary>
    object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type);
}
