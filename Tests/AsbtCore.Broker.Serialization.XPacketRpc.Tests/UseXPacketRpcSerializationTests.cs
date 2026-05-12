using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.XPacketRpc;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

public sealed class UseXPacketRpcSerializationTests
{
    [Test]
    public async Task ServerBuilder_Registers_XPacketRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        builder.UseXPacketRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<XPacketRpcSerializer>();
    }

    [Test]
    public async Task ClientBuilder_Registers_XPacketRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        builder.UseXPacketRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<XPacketRpcSerializer>();
    }

    [Test]
    public async Task ContentType_Matches_Plan()
    {
        var sut = new XPacketRpcSerializer();
        await Assert.That(sut.ContentType).IsEqualTo("application/x-xpacket-rpc");
    }
}
