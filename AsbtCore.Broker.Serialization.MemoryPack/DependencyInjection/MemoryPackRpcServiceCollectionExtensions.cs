using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public static class MemoryPackRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseMemoryPackRpcSerialization(this RpcServerBuilder builder)
        => UseMemoryPackRpcSerialization(builder, configure: null);

    public static RpcServerBuilder UseMemoryPackRpcSerialization(
        this RpcServerBuilder builder, Action<MemoryPackRpcOptions>? configure)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ =>
        {
            var options = new MemoryPackRpcOptions();
            configure?.Invoke(options);
            return new MemoryPackRpcSerializer(options);
        });
        return builder;
    }

    public static RpcClientBuilder UseMemoryPackRpcSerialization(this RpcClientBuilder builder)
        => UseMemoryPackRpcSerialization(builder, configure: null);

    public static RpcClientBuilder UseMemoryPackRpcSerialization(
        this RpcClientBuilder builder, Action<MemoryPackRpcOptions>? configure)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ =>
        {
            var options = new MemoryPackRpcOptions();
            configure?.Invoke(options);
            return new MemoryPackRpcSerializer(options);
        });
        return builder;
    }
}
