using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Core.Serialization
{
    public static class RpcSerializationServiceCollectionExtensions
    {
        public static IServiceCollection AddRpcSerialization(this IServiceCollection services)
        {
            services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer(RpcJson.Options));
            return services;
        }

        public static IServiceCollection AddRpcSerialization(
            this IServiceCollection services,
            Action<JsonSerializerOptions> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));

            var options = new JsonSerializerOptions(RpcJson.Options);
            configure(options);

            services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer(options));
            return services;
        }

        public static IServiceCollection AddRpcSerialization<TSerializer>(this IServiceCollection services)
            where TSerializer : class, IRpcSerializer
        {
            services.TryAddSingleton<IRpcSerializer, TSerializer>();
            return services;
        }
    }
}
