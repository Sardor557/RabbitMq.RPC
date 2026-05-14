namespace AsbtCore.Broker.Serialization.MemoryPack;

using System.Collections.Concurrent;
using System.Reflection;
using AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;
using AsbtCore.Broker.Serialization.MemoryPack.Reflection;
using global::MemoryPack;

public sealed class MemoryPackRpcOptions
{
    private readonly List<Type> prewarmTypes = new();
    private readonly List<(Assembly Asm, Func<Type, bool>? Filter)> prewarmAssemblies = new();
    private readonly List<(Action Register, IReadOnlyList<Type> DerivedTypes)> unionRegistrations = new();

    private static readonly ConcurrentDictionary<Type, byte> RegisteredUnionBases = new();

    public MemoryPackRpcOptions PrewarmType<T>()
    {
        prewarmTypes.Add(typeof(T));
        return this;
    }

    public MemoryPackRpcOptions PrewarmTypes(params Type[] types)
    {
        prewarmTypes.AddRange(types);
        return this;
    }

    public MemoryPackRpcOptions PrewarmAssembly(Assembly asm, Func<Type, bool>? filter = null)
    {
        prewarmAssemblies.Add((asm, filter));
        return this;
    }

    public MemoryPackRpcOptions RegisterUnion<TBase>(Action<UnionBuilder<TBase>> configure)
        where TBase : class
    {
        var builder = new UnionBuilder<TBase>();
        configure(builder); // synchronous — duplicate-tag errors surface here
        var derivedTypes = builder.DerivedTypes;
        unionRegistrations.Add((() =>
        {
            if (!RegisteredUnionBases.TryAdd(typeof(TBase), 0))
                throw new InvalidOperationException(
                    $"Union for type {typeof(TBase).FullName} is already registered.");
            MemoryPackFormatterProvider.Register(builder.Build());
        }, derivedTypes));
        return this;
    }

    internal void Apply(ReflectionMemoryPackRegistry registry)
    {
        foreach (var (register, derivedTypes) in unionRegistrations)
        {
            register();
            foreach (var t in derivedTypes) registry.EnsureRegistered(t);
        }
        foreach (var type in prewarmTypes) registry.EnsureRegistered(type);
        foreach (var (asm, filter) in prewarmAssemblies)
        {
            foreach (var t in asm.GetTypes())
            {
                if (!t.IsClass || t.IsAbstract) continue;
                if (filter is not null && !filter(t)) continue;
                registry.EnsureRegistered(t);
            }
        }
    }

    // For test isolation only — resets the process-wide union-tracking set.
    internal static void ResetRegisteredUnionsForTests()
    {
        RegisteredUnionBases.Clear();
    }
}
