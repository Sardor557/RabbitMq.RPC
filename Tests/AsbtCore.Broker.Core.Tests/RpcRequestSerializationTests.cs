using System.Text.Json;

namespace AsbtCore.Broker.Core.Tests;

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
}
