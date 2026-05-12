using System.Linq;
using System.Threading.Tasks;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.ClientServer.Tests.Server;

public sealed class ServerDependencyInjectionTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitMqRpc:HostName"] = "localhost",
                ["RabbitMqRpc:Port"] = "5672",
                ["RabbitMqRpc:UserName"] = "guest",
                ["RabbitMqRpc:Password"] = "guest",
                ["RabbitMqRpc:VirtualHost"] = "/",
                ["RabbitMqRpc:ClientProvidedName"] = "test"
            })
            .Build();

    [Test]
    public async Task AddRabbitRpcServer_ReturnsRpcServerBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddRabbitRpcServer(BuildConfig());

        await Assert.That(builder).IsNotNull();
        await Assert.That(builder).IsAssignableTo<RpcServerBuilder>();
    }

    [Test]
    public async Task AddRabbitRpcServer_RegistersIRpcRouteResolver()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcServer(BuildConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRpcRouteResolver));

        await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task AddRabbitRpcServer_DoesNotRegisterDefaultSerializer()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcServer(BuildConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRpcSerializer));

        await Assert.That(descriptor).IsNull();
    }

    [Test]
    public async Task AddRabbitRpcServer_RegistersRpcServerRegistry()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcServer(BuildConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(RpcServerRegistry));

        await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task AddRabbitRpcServer_RegistersRpcRequestDispatcher()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcServer(BuildConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(RpcRequestDispatcher));

        await Assert.That(descriptor).IsNotNull();
    }

    [Test]
    public async Task AddRabbitRpcServer_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcServer(BuildConfig());

        var hostedServiceDescriptor = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Any(d => d.ImplementationType == typeof(RpcServerHostedService));

        await Assert.That(hostedServiceDescriptor).IsTrue();
    }

    [Test]
    public async Task BuildHost_WithoutSerializer_ThrowsOptionsValidationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitRpcServer(BuildConfig());

        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = sp.GetRequiredService<IOptions<RpcOptions>>().Value;
        });

        await Assert.That(ex).IsNotNull();
        await Assert.That(string.Join("|", ex!.Failures)).Contains("IRpcSerializer");
    }
}
