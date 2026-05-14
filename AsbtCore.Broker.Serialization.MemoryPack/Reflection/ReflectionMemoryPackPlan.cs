namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Linq.Expressions;
using System.Reflection;

internal sealed class ReflectionMemoryPackPlan<T>
{
    public IReadOnlyList<MemberDescriptor> Members { get; }
    public Func<object?[], T> Activator { get; }

    private ReflectionMemoryPackPlan(IReadOnlyList<MemberDescriptor> members, Func<object?[], T> activator)
    {
        this.Members = members;
        this.Activator = activator;
    }

    public static ReflectionMemoryPackPlan<T> Build()
    {
        var type = typeof(T);
        if (type.IsAbstract || type.IsInterface)
        {
            throw new InvalidOperationException(
                $"Type {type.FullName} is abstract or an interface; register a union via MemoryPackRpcOptions.RegisterUnion<TBase>.");
        }
        if (type.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"Open generic type {type.FullName} cannot be registered. Provide a closed generic type.");
        }

        var members = ReflectionMemberAccessor.DiscoverMembers(type);
        var ctor = type.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Type {type.FullName} has no usable constructor. Parameterless ctor required (records / init-only support added later).");

        var argsParam = Expression.Parameter(typeof(object?[]), "args");
        var activator = Expression.Lambda<Func<object?[], T>>(Expression.New(ctor), argsParam).Compile();
        return new ReflectionMemoryPackPlan<T>(members, activator);
    }
}
