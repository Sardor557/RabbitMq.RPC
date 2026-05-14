namespace AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;

using global::MemoryPack.Formatters;

public sealed class UnionBuilder<TBase> where TBase : class
{
    private readonly List<(ushort Tag, Type DerivedType)> entries = new();
    private readonly HashSet<ushort> tags = new();
    private readonly HashSet<Type> types = new();

    public UnionBuilder<TBase> Add<TDerived>(ushort tag) where TDerived : TBase
    {
        if (!tags.Add(tag))
            throw new InvalidOperationException(
                $"Tag {tag} already mapped on union {typeof(TBase).FullName}.");
        var derived = typeof(TDerived);
        if (!types.Add(derived))
            throw new InvalidOperationException(
                $"Type {derived.FullName} already mapped on union {typeof(TBase).FullName}.");
        entries.Add((tag, derived));
        return this;
    }

    internal DynamicUnionFormatter<TBase> Build() => new(entries.ToArray());
}
