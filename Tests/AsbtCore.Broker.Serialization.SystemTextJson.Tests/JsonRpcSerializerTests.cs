using System.Text.Json;
using System.Text.Json.Serialization;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.SystemTextJson;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class RpcJsonOptionsTests
{
    [Test]
    public async Task Options_UseCamelCase()
    {
        await Assert.That(RpcJson.Options.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task Options_IgnoreNullsOnWrite()
    {
        await Assert.That(RpcJson.Options.DefaultIgnoreCondition).IsEqualTo(JsonIgnoreCondition.WhenWritingNull);
    }

    [Test]
    public async Task Options_AreCaseInsensitive()
    {
        await Assert.That(RpcJson.Options.PropertyNameCaseInsensitive).IsTrue();
    }
}

public class JsonRpcSerializerEnvelopeTests
{
    private static IRpcSerializer NewSerializer() => new JsonRpcSerializer();

    [Test]
    public async Task ContentType_IsSystemTextJson()
    {
        await Assert.That(NewSerializer().ContentType).IsEqualTo("application/json");
    }

    [Test]
    public async Task Serialize_Deserialize_RpcRequest_Roundtrip()
    {
        var sut = NewSerializer();
        var request = new RpcRequest
        {
            RequestId = "rid",
            InterfaceName = "IFoo",
            MethodName = "M",
            Arguments =
            {
                new RpcArgument { TypeName = "System.Int32, System.Private.CoreLib", Payload = new byte[] { 1, 2, 3 } }
            }
        };

        var bytes = sut.Serialize(request);
        var roundtrip = sut.Deserialize<RpcRequest>(bytes);

        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.RequestId).IsEqualTo("rid");
        await Assert.That(roundtrip.Arguments.Count).IsEqualTo(1);
        await Assert.That(roundtrip.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_NullResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse { RequestId = "rid", Success = true, Result = null };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Result.HasValue).IsFalse();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_WithResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse { RequestId = "rid", Success = true, Result = new byte[] { 9, 8, 7 } };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Result!.Value.Span.SequenceEqual(new byte[] { 9, 8, 7 })).IsTrue();
    }
}

public class JsonRpcSerializerFragmentTests
{
    private record UserDto(Guid Id, string Name);

    private static IRpcSerializer NewSerializer() => new JsonRpcSerializer();

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
    public async Task Fragment_Roundtrips_Guid()
    {
        var sut = NewSerializer();
        var g = Guid.NewGuid();
        var bytes = sut.SerializeFragment(g, typeof(Guid));
        var value = (Guid)sut.DeserializeFragment(bytes, typeof(Guid))!;
        await Assert.That(value).IsEqualTo(g);
    }

    [Test]
    public async Task Fragment_Roundtrips_Dto()
    {
        var sut = NewSerializer();
        var original = new UserDto(Guid.NewGuid(), "Alice");
        var bytes = sut.SerializeFragment(original, typeof(UserDto));
        var value = (UserDto)sut.DeserializeFragment(bytes, typeof(UserDto))!;
        await Assert.That(value).IsEqualTo(original);
    }

    [Test]
    public async Task Fragment_Roundtrips_NullableInt_WithValue()
    {
        var sut = NewSerializer();
        int? original = 7;
        var bytes = sut.SerializeFragment(original, typeof(int?));
        var value = (int?)sut.DeserializeFragment(bytes, typeof(int?));
        await Assert.That(value).IsEqualTo(7);
    }

    [Test]
    public async Task Fragment_Roundtrips_NullReference()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(null, typeof(string));
        var value = sut.DeserializeFragment(bytes, typeof(string));
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Fragment_Roundtrips_List()
    {
        var sut = NewSerializer();
        var list = new List<int> { 1, 2, 3 };
        var bytes = sut.SerializeFragment(list, typeof(List<int>));
        var value = (List<int>)sut.DeserializeFragment(bytes, typeof(List<int>))!;
        await Assert.That(value.SequenceEqual(list)).IsTrue();
    }
}
