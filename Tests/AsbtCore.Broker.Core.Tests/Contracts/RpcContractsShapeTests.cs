namespace AsbtCore.Broker.Core.Tests.Contracts;

public sealed class RpcContractsShapeTests
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
        // No public property type on the contract DTOs should live in System.Text.Json.
        // (Transitive assembly references may still exist; this test only constrains the surface.)
        var jsonElementUsages = typeof(RpcRequest).GetProperties()
            .Concat(typeof(RpcArgument).GetProperties())
            .Concat(typeof(RpcResponse).GetProperties())
            .Where(p => p.PropertyType.Namespace?.StartsWith("System.Text.Json") == true)
            .ToArray();
        await Assert.That(jsonElementUsages.Length).IsEqualTo(0);
    }
}
