using AsbtCore.Broker.Core.Serialization;

namespace AsbtCore.Broker.Core.Tests.Serialization;

public sealed class StableTypeNameTests
{
    [Test]
    public async Task From_PrimitiveType_ReturnsSimpleAssemblyForm()
    {
        var name = StableTypeName.From(typeof(int));

        await Assert.That(name).IsEqualTo("System.Int32, System.Private.CoreLib");
    }

    [Test]
    public async Task From_StringType_ContainsFullName()
    {
        var name = StableTypeName.From(typeof(string));

        await Assert.That(name).StartsWith("System.String,");
    }

    [Test]
    public async Task From_SameType_ReturnsCachedInstance()
    {
        var a = StableTypeName.From(typeof(Guid));
        var b = StableTypeName.From(typeof(Guid));

        await Assert.That(b).IsSameReferenceAs(a);
    }

    [Test]
    public async Task From_GenericList_ContainsBacktickAndArgument()
    {
        var name = StableTypeName.From(typeof(List<int>));

        await Assert.That(name).Contains("List`1");
        await Assert.That(name).Contains("System.Int32");
    }

    [Test]
    public async Task From_GenericDictionary_ContainsBothArgs()
    {
        var name = StableTypeName.From(typeof(Dictionary<string, int>));

        await Assert.That(name).Contains("Dictionary`2");
        await Assert.That(name).Contains("System.String");
        await Assert.That(name).Contains("System.Int32");
    }

    [Test]
    public async Task Resolve_PrimitiveRoundTrip_ReturnsCorrectType()
    {
        var name = StableTypeName.From(typeof(int));
        var resolved = StableTypeName.Resolve(name);

        await Assert.That(resolved).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task Resolve_StringRoundTrip_ReturnsCorrectType()
    {
        var name = StableTypeName.From(typeof(string));
        var resolved = StableTypeName.Resolve(name);

        await Assert.That(resolved).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task Resolve_GenericRoundTrip_ReturnsCorrectType()
    {
        var name = StableTypeName.From(typeof(List<string>));
        var resolved = StableTypeName.Resolve(name);

        await Assert.That(resolved).IsEqualTo(typeof(List<string>));
    }

    [Test]
    public async Task Resolve_SameName_ReturnsCachedInstance()
    {
        var name = StableTypeName.From(typeof(Guid));
        var a = StableTypeName.Resolve(name);
        var b = StableTypeName.Resolve(name);

        await Assert.That(b).IsSameReferenceAs(a);
    }
}
