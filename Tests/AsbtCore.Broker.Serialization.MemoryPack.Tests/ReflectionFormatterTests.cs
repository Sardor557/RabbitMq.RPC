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


    [Test]
    public async Task Collections_Nullable_Enum_RoundTrip()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new CollectionsDto
        {
            Numbers = [1, 2, 3],
            Map = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
            OptionalCount = 42,
            Mode = SampleEnum.Third,
        };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<CollectionsDto>(bytes);

        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.Numbers).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(roundtrip.Map.Count).IsEqualTo(2);
        await Assert.That(roundtrip.Map["a"]).IsEqualTo(1);
        await Assert.That(roundtrip.OptionalCount).IsEqualTo(42);
        await Assert.That(roundtrip.Mode).IsEqualTo(SampleEnum.Third);
    }

    [Test]
    public async Task NullableProperty_NullValue_RoundTrip()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new CollectionsDto { OptionalCount = null };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<CollectionsDto>(bytes);

        await Assert.That(roundtrip!.OptionalCount).IsNull();
    }

    [Test]
    public async Task CyclicTypeGraph_RegistersWithoutOverflow()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new GraphA
        {
            Value = 1,
            Child = new GraphB
            {
                Label = "b",
                Parent = null, // avoid runtime data cycle
            },
        };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<GraphA>(bytes);

        await Assert.That(roundtrip!.Value).IsEqualTo(1);
        await Assert.That(roundtrip.Child).IsNotNull();
        await Assert.That(roundtrip.Child!.Label).IsEqualTo("b");
        await Assert.That(roundtrip.Child.Parent).IsNull();
    }
}
