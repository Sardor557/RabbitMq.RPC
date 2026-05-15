using AsbtCore.Broker.Core;

namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

public sealed class MemoryPackRpcSerializerContentTypeTests
{
    [Test]
    public async Task ContentType_IsMemoryPackRpc()
    {
        await Assert.That(new MemoryPackRpcSerializer().ContentType)
            .IsEqualTo("application/x-memorypack-rpc");
    }
}

public sealed class MemoryPackRpcSerializerEnvelopeTests
{
    private static MemoryPackRpcSerializer NewSerializer() => new();

    [Test]
    public async Task Serialize_Deserialize_RpcRequest_Roundtrip()
    {
        var sut = NewSerializer();
        var request = new RpcRequest
        {
            RequestId = "req-1",
            InterfaceName = "IFoo",
            MethodName = "Bar",
            Arguments =
            [
                new RpcArgument { TypeName = "System.Int32", Payload = new byte[] { 1, 2, 3 } }
            ]
        };
        var bytes = sut.Serialize(request);
        var roundtrip = sut.Deserialize<RpcRequest>(bytes);
        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.RequestId).IsEqualTo("req-1");
        await Assert.That(roundtrip.InterfaceName).IsEqualTo("IFoo");
        await Assert.That(roundtrip.MethodName).IsEqualTo("Bar");
        await Assert.That(roundtrip.Arguments.Count).IsEqualTo(1);
        await Assert.That(roundtrip.Arguments[0].TypeName).IsEqualTo("System.Int32");
        await Assert.That(roundtrip.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcRequest_EmptyArguments_Roundtrip()
    {
        var sut = NewSerializer();
        var request = new RpcRequest { RequestId = "r", InterfaceName = "IX", MethodName = "M" };
        var roundtrip = sut.Deserialize<RpcRequest>(sut.Serialize(request));
        await Assert.That(roundtrip!.Arguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_Success_WithResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse
        {
            RequestId = "req-1",
            Success = true,
            ResultTypeName = "System.Int32",
            Result = new byte[] { 9, 8, 7 },
            Error = null
        };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.RequestId).IsEqualTo("req-1");
        await Assert.That(roundtrip.Success).IsTrue();
        await Assert.That(roundtrip.ResultTypeName).IsEqualTo("System.Int32");
        await Assert.That(roundtrip.Result!.Value.Span.SequenceEqual(new byte[] { 9, 8, 7 })).IsTrue();
        await Assert.That(roundtrip.Error).IsNull();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_NullResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse { RequestId = "r", Success = true, Result = null };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Result.HasValue).IsFalse();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_Failure_WithError_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse
        {
            RequestId = "req-1",
            Success = false,
            Result = null,
            Error = new RpcError
            {
                Code = "E001",
                Message = "Boom",
                Details = "detail",
                ExceptionType = "System.Exception"
            }
        };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Success).IsFalse();
        await Assert.That(roundtrip.Result.HasValue).IsFalse();
        await Assert.That(roundtrip.Error).IsNotNull();
        await Assert.That(roundtrip.Error!.Code).IsEqualTo("E001");
        await Assert.That(roundtrip.Error.Message).IsEqualTo("Boom");
        await Assert.That(roundtrip.Error.Details).IsEqualTo("detail");
        await Assert.That(roundtrip.Error.ExceptionType).IsEqualTo("System.Exception");
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_Error_NullOptionalFields_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse
        {
            RequestId = "r",
            Success = false,
            Error = new RpcError { Code = "ERR", Message = "msg", Details = null, ExceptionType = null }
        };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Error!.Details).IsNull();
        await Assert.That(roundtrip.Error.ExceptionType).IsNull();
    }
}

[MemoryPackable]
public sealed partial record TestFragmentDto(int X, string Label);

public sealed class MemoryPackRpcSerializerFragmentTests
{
    private static MemoryPackRpcSerializer NewSerializer() => new();

    [Test]
    public async Task Fragment_Roundtrips_Int()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(42, typeof(int));
        var value = (int)sut.DeserializeFragment(bytes, typeof(int))!;
        await Assert.That(value).IsEqualTo(42);
    }

    [Test]
    public async Task Fragment_Roundtrips_String()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment("hello", typeof(string));
        var value = (string)sut.DeserializeFragment(bytes, typeof(string))!;
        await Assert.That(value).IsEqualTo("hello");
    }

    [Test]
    public async Task Fragment_Roundtrips_Bool()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(true, typeof(bool));
        var value = (bool)sut.DeserializeFragment(bytes, typeof(bool))!;
        await Assert.That(value).IsTrue();
    }

    [Test]
    public async Task Fragment_Roundtrips_MemoryPackable_Dto()
    {
        var sut = NewSerializer();
        var dto = new TestFragmentDto(7, "hello");
        var bytes = sut.SerializeFragment(dto, typeof(TestFragmentDto));
        var value = (TestFragmentDto)sut.DeserializeFragment(bytes, typeof(TestFragmentDto))!;
        await Assert.That(value.X).IsEqualTo(7);
        await Assert.That(value.Label).IsEqualTo("hello");
    }

    [Test]
    public async Task Fragment_Roundtrips_NullString()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(null, typeof(string));
        var value = sut.DeserializeFragment(bytes, typeof(string));
        await Assert.That(value).IsNull();
    }
}
