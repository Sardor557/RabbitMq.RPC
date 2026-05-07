using System;
using System.Linq;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Server
{
    [TestClass]
    public class RpcServerRegistryTests
    {
        private static Mock<IRpcRouteResolver> CreateRoute()
        {
            var mock = new Mock<IRpcRouteResolver>();
            mock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => "rpc." + t.FullName);
            return mock;
        }

        [TestMethod]
        public void TryGet_Registered_ReturnsDescriptor()
        {
            var registrations = new[]
            {
                new RpcServerRegistration(typeof(ITestService), typeof(TestServiceImpl))
            };
            var sut = new RpcServerRegistry(registrations, CreateRoute().Object);

            var found = sut.TryGet(typeof(ITestService).FullName!, out var descriptor);

            Assert.IsTrue(found);
            Assert.AreEqual(typeof(ITestService), descriptor!.InterfaceType);
            Assert.AreEqual(typeof(TestServiceImpl), descriptor.ImplementationType);
        }

        [TestMethod]
        public void TryGet_Unknown_ReturnsFalse()
        {
            var sut = new RpcServerRegistry(Array.Empty<RpcServerRegistration>(), CreateRoute().Object);

            var found = sut.TryGet("No.Such", out var descriptor);

            Assert.IsFalse(found);
            Assert.IsNull(descriptor);
        }

        [TestMethod]
        public void Descriptor_TryGetMethod_MatchesBySignature()
        {
            var registrations = new[]
            {
                new RpcServerRegistration(typeof(ITestService), typeof(TestServiceImpl))
            };
            var sut = new RpcServerRegistry(registrations, CreateRoute().Object);
            sut.TryGet(typeof(ITestService).FullName!, out var descriptor);

            var paramTypes = new[]
            {
                typeof(int).AssemblyQualifiedName!,
                typeof(int).AssemblyQualifiedName!
            };

            var ok = descriptor!.TryGetMethod(nameof(ITestService.AddAsync), paramTypes, out var method);

            Assert.IsTrue(ok);
            Assert.AreEqual(nameof(ITestService.AddAsync), method!.Method.Name);
        }

        [TestMethod]
        public void GetRoutes_DistinctRoutesPerRegistration()
        {
            var registrations = new[]
            {
                new RpcServerRegistration(typeof(ITestService), typeof(TestServiceImpl))
            };
            var sut = new RpcServerRegistry(registrations, CreateRoute().Object);

            var routes = sut.GetRoutes();

            Assert.AreEqual(1, routes.Count);
            CollectionAssert.Contains(routes.ToArray(), "rpc." + typeof(ITestService).FullName);
        }

        [TestMethod]
        public void Registration_ExplicitRoute_OverridesResolver()
        {
            var registrations = new[]
            {
                new RpcServerRegistration(typeof(ITestService), typeof(TestServiceImpl), "custom.route")
            };
            var sut = new RpcServerRegistry(registrations, CreateRoute().Object);
            sut.TryGet(typeof(ITestService).FullName!, out var descriptor);

            Assert.AreEqual("custom.route", descriptor!.Route);
        }
    }
}
