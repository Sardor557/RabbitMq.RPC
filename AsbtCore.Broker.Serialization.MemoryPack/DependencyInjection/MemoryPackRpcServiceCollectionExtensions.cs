using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public static class MemoryPackRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseMemoryPackRpcSerialization(this RpcServerBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new MemoryPackRpcSerializer());
        return builder;
    }

    public static RpcClientBuilder UseMemoryPackRpcSerialization(this RpcClientBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new MemoryPackRpcSerializer());
        return builder;
    }
}
