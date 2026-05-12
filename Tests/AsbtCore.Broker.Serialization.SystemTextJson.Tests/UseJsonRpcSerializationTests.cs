using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public sealed class UseJsonRpcSerializationTests
{
    [Test]
    public async Task ServerBuilder_Registers_JsonRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        builder.UseJsonRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<JsonRpcSerializer>();
    }

    [Test]
    public async Task ClientBuilder_Registers_JsonRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        builder.UseJsonRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<JsonRpcSerializer>();
    }

    [Test]
    public async Task ServerBuilder_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        var configureCalled = false;
        builder.UseJsonRpcSerialization(o =>
        {
            configureCalled = true;
            o.WriteIndented = true;
        });
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(configureCalled).IsTrue();
        await Assert.That(serializer).IsNotNull();
    }

    [Test]
    public async Task ClientBuilder_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        var configureCalled = false;
        builder.UseJsonRpcSerialization(o =>
        {
            configureCalled = true;
            o.WriteIndented = true;
        });
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(configureCalled).IsTrue();
        await Assert.That(serializer).IsNotNull();
    }
}
