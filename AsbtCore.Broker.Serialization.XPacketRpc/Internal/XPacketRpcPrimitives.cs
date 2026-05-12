using System.Buffers;
using AsbtCore.Broker.Core;
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

        // --- envelope DTOs (Core.RpcContracts) ---
        //
        // The XPacketRpc source generator cannot emit codecs for the envelope types because
        // it has no built-in WireKind for ReadOnlyMemory<byte> (the type used by
        // RpcArgument.Payload and RpcResponse.Result). Generated code would reference a
        // non-existent __XPRpcGen_ReadOnlyMemory class. Furthermore, the generator only emits
        // for types that appear in a Touch<T>() call site, and the envelope types are never
        // touched (intentionally — this adapter owns their codecs).
        //
        // We hand-register them here so that the serializer's whole-envelope path
        // (XPRpc.Write<RpcRequest>/Read<RpcResponse>) just works. Because no generator
        // emit exists for these types, no module-initializer can later overwrite
        // Cache<T>.Writer — our registration is the only one and wins by default.
        //
        // Wire formats (self-consistent: both ends are this codec, so we don't need to match
        // any generator-produced bitmap layout — a single bool tag per nullable is enough):
        //
        //   RpcArgument := WriteString(TypeName) WriteBytes(Payload)
        //   RpcError    := WriteString(Code) WriteString(Message)
        //                  bool(Details.HasValue)      [WriteString(Details)]?
        //                  bool(ExceptionType.HasValue)[WriteString(ExceptionType)]?
        //   RpcRequest  := WriteString(RequestId) WriteString(InterfaceName) WriteString(MethodName)
        //                  WriteVarUInt32(Arguments.Count) RpcArgument[Count]
        //   RpcResponse := WriteString(RequestId) bool(Success)
        //                  bool(ResultTypeName.HasValue)[WriteString(ResultTypeName)]?
        //                  bool(Result.HasValue)       [WriteBytes(Result.Value)]?
        //                  bool(Error != null)         [RpcError]?

        // Local delegates captured *before* Register so nested codecs (RpcRequest -> RpcArgument)
        // invoke directly via ref reader / writer, avoiding any reflection round-trip.
        XPRpc.WriteDelegate<RpcArgument> writeArg = (arg, w) =>
        {
            Writers.WriteString(arg.TypeName ?? string.Empty, w);
            // Reuse the ReadOnlyMemory<byte> wire format defined above.
            var payload = arg.Payload;
            Writers.WriteVarUInt32((uint)payload.Length, w);
            if (payload.Length > 0)
            {
                var span = w.GetSpan(payload.Length);
                payload.Span.CopyTo(span);
                w.Advance(payload.Length);
            }
        };
        XPRpc.ReadDelegate<RpcArgument> readArg = (ref XPRpcReader r) => new RpcArgument
        {
            TypeName = r.ReadString(),
            Payload = new ReadOnlyMemory<byte>(r.ReadBytes()),
        };
        XPRpc.Register<RpcArgument>(writeArg, readArg);

        XPRpc.WriteDelegate<RpcError> writeErr = (err, w) =>
        {
            Writers.WriteString(err.Code ?? string.Empty, w);
            Writers.WriteString(err.Message ?? string.Empty, w);
            WriteNullableString(err.Details, w);
            WriteNullableString(err.ExceptionType, w);
        };
        XPRpc.ReadDelegate<RpcError> readErr = (ref XPRpcReader r) => new RpcError
        {
            Code = r.ReadString(),
            Message = r.ReadString(),
            Details = ReadNullableString(ref r),
            ExceptionType = ReadNullableString(ref r),
        };
        XPRpc.Register<RpcError>(writeErr, readErr);

        XPRpc.Register<RpcRequest>(
            (req, w) =>
            {
                Writers.WriteString(req.RequestId ?? string.Empty, w);
                Writers.WriteString(req.InterfaceName ?? string.Empty, w);
                Writers.WriteString(req.MethodName ?? string.Empty, w);
                var args = req.Arguments ?? new List<RpcArgument>();
                Writers.WriteVarUInt32((uint)args.Count, w);
                for (int i = 0; i < args.Count; i++)
                {
                    writeArg(args[i], w);
                }
            },
            (ref XPRpcReader r) =>
            {
                var req = new RpcRequest
                {
                    RequestId = r.ReadString(),
                    InterfaceName = r.ReadString(),
                    MethodName = r.ReadString(),
                };
                uint count = r.ReadVarUInt32();
                var list = new List<RpcArgument>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    list.Add(readArg(ref r));
                }
                req.Arguments = list;
                return req;
            });

        XPRpc.Register<RpcResponse>(
            (resp, w) =>
            {
                Writers.WriteString(resp.RequestId ?? string.Empty, w);
                Writers.WriteByte((byte)(resp.Success ? 1 : 0), w);
                WriteNullableString(resp.ResultTypeName, w);

                if (resp.Result.HasValue)
                {
                    Writers.WriteByte(1, w);
                    var payload = resp.Result.Value;
                    Writers.WriteVarUInt32((uint)payload.Length, w);
                    if (payload.Length > 0)
                    {
                        var span = w.GetSpan(payload.Length);
                        payload.Span.CopyTo(span);
                        w.Advance(payload.Length);
                    }
                }
                else
                {
                    Writers.WriteByte(0, w);
                }

                if (resp.Error is not null)
                {
                    Writers.WriteByte(1, w);
                    writeErr(resp.Error, w);
                }
                else
                {
                    Writers.WriteByte(0, w);
                }
            },
            (ref XPRpcReader r) =>
            {
                var resp = new RpcResponse
                {
                    RequestId = r.ReadString(),
                    Success = r.ReadByte() != 0,
                    ResultTypeName = ReadNullableString(ref r),
                };
                bool hasResult = r.ReadByte() != 0;
                if (hasResult)
                {
                    resp.Result = new ReadOnlyMemory<byte>(r.ReadBytes());
                }
                bool hasError = r.ReadByte() != 0;
                if (hasError)
                {
                    resp.Error = readErr(ref r);
                }
                return resp;
            });

        return true;
    }

    private static void WriteNullableString(string? value, IBufferWriter<byte> w)
    {
        if (value is null)
        {
            Writers.WriteByte(0, w);
        }
        else
        {
            Writers.WriteByte(1, w);
            Writers.WriteString(value, w);
        }
    }

    private static string? ReadNullableString(ref XPRpcReader r)
        => r.ReadByte() == 0 ? null : r.ReadString();
}
