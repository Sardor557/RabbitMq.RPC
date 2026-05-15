using System.Linq.Expressions;
using System.Reflection;

namespace AsbtCore.Broker.Server;

internal delegate Task<object?> RpcMethodInvocation(object instance, object?[] args);

internal static class RpcServerMethodInvoker
{
    internal static RpcMethodInvocation Build(MethodInfo method)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var argsParam = Expression.Parameter(typeof(object?[]), "args");

        var parameters = method.GetParameters();
        var argExpressions = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
            argExpressions[i] = Expression.Convert(arrayAccess, parameters[i].ParameterType);
        }

        Expression? instanceExpr = method.IsStatic
            ? null
            : Expression.Convert(instanceParam, method.DeclaringType!);

        Expression call = Expression.Call(instanceExpr, method, argExpressions);

        var returnType = method.ReturnType;

        Expression body;

        if (returnType == typeof(void))
        {
            body = Expression.Block(
                call,
                Expression.Constant(Task.FromResult<object?>(null), typeof(Task<object?>)));
        }
        else if (returnType == typeof(Task))
        {
            var nonGenericAdaptor = typeof(RpcServerMethodInvoker)
                .GetMethod(nameof(WrapNonGenericTask), BindingFlags.NonPublic | BindingFlags.Static)!;
            body = Expression.Call(nonGenericAdaptor, call);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var genericAdaptor = typeof(RpcServerMethodInvoker)
                .GetMethod(nameof(WrapGenericTask), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            body = Expression.Call(genericAdaptor, call);
        }
        else
        {
            // Sync non-task return
            var castResult = Expression.Convert(call, typeof(object));
            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(typeof(object));
            body = Expression.Call(fromResult, castResult);
        }

        var lambda = Expression.Lambda<RpcMethodInvocation>(body, instanceParam, argsParam);
        return lambda.Compile();
    }

    private static async Task<object?> WrapNonGenericTask(Task task)
    {
        await task.ConfigureAwait(false);
        return null;
    }

    private static async Task<object?> WrapGenericTask<T>(Task<T> task)
    {
        var result = await task.ConfigureAwait(false);
        return result;
    }
}
