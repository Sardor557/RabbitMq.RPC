using System.Collections.Concurrent;

namespace AsbtCore.Broker.Core.Serialization;

internal static class TypeNameCache
{
    private static readonly ConcurrentDictionary<string, Type> cache = new(StringComparer.Ordinal);

    internal static Type Resolve(string typeName)
        => cache.GetOrAdd(typeName, static n => Type.GetType(n, throwOnError: true)!);
}
