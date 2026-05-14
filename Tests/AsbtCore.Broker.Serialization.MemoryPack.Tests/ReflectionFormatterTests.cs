namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class ReflectionFormatterTests
{
    [Test]
    public async Task SimplePoco_RoundTrips()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new SimplePocoDto { Id = 42, Name = "answer" };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<SimplePocoDto>(bytes);

        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.Id).IsEqualTo(42);
        await Assert.That(roundtrip.Name).IsEqualTo("answer");
    }
}
