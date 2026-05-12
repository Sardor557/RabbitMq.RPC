using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Converters;

public sealed class ReadOnlyMemoryByteJsonConverter : JsonConverter<ReadOnlyMemory<byte>>
{
    public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetBytesFromBase64();

    public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte> value, JsonSerializerOptions options)
        => writer.WriteBase64StringValue(value.Span);
}
