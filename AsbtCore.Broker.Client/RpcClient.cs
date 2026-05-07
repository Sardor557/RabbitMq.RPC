using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;

namespace AsbtCore.Broker.Client
{
    public sealed class RpcClient
    {
        private readonly IRpcTransport transport;
        private readonly IRpcRouteResolver routeResolver; 
        private readonly RpcOptions options;

        public RpcClient(
            IRpcTransport transport,
            IRpcRouteResolver routeResolver,
            IRpcSerializer serializer,
            IOptions<RpcOptions> options)
        {
            this.transport = transport;
            this.routeResolver = routeResolver; 
            this.options = options.Value;
        }

        internal object InvokeProxy(Type interfaceType, MethodInfo targetMethod,
            object[] args, TimeSpan? timeout = null)
        {
            if (targetMethod.ReturnType == typeof(Task))
            {
                return InvokeVoidAsync(interfaceType, targetMethod, args, timeout, CancellationToken.None);
            }

            if (targetMethod.ReturnType.IsGenericType &&
                targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = targetMethod.ReturnType.GetGenericArguments()[0];

                var method = typeof(RpcClient)
                    .GetMethod(nameof(InvokeGenericAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(resultType);

                return method.Invoke(this, new object[] { interfaceType, targetMethod, args, timeout, CancellationToken.None })!;
            }

            throw new NotSupportedException($"Remote method '{targetMethod.Name}' must return Task or Task<T>.");
        }

        private Task InvokeVoidAsync(Type interfaceType, MethodInfo method,
            object[] args, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return SendAsync<object>(interfaceType, method, args, timeout, expectsResult: false, cancellationToken);
        }

        private Task<T> InvokeGenericAsync<T>(
            Type interfaceType,
            MethodInfo method,
            object[] args,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            return SendAsync<T>(interfaceType, method, args, timeout, expectsResult: true, cancellationToken);
        }

        private async Task<T?> SendAsync<T>(
            Type interfaceType,
            MethodInfo method,
            object[] args,
            TimeSpan? timeout,
            bool expectsResult,
            CancellationToken cancellationToken)
        {
            var request = BuildRequest(interfaceType, method, args);
            var route = routeResolver.Resolve(interfaceType);

            var response = await transport.SendAsync(
                request,
                route,
                timeout ?? TimeSpan.FromSeconds( options.DefaultTimeoutSeconds),
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

            if (response.Result is null || response.Result.Value.ValueKind == JsonValueKind.Undefined)
                return default;

            return response.Result.Value.Deserialize<T>(RpcJson.Options);
        }

        private RpcRequest BuildRequest(Type interfaceType, MethodInfo method, object[] args)
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

                var typeName = parameterType.AssemblyQualifiedName
                    ?? parameterType.FullName
                    ?? throw new InvalidOperationException($"Cannot resolve type name for {parameterType}.");

                var bytes = JsonSerializer.SerializeToUtf8Bytes(args[i], parameterType, RpcJson.Options);
                using var doc = JsonDocument.Parse(bytes);

                request.Arguments.Add(new RpcArgument
                {
                    TypeName = typeName,
                    Payload = doc.RootElement.Clone()
                });
            }

            return request;
        }
    }
}
