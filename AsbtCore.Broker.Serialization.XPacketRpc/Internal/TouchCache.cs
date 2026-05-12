using System.Collections.Concurrent;
using System.Reflection;
using XPacketRpc;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Internal;

/// <summary>
/// Caches the closed-generic <c>XPRpc.Touch&lt;T&gt;</c> <see cref="MethodInfo"/> per type and
/// invokes it once. The Touch call is a no-op at runtime, but the source generator's
/// call-site analysis observes the <c>T</c> argument and emits the corresponding writer/reader
/// codec into a module initializer. This is how DTO types get registered when the adapter
/// fragments them via <see cref="Type"/> (non-generic <c>SerializeFragment</c>).
/// </summary>
internal static class TouchCache
{
    private static readonly MethodInfo TouchOpen =
        typeof(XPRpc).GetMethod(nameof(XPRpc.Touch), BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("XPRpc.Touch<T> not found.");

    private static readonly ConcurrentDictionary<Type, MethodInfo> closed = new();

    /// <summary>
    /// Returns (and caches) the closed-generic <c>Touch&lt;T&gt;</c> for the given runtime type.
    /// </summary>
    public static MethodInfo Get(Type t)
        => closed.GetOrAdd(t, static x => TouchOpen.MakeGenericMethod(x));

    /// <summary>
    /// Invokes <c>XPRpc.Touch&lt;T&gt;</c> for the given runtime type. The call itself is a no-op;
    /// the generator emits codecs based on its presence at compile time.
    /// </summary>
    public static void Touch(Type t) => Get(t).Invoke(null, null);
}
