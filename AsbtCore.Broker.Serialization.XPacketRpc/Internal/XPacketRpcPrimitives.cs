using System.Buffers;
using XPacketRpc;
using XPacketRpc.Internal;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Internal;

/// <summary>
/// Hand-registers <see cref="XPRpc"/> codecs for built-in <c>System.*</c> primitive types.
/// <para>
/// The XPacketRpc source generator deliberately skips any type whose namespace starts with
/// <c>System</c> (see <c>IsBuiltinSkipType</c> in <c>XPacketRpcGenerator.cs</c>) — DTOs work
/// because the generator emits <em>inline</em> reads/writes for primitive properties, but
/// the top-level entrypoint <see cref="XPRpc.Write{T}"/> for primitive <c>T</c> would throw
/// <see cref="MissingSerializerException"/>. This bootstrap closes the gap so that the
/// adapter's <c>SerializeFragment</c>/<c>DeserializeFragment</c> path can handle primitives
/// like <c>int Add(int, int)</c> arguments and <c>Task&lt;Guid&gt;</c> results.
/// </para>
/// <para>
/// The codecs produce <em>byte-for-byte identical wire output</em> to what the generator
/// would emit inline; they call the same <see cref="Writers"/>/<see cref="XPRpcReader"/>
/// helpers used by generated code. This is verified by the round-trip tests.
/// </para>
/// </summary>
internal static class XPacketRpcPrimitives
{
    private static readonly Lazy<bool> initOnce =
        new(Register, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Registers all primitive codecs exactly once across the AppDomain. Thread-safe and
    /// idempotent — repeated calls are no-ops after the first successful registration.
    /// </summary>
    public static void EnsureRegistered() => _ = initOnce.Value;

    private static bool Register()
    {
        // --- integral types ---
        XPRpc.Register<bool>(
            (v, w) => Writers.WriteByte((byte)(v ? 1 : 0), w),
            (ref XPRpcReader r) => r.ReadByte() != 0);

        XPRpc.Register<byte>(
            (v, w) => Writers.WriteByte(v, w),
            (ref XPRpcReader r) => r.ReadByte());

        XPRpc.Register<sbyte>(
            (v, w) => Writers.WriteByte((byte)v, w),
            (ref XPRpcReader r) => (sbyte)r.ReadByte());

        XPRpc.Register<short>(
            (v, w) => Writers.WriteInt16LE(v, w),
            (ref XPRpcReader r) => r.ReadInt16());

        XPRpc.Register<ushort>(
            (v, w) => Writers.WriteUInt16LE(v, w),
            (ref XPRpcReader r) => r.ReadUInt16());

        XPRpc.Register<int>(
            (v, w) => Writers.WriteInt32LE(v, w),
            (ref XPRpcReader r) => r.ReadInt32());

        XPRpc.Register<uint>(
            (v, w) => Writers.WriteUInt32LE(v, w),
            (ref XPRpcReader r) => r.ReadUInt32());

        XPRpc.Register<long>(
            (v, w) => Writers.WriteInt64LE(v, w),
            (ref XPRpcReader r) => r.ReadInt64());

        XPRpc.Register<ulong>(
            (v, w) => Writers.WriteUInt64LE(v, w),
            (ref XPRpcReader r) => r.ReadUInt64());

        // --- floating point + decimal ---
        XPRpc.Register<float>(
            (v, w) => Writers.WriteSingleLE(v, w),
            (ref XPRpcReader r) => r.ReadSingle());

        XPRpc.Register<double>(
            (v, w) => Writers.WriteDoubleLE(v, w),
            (ref XPRpcReader r) => r.ReadDouble());

        XPRpc.Register<decimal>(
            (v, w) => Writers.WriteDecimalLE(v, w),
            (ref XPRpcReader r) => r.ReadDecimal());

        // --- string ---
        XPRpc.Register<string>(
            (v, w) => Writers.WriteString(v ?? string.Empty, w),
            (ref XPRpcReader r) => r.ReadString());

        // --- temporal ---
        XPRpc.Register<DateTime>(
            (v, w) => Writers.WriteDateTime(v, w),
            (ref XPRpcReader r) => r.ReadDateTime());

        XPRpc.Register<DateTimeOffset>(
            (v, w) => Writers.WriteDateTimeOffset(v, w),
            (ref XPRpcReader r) => r.ReadDateTimeOffset());

        XPRpc.Register<TimeSpan>(
            (v, w) => Writers.WriteTimeSpan(v, w),
            (ref XPRpcReader r) => r.ReadTimeSpan());

        // --- identity ---
        XPRpc.Register<Guid>(
            (v, w) => Writers.WriteGuid(v, w),
            (ref XPRpcReader r) => r.ReadGuid());

        // --- byte payloads ---
        XPRpc.Register<byte[]>(
            (v, w) => Writers.WriteBytes(v ?? Array.Empty<byte>(), w),
            (ref XPRpcReader r) => r.ReadBytes());

        // ReadOnlyMemory<byte> shares the byte[] wire format (varint length + raw bytes).
        // This matches what the System.Text.Json adapter exposes for RpcArgument.Payload.
        XPRpc.Register<ReadOnlyMemory<byte>>(
            (v, w) =>
            {
                Writers.WriteVarUInt32((uint)v.Length, w);
                if (v.Length == 0) return;
                var span = w.GetSpan(v.Length);
                v.Span.CopyTo(span);
                w.Advance(v.Length);
            },
            (ref XPRpcReader r) => new ReadOnlyMemory<byte>(r.ReadBytes()));

        return true;
    }
}
