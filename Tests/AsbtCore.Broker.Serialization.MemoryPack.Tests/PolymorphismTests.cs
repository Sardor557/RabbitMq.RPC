namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class PolymorphismTests
{
    [Test]
    public async Task Union_RoundTrips_AsBase()
    {
        var options = new MemoryPackRpcOptions()
            .RegisterUnion<AnimalBase>(b => b.Add<Cat>(1).Add<Dog>(2));
        var serializer = new MemoryPackRpcSerializer(options);

        AnimalBase original = new Cat { Name = "Mia", IsIndoor = true };
        var bytes = serializer.SerializeFragment(original, typeof(AnimalBase));
        var roundtrip = (AnimalBase?)serializer.DeserializeFragment(bytes, typeof(AnimalBase));

        await Assert.That(roundtrip).IsTypeOf<Cat>();
        await Assert.That(((Cat)roundtrip!).IsIndoor).IsTrue();
        await Assert.That(roundtrip.Name).IsEqualTo("Mia");
    }
}
