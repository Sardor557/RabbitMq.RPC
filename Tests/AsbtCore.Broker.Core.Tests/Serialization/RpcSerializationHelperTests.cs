using System.Text.Json;
using AsbtCore.Broker.Core.Serialization;

namespace AsbtCore.Broker.Core.Tests.Serialization;

public sealed class RpcSerializationHelperTests
{
    private sealed record SampleDto(int Id, string Name, double[] Values);

    [Test]
    public async Task ToElement_PrimitiveInt_ReturnsNumberElement()
    {
        var element = RpcSerializationHelper.ToElement(42, typeof(int));

        await Assert.That(element.ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(element.GetInt32()).IsEqualTo(42);
    }

    [Test]
    public async Task ToElement_NullValue_ReturnsNullElement()
    {
        var element = RpcSerializationHelper.ToElement(null, typeof(string));

        await Assert.That(element.ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task ToElement_Dto_RoundTripsWithFromElement()
    {
        var input = new SampleDto(7, "x", [1.5, 2.5]);

        var element = RpcSerializationHelper.ToElement(input, typeof(SampleDto));
        var restored = (SampleDto?)RpcSerializationHelper.FromElement(element, typeof(SampleDto));

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Id).IsEqualTo(input.Id);
        await Assert.That(restored.Name).IsEqualTo(input.Name);
        await Assert.That(restored.Values).IsEquivalentTo(input.Values);
    }

    [Test]
    public async Task FromElement_NullJsonElement_ReturnsNullForReferenceType()
    {
        var element = RpcSerializationHelper.ToElement(null, typeof(string));

        var restored = RpcSerializationHelper.FromElement(element, typeof(string));

        await Assert.That(restored).IsNull();
    }
}
