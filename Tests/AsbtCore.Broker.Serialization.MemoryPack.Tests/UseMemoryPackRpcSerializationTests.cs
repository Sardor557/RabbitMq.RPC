using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

public sealed class UseMemoryPackRpcSerializationTests
{
    [Test]
    public async Task ServerBuilder_Registers_MemoryPackRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        builder.UseMemoryPackRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<MemoryPackRpcSerializer>();
    }

    [Test]
    public async Task ClientBuilder_Registers_MemoryPackRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        builder.UseMemoryPackRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<MemoryPackRpcSerializer>();
    }
}
