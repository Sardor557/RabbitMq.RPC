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

        public byte[] Serialize(object? value, Type type)
            => JsonSerializer.SerializeToUtf8Bytes(value, type, options);

        public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
            => JsonSerializer.Deserialize<T>(payload.Span, options);

        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
            => JsonSerializer.Deserialize(payload.Span, type, options);

        public RpcArgument PackArgument(Type type, object? value)
        {
            var typeName = type.AssemblyQualifiedName
                           ?? type.FullName
                           ?? throw new InvalidOperationException($"Cannot resolve type name for {type}.");

            // value → JsonElement через UTF-8 bytes (без string-хопа)
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type, options);
            using var doc = JsonDocument.Parse(bytes);

            return new RpcArgument
            {
                TypeName = typeName,
                Payload = doc.RootElement.Clone()
            };
        }

        public object? UnpackArgument(RpcArgument argument)
        {
            var type = Type.GetType(argument.TypeName, throwOnError: true)!;
            return argument.Payload.Deserialize(type, options);
        }

        public JsonElement? PackResult(object? value, Type resultType)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, resultType, options);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }

        public T? UnpackResult<T>(JsonElement? element)
        {
            if (element is null || element.Value.ValueKind == JsonValueKind.Undefined)
                return default;

            return element.Value.Deserialize<T>(options);
        }
    }
}
