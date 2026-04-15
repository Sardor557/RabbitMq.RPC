using System;
using System.Text.Json;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Core.Tests.Fixtures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Serialization
{
    [TestClass]
    public class JsonRpcSerializerTests
    {
        private JsonRpcSerializer sut = null!;

        [TestInitialize]
        public void Init() => sut = new JsonRpcSerializer();

        [TestMethod]
        public void PackArgument_Primitive_SetsTypeNameAndPayload()
        {
            var arg = sut.PackArgument(typeof(int), 42);

            Assert.IsTrue(arg.TypeName.Contains("System.Int32"));
            Assert.AreEqual(JsonValueKind.Number, arg.Payload.ValueKind);
            Assert.AreEqual(42, arg.Payload.GetInt32());
        }

        [TestMethod]
        public void PackArgument_ComplexObject_SerializesAsJsonElement()
        {
            var user = new UserDto(Guid.Empty, "Alice");

            var arg = sut.PackArgument(typeof(UserDto), user);

            Assert.AreEqual(JsonValueKind.Object, arg.Payload.ValueKind);
            Assert.AreEqual("Alice", arg.Payload.GetProperty("name").GetString());
        }

        [TestMethod]
        public void PackArgument_Null_ProducesNullPayload()
        {
            var arg = sut.PackArgument(typeof(string), null);

            Assert.AreEqual(JsonValueKind.Null, arg.Payload.ValueKind);
        }

        [TestMethod]
        public void UnpackArgument_RoundTrip_ReturnsEqualValue()
        {
            var arg = sut.PackArgument(typeof(string), "hello");

            var result = sut.UnpackArgument(arg);

            Assert.AreEqual("hello", result);
        }

        [TestMethod]
        public void UnpackResult_Generic_ReturnsTypedValue()
        {
            var packed = sut.PackResult(new UserDto(Guid.Empty, "Bob"), typeof(UserDto));

            var result = sut.UnpackResult<UserDto>(packed);

            Assert.IsNotNull(result);
            Assert.AreEqual("Bob", result!.Name);
        }

        [TestMethod]
        public void UnpackResult_NullElement_ReturnsDefault()
        {
            var result = sut.UnpackResult<UserDto>(null);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void SerializeDeserialize_RpcRequest_RoundTrips()
        {
            var request = new RpcRequest
            {
                InterfaceName = "ITest",
                MethodName = "Do",
                Arguments = { sut.PackArgument(typeof(int), 1) }
            };

            var bytes = sut.Serialize(request);
            var result = sut.Deserialize<RpcRequest>(bytes);

            Assert.IsNotNull(result);
            Assert.AreEqual("ITest", result!.InterfaceName);
            Assert.AreEqual("Do", result.MethodName);
            Assert.AreEqual(1, result.Arguments.Count);
        }
    }
}
