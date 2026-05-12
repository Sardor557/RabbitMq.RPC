using AsbtCore.Broker.Serialization.XPacketRpc;
using AsbtCore.Broker.Serialization.XPacketRpc.Internal;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// Asserts that bootstrap + <c>EnsureRegistered(typeof(IFoo))</c> covers the primitive
/// signature types referenced by the contract, plus the DTO return type. Primitives are
/// covered by <c>XPacketRpcPrimitives.EnsureRegistered()</c> at construction; DTOs by the
/// generator-emitted module initializer (driven by <c>Touch&lt;T&gt;()</c> sites).
/// </summary>
public sealed class RpcTypeRegistryTests
{
    [Test]
    public async Task EnsureRegistered_Covers_Primitives_And_Dto()
    {
        _ = new XPacketRpcSerializer(); // triggers primitive bootstrap
        RpcTypeRegistry.EnsureRegistered(typeof(ISampleService));

        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(int))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(string))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(Guid))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(UserDto))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(OrderDto))).IsTrue();
    }

    [Test]
    public async Task Prewarm_From_Serializer_Walks_Signature()
    {
        var sut = new XPacketRpcSerializer();
        sut.Prewarm(typeof(ISampleService));
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(UserDto))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(int))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(string))).IsTrue();
        await Assert.That(RpcTypeRegistry.IsRegistered(typeof(Guid))).IsTrue();
    }
}
