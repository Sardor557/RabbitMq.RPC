using AsbtCore.Broker.Core.Serialization;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.MessagePack;

public static class MessagePackRpcSerializationServiceCollectionExtensions
{
    public static IServiceCollection AddRpcMessagePackSerialization(this IServiceCollection services)
    {
        services.TryAddSingleton<IRpcSerializer, MessagePackRpcSerializer>();
        return services;
    }

    public static IServiceCollection AddRpcMessagePackSerialization(
        this IServiceCollection services,
        MessagePackSerializerOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        services.TryAddSingleton<IRpcSerializer>(_ => new MessagePackRpcSerializer(options));
        return services;
    }
}
