using System.Text;
using System.Text.Json;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.ClientServer.Tests.Fixtures;

/// <summary>
/// Deterministic test fake — envelope uses JSON for readability; fragments are
/// wire-encoded as <c>"TYPE:VALUE"</c> UTF-8 so tests can assert payload contents
/// without depending on a real binary format.
/// Tracks call counts so tests can verify behavior.
/// </summary>
internal sealed class TestSerializer : IRpcSerializer
{
    public string ContentType => "application/test";

    public int SerializeFragmentCalls;
    public int DeserializeFragmentCalls;
    public List<(object? Value, Type Type)> SerializeFragmentArgs = new();
    public List<(ReadOnlyMemory<byte> Payload, Type Type)> DeserializeFragmentArgs = new();

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Deserialize<T>(payload.Span);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
    {
        Interlocked.Increment(ref SerializeFragmentCalls);
        lock (SerializeFragmentArgs) SerializeFragmentArgs.Add((value, type));
        var text = $"{type.FullName}:{FormatValue(value)}";
        return Encoding.UTF8.GetBytes(text);
    }

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
    {
        Interlocked.Increment(ref DeserializeFragmentCalls);
        lock (DeserializeFragmentArgs) DeserializeFragmentArgs.Add((payload, type));
        var text = Encoding.UTF8.GetString(payload.Span);
        var idx = text.IndexOf(':');
        var valueText = idx < 0 ? text : text[(idx + 1)..];
        if (valueText == "null") return null;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return valueText;
        if (underlying == typeof(Guid)) return Guid.Parse(valueText);

        if (underlying.IsClass || (underlying.IsValueType && !underlying.IsPrimitive && underlying != typeof(decimal) && underlying != typeof(Guid) && underlying != typeof(DateTime) && underlying != typeof(DateTimeOffset) && underlying != typeof(TimeSpan)))
        {
            // For complex types — round-trip through JSON, since values are written via ToString() which is lossy.
            // Tests that rely on complex deserialization should use `Serialize`/`Deserialize` envelope APIs, or
            // call `BuildFragment` helper to embed a JSON value as the textual payload. We try JSON parse as a fallback.
            try
            {
                return JsonSerializer.Deserialize(valueText, underlying);
            }
            catch
            {
                return null;
            }
        }

        return System.Convert.ChangeType(valueText, underlying);
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "null";
        var t = value.GetType();
        if (t == typeof(string) || t.IsPrimitive || t == typeof(Guid) || t == typeof(decimal) ||
            t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan))
        {
            return value.ToString() ?? "null";
        }
        // complex types: serialize via JSON so DeserializeFragment can round-trip.
        return JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// Helper for tests — builds a fragment payload exactly as <see cref="SerializeFragment"/> would.
    /// Doesn't count toward call counters.
    /// </summary>
    public static ReadOnlyMemory<byte> BuildFragment(object? value, Type type)
    {
        var text = $"{type.FullName}:{FormatValue(value)}";
        return Encoding.UTF8.GetBytes(text);
    }
}
