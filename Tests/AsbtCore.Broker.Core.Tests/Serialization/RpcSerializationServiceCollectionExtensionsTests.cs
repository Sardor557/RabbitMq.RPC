using System.Text.Json;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Core.Tests.Serialization;

public sealed class RpcSerializationServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddRpcSerialization_Default_RegistersJsonRpcSerializer()
    {
        var services = new ServiceCollection();
        services.AddRpcSerialization();
        var sp = services.BuildServiceProvider();

        var serializer = sp.GetRequiredService<IRpcSerializer>();

        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer.ContentType).IsEqualTo("System.Text.Json");
    }

    [Test]
    public async Task AddRpcSerialization_Default_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddRpcSerialization();
        var sp = services.BuildServiceProvider();

        var a = sp.GetRequiredService<IRpcSerializer>();
        var b = sp.GetRequiredService<IRpcSerializer>();

        await Assert.That(b).IsSameReferenceAs(a);
    }

    [Test]
    public async Task AddRpcSerialization_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddRpcSerialization(opts => opts.WriteIndented = true);
        var sp = services.BuildServiceProvider();

        var serializer = sp.GetRequiredService<IRpcSerializer>();

        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer.ContentType).IsEqualTo("System.Text.Json");
    }

    [Test]
    public async Task AddRpcSerialization_WithNullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            services.AddRpcSerialization(null!);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task AddRpcSerialization_Generic_RegistersCustomSerializer()
    {
        var services = new ServiceCollection();
        services.AddRpcSerialization<JsonRpcSerializer>();
        var sp = services.BuildServiceProvider();

        var serializer = sp.GetRequiredService<IRpcSerializer>();

        await Assert.That(serializer).IsAssignableTo<JsonRpcSerializer>();
    }

    [Test]
    public async Task AddRpcSerialization_CalledTwice_DoesNotOverrideFirstRegistration()
    {
        var services = new ServiceCollection();
        services.AddRpcSerialization();
        services.AddRpcSerialization();
        var sp = services.BuildServiceProvider();

        var serializers = sp.GetServices<IRpcSerializer>().ToList();

        await Assert.That(serializers.Count).IsEqualTo(1);
    }
}
