using System.Text.Json;
using AsbtCore.Broker.Core;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class RpcRequestSerializationTests
{
    [Test]
    public async Task Deserialize_PreservesProvidedRequestId()
    {
        var json = """{"requestId":"abc","interfaceName":"I","methodName":"M","arguments":[]}""";
        var request = JsonSerializer.Deserialize<RpcRequest>(json, RpcJson.Options);
        await Assert.That(request).IsNotNull();
        await Assert.That(request!.RequestId).IsEqualTo("abc");
    }

    [Test]
    public async Task Deserialize_MissingRequestId_LeavesItNull()
    {
        var json = """{"interfaceName":"I","methodName":"M","arguments":[]}""";
        var request = JsonSerializer.Deserialize<RpcRequest>(json, RpcJson.Options);
        await Assert.That(request).IsNotNull();
        await Assert.That(request!.RequestId).IsNull();
    }

    [Test]
    public async Task Serialize_RpcArgument_PayloadIsBase64()
    {
        var arg = new RpcArgument { TypeName = "X", Payload = new byte[] { 0xAA, 0xBB } };
        var json = JsonSerializer.Serialize(arg, RpcJson.Options);
        await Assert.That(json).Contains("\"payload\":\"qrs=\"");
    }
}
