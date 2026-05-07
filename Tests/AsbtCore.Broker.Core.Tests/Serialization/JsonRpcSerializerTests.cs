using System;
using System.Text.Json;
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
        public void SerializeDeserialize_RpcRequest_RoundTrips()
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(42, typeof(int), RpcJson.Options);
            using var doc = JsonDocument.Parse(bytes);
            var arg = new RpcArgument { TypeName = typeof(int).AssemblyQualifiedName!, Payload = doc.RootElement.Clone() };

            var request = new RpcRequest
            {
                InterfaceName = "ITest",
                MethodName = "Do",
                Arguments = { arg }
            };

            var serialized = sut.Serialize(request);
            var result = sut.Deserialize<RpcRequest>(serialized);

            Assert.IsNotNull(result);
            Assert.AreEqual("ITest", result!.InterfaceName);
            Assert.AreEqual("Do", result.MethodName);
            Assert.AreEqual(1, result.Arguments.Count);
        }

        [TestMethod]
        public void Serialize_Deserialize_PrimitiveRoundTrip()
        {
            var bytes = sut.Serialize(42);
            var result = sut.Deserialize<int>(bytes);

            Assert.AreEqual(42, result);
        }

        [TestMethod]
        public void Serialize_Deserialize_ComplexObjectRoundTrip()
        {
            var user = new UserDto(Guid.Empty, "Alice");

            var bytes = sut.Serialize(user);
            var result = sut.Deserialize<UserDto>(bytes);

            Assert.IsNotNull(result);
            Assert.AreEqual("Alice", result!.Name);
        }
    }
}
