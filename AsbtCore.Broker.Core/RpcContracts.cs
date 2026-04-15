using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsbtCore.Broker.Core
{
    public sealed class RpcRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        public string InterfaceName { get; set; } = default!;
        public string MethodName { get; set; } = default!;
        public List<RpcArgument> Arguments { get; set; } = new();
    }

    public sealed class RpcArgument
    {
        public string TypeName { get; set; } = default!;

        /// <summary>
        /// Полезная нагрузка аргумента. Хранится как <see cref="JsonElement"/>,
        /// что позволяет вкладывать её в envelope без повторного string-кодирования.
        /// </summary>
        public JsonElement Payload { get; set; }
    }

    public sealed class RpcResponse
    {
        public string RequestId { get; set; } = default!;
        public bool Success { get; set; }
        public string? ResultTypeName { get; set; }

        /// <summary>Результат вызова. Inline-JsonElement без вложенного string-кодирования.</summary>
        public JsonElement? Result { get; set; }

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
