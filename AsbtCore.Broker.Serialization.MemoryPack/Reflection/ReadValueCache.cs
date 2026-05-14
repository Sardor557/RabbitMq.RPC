namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using global::MemoryPack;

internal delegate TResult RefFunc<T1, TResult>(ref T1 arg) where T1 : allows ref struct;

internal static class ReadValueCache
{
    private static readonly ConcurrentDictionary<Type, RefFunc<MemoryPackReader, object?>> Cache = new();

    public static RefFunc<MemoryPackReader, object?> Get(Type type)
        => Cache.GetOrAdd(type, BuildReader);

    private static RefFunc<MemoryPackReader, object?> BuildReader(Type type)
    {
        var readMethod = typeof(MemoryPackReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == nameof(MemoryPackReader.ReadValue)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsByRef)
            .MakeGenericMethod(type);

        var reader = Expression.Parameter(typeof(MemoryPackReader).MakeByRefType(), "reader");
        var tmp = Expression.Variable(type, "tmp");
        var defaultValue = Expression.Default(type);
        var call = Expression.Call(reader, readMethod, tmp);
        var boxed = Expression.Convert(tmp, typeof(object));
        var body = Expression.Block(
            variables: new[] { tmp },
            Expression.Assign(tmp, defaultValue),
            call,
            boxed);
        return Expression.Lambda<RefFunc<MemoryPackReader, object?>>(body, reader).Compile();
    }
}
