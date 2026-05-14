namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Collections.Concurrent;
using System.Reflection;
using global::MemoryPack;

internal sealed class ReflectionMemoryPackRegistry
{
    public static ReflectionMemoryPackRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, Lazy<bool>> registrations = new();

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

        var lazy = this.registrations.GetOrAdd(type, t => new Lazy<bool>(
            () => RegisterCore(t),
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
        // This breaks cycles: a member type that refers back to T sees T already registered
        // and skips re-registration. Concurrent EnsureRegistered calls now wait on Lazy<bool>
        // until our work completes, and they observe IsRegistered<T>() == true on resume.
        MemoryPackFormatterProvider.Register(new ReflectionMemoryPackFormatter<T>(plan));

        foreach (var member in plan.Members)
        {
            self.EnsureRegistered(member.Property.PropertyType);
        }
    }
}
