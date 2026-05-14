namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Collections.Concurrent;
using System.Reflection;
using global::MemoryPack;

internal sealed class ReflectionMemoryPackRegistry
{
    public static ReflectionMemoryPackRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, Lazy<bool>> registrations = new();

    // Per-thread set of types currently being registered ON THIS THREAD. Distinguishes
    // same-thread cycle re-entrance (must return early) from cross-thread concurrent
    // access (must wait on Lazy.Value so the formatter is fully registered before we resume).
    [ThreadStatic]
    private static HashSet<Type>? inFlightOnThisThread;

    public void EnsureRegistered(Type type)
    {
        if (type.IsArray)
        {
            EnsureRegistered(type.GetElementType()!);
            return;
        }
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            EnsureRegistered(underlying);
            return;
        }
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type.IsEnum)
        {
            return;
        }
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments()) EnsureRegistered(arg);
            return;
        }

        inFlightOnThisThread ??= new HashSet<Type>();
        if (inFlightOnThisThread.Contains(type))
        {
            // Same-thread re-entrance => cyclic type graph; formatter was registered
            // eagerly by the outer factory call before recursing.
            return;
        }

        var lazy = this.registrations.GetOrAdd(type, t => new Lazy<bool>(
            () =>
            {
                inFlightOnThisThread!.Add(t);
                try { return RegisterCore(t); }
                finally { inFlightOnThisThread.Remove(t); }
            },
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            _ = lazy.Value;
        }
        catch
        {
            this.registrations.TryRemove(type, out _);
            throw;
        }
    }

    private bool RegisterCore(Type type)
    {
        var registerMethod = typeof(ReflectionMemoryPackRegistry)
            .GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);

        try
        {
            registerMethod.Invoke(null, new object[] { this });
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
        return true;
    }

    private static void RegisterTyped<T>(ReflectionMemoryPackRegistry self)
    {
        if (MemoryPackFormatterProvider.IsRegistered<T>()) return;

        var plan = ReflectionMemoryPackPlan<T>.Build();

        // Register formatter EAGERLY before recursing into members.
        // Cyclic member types re-enter EnsureRegistered for T and hit the
        // inFlightOnThisThread early-return; by then the formatter is already
        // registered with MemoryPack, so subsequent serialize lookups succeed.
        MemoryPackFormatterProvider.Register(new ReflectionMemoryPackFormatter<T>(plan));

        foreach (var member in plan.Members)
        {
            self.EnsureRegistered(member.Property.PropertyType);
        }
    }
}
