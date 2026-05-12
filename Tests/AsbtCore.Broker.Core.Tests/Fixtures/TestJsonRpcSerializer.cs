using System.Text.Json;
using System.Text.Json.Serialization;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.Core.Tests.Fixtures;

/// <summary>
/// Test-only <see cref="IRpcSerializer"/> backed by <see cref="System.Text.Json"/>.
/// The real System.Text.Json adapter lives in the Phase 2 separate package; this stub exists
/// so the transport tests can round-trip envelopes against the new contract.
/// </summary>
internal sealed class TestJsonRpcSerializer : IRpcSerializer
{
    public string ContentType => "application/json";

    private static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new ReadOnlyMemoryByteConverter());
        o.Converters.Add(new NullableReadOnlyMemoryByteConverter());
        return o;
    }

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Deserialize<T>(payload.Span, Options);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
        => JsonSerializer.SerializeToUtf8Bytes(value, type, Options);

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
        => JsonSerializer.Deserialize(payload.Span, type, Options);

    private sealed class ReadOnlyMemoryByteConverter : JsonConverter<ReadOnlyMemory<byte>>
    {
        public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetBytesFromBase64();

        public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte> value, JsonSerializerOptions options)
            => writer.WriteBase64StringValue(value.Span);
    }

    private sealed class NullableReadOnlyMemoryByteConverter : JsonConverter<ReadOnlyMemory<byte>?>
    {
        public override ReadOnlyMemory<byte>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            return reader.GetBytesFromBase64();
        }

        public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte>? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteBase64StringValue(value.Value.Span);
        }
    }
}
