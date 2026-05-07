using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace AsbtCore.Broker.Client;

internal delegate object RpcClientInvocation(
    RpcClient client,
    Type interfaceType,
    object[] args,
    TimeSpan? timeout,
    CancellationToken cancellationToken);

internal static class RpcClientInvokerCache
{
    private static readonly ConcurrentDictionary<MethodInfo, RpcClientInvocation> cache = new();

    internal static RpcClientInvocation Get(MethodInfo method)
        => cache.GetOrAdd(method, BuildInvocation);

    private static RpcClientInvocation BuildInvocation(MethodInfo method)
    {
        var returnType = method.ReturnType;

        if (returnType == typeof(Task))
            return BuildVoidInvocation(method);

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return BuildGenericInvocation(method, returnType.GetGenericArguments()[0]);

        throw new NotSupportedException(
            $"Remote method '{method.Name}' must return Task or Task<T>; got {returnType}.");
    }

    private static RpcClientInvocation BuildVoidInvocation(MethodInfo method)
    {
        var voidAsync = typeof(RpcClient)
            .GetMethod("InvokeVoidAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return BuildLambda(method, voidAsync);
    }

    private static RpcClientInvocation BuildGenericInvocation(MethodInfo method, Type resultType)
    {
        var genericAsync = typeof(RpcClient)
            .GetMethod("InvokeGenericAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType);
        return BuildLambda(method, genericAsync);
    }

    private static RpcClientInvocation BuildLambda(MethodInfo interfaceMethod, MethodInfo target)
    {
        var clientParam = Expression.Parameter(typeof(RpcClient), "client");
        var interfaceTypeParam = Expression.Parameter(typeof(Type), "interfaceType");
        var argsParam = Expression.Parameter(typeof(object[]), "args");
        var timeoutParam = Expression.Parameter(typeof(TimeSpan?), "timeout");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var call = Expression.Call(
            clientParam,
            target,
            interfaceTypeParam,
            Expression.Constant(interfaceMethod, typeof(MethodInfo)),
            argsParam,
            timeoutParam,
            ctParam);

        var body = Expression.Convert(call, typeof(object));
        return Expression.Lambda<RpcClientInvocation>(body,
            clientParam, interfaceTypeParam, argsParam, timeoutParam, ctParam).Compile();
    }
}
