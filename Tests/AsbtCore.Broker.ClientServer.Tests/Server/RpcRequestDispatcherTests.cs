using System;
using System.Threading.Tasks;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Server
{
    [TestClass]
    public class RpcRequestDispatcherTests
    {
        private IRpcSerializer serializer = null!;

        [TestInitialize]
        public void Init() => serializer = new JsonRpcSerializer();

        private (RpcRequestDispatcher sut, ServiceProvider sp) BuildSut<TImpl>() where TImpl : class, ITestService
        {
            var routeMock = new Mock<IRpcRouteResolver>();
            routeMock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => "rpc." + t.FullName);

            var registry = new RpcServerRegistry(
                new[] { new RpcServerRegistration(typeof(ITestService), typeof(TImpl)) },
                routeMock.Object);

            var services = new ServiceCollection();
            services.AddScoped<TImpl>();
            var sp = services.BuildServiceProvider();

            var dispatcher = new RpcRequestDispatcher(
                registry,
                sp.GetRequiredService<IServiceScopeFactory>(),
                serializer);

            return (dispatcher, sp);
        }

        private RpcRequest BuildAddRequest(int a, int b) => new()
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.AddAsync),
            Arguments =
            {
                serializer.PackArgument(typeof(int), a),
                serializer.PackArgument(typeof(int), b)
            }
        };

        [TestMethod]
        public async Task Dispatch_KnownRequest_ReturnsSuccessWithResult()
        {
            var (sut, sp) = BuildSut<TestServiceImpl>();
            using var _ = sp;

            var response = await sut.DispatchAsync(BuildAddRequest(2, 3));

            Assert.IsTrue(response.Success);
            Assert.IsNull(response.Error);
            Assert.AreEqual(5, serializer.UnpackResult<int>(response.Result));
        }

        [TestMethod]
        public async Task Dispatch_UnknownInterface_ReturnsServiceNotFoundError()
        {
            var (sut, sp) = BuildSut<TestServiceImpl>();
            using var _ = sp;

            var response = await sut.DispatchAsync(new RpcRequest
            {
                InterfaceName = "No.Such.Service",
                MethodName = "Whatever"
            });

            Assert.IsFalse(response.Success);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual("service_not_found", response.Error!.Code);
        }

        [TestMethod]
        public async Task Dispatch_UnknownMethodSignature_ReturnsMethodNotFoundError()
        {
            var (sut, sp) = BuildSut<TestServiceImpl>();
            using var _ = sp;

            var response = await sut.DispatchAsync(new RpcRequest
            {
                InterfaceName = typeof(ITestService).FullName!,
                MethodName = "DoesNotExist"
            });

            Assert.IsFalse(response.Success);
            Assert.AreEqual("method_not_found", response.Error!.Code);
        }

        [TestMethod]
        public async Task Dispatch_ServiceThrows_ReturnsInvocationErrorWithExceptionType()
        {
            var (sut, sp) = BuildSut<ThrowingServiceImpl>();
            using var _ = sp;

            var response = await sut.DispatchAsync(BuildAddRequest(1, 2));

            Assert.IsFalse(response.Success);
            Assert.AreEqual("invocation_error", response.Error!.Code);
            Assert.AreEqual(typeof(InvalidOperationException).FullName, response.Error.ExceptionType);
            Assert.AreEqual("boom", response.Error.Message);
        }

        [TestMethod]
        public async Task Dispatch_VoidTaskMethod_ResultFieldIsNull()
        {
            var (sut, sp) = BuildSut<TestServiceImpl>();
            using var _ = sp;

            var response = await sut.DispatchAsync(new RpcRequest
            {
                InterfaceName = typeof(ITestService).FullName!,
                MethodName = nameof(ITestService.NotifyAsync),
                Arguments = { serializer.PackArgument(typeof(string), "hi") }
            });

            Assert.IsTrue(response.Success);
            Assert.IsNull(response.Result);
        }
    }
}
