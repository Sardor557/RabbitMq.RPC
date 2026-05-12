using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Core.Internal;

/// <summary>
/// Fails fast at host startup if no <see cref="IRpcSerializer"/> is registered.
/// Wired by <c>AddRabbitRpcClient</c> and <c>AddRabbitRpcServer</c>; users must call
/// <c>.UseXPacketRpcSerialization()</c> or <c>.UseJsonRpcSerialization()</c> (or register
/// a custom <see cref="IRpcSerializer"/> in DI) to satisfy this validator.
/// </summary>
internal sealed class RpcSerializerStartupValidator : IValidateOptions<RpcOptions>
{
    private readonly IServiceProvider services;

    public RpcSerializerStartupValidator(IServiceProvider services)
    {
        this.services = services;
    }

    public ValidateOptionsResult Validate(string? name, RpcOptions options)
        => services.GetService<IRpcSerializer>() is not null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "No IRpcSerializer is registered. Call .UseXPacketRpcSerialization() " +
                "or .UseJsonRpcSerialization() on the builder, or register a custom IRpcSerializer in DI.");
}
