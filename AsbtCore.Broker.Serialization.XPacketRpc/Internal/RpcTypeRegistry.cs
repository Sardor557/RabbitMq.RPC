using System.Collections.Concurrent;
using System.Reflection;
using XPacketRpc;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Internal;

/// <summary>
/// Walks an RPC contract interface and ensures every type that travels on the wire is
/// known to <see cref="XPRpc"/>. The walk covers:
/// <list type="bullet">
///   <item>every parameter type of every method,</item>
///   <item>the return type (unwrapped from <c>Task</c>/<c>ValueTask</c>),</item>
///   <item>recursively all referenced argument/result types, including generic arguments
///         of nested collections and dictionaries.</item>
/// </list>
/// Primitives are pre-registered by <see cref="XPacketRpcPrimitives"/>. DTOs require the
/// XPacketRpc source generator to have observed a <c>Touch&lt;T&gt;</c> call site in the
/// owning compilation — this class drives <see cref="TouchCache.Touch"/> for every reachable
/// non-primitive type.
/// </summary>
internal static class RpcTypeRegistry
{
    private static readonly ConcurrentDictionary<Type, bool> registered = new();
    private static readonly ConcurrentDictionary<Type, bool> visited = new();

    /// <summary>
    /// Ensures both primitive bootstrap and DTO touches have run for the closure
    /// reachable from the supplied interface type.
    /// </summary>
    public static void EnsureRegistered(Type contractInterface)
    {
        ArgumentNullException.ThrowIfNull(contractInterface);
        XPacketRpcPrimitives.EnsureRegistered();

        foreach (var m in contractInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var p in m.GetParameters())
            {
                Visit(p.ParameterType);
            }
            Visit(UnwrapAsync(m.ReturnType));
        }
    }

    /// <summary>True when <paramref name="t"/> has a recorded codec (primitive or DTO).</summary>
    public static bool IsRegistered(Type t) => registered.ContainsKey(t);

    private static void Visit(Type t)
    {
        if (t is null || t == typeof(void)) return;
        if (!visited.TryAdd(t, true)) return;

        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null)
        {
            Visit(underlying);
            return;
        }

        if (IsPrimitiveOrSystemBuiltin(t))
        {
            registered.TryAdd(t, true);
            return;
        }

        if (t.IsArray)
        {
            var elem = t.GetElementType();
            if (elem is not null) Visit(elem);
            registered.TryAdd(t, true);
            return;
        }

        if (t.IsConstructedGenericType)
        {
            foreach (var arg in t.GetGenericArguments()) Visit(arg);
        }

        // DTO / user type: drive the generator via Touch<T>().
        // If the generator emitted a codec, it will be registered before the
        // serializer is first invoked (module initializer time).
        TouchCache.Touch(t);
        registered.TryAdd(t, true);
    }

    private static Type UnwrapAsync(Type t)
    {
        if (!t.IsGenericType) return t == typeof(Task) || t == typeof(ValueTask) ? typeof(void) : t;
        var def = t.GetGenericTypeDefinition();
        if (def == typeof(Task<>) || def == typeof(ValueTask<>))
            return t.GetGenericArguments()[0];
        return t;
    }

    private static bool IsPrimitiveOrSystemBuiltin(Type t)
    {
        if (t.IsPrimitive) return true;
        if (t == typeof(string) || t == typeof(decimal) || t == typeof(Guid)
            || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan)
            || t == typeof(byte[]) || t == typeof(ReadOnlyMemory<byte>))
            return true;
        return false;
    }
}
