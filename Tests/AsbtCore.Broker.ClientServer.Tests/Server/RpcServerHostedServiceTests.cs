using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Server
{
    [TestClass]
    public class RpcServerHostedServiceTests
    {
        private static RpcServerRegistry BuildRegistry()
        {
            var route = new Mock<IRpcRouteResolver>();
            route.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => "rpc." + t.FullName);
            return new RpcServerRegistry(
                new[] { new RpcServerRegistration(typeof(ITestService), typeof(TestServiceImpl)) },
                route.Object);
        }

        private static RpcRequestDispatcher BuildDispatcher(RpcServerRegistry registry)
        {
            var services = new ServiceCollection();
            services.AddScoped<TestServiceImpl>();
            var sp = services.BuildServiceProvider();

            return new RpcRequestDispatcher(
                registry,
                sp.GetRequiredService<IServiceScopeFactory>(),
                new JsonRpcSerializer());
        }

        [TestMethod]
        public async Task StartAsync_PassesRoutesToTransportHost()
        {
            var registry = BuildRegistry();
            var dispatcher = BuildDispatcher(registry);
            var hostMock = new Mock<IRpcTransportHost>();
            hostMock
                .Setup(x => x.StartAsync(
                    It.IsAny<Func<RpcRequest, CancellationToken, Task<RpcResponse>>>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var sut = new RpcServerHostedService(
                hostMock.Object, registry, dispatcher,
                NullLogger<RpcServerHostedService>.Instance);

            await sut.StartAsync(CancellationToken.None);

            hostMock.Verify(x => x.StartAsync(
                It.IsAny<Func<RpcRequest, CancellationToken, Task<RpcResponse>>>(),
                It.Is<IReadOnlyCollection<string>>(r => r.Count == 1),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task StopAsync_CompletesSuccessfully()
        {
            var registry = BuildRegistry();
            var dispatcher = BuildDispatcher(registry);
            var sut = new RpcServerHostedService(
                Mock.Of<IRpcTransportHost>(), registry, dispatcher,
                NullLogger<RpcServerHostedService>.Instance);

            await sut.StopAsync(CancellationToken.None);
        }
    }
}
