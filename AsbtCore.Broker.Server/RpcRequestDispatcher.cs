using System.Reflection;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Server;

public sealed class RpcRequestDispatcher
{
    private readonly RpcServerRegistry registry;
    private readonly IServiceScopeFactory scopeFactory;

    public RpcRequestDispatcher(RpcServerRegistry registry, IServiceScopeFactory scopeFactory)
    {
        this.registry = registry;
        this.scopeFactory = scopeFactory;
    }

    public async Task<RpcResponse> DispatchAsync(RpcRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!registry.TryGet(request.InterfaceName, out var descriptor))
            {
                return CreateError(request.RequestId, "service_not_found",
                    $"Service '{request.InterfaceName}' not found.");
            }

            var parameterTypeNames = request.Arguments.Select(x => x.TypeName).ToArray();

            if (!descriptor.TryGetMethod(request.MethodName, parameterTypeNames, out var entry))
            {
                return CreateError(request.RequestId, "method_not_found",
                    $"Method '{request.MethodName}' with specified signature was not found.");
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService(descriptor.ImplementationType);

            var args = new object?[request.Arguments.Count];
            for (int i = 0; i < request.Arguments.Count; i++)
            {
                var arg = request.Arguments[i];
                Type type;
                try
                {
                    type = TypeNameCache.Resolve(arg.TypeName);
                }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "type_not_found",
                        $"Type '{arg.TypeName}' (argument {i}) could not be resolved.", ex);
                }

                try
                {
                    args[i] = RpcSerializationHelper.FromElement(arg.Payload, type);
                }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "deserialization_error",
                        $"Failed to deserialize argument {i} of method '{request.MethodName}'.", ex);
                }
            }

            object? result;
            try
            {
                result = await entry.Invoker(service, args);
            }
            catch (TargetInvocationException ex)
            {
                var real = ex.InnerException ?? ex;
                return CreateError(request.RequestId, "invocation_error", real.Message, real);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return CreateError(request.RequestId, "invocation_error", ex.Message, ex);
            }

            var logicalResultType = GetLogicalResultType(entry.Method.ReturnType);

            return new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ResultTypeName = logicalResultType?.AssemblyQualifiedName ?? logicalResultType?.FullName,
                Result = logicalResultType is null ? null : RpcSerializationHelper.ToElement(result, logicalResultType)
            };
        }
        catch (Exception ex)
        {
            return CreateError(request.RequestId, "server_error", ex.Message, ex);
        }
    }

    private static Type? GetLogicalResultType(Type returnType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task))
            return null;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return returnType.GetGenericArguments()[0];

        return returnType;
    }

    private static RpcResponse CreateError(string requestId, string code, string message, Exception? exception = null)
        => new()
        {
            RequestId = requestId,
            Success = false,
            Error = new RpcError
            {
                Code = code,
                Message = message,
                Details = exception?.ToString(),
                ExceptionType = exception?.GetType().FullName
            }
        };
}
