using AsbtCore.Broker.Core;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class LifetimeContractTests
{
    [Test]
    public async Task Deserialized_Payload_Survives_BufferOverwrite()
    {
        var sut = new JsonRpcSerializer();
        var original = new RpcRequest
        {
            RequestId = "rid",
            InterfaceName = "I",
            MethodName = "M",
            Arguments = { new RpcArgument { TypeName = "T", Payload = new byte[] { 7, 8, 9 } } }
        };

        var bytes = sut.Serialize(original).ToArray();
        var deserialized = sut.Deserialize<RpcRequest>(bytes)!;

        Array.Clear(bytes, 0, bytes.Length);

        await Assert.That(deserialized.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 7, 8, 9 })).IsTrue();
    }
}
