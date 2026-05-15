using AsbtCore.Broker.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

public class RpcClientBuilderTests
{
    [Test]
    public async Task Services_PropertyExposesUnderlyingCollection()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        await Assert.That(builder.Services).IsEqualTo(services);
    }

    [Test]
    public async Task AddProxy_RegistersProxyOnce()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        builder.AddProxy<IDummyContract>();
        var count = services.Count(d => d.ServiceType == typeof(IDummyContract));
        await Assert.That(count).IsEqualTo(1);
    }

    public interface IDummyContract { Task DoAsync(); }
}
