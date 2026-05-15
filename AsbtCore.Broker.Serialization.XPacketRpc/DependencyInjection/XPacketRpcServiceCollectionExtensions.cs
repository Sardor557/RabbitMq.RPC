using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.XPacketRpc;

/// <summary>
/// DI extensions registering <see cref="XPacketRpcSerializer"/> as the application's
/// <see cref="IRpcSerializer"/>. Mirrors the System.Text.Json adapter's surface so that
/// callers can swap implementations with a single line change.
/// </summary>
public static class XPacketRpcServiceCollectionExtensions
{
    /// <summary>Registers <see cref="XPacketRpcSerializer"/> for the RPC server pipeline.</summary>
    public static RpcServerBuilder UseXPacketRpcSerialization(this RpcServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new XPacketRpcSerializer());
        return builder;
    }

    /// <summary>Registers <see cref="XPacketRpcSerializer"/> for the RPC client pipeline.</summary>
    public static RpcClientBuilder UseXPacketRpcSerialization(this RpcClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new XPacketRpcSerializer());
        return builder;
    }
}
