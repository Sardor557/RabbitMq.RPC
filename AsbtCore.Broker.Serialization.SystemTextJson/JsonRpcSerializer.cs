using System.Text.Json;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.Serialization.SystemTextJson;

public sealed class JsonRpcSerializer : IRpcSerializer
{
    public string ContentType => "application/json";

    private readonly JsonSerializerOptions options;

    public JsonRpcSerializer() : this(RpcJson.Options) { }

    public JsonRpcSerializer(JsonSerializerOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, options);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Deserialize<T>(payload.Span, options);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
        => JsonSerializer.SerializeToUtf8Bytes(value, type, options);

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
        => JsonSerializer.Deserialize(payload.Span, type, options);
}
