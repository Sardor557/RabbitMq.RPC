using System.Text.Json;
using System.Text.Json.Serialization;
using AsbtCore.Broker.Serialization.SystemTextJson.Converters;

namespace AsbtCore.Broker.Serialization.SystemTextJson;

/// <summary>
/// Default <see cref="JsonSerializerOptions"/> for RabbitRpc JSON envelopes.
/// Pre-registers <see cref="ReadOnlyMemoryByteJsonConverter"/> so that
/// <c>RpcArgument.Payload</c> / <c>RpcResponse.Result</c> round-trip through base64.
/// </summary>
public static class RpcJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    /// <summary>Builds a fresh, mutable options instance preconfigured for RabbitRpc.</summary>
    public static JsonSerializerOptions Build() => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ReadOnlyMemoryByteJsonConverter() }
    };
}
