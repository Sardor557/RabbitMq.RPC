namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Linq.Expressions;
using System.Reflection;

internal sealed class ReflectionMemoryPackPlan<T>
{
    public IReadOnlyList<MemberDescriptor> Members { get; }
    public Func<object?[], T> Activator { get; }
    public int[] CtorMemberIndices { get; }
    public bool[] HasSetter { get; }
    public int CtorParameterCount { get; }

    private ReflectionMemoryPackPlan(
        IReadOnlyList<MemberDescriptor> members,
        Func<object?[], T> activator,
        int[] ctorIndices,
        bool[] hasSetter,
        int ctorParameterCount)
    {
        this.Members = members;
        this.Activator = activator;
        this.CtorMemberIndices = ctorIndices;
        this.HasSetter = hasSetter;
        this.CtorParameterCount = ctorParameterCount;
    }

    public static ReflectionMemoryPackPlan<T> Build()
    {
        var type = typeof(T);
        if (type.IsAbstract || type.IsInterface)
            throw new InvalidOperationException(
                $"Type {type.FullName} is abstract or an interface; register a union via MemoryPackRpcOptions.RegisterUnion<TBase>.");
        if (type.IsGenericTypeDefinition)
            throw new InvalidOperationException(
                $"Open generic type {type.FullName} cannot be registered. Provide a closed generic type.");

        var members = ReflectionMemberAccessor.DiscoverMembers(type);
        var ctorIndices = new int[members.Count];
        Array.Fill(ctorIndices, -1);
        var hasSetter = members.Select(m => m.Property.SetMethod?.IsPublic == true).ToArray();
        var ctorParamCount = 0;

        Func<object?[], T> activator;
        var parameterless = type.GetConstructor(Type.EmptyTypes);
        if (parameterless is not null)
        {
            activator = _ => (T)parameterless.Invoke(null)!;
        }
        else
        {
            var bestCtor = SelectBestMatchingCtor(type, members)
                ?? throw new InvalidOperationException(
                    $"Type {type.FullName} has no usable constructor.");
            var parameters = bestCtor.GetParameters();
            ctorParamCount = parameters.Length;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var matchIndex = -1;
                for (int j = 0; j < members.Count; j++)
                {
                    if (string.Equals(members[j].Property.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchIndex = j;
                        break;
                    }
                }
                if (matchIndex < 0)
                    throw new InvalidOperationException(
                        $"Constructor parameter '{p.Name}' on {type.FullName} does not match any public property.");
                ctorIndices[matchIndex] = i;
            }
            activator = BuildCtorInvoker(bestCtor);
        }

        return new ReflectionMemoryPackPlan<T>(members, activator, ctorIndices, hasSetter, ctorParamCount);
    }

    private static ConstructorInfo? SelectBestMatchingCtor(Type type, IReadOnlyList<MemberDescriptor> members)
    {
        var memberNames = new HashSet<string>(
            members.Select(m => m.Property.Name), StringComparer.OrdinalIgnoreCase);
        return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().All(p => p.Name is not null && memberNames.Contains(p.Name)))
            .OrderByDescending(c => c.GetParameters().Length)
            .ThenBy(c => c.MetadataToken)
            .FirstOrDefault();
    }

    private static Func<object?[], T> BuildCtorInvoker(ConstructorInfo ctor)
    {
        var args = Expression.Parameter(typeof(object?[]), "args");
        var parameters = ctor.GetParameters();
        var ctorArgs = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var indexed = Expression.ArrayIndex(args, Expression.Constant(i));
            ctorArgs[i] = Expression.Convert(indexed, parameters[i].ParameterType);
        }
        var newExpr = Expression.New(ctor, ctorArgs);
        return Expression.Lambda<Func<object?[], T>>(newExpr, args).Compile();
    }
}
