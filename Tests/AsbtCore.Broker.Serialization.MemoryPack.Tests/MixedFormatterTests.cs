namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class MixedFormatterTests
{
    [Test]
    public async Task TaggedAndPlain_RoundTrip()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new WrapperDto
        {
            Tagged = new TaggedDto { Id = 7, Note = "tagged" },
            OuterLabel = "wrap",
        };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<WrapperDto>(bytes);

        await Assert.That(roundtrip!.OuterLabel).IsEqualTo("wrap");
        await Assert.That(roundtrip.Tagged).IsNotNull();
        await Assert.That(roundtrip.Tagged!.Id).IsEqualTo(7);
        await Assert.That(roundtrip.Tagged.Note).IsEqualTo("tagged");
    }

    [Test]
    public async Task TaggedFormatter_NotReplaced()
    {
        var serializer = new MemoryPackRpcSerializer();
        var tagged = new TaggedDto { Id = 1, Note = "x" };
        var bytes = serializer.Serialize(tagged);
        // Bytes should match what MemoryPack source-gen produces directly.
        var direct = global::MemoryPack.MemoryPackSerializer.Serialize(tagged);
        await Assert.That(bytes.Span.SequenceEqual(direct)).IsTrue();
    }
}
