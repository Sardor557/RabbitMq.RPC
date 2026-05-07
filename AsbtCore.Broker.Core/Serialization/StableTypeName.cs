using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace AsbtCore.Broker.Core.Serialization;

/// <summary>
/// Builds and resolves wire-stable type identifiers of the form
/// <c>Namespace.Type, AssemblySimpleName</c> (no <c>Version</c>/<c>Culture</c>/<c>PublicKeyToken</c>).
/// Generic instances are encoded as
/// <c>Namespace.OpenType`N[[arg1],[arg2]], AssemblySimpleName</c>.
/// Replaces the v2 <c>TypeNameCache</c>.
/// </summary>
internal static class StableTypeName
{
    private static readonly ConcurrentDictionary<Type, string> writeCache = new();
    private static readonly ConcurrentDictionary<string, Type> readCache  = new(StringComparer.Ordinal);

    internal static string From(Type type) => writeCache.GetOrAdd(type, static t => Build(t));

    internal static Type Resolve(string name) =>
        readCache.GetOrAdd(name, static n =>
            Type.GetType(
                typeName:         n,
                assemblyResolver: ResolveAssembly,
                typeResolver:     ResolveType,
                throwOnError:     true)!);

    private static string Build(Type t)
    {
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            var def  = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            var sb   = new StringBuilder(def.FullName!).Append('[');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(Build(args[i])).Append(']');
            }
            return sb.Append("], ").Append(def.Assembly.GetName().Name).ToString();
        }
        return $"{t.FullName}, {t.Assembly.GetName().Name}";
    }

    private static Assembly? ResolveAssembly(AssemblyName name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.Ordinal))
        ?? Assembly.Load(name.Name!);

    private static Type? ResolveType(Assembly? assembly, string typeName, bool ignoreCase) =>
        assembly?.GetType(typeName, throwOnError: false, ignoreCase) ?? Type.GetType(typeName);
}
