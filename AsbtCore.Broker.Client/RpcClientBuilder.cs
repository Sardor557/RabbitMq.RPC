using AsbtCore.Broker.Core.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Client;

/// <summary>
/// Fluent registration surface for the RabbitMq RPC client. Mirrors <c>RpcServerBuilder</c>
/// so that <c>AddRabbitRpcClient</c> and <c>AddRabbitRpcServer</c> compose identically.
/// </summary>
public sealed class RpcClientBuilder
{
    public IServiceCollection Services { get; }

    public RpcClientBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Registers a typed RPC proxy for <typeparamref name="TInterface"/>. The proxy is created
    /// by <see cref="RpcProxyFactory"/> on first resolution and lives for the application lifetime.
    /// </summary>
    public RpcClientBuilder AddProxy<TInterface>() where TInterface : class
    {
        Services.TryAddSingleton<TInterface>(sp =>
            sp.GetRequiredService<RpcProxyFactory>()
              .CreateProxy<TInterface>());

        Services.AddSingleton(new RpcInterfaceRegistration(typeof(TInterface)));
        return this;
    }
}
