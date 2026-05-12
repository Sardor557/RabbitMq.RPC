using System.Collections.Generic;
using System.Threading.Tasks;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        var builder = services.AddRabbitRpcClient(BuildConfig());
        // user must provide serializer separately (v4 breaking change)
        services.AddSingleton<IRpcSerializer, TestSerializer>();

        var sp = services.BuildServiceProvider();

        await Assert.That(builder).IsAssignableTo<RpcClientBuilder>();
        await Assert.That(sp.GetService<IRpcRouteResolver>()).IsNotNull();
        await Assert.That(sp.GetService<RpcClient>()).IsNotNull();
        await Assert.That(sp.GetService<RpcProxyFactory>()).IsNotNull();
        await Assert.That(sp.GetService<IRabbitMqConnectionProvider>()).IsNotNull();
    }

    [Test]
    public async Task AddRabbitRpcClient_DoesNotRegisterDefaultSerializer()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcClient(BuildConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRpcSerializer));

        await Assert.That(descriptor).IsNull();
    }

    [Test]
    public async Task AddProxy_OnBuilder_RegistersProxyForInterface()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitRpcClient(BuildConfig()).AddProxy<ITestService>();
        services.AddSingleton<IRpcSerializer, TestSerializer>();
        var sp = services.BuildServiceProvider();

        var proxy = sp.GetService<ITestService>();

        await Assert.That(proxy).IsNotNull();
    }

    [Test]
    public async Task BuildHost_WithoutSerializer_ThrowsOptionsValidationException()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcClient(BuildConfig());

        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = sp.GetRequiredService<IOptions<RpcOptions>>().Value;
        });

        await Assert.That(ex).IsNotNull();
        await Assert.That(string.Join("|", ex!.Failures)).Contains("IRpcSerializer");
    }
}
