using System.Reflection;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public sealed class RpcClient
{
    private readonly IRpcTransport transport;
    private readonly IRpcRouteResolver routeResolver;
    private readonly IRpcSerializer serializer;
    private readonly RpcOptions options;

    public RpcClient(
        IRpcTransport transport,
        IRpcRouteResolver routeResolver,
        IRpcSerializer serializer,
        IOptions<RpcOptions> options)
    {
        this.transport = transport;
        this.routeResolver = routeResolver;
        this.serializer = serializer;
        this.options = options.Value;
    }

    internal object InvokeProxy(Type interfaceType, MethodInfo targetMethod, object[] args, TimeSpan? timeout = null)
    {
        var invocation = RpcClientInvokerCache.Get(targetMethod);
        return invocation(this, interfaceType, args, timeout, CancellationToken.None);
    }

    private Task InvokeVoidAsync(Type interfaceType, MethodInfo method,
        object[] args, TimeSpan? timeout, CancellationToken cancellationToken)
        => SendAsync<object>(interfaceType, method, args, timeout, expectsResult: false, cancellationToken);

    private Task<T?> InvokeGenericAsync<T>(Type interfaceType, MethodInfo method,
        object[] args, TimeSpan? timeout, CancellationToken cancellationToken)
        => SendAsync<T>(interfaceType, method, args, timeout, expectsResult: true, cancellationToken);

    private async Task<T?> SendAsync<T>(
        Type interfaceType,
        MethodInfo method,
        object[] args,
        TimeSpan? timeout,
        bool expectsResult,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(interfaceType, method, args, serializer);
        var route = routeResolver.Resolve(interfaceType);

        var response = await transport.SendAsync(
            request,
            route,
            timeout ?? TimeSpan.FromSeconds(options.DefaultTimeoutSeconds),
            cancellationToken);

        if (!response.Success)
        {
            throw new RpcRemoteException(
                response.Error?.Message ?? "Remote call failed.",
                response.Error?.Code,
                response.Error?.ExceptionType,
                response.Error?.Details);
        }

        if (!expectsResult)
            return default;

        if (response.Result is null)
            return default;

        return (T?)serializer.UnpackPayload(response.Result, typeof(T));
    }

    private static RpcRequest BuildRequest(Type interfaceType, MethodInfo method, object[] args, IRpcSerializer serializer)
    {
        var interfaceName = interfaceType.FullName
            ?? throw new InvalidOperationException($"Type {interfaceType} has no FullName.");

        var parameters = method.GetParameters();
        args ??= Array.Empty<object>();

        if (parameters.Length != args.Length)
        {
            throw new InvalidOperationException(
                $"Argument count mismatch for method '{method.Name}'. Expected {parameters.Length}, got {args.Length}.");
        }

        var request = new RpcRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            InterfaceName = interfaceName,
            MethodName = method.Name
        };

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;

            if (parameterType == typeof(CancellationToken))
            {
                throw new NotSupportedException(
                    $"Method '{method.Name}' contains CancellationToken parameter. Use timeout on transport/client level.");
            }

            var typeName = StableTypeName.From(parameterType);

            request.Arguments.Add(new RpcArgument
            {
                TypeName = typeName,
                Payload = serializer.PackPayload(args[i], parameterType)
            });
        }

        return request;
    }
}
