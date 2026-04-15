using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Client
{
    public static class ClientPackageExtensions
    {
        public static IServiceCollection AddRabbitRpcClient(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddOptions<RpcOptions>().Bind(configuration.GetSection("RabbitMqRpc"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddRpcSerialization();
            services.TryAddSingleton<IRpcRouteResolver, DefaultRpcRouteResolver>();
            services.TryAddSingleton<RpcClient>();
            services.TryAddSingleton<RpcProxyFactory>();

            services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
            services.TryAddSingleton<IRpcTransport, RabbitMqRpcTransport>();
            services.TryAddSingleton<IRpcTransportHost, RabbitMqRpcTransportHost>();

            return services;
        }

        public static IServiceCollection AddRpcProxy<TInterface>(this IServiceCollection services) where TInterface : class
        {
            services.TryAddSingleton<TInterface>(sp =>
            sp.GetRequiredService<RpcProxyFactory>()
                .CreateProxy<TInterface>());

            return services;
        }
    }
}
