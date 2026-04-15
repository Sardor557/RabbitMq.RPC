using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AsbtCore.Broker.Server
{
    public static class DependencyInjection
    {
        public static RpcServerBuilder AddRabbitRpcServer(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddOptions<RpcOptions>().Bind(configuration.GetSection("RabbitMqRpc"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddRpcSerialization();


            services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
            services.TryAddSingleton<IRpcTransport, RabbitMqRpcTransport>();
            services.TryAddSingleton<IRpcTransportHost, RabbitMqRpcTransportHost>();

            services.TryAddSingleton<IRpcRouteResolver, DefaultRpcRouteResolver>();
            services.TryAddSingleton<RpcServerRegistry>();
            services.TryAddSingleton<RpcRequestDispatcher>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RpcServerHostedService>());

            return new RpcServerBuilder(services);
        }
    }
}
