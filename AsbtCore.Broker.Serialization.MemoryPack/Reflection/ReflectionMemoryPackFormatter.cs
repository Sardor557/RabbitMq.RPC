namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using global::MemoryPack;

internal sealed class ReflectionMemoryPackFormatter<T> : MemoryPackFormatter<T>
{
    private readonly ReflectionMemoryPackPlan<T> plan;

    public ReflectionMemoryPackFormatter(ReflectionMemoryPackPlan<T> plan)
    {
        this.plan = plan;
    }

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }
        writer.WriteObjectHeader((byte)plan.Members.Count);
        foreach (var member in plan.Members)
        {
            var memberValue = member.Property.GetValue(value);
            MemoryPackSerializer.Serialize(member.Property.PropertyType, ref writer, memberValue);
        }
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = default;
            return;
        }
        if (count != plan.Members.Count)
        {
            throw new MemoryPackSerializationException(
                $"Member count mismatch for {typeof(T).FullName}: payload has {count}, expected {plan.Members.Count}.");
        }

        var values = new object?[plan.Members.Count];
        for (int i = 0; i < plan.Members.Count; i++)
        {
            var read = ReadValueCache.Get(plan.Members[i].Property.PropertyType);
            values[i] = read(ref reader);
        }

        object?[] ctorArgs = plan.CtorParameterCount > 0
            ? new object?[plan.CtorParameterCount]
            : System.Array.Empty<object?>();
        for (int i = 0; i < plan.Members.Count; i++)
        {
            if (plan.CtorMemberIndices[i] >= 0)
            {
                ctorArgs[plan.CtorMemberIndices[i]] = values[i];
            }
        }

        value = plan.Activator(ctorArgs);

        for (int i = 0; i < plan.Members.Count; i++)
        {
            if (plan.CtorMemberIndices[i] < 0 && plan.HasSetter[i])
            {
                plan.Members[i].Property.SetValue(value, values[i]);
            }
        }
    }
}
