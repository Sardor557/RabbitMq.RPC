using System.Collections.Generic;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

public class ClientPackageExtensionsTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMqRpc:HostName"] = "localhost",
                ["RabbitMqRpc:Port"] = "5672",
                ["RabbitMqRpc:VirtualHost"] = "/",
                ["RabbitMqRpc:UserName"] = "guest",
                ["RabbitMqRpc:Password"] = "guest",
                ["RabbitMqRpc:ClientProvidedName"] = "test",
                ["RabbitMqRpc:RoutePrefix"] = "rpc.",
            })
            .Build();

    [Test]
    public async Task AddRabbitRpcClient_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddRabbitRpcClient(BuildConfig());
        var sp = services.BuildServiceProvider();

        await Assert.That(sp.GetService<IRpcSerializer>()).IsNotNull();
        await Assert.That(sp.GetService<IRpcRouteResolver>()).IsNotNull();
        await Assert.That(sp.GetService<RpcClient>()).IsNotNull();
        await Assert.That(sp.GetService<RpcProxyFactory>()).IsNotNull();
        await Assert.That(sp.GetService<IRabbitMqConnectionProvider>()).IsNotNull();
    }

    [Test]
    public async Task AddRpcProxy_ResolvesAsImplementationOfInterface()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitRpcClient(BuildConfig());
        services.AddRpcProxy<ITestService>();
        var sp = services.BuildServiceProvider();

        var proxy = sp.GetService<ITestService>();

        await Assert.That(proxy).IsNotNull();
    }
}
