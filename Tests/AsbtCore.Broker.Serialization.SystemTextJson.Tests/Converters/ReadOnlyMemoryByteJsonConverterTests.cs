using System.Text.Json;
using AsbtCore.Broker.Serialization.SystemTextJson.Converters;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests.Converters;

public class ReadOnlyMemoryByteJsonConverterTests
{
    private static JsonSerializerOptions OptionsWithConverter()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new ReadOnlyMemoryByteJsonConverter());
        return o;
    }

    [Test]
    public async Task Write_EmitsBase64String()
    {
        var bytes = new ReadOnlyMemory<byte>(new byte[] { 0x01, 0x02, 0x03 });
        var json = JsonSerializer.Serialize(bytes, OptionsWithConverter());
        await Assert.That(json).IsEqualTo("\"AQID\"");
    }

    [Test]
    public async Task Read_ParsesBase64String()
    {
        var bytes = JsonSerializer.Deserialize<ReadOnlyMemory<byte>>("\"AQID\"", OptionsWithConverter());
        await Assert.That(bytes.Length).IsEqualTo(3);
        await Assert.That(bytes.Span[0]).IsEqualTo((byte)0x01);
        await Assert.That(bytes.Span[1]).IsEqualTo((byte)0x02);
        await Assert.That(bytes.Span[2]).IsEqualTo((byte)0x03);
    }

    [Test]
    public async Task Roundtrip_PreservesArbitraryBytes()
    {
        var original = new byte[256];
        for (int i = 0; i < 256; i++) original[i] = (byte)i;
        var json = JsonSerializer.Serialize<ReadOnlyMemory<byte>>(original, OptionsWithConverter());
        var roundtrip = JsonSerializer.Deserialize<ReadOnlyMemory<byte>>(json, OptionsWithConverter());
        await Assert.That(roundtrip.Span.SequenceEqual(original)).IsTrue();
    }
}
