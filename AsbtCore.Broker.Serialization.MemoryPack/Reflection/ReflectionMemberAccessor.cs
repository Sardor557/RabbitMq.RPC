namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Reflection;

internal static class ReflectionMemberAccessor
{
    public static IReadOnlyList<MemberDescriptor> DiscoverMembers(Type type)
    {
        var nullCtx = new NullabilityInfoContext();
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true })
            .OrderBy(p => p.MetadataToken)
            .ToArray();

        var descriptors = new List<MemberDescriptor>(properties.Length);
        foreach (var prop in properties)
        {
            var allowsNull = prop.PropertyType.IsValueType
                ? Nullable.GetUnderlyingType(prop.PropertyType) is not null
                : nullCtx.Create(prop).WriteState != NullabilityState.NotNull;
            descriptors.Add(new MemberDescriptor(prop, allowsNull));
        }
        return descriptors;
    }
}

internal sealed record MemberDescriptor(PropertyInfo Property, bool AllowsNull);
