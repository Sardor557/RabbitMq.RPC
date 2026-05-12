using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Internal;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public static class ClientPackageExtensions
{
    public static RpcClientBuilder AddRabbitRpcClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RpcOptions>().Bind(configuration.GetSection("RabbitMqRpc"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RpcOptions>, RpcSerializerStartupValidator>();

        services.TryAddSingleton<IRpcRouteResolver, DefaultRpcRouteResolver>();
        services.TryAddSingleton<RpcClient>();
        services.TryAddSingleton<RpcProxyFactory>();

        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.TryAddSingleton<IRpcTransport, RabbitMqRpcTransport>();
        services.TryAddSingleton<IRpcTransportHost, RabbitMqRpcTransportHost>();

        return new RpcClientBuilder(services);
    }
}
