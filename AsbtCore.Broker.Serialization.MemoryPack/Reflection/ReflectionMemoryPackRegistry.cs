namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Collections.Concurrent;
using System.Reflection;
using global::MemoryPack;

internal sealed class ReflectionMemoryPackRegistry
{
    public static ReflectionMemoryPackRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, RegistrationState> states = new();

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
            return; // MemoryPack handles primitives/enums/string natively.
        }
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments()) EnsureRegistered(arg);
            return; // collection formatters built-in; element types registered above.
        }

        var current = states.GetOrAdd(type, RegistrationState.Pending);
        if (current == RegistrationState.Registered) return;
        if (current == RegistrationState.InProgress) return;

        lock (states)
        {
            if (states[type] != RegistrationState.Pending) return;
            states[type] = RegistrationState.InProgress;
        }

        var registerMethod = typeof(ReflectionMemoryPackRegistry)
            .GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);

        try
        {
            registerMethod.Invoke(null, new object[] { this });
            states[type] = RegistrationState.Registered;
        }
        catch
        {
            states.TryRemove(type, out _);
            throw;
        }
    }

    private static void RegisterTyped<T>(ReflectionMemoryPackRegistry self)
    {
        if (MemoryPackFormatterProvider.IsRegistered<T>()) return;

        var plan = ReflectionMemoryPackPlan<T>.Build();
        foreach (var member in plan.Members)
        {
            self.EnsureRegistered(member.Property.PropertyType);
        }

        if (!MemoryPackFormatterProvider.IsRegistered<T>())
        {
            MemoryPackFormatterProvider.Register(new ReflectionMemoryPackFormatter<T>(plan));
        }
    }

    private enum RegistrationState : byte
    {
        Pending = 0,
        InProgress = 1,
        Registered = 2,
    }
}
