using System.Text.Json;

namespace AsbtCore.Broker.Core.Serialization;

internal static class RpcSerializationHelper
{
    internal static JsonElement ToElement(object? value, Type type)
        => JsonSerializer.SerializeToElement(value, type, RpcJson.Options);

    internal static object? FromElement(JsonElement element, Type type)
        => element.Deserialize(type, RpcJson.Options);
}
