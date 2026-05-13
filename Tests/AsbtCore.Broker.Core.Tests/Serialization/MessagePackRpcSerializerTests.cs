using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Serialization.MessagePack;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Core.Tests.Serialization;

public sealed class MessagePackRpcSerializerTests
{
    private readonly MessagePackRpcSerializer sut = new();

    [Test]
    public async Task SerializeDeserialize_RpcRequest_RoundTrips()
    {
        var request = new RpcRequest
        {
            RequestId = "r1",
            InterfaceName = "ITest",
            MethodName = "Do",
            Arguments =
            {
                new RpcArgument
                {
                    TypeName = typeof(int).AssemblyQualifiedName!,
                    Payload = sut.PackPayload(42, typeof(int))
                }
            }
        };

        var bytes = sut.Serialize(request);
        var restored = sut.Deserialize<RpcRequest>(bytes);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Arguments.Count).IsEqualTo(1);

        var argValue = (int?)sut.UnpackPayload(restored.Arguments[0].Payload, typeof(int));
        await Assert.That(argValue).IsEqualTo(42);
    }

    [Test]
    public async Task AddRpcMessagePackSerialization_RegistersMessagePackSerializer()
    {
        var services = new ServiceCollection();
        services.AddRpcMessagePackSerialization();
        var sp = services.BuildServiceProvider();

        var serializer = sp.GetRequiredService<IRpcSerializer>();

        await Assert.That(serializer).IsAssignableTo<MessagePackRpcSerializer>();
        await Assert.That(serializer.ContentType).IsEqualTo("application/x-msgpack");
    }
}
