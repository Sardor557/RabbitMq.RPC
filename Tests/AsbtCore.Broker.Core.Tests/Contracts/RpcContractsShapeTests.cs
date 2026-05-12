using AsbtCore.Broker.Core;

namespace AsbtCore.Broker.Core.Tests.Contracts;

public class RpcContractsShapeTests
{
    [Test]
    public async Task RpcArgument_Payload_IsReadOnlyMemoryOfByte()
    {
        var prop = typeof(RpcArgument).GetProperty(nameof(RpcArgument.Payload))!;
        await Assert.That(prop.PropertyType).IsEqualTo(typeof(ReadOnlyMemory<byte>));
    }

    [Test]
    public async Task RpcResponse_Result_IsNullableReadOnlyMemoryOfByte()
    {
        var prop = typeof(RpcResponse).GetProperty(nameof(RpcResponse.Result))!;
        await Assert.That(prop.PropertyType).IsEqualTo(typeof(ReadOnlyMemory<byte>?));
    }

    [Test]
    public async Task RpcRequest_DoesNotReference_JsonElement()
    {
        var refs = typeof(RpcRequest).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();
        // System.Text.Json may still be transitively reachable, but RpcRequest itself
        // must not depend on JsonElement: check no public property type lives in System.Text.Json.
        var jsonElementUsages = typeof(RpcRequest).GetProperties()
            .Concat(typeof(RpcArgument).GetProperties())
            .Concat(typeof(RpcResponse).GetProperties())
            .Where(p => p.PropertyType.Namespace?.StartsWith("System.Text.Json") == true)
            .ToArray();
        await Assert.That(jsonElementUsages.Length).IsEqualTo(0);
    }
}
