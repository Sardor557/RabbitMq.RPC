using System;
using System.Text.Json;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Core.Tests.Fixtures;

namespace AsbtCore.Broker.Core.Tests.Serialization;

public class JsonRpcSerializerTests
{
    private JsonRpcSerializer sut = null!;

    [Before(Test)]
    public void Init() => sut = new JsonRpcSerializer();

    [Test]
    public async Task SerializeDeserialize_RpcRequest_RoundTrips()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(42, typeof(int), RpcJson.Options);
        using var doc = JsonDocument.Parse(bytes);
        var arg = new RpcArgument { TypeName = typeof(int).AssemblyQualifiedName!, Payload = doc.RootElement.Clone() };

        var request = new RpcRequest
        {
            InterfaceName = "ITest",
            MethodName = "Do",
            Arguments = { arg }
        };

        var serialized = sut.Serialize(request);
        var result = sut.Deserialize<RpcRequest>(serialized);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.InterfaceName).IsEqualTo("ITest");
        await Assert.That(result.MethodName).IsEqualTo("Do");
        await Assert.That(result.Arguments.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Serialize_Deserialize_PrimitiveRoundTrip()
    {
        var bytes = sut.Serialize(42);
        var result = sut.Deserialize<int>(bytes);

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Serialize_Deserialize_ComplexObjectRoundTrip()
    {
        var user = new UserDto(Guid.Empty, "Alice");

        var bytes = sut.Serialize(user);
        var result = sut.Deserialize<UserDto>(bytes);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("Alice");
    }

    [Test]
    public async Task ContentType_ReturnsExpectedString()
    {
        await Assert.That(sut.ContentType).IsEqualTo("System.Text.Json");
    }

    [Test]
    public async Task ParameterlessCtor_UsesDefaultOptions()
    {
        var serializer = new JsonRpcSerializer();
        var bytes = serializer.Serialize(99);
        var result = serializer.Deserialize<int>(bytes);

        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task CustomOptions_Ctor_NullThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            _ = new JsonRpcSerializer(null!);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Deserialize_NullPayload_ReturnsDefault()
    {
        var bytes = sut.Serialize<string?>(null);
        var result = sut.Deserialize<string?>(bytes);

        await Assert.That(result).IsNull();
    }
}
