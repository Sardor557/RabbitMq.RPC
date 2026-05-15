using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.ClientServer.Tests.Server;

public class RpcServerBuilderTests
{
    [Test]
    public async Task Register_AddsImplementationAndInterfaceToDi()
    {
        var services = new ServiceCollection();
        var sut = new RpcServerBuilder(services);

        sut.Register<ITestService, TestServiceImpl>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ITestService>()).IsAssignableTo<TestServiceImpl>();
        await Assert.That(scope.ServiceProvider.GetRequiredService<TestServiceImpl>()).IsNotNull();
    }

    [Test]
    public async Task Register_AddsRpcServerRegistration()
    {
        var services = new ServiceCollection();
        var sut = new RpcServerBuilder(services);

        sut.Register<ITestService, TestServiceImpl>(route: "custom.route");

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetServices<RpcServerRegistration>().ToList();

        await Assert.That(registrations.Count).IsEqualTo(1);
        await Assert.That(registrations[0].InterfaceType).IsEqualTo(typeof(ITestService));
        await Assert.That(registrations[0].ImplementationType).IsEqualTo(typeof(TestServiceImpl));
        await Assert.That(registrations[0].ExplicitRoute).IsEqualTo("custom.route");
    }

    [Test]
    public async Task Register_Singleton_UsesSingletonLifetime()
    {
        var services = new ServiceCollection();
        var sut = new RpcServerBuilder(services);

        sut.Register<ITestService, TestServiceImpl>(ServiceLifetime.Singleton);

        var sp = services.BuildServiceProvider();
        var a = sp.GetRequiredService<ITestService>();
        var b = sp.GetRequiredService<ITestService>();

        await Assert.That(b).IsSameReferenceAs(a);
    }
}
