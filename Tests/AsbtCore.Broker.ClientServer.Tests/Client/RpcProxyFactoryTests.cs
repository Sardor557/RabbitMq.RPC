using System;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.ClientServer.Tests.Client
{
    [TestClass]
    public class RpcProxyFactoryTests
    {
        [TestMethod]
        public async Task CreateProxy_InvokeMethod_DelegatesThroughTransport()
        {
            var transport = new Mock<IRpcTransport>();
            var route = new Mock<IRpcRouteResolver>();
            route.Setup(x => x.Resolve(It.IsAny<Type>())).Returns("rpc.route");

            var serializer = new JsonRpcSerializer();

            transport
                .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RpcResponse
                {
                    RequestId = "r",
                    Success = true,
                    Result = serializer.PackResult(10, typeof(int))
                });

            var client = new RpcClient(
                transport.Object, route.Object, serializer,
                MsOptions.Create(new RpcOptions
                {
                    HostName = "x", VirtualHost = "/", UserName = "u", Password = "p",
                    ClientProvidedName = "c", Port = 5672
                }));

            var factory = new RpcProxyFactory(client);

            var proxy = factory.CreateProxy<ITestService>();
            var result = await proxy.AddAsync(3, 4);

            Assert.AreEqual(10, result);
            transport.Verify(x => x.SendAsync(
                It.Is<RpcRequest>(r => r.MethodName == nameof(ITestService.AddAsync)
                    && r.InterfaceName == typeof(ITestService).FullName),
                "rpc.route",
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
