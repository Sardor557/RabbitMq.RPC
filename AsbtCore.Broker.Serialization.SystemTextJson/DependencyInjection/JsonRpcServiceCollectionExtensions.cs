using System.Text.Json;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.SystemTextJson;

/// <summary>
/// DI extensions registering <see cref="JsonRpcSerializer"/> as the application's
/// <see cref="IRpcSerializer"/>. Available for both client and server builders.
/// </summary>
public static class JsonRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseJsonRpcSerialization(this RpcServerBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer());
        return builder;
    }

    public static RpcServerBuilder UseJsonRpcSerialization(
        this RpcServerBuilder builder,
        Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = RpcJson.Build();
        configure(options);
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer(options));
        return builder;
    }

    public static RpcClientBuilder UseJsonRpcSerialization(this RpcClientBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer());
        return builder;
    }

    public static RpcClientBuilder UseJsonRpcSerialization(
        this RpcClientBuilder builder,
        Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = RpcJson.Build();
        configure(options);
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer(options));
        return builder;
    }
}
