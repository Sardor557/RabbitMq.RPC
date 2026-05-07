using System.Text.Json;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Serialization;

[TestClass]
public sealed class RpcSerializationHelperTests
{
    private sealed record SampleDto(int Id, string Name, double[] Values);

    [TestMethod]
    public void ToElement_PrimitiveInt_ReturnsNumberElement()
    {
        var element = RpcSerializationHelper.ToElement(42, typeof(int));

        Assert.AreEqual(JsonValueKind.Number, element.ValueKind);
        Assert.AreEqual(42, element.GetInt32());
    }

    [TestMethod]
    public void ToElement_NullValue_ReturnsNullElement()
    {
        var element = RpcSerializationHelper.ToElement(null, typeof(string));

        Assert.AreEqual(JsonValueKind.Null, element.ValueKind);
    }

    [TestMethod]
    public void ToElement_Dto_RoundTripsWithFromElement()
    {
        var input = new SampleDto(7, "x", new[] { 1.5, 2.5 });

        var element = RpcSerializationHelper.ToElement(input, typeof(SampleDto));
        var restored = (SampleDto?)RpcSerializationHelper.FromElement(element, typeof(SampleDto));

        Assert.IsNotNull(restored);
        // record equality on double[] compares by reference, so assert fields individually
        Assert.AreEqual(input.Id, restored.Id);
        Assert.AreEqual(input.Name, restored.Name);
        CollectionAssert.AreEqual(input.Values, restored.Values);
    }

    [TestMethod]
    public void FromElement_NullJsonElement_ReturnsNullForReferenceType()
    {
        var element = RpcSerializationHelper.ToElement(null, typeof(string));

        var restored = RpcSerializationHelper.FromElement(element, typeof(string));

        Assert.IsNull(restored);
    }
}
