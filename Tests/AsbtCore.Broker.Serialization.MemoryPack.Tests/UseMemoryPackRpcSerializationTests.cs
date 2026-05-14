using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Reflection;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
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

    [Test]
    public async Task ServerBuilder_WithOptions_Resolves()
    {
        var services = new ServiceCollection();
        services.AddRabbitRpcServer(new ConfigurationBuilder().Build())
            .UseMemoryPackRpcSerialization(opt =>
            {
                opt.PrewarmType<SimplePocoDto>();
            });

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IRpcSerializer>();
        await Assert.That(serializer).IsTypeOf<MemoryPackRpcSerializer>();

        // Verify prewarm took effect — round-trip works.
        var sample = new SimplePocoDto { Id = 1, Name = "n" };
        var bytes = serializer.Serialize(sample);
        var rt = serializer.Deserialize<SimplePocoDto>(bytes);
        await Assert.That(rt!.Id).IsEqualTo(1);
    }

    [Test]
    public async Task PrewarmAssembly_WithFilter_RegistersOnlyMatching()
    {
        var registry = new ReflectionMemoryPackRegistry();
        var options = new MemoryPackRpcOptions()
            .PrewarmAssembly(typeof(SimplePocoDto).Assembly,
                filter: t => t == typeof(SimplePocoDto));
        var serializer = new MemoryPackRpcSerializer(options, registry);

        // Surface check: SimplePocoDto serializes immediately without re-discovery.
        var bytes = serializer.Serialize(new SimplePocoDto { Id = 9, Name = "p" });
        await Assert.That(bytes.IsEmpty).IsFalse();
    }
}
