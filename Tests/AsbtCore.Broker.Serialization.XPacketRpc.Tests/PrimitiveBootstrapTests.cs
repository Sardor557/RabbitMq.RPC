namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// Round-trip coverage for every primitive registered by <c>XPacketRpcPrimitives</c>.
/// Asserts the bootstrap codecs produce a buffer the matching reader consumes back to the
/// original value (which doubles as proof that the wire format matches what the source
/// generator would emit inline).
/// </summary>
public sealed class PrimitiveBootstrapTests
{
    private static readonly XPacketRpcSerializer Sut = new();

    [Test]
    public async Task Bool_Roundtrip()
    {
        var bytesT = Sut.SerializeFragment(true, typeof(bool));
        var bytesF = Sut.SerializeFragment(false, typeof(bool));
        await Assert.That((bool)Sut.DeserializeFragment(bytesT, typeof(bool))!).IsTrue();
        await Assert.That((bool)Sut.DeserializeFragment(bytesF, typeof(bool))!).IsFalse();
    }

    [Test]
    public async Task Byte_Roundtrip()
    {
        byte v = 0xA5;
        var bytes = Sut.SerializeFragment(v, typeof(byte));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(byte))).IsEqualTo(v);
    }

    [Test]
    public async Task SByte_Roundtrip()
    {
        sbyte v = -42;
        var bytes = Sut.SerializeFragment(v, typeof(sbyte));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(sbyte))).IsEqualTo(v);
    }

    [Test]
    public async Task Int16_Roundtrip()
    {
        short v = -12345;
        var bytes = Sut.SerializeFragment(v, typeof(short));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(short))).IsEqualTo(v);
    }

    [Test]
    public async Task UInt16_Roundtrip()
    {
        ushort v = 54321;
        var bytes = Sut.SerializeFragment(v, typeof(ushort));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(ushort))).IsEqualTo(v);
    }

    [Test]
    public async Task Int32_Roundtrip()
    {
        int v = -1_234_567;
        var bytes = Sut.SerializeFragment(v, typeof(int));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(int))).IsEqualTo(v);
    }

    [Test]
    public async Task UInt32_Roundtrip()
    {
        uint v = 3_456_789u;
        var bytes = Sut.SerializeFragment(v, typeof(uint));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(uint))).IsEqualTo(v);
    }

    [Test]
    public async Task Int64_Roundtrip()
    {
        long v = -9_876_543_210L;
        var bytes = Sut.SerializeFragment(v, typeof(long));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(long))).IsEqualTo(v);
    }

    [Test]
    public async Task UInt64_Roundtrip()
    {
        ulong v = 1234567890123UL;
        var bytes = Sut.SerializeFragment(v, typeof(ulong));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(ulong))).IsEqualTo(v);
    }

    [Test]
    public async Task Single_Roundtrip()
    {
        float v = -3.14159f;
        var bytes = Sut.SerializeFragment(v, typeof(float));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(float))).IsEqualTo(v);
    }

    [Test]
    public async Task Double_Roundtrip()
    {
        double v = 2.718281828459045;
        var bytes = Sut.SerializeFragment(v, typeof(double));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(double))).IsEqualTo(v);
    }

    [Test]
    public async Task Decimal_Roundtrip()
    {
        decimal v = -1234567.890123m;
        var bytes = Sut.SerializeFragment(v, typeof(decimal));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(decimal))).IsEqualTo(v);
    }

    [Test]
    public async Task String_Roundtrip()
    {
        var v = "Hello, мир! 🌍";
        var bytes = Sut.SerializeFragment(v, typeof(string));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(string))).IsEqualTo(v);
    }

    [Test]
    public async Task String_Empty_Roundtrip()
    {
        var bytes = Sut.SerializeFragment(string.Empty, typeof(string));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(string))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Guid_Roundtrip()
    {
        var v = Guid.NewGuid();
        var bytes = Sut.SerializeFragment(v, typeof(Guid));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(Guid))).IsEqualTo(v);
    }

    [Test]
    public async Task DateTime_Roundtrip()
    {
        var v = new DateTime(2026, 5, 13, 14, 30, 0, DateTimeKind.Utc);
        var bytes = Sut.SerializeFragment(v, typeof(DateTime));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(DateTime))).IsEqualTo(v);
    }

    [Test]
    public async Task DateTimeOffset_Roundtrip()
    {
        var v = new DateTimeOffset(2026, 5, 13, 14, 30, 0, TimeSpan.FromHours(5));
        var bytes = Sut.SerializeFragment(v, typeof(DateTimeOffset));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(DateTimeOffset))).IsEqualTo(v);
    }

    [Test]
    public async Task TimeSpan_Roundtrip()
    {
        var v = TimeSpan.FromMilliseconds(987654);
        var bytes = Sut.SerializeFragment(v, typeof(TimeSpan));
        await Assert.That(Sut.DeserializeFragment(bytes, typeof(TimeSpan))).IsEqualTo(v);
    }

    [Test]
    public async Task ByteArray_Roundtrip()
    {
        var v = new byte[] { 1, 2, 3, 254, 255 };
        var bytes = Sut.SerializeFragment(v, typeof(byte[]));
        var roundTripped = (byte[])Sut.DeserializeFragment(bytes, typeof(byte[]))!;
        await Assert.That(roundTripped.SequenceEqual(v)).IsTrue();
    }

    [Test]
    public async Task ReadOnlyMemoryOfByte_Roundtrip()
    {
        ReadOnlyMemory<byte> v = new byte[] { 9, 8, 7 };
        var bytes = Sut.SerializeFragment(v, typeof(ReadOnlyMemory<byte>));
        var roundTripped = (ReadOnlyMemory<byte>)Sut.DeserializeFragment(bytes, typeof(ReadOnlyMemory<byte>))!;
        await Assert.That(roundTripped.Span.SequenceEqual(v.Span)).IsTrue();
    }
}
