using System.Text.Json;

namespace AsbtCore.Broker.Core.Serialization
{
    /// <summary>
    /// Реализация <see cref="IRpcSerializer"/> на <see cref="System.Text.Json"/>.
    /// Всегда работает с UTF-8 байтами напрямую: <see cref="JsonSerializer.SerializeToUtf8Bytes(object?, Type, JsonSerializerOptions?)"/>
    /// и <see cref="JsonSerializer.Deserialize{TValue}(ReadOnlySpan{byte}, JsonSerializerOptions?)"/>.
    /// Ни одного промежуточного <see cref="string"/>.
    /// </summary>
    public sealed class JsonRpcSerializer : IRpcSerializer
    {
        public string ContentType { get; } = "System.Text.Json";

        private readonly JsonSerializerOptions options;

        public JsonRpcSerializer() : this(RpcJson.Options) { }

        public JsonRpcSerializer(JsonSerializerOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public byte[] Serialize<T>(T value)
            => JsonSerializer.SerializeToUtf8Bytes(value, options);

        public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
            => JsonSerializer.Deserialize<T>(payload.Span, options);

        public RpcPayload PackPayload(object? value, Type type)
            => new()
            {
                Json = JsonSerializer.SerializeToElement(value, type, options)
            };

        public object? UnpackPayload(RpcPayload payload, Type type)
        {
            if (!payload.HasJson)
                throw new InvalidOperationException("JSON payload is missing.");

            return payload.Json!.Value.Deserialize(type, options);
        }
    }
}
