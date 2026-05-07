using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Tests.Fixtures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Routing
{
    [TestClass]
    public class DefaultRpcRouteResolverTests
    {
        private DefaultRpcRouteResolver Create(string prefix = "rpc.")
            => new(Microsoft.Extensions.Options.Options.Create(new RpcOptions
            {
                HostName = "x",
                VirtualHost = "/",
                UserName = "u",
                Password = "p",
                ClientProvidedName = "c",
                RoutePrefix = prefix,
                Port = 5672
            }));

        [TestMethod]
        public void Resolve_Interface_UsesFullNameWithPrefix()
        {
            var sut = Create("rpc.");

            var route = sut.Resolve(typeof(ITestService));

            Assert.AreEqual("rpc." + typeof(ITestService).FullName, route);
        }

        [TestMethod]
        public void Resolve_StringOverload_ReturnsSameResult()
        {
            var sut = Create("rpc.");
            var expected = sut.Resolve(typeof(ITestService));

            var actual = sut.Resolve(typeof(ITestService).FullName!);

            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Resolve_NestedTypePlus_ReplacedWithDot()
        {
            var sut = Create("rpc.");

            var route = sut.Resolve("Some.NS+Nested");

            Assert.AreEqual("rpc.Some.NS.Nested", route);
        }

        [TestMethod]
        public void Resolve_SameInterfaceTwice_ReturnsCachedInstance()
        {
            var sut = Create("rpc.");
            var a = sut.Resolve("Same.Type");
            var b = sut.Resolve("Same.Type");

            Assert.AreSame(a, b);
        }
    }
}
