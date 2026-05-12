using System.Buffers;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.XPacketRpc.Internal;

namespace AsbtCore.Broker.Serialization.XPacketRpc;

/// <summary>
/// Binary <see cref="IRpcSerializer"/> backed by the <c>XPacketRpc</c> library
/// (<see cref="global::XPacketRpc.XPRpc"/>). The whole-envelope path
/// (<c>RpcRequest</c>/<c>RpcResponse</c>) uses generator-emitted codecs; the fragment path
/// uses a per-type compiled invoker (<see cref="FragmentInvokerCache"/>). On construction
/// this serializer ensures the primitive bootstrap codecs are present in the
/// <see cref="global::XPacketRpc.XPRpc"/> static cache.
/// </summary>
public sealed class XPacketRpcSerializer : IRpcSerializer
{
    /// <summary>Wire identifier written to <c>BasicProperties.ContentType</c>.</summary>
    public const string XPacketRpcContentType = "application/x-xpacket-rpc";

    private readonly FragmentInvokerCache fragmentCache = new();

    public XPacketRpcSerializer()
    {
        XPacketRpcPrimitives.EnsureRegistered();
    }

    public string ContentType => XPacketRpcContentType;

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        using var buffer = new global::XPacketRpc.Internal.PooledBufferWriter(ArrayPool<byte>.Shared, 256);
        global::XPacketRpc.XPRpc.Write(value, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => global::XPacketRpc.XPRpc.Read<T>(payload.Span);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return this.fragmentCache.Serialize(value, type);
    }

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return this.fragmentCache.Deserialize(payload, type);
    }

    /// <summary>
    /// Eagerly registers every type reachable from the supplied contract interface.
    /// Useful at startup to amortize first-call cost.
    /// </summary>
    public void Prewarm(Type contractInterface)
        => RpcTypeRegistry.EnsureRegistered(contractInterface);

    /// <summary>Test seam: exposes the fragment-cache build count.</summary>
    internal int FragmentBuildCount => this.fragmentCache.BuildCount;

    /// <summary>Test seam: exposes the fragment cache for concurrency assertions.</summary>
    internal FragmentInvokerCache FragmentCache => this.fragmentCache;
}
