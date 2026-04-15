using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AsbtCore.Broker.Server
{
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
                    return CreateError(
                        request.RequestId,
                        "service_not_found",
                        $"Service '{request.InterfaceName}' not found.");
                }

                var parameterTypeNames = request.Arguments
                    .Select(x => x.TypeName)
                    .ToArray();

                if (!descriptor.TryGetMethod(request.MethodName, parameterTypeNames, out var method))
                {
                    return CreateError(
                        request.RequestId,
                        "method_not_found",
                        $"Method '{request.MethodName}' with specified signature was not found.");
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService(descriptor.ImplementationType);

                var args = request.Arguments
                    .Select(serializer.UnpackArgument)
                    .ToArray();

                var result = await InvokeMethodAsync(service, method, args);
                var logicalResultType = GetLogicalResultType(method.ReturnType);

                return new RpcResponse
                {
                    RequestId = request.RequestId,
                    Success = true,
                    ResultTypeName = logicalResultType?.AssemblyQualifiedName ?? logicalResultType?.FullName,
                    Result = logicalResultType is null
                        ? null
                        : serializer.PackResult(result, logicalResultType)
                };
            }
            catch (TargetInvocationException ex)
            {
                var real = ex.InnerException ?? ex;
                return CreateError(request.RequestId, "invocation_error", real.Message, real);
            }
            catch (Exception ex)
            {
                return CreateError(request.RequestId, "server_error", ex.Message, ex);
            }
        }

        private static async Task<object?> InvokeMethodAsync(object instance, MethodInfo method, object?[] args)
        {
            var result = method.Invoke(instance, args);

            if (result is Task task)
            {
                await task.ConfigureAwait(false);

                if (method.ReturnType.IsGenericType &&
                    method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    return task.GetType().GetProperty("Result")!.GetValue(task);
                }

                return null;
            }

            return result;
        }

        private static Type? GetLogicalResultType(Type returnType)
        {
            if (returnType == typeof(void) || returnType == typeof(Task))
                return null;

            if (returnType.IsGenericType &&
                returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return returnType.GetGenericArguments()[0];
            }

            return returnType;
        }

        private static RpcResponse CreateError(
            string requestId,
            string code,
            string message,
            Exception? exception = null)
        {
            return new RpcResponse
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
    }
}
