namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

[NotInParallel]
public sealed class PolymorphismTests
{
    [Before(Class)]
    public static Task ResetUnionRegistrationsBeforeClass()
    {
        MemoryPackRpcOptions.ResetRegisteredUnionsForTests();
        return Task.CompletedTask;
    }

    [Before(Test)]
    public Task ResetUnionRegistrations()
    {
        MemoryPackRpcOptions.ResetRegisteredUnionsForTests();
        return Task.CompletedTask;
    }

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

    [Test]
    public async Task Union_DuplicateRegistration_Throws()
    {
        var options = new MemoryPackRpcOptions().RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
        _ = new MemoryPackRpcSerializer(options);

        var options2 = new MemoryPackRpcOptions().RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
        await Assert.That(() => new MemoryPackRpcSerializer(options2))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Union_DuplicateTag_Throws()
    {
        var options = new MemoryPackRpcOptions();
        await Assert.That(() => options.RegisterUnion<AnimalBase>(b => b.Add<Cat>(1).Add<Dog>(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Union_NullValue_RoundTrips()
    {
        var options = new MemoryPackRpcOptions().RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
        var serializer = new MemoryPackRpcSerializer(options);

        var bytes = serializer.SerializeFragment(null, typeof(AnimalBase));
        var roundtrip = serializer.DeserializeFragment(bytes, typeof(AnimalBase));

        await Assert.That(roundtrip).IsNull();
    }
}
