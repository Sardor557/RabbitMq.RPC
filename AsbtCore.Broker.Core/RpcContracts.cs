using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsbtCore.Broker.Core
{
    public sealed class RpcPayload
    {
        public JsonElement? Json { get; set; }
        public byte[]? Binary { get; set; }

        [JsonIgnore]
        public bool HasJson => Json is JsonElement value && value.ValueKind != JsonValueKind.Undefined;

        [JsonIgnore]
        public bool HasBinary => Binary is not null;

        public static RpcPayload FromJson(JsonElement value) => new() { Json = value.Clone() };
        public static RpcPayload FromBinary(byte[] value)
            => new() { Binary = value ?? throw new ArgumentNullException(nameof(value)) };

        public static implicit operator RpcPayload(JsonElement value) => FromJson(value);
        public static implicit operator RpcPayload(byte[] value) => FromBinary(value);
    }

    public sealed class RpcRequest
    {
        public string RequestId { get; set; } = default!;
        public string InterfaceName { get; set; } = default!;
        public string MethodName { get; set; } = default!;
        public List<RpcArgument> Arguments { get; set; } = new();
    }

    public sealed class RpcArgument
    {
        public string TypeName { get; set; } = default!;

        /// <summary>
        /// Полезная нагрузка аргумента в динамическом формате:
        /// либо <see cref="JsonElement"/>, либо <see cref="byte"/>[].
        /// </summary>
        public RpcPayload Payload { get; set; } = new();
    }

    public sealed class RpcResponse
    {
        public string RequestId { get; set; } = default!;
        public bool Success { get; set; }
        public string? ResultTypeName { get; set; }

        /// <summary>Результат вызова в формате <see cref="RpcPayload"/>.</summary>
        public RpcPayload? Result { get; set; }

        public RpcError? Error { get; set; }
    }

    public sealed class RpcError
    {
        public string Code { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string? Details { get; set; }
        public string? ExceptionType { get; set; }
    }

    public static class RpcJson
    {
        public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
