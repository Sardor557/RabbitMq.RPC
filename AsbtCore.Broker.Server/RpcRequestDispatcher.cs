using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Server;

public sealed class RpcRequestDispatcher
{
    private readonly RpcServerRegistry registry;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IRpcSerializer serializer;

    public RpcRequestDispatcher(
        RpcServerRegistry registry,
        IServiceScopeFactory scopeFactory,
        IRpcSerializer serializer)
    {
        this.registry = registry;
        this.scopeFactory = scopeFactory;
        this.serializer = serializer;
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
                    type = StableTypeName.Resolve(arg.TypeName);
                }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "type_not_found",
                        $"Type '{arg.TypeName}' (argument {i}) could not be resolved.", ex);
                }

                try
                {
                    args[i] = serializer.DeserializeFragment(arg.Payload, type);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return CreateError(request.RequestId, "invocation_error", ex.Message, ex);
            }

            var logicalResultType = entry.LogicalResultType;

            ReadOnlyMemory<byte>? resultPayload;
            if (logicalResultType is null)
            {
                resultPayload = null;
            }
            else
            {
                resultPayload = serializer.SerializeFragment(result, logicalResultType);
            }

            return new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ResultTypeName = logicalResultType is null ? null : StableTypeName.From(logicalResultType),
                Result = resultPayload
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CreateError(request.RequestId, "server_error", ex.Message, ex);
        }
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
