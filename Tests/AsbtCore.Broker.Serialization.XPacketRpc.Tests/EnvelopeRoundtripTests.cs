using AsbtCore.Broker.Core;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// Round-trip tests for the hand-registered envelope codecs (<see cref="RpcRequest"/> and
/// <see cref="RpcResponse"/>). The XPacketRpc source generator cannot emit codecs for these
/// types because they expose <c>ReadOnlyMemory&lt;byte&gt;</c>/<c>ReadOnlyMemory&lt;byte&gt;?</c>
/// members; the codecs live in <see cref="Internal.XPacketRpcPrimitives"/>'s bootstrap.
/// </summary>
public sealed class EnvelopeRoundtripTests
{
    [Test]
    public async Task RpcRequest_With_ThreeArguments_Roundtrips()
    {
        var sut = new XPacketRpcSerializer();
        var req = new RpcRequest
        {
            RequestId = "req-42",
            InterfaceName = "AsbtCore.Broker.Tests.ISample",
            MethodName = "Frob",
            Arguments = new List<RpcArgument>
            {
                new() { TypeName = "System.Int32", Payload = new ReadOnlyMemory<byte>(new byte[] { 0x01, 0x00, 0x00, 0x00 }) },
                new() { TypeName = "System.String", Payload = new ReadOnlyMemory<byte>(new byte[] { 0x68, 0x69 }) },
                new() { TypeName = "System.Byte[]", Payload = new ReadOnlyMemory<byte>(Array.Empty<byte>()) },
            },
        };

        var bytes = sut.Serialize(req);
        var back = sut.Deserialize<RpcRequest>(bytes);

        await Assert.That(back).IsNotNull();
        await Assert.That(back!.RequestId).IsEqualTo(req.RequestId);
        await Assert.That(back.InterfaceName).IsEqualTo(req.InterfaceName);
        await Assert.That(back.MethodName).IsEqualTo(req.MethodName);
        await Assert.That(back.Arguments.Count).IsEqualTo(3);
        for (int i = 0; i < 3; i++)
        {
            await Assert.That(back.Arguments[i].TypeName).IsEqualTo(req.Arguments[i].TypeName);
            await Assert.That(back.Arguments[i].Payload.ToArray().SequenceEqual(req.Arguments[i].Payload.ToArray())).IsTrue();
        }
    }

    [Test]
    public async Task RpcRequest_With_Zero_Arguments_Roundtrips()
    {
        var sut = new XPacketRpcSerializer();
        var req = new RpcRequest
        {
            RequestId = "req-empty",
            InterfaceName = "I",
            MethodName = "Ping",
            Arguments = new List<RpcArgument>(),
        };

        var bytes = sut.Serialize(req);
        var back = sut.Deserialize<RpcRequest>(bytes)!;

        await Assert.That(back.RequestId).IsEqualTo("req-empty");
        await Assert.That(back.MethodName).IsEqualTo("Ping");
        await Assert.That(back.Arguments).IsNotNull();
        await Assert.That(back.Arguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RpcResponse_Success_With_Result_Roundtrips()
    {
        var sut = new XPacketRpcSerializer();
        var resultBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var resp = new RpcResponse
        {
            RequestId = "req-99",
            Success = true,
            ResultTypeName = "System.Int32",
            Result = new ReadOnlyMemory<byte>(resultBytes),
            Error = null,
        };

        var bytes = sut.Serialize(resp);
        var back = sut.Deserialize<RpcResponse>(bytes)!;

        await Assert.That(back.RequestId).IsEqualTo("req-99");
        await Assert.That(back.Success).IsTrue();
        await Assert.That(back.ResultTypeName).IsEqualTo("System.Int32");
        await Assert.That(back.Result.HasValue).IsTrue();
        await Assert.That(back.Result!.Value.ToArray().SequenceEqual(resultBytes)).IsTrue();
        await Assert.That(back.Error).IsNull();
    }

    [Test]
    public async Task RpcResponse_Failure_With_Error_Roundtrips()
    {
        var sut = new XPacketRpcSerializer();
        var resp = new RpcResponse
        {
            RequestId = "req-err",
            Success = false,
            ResultTypeName = null,
            Result = null,
            Error = new RpcError
            {
                Code = "REMOTE_FAIL",
                Message = "boom",
                Details = "stack trace here",
                ExceptionType = "System.InvalidOperationException",
            },
        };

        var bytes = sut.Serialize(resp);
        var back = sut.Deserialize<RpcResponse>(bytes)!;

        await Assert.That(back.Success).IsFalse();
        await Assert.That(back.ResultTypeName).IsNull();
        await Assert.That(back.Result.HasValue).IsFalse();
        await Assert.That(back.Error).IsNotNull();
        await Assert.That(back.Error!.Code).IsEqualTo("REMOTE_FAIL");
        await Assert.That(back.Error.Message).IsEqualTo("boom");
        await Assert.That(back.Error.Details).IsEqualTo("stack trace here");
        await Assert.That(back.Error.ExceptionType).IsEqualTo("System.InvalidOperationException");
    }

    [Test]
    public async Task RpcResponse_Failure_With_Error_PartialNullableFields_Roundtrips()
    {
        // Details and ExceptionType null -> wire emits 0-tag, read back as null.
        var sut = new XPacketRpcSerializer();
        var resp = new RpcResponse
        {
            RequestId = "req-err2",
            Success = false,
            ResultTypeName = null,
            Result = null,
            Error = new RpcError
            {
                Code = "X",
                Message = "y",
                Details = null,
                ExceptionType = null,
            },
        };

        var bytes = sut.Serialize(resp);
        var back = sut.Deserialize<RpcResponse>(bytes)!;

        await Assert.That(back.Error).IsNotNull();
        await Assert.That(back.Error!.Code).IsEqualTo("X");
        await Assert.That(back.Error.Message).IsEqualTo("y");
        await Assert.That(back.Error.Details).IsNull();
        await Assert.That(back.Error.ExceptionType).IsNull();
    }

    [Test]
    public async Task RpcResponse_Null_Result_Roundtrips_As_HasValue_False()
    {
        var sut = new XPacketRpcSerializer();
        var resp = new RpcResponse
        {
            RequestId = "req-void",
            Success = true,
            ResultTypeName = null,
            Result = null,
            Error = null,
        };

        var bytes = sut.Serialize(resp);
        var back = sut.Deserialize<RpcResponse>(bytes)!;

        await Assert.That(back.RequestId).IsEqualTo("req-void");
        await Assert.That(back.Success).IsTrue();
        await Assert.That(back.ResultTypeName).IsNull();
        await Assert.That(back.Result.HasValue).IsFalse();
        await Assert.That(back.Error).IsNull();
    }

    [Test]
    public async Task RpcArgument_Payload_Survives_SourceBuffer_Overwrite()
    {
        // Lifetime contract: after deserialization, the Payload of an argument must own
        // its bytes — overwriting the wire buffer must not corrupt the deserialized value.
        var sut = new XPacketRpcSerializer();
        var src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var req = new RpcRequest
        {
            RequestId = "lifetime",
            InterfaceName = "I",
            MethodName = "M",
            Arguments = new List<RpcArgument>
            {
                new() { TypeName = "System.Byte[]", Payload = new ReadOnlyMemory<byte>(src) },
            },
        };

        var wire = sut.Serialize(req).ToArray();
        var back = sut.Deserialize<RpcRequest>(wire)!;

        // Overwrite the entire wire buffer.
        Array.Clear(wire, 0, wire.Length);

        var payloadBytes = back.Arguments[0].Payload.ToArray();
        await Assert.That(payloadBytes.SequenceEqual(src)).IsTrue();
    }

    [Test]
    public async Task RpcResponse_Result_Survives_SourceBuffer_Overwrite()
    {
        var sut = new XPacketRpcSerializer();
        var src = new byte[] { 0x10, 0x20, 0x30 };
        var resp = new RpcResponse
        {
            RequestId = "lifetime-resp",
            Success = true,
            ResultTypeName = "T",
            Result = new ReadOnlyMemory<byte>(src),
        };

        var wire = sut.Serialize(resp).ToArray();
        var back = sut.Deserialize<RpcResponse>(wire)!;

        Array.Clear(wire, 0, wire.Length);

        await Assert.That(back.Result.HasValue).IsTrue();
        await Assert.That(back.Result!.Value.ToArray().SequenceEqual(src)).IsTrue();
    }
}
