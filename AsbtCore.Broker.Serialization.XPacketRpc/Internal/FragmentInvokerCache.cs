using System.Buffers;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using XPacketRpc;
using XPacketRpc.Internal;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Internal;

/// <summary>
/// Per-type compiled writer/reader for use by <c>SerializeFragment</c>/<c>DeserializeFragment</c>.
/// </summary>
internal sealed class FragmentInvoker
{
    public required Action<object?, IBufferWriter<byte>> Write { get; init; }
    public required Func<ReadOnlyMemory<byte>, object?> Read { get; init; }
}

/// <summary>
/// Compiles and caches one <see cref="FragmentInvoker"/> per runtime type, ensuring the
/// (relatively expensive) Expression compilation runs exactly once per type even under
/// concurrent first-touch from many threads.
/// <para>
/// Concurrency strategy: <see cref="ConcurrentDictionary{TKey, TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
/// may invoke its factory multiple times under contention; wrapping the factory in
/// <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
/// guarantees the underlying build runs exactly once.
/// </para>
/// </summary>
internal sealed class FragmentInvokerCache
{
    private readonly ConcurrentDictionary<Type, Lazy<FragmentInvoker>> cache = new();
    private readonly Func<Type, FragmentInvoker> builder;
    private int buildCount;

    public FragmentInvokerCache() : this(BuildDefault) { }

    /// <summary>Test seam: lets the concurrency test inject a counting builder.</summary>
    internal FragmentInvokerCache(Func<Type, FragmentInvoker> builder)
    {
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <summary>Total number of factory invocations across the cache's lifetime (test diagnostic).</summary>
    internal int BuildCount => System.Threading.Volatile.Read(ref this.buildCount);

    public FragmentInvoker GetOrBuild(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var lazy = this.cache.GetOrAdd(type, t =>
            new Lazy<FragmentInvoker>(() =>
            {
                System.Threading.Interlocked.Increment(ref this.buildCount);
                return this.builder(t);
            }, LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    // --- default builder: uses XPRpc.Write<T> / XPRpc.Read<T> via reflection-bound generics ---

    private static readonly MethodInfo WriteOpen =
        typeof(XPRpc).GetMethod(nameof(XPRpc.Write), BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("XPRpc.Write<T> not found.");

    private static readonly MethodInfo ReadOpen =
        typeof(XPRpc).GetMethod(nameof(XPRpc.Read), BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("XPRpc.Read<T> not found.");

    private static FragmentInvoker BuildDefault(Type t)
    {
        var writeClosed = WriteOpen.MakeGenericMethod(t);
        var readClosed = ReadOpen.MakeGenericMethod(t);

        // Compiled write: (object? value, IBufferWriter<byte> w) => XPRpc.Write<T>((T)value!, w);
        var valueParam = Expression.Parameter(typeof(object), "value");
        var writerParam = Expression.Parameter(typeof(IBufferWriter<byte>), "w");
        var castValue = t.IsValueType
            ? Expression.Unbox(valueParam, t)
            : Expression.Convert(valueParam, t);
        var writeCall = Expression.Call(writeClosed, castValue, writerParam);
        var writeLambda = Expression.Lambda<Action<object?, IBufferWriter<byte>>>(
            writeCall, valueParam, writerParam).Compile();

        // Compiled read: (ReadOnlyMemory<byte> mem) => (object?) XPRpc.Read<T>(mem.Span);
        var memParam = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "mem");
        var spanProp = typeof(ReadOnlyMemory<byte>).GetProperty(nameof(ReadOnlyMemory<byte>.Span))!;
        var spanExpr = Expression.Property(memParam, spanProp);
        var readCall = Expression.Call(readClosed, spanExpr);
        var box = Expression.Convert(readCall, typeof(object));
        var readLambda = Expression.Lambda<Func<ReadOnlyMemory<byte>, object?>>(
            box, memParam).Compile();

        return new FragmentInvoker { Write = writeLambda, Read = readLambda };
    }

    /// <summary>
    /// Convenience: serialize a fragment value of the given runtime <paramref name="type"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Serialize(object? value, Type type)
    {
        var invoker = GetOrBuild(type);
        using var buffer = new PooledBufferWriter(ArrayPool<byte>.Shared, 64);
        invoker.Write(value, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Convenience: deserialize a fragment of the given runtime <paramref name="type"/>.
    /// </summary>
    public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
    {
        var invoker = GetOrBuild(type);
        return invoker.Read(payload);
    }
}
