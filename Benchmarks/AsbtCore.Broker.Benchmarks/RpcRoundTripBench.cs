using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Server;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Benchmarks;

public interface IBenchService
{
    Task PingAsync();
    Task<int> SumAsync(int a, int b);
    Task<UserDto> GetByIdAsync(int id);
}

public sealed record UserDto(int Id, string Name);

public sealed class BenchService : IBenchService
{
    public Task PingAsync() => Task.CompletedTask;
    public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);
    public Task<UserDto> GetByIdAsync(int id) => Task.FromResult(new UserDto(id, $"u{id}"));
}

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class RpcRoundTripBench
{
    private IBenchService proxy = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BenchService>();
        services.AddSingleton<IBenchService>(sp => sp.GetRequiredService<BenchService>());
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new RpcOptions
        {
            HostName = "h", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "bench", Port = 1, DefaultTimeoutSeconds = 30
        });
        var resolver = new DefaultRpcRouteResolver(options);
        var registry = new RpcServerRegistry(
            new[] { new RpcServerRegistration(typeof(IBenchService), typeof(BenchService)) },
            resolver);
        var dispatcher = new RpcRequestDispatcher(registry, sp.GetRequiredService<IServiceScopeFactory>());
        var transport = new InMemoryTransport(dispatcher);
        var client = new RpcClient(transport, resolver, new JsonRpcSerializer(), options);
        var factory = new RpcProxyFactory(client, options);
        proxy = factory.CreateProxy<IBenchService>();
    }

    [Benchmark]
    public Task PingAsync() => proxy.PingAsync();

    [Benchmark]
    public Task<int> SumAsync() => proxy.SumAsync(2, 3);

    [Benchmark]
    public Task<UserDto> GetByIdAsync() => proxy.GetByIdAsync(7);
}
