using System.Text.Json;

namespace AsbtCore.Broker.Core.Serialization;

internal static class RpcSerializationHelper
{
    public static JsonElement ToElement(object? value, Type type)
        => JsonSerializer.SerializeToElement(value, type, RpcJson.Options);

    public static object? FromElement(JsonElement element, Type type)
        => element.Deserialize(type, RpcJson.Options);
}
