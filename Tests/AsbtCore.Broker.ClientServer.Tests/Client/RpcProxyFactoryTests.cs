using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

public class RpcProxyFactoryTests
{
    [Test]
    public async Task CreateProxy_InvokeMethod_DelegatesThroughTransport()
    {
        var transport = new Mock<IRpcTransport>();
        var route = new Mock<IRpcRouteResolver>();
        route.Setup(x => x.Resolve(It.IsAny<Type>())).Returns("rpc.route");

        var resultBytes = JsonSerializer.SerializeToUtf8Bytes(10, typeof(int), RpcJson.Options);
        using var resultDoc = JsonDocument.Parse(resultBytes);
        var packedResult = resultDoc.RootElement.Clone();

        transport
            .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpcResponse
            {
                RequestId = "r",
                Success = true,
                Result = packedResult
            });

        var options = MsOptions.Create(new RpcOptions
        {
            HostName = "x", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "c", Port = 5672
        });

        var client = new RpcClient(transport.Object, route.Object, options);
        var factory = new RpcProxyFactory(client, options);

        var proxy = factory.CreateProxy<ITestService>();
        var result = await proxy.AddAsync(3, 4);

        await Assert.That(result).IsEqualTo(10);
        transport.Verify(x => x.SendAsync(
            It.Is<RpcRequest>(r => r.MethodName == nameof(ITestService.AddAsync)
                && r.InterfaceName == typeof(ITestService).FullName),
            "rpc.route",
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CreateProxy_UsesDefaultTimeoutSecondsFromOptions()
    {
        var transportSpy = new TimeoutCapturingTransport();
        var route = new Mock<IRpcRouteResolver>();
        route.Setup(x => x.Resolve(It.IsAny<Type>())).Returns("rpc.route");

        var options = MsOptions.Create(new RpcOptions
        {
            HostName = "h", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "c", Port = 1, DefaultTimeoutSeconds = 7
        });

        var client = new RpcClient(transportSpy, route.Object, options);
        var factory = new RpcProxyFactory(client, options);

        var proxy = factory.CreateProxy<ITestService>();
        await proxy.NotifyAsync("ping");

        await Assert.That(transportSpy.LastTimeout).IsEqualTo(TimeSpan.FromSeconds(7));
    }
}
