namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// Mirrors the lifetime contract enforced by the SystemTextJson adapter: a deserialized
/// payload must own its bytes (no aliasing into the source buffer). XPacketRpc's
/// <c>ReadString</c> calls <c>Encoding.UTF8.GetString</c> on a slice; <c>ReadBytes</c>
/// calls <c>ToArray()</c>. Both produce owned data — overwriting the source must not
/// corrupt the deserialized value.
/// </summary>
public sealed class LifetimeContractTests
{
    [Test]
    public async Task Deserialized_String_Survives_BufferOverwrite()
    {
        var sut = new XPacketRpcSerializer();
        var bytes = sut.SerializeFragment("payload-data", typeof(string)).ToArray();
        var s = (string?)sut.DeserializeFragment(bytes, typeof(string));

        Array.Clear(bytes, 0, bytes.Length);

        await Assert.That(s).IsEqualTo("payload-data");
    }

    [Test]
    public async Task Deserialized_ByteArray_Survives_BufferOverwrite()
    {
        var sut = new XPacketRpcSerializer();
        var src = new byte[] { 7, 8, 9, 10, 11 };
        var bytes = sut.SerializeFragment(src, typeof(byte[])).ToArray();
        var back = (byte[])sut.DeserializeFragment(bytes, typeof(byte[]))!;

        Array.Clear(bytes, 0, bytes.Length);

        await Assert.That(back.SequenceEqual(new byte[] { 7, 8, 9, 10, 11 })).IsTrue();
    }
}
