using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using RabbitMQ.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Transport
{
    [TestClass]
    public class RabbitMqRpcTransportHostTests
    {
        [TestMethod]
        public async Task StartAsync_PerRoute_DeclaresQueueSetsQosAndConsumes()
        {
            var providerMock = new Mock<IRabbitMqConnectionProvider>();
            var connectionMock = new Mock<IConnection>();
            var channelMock = new Mock<IChannel>();

            providerMock
                .Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(connectionMock.Object);

            connectionMock
                .Setup(x => x.CreateChannelAsync(
                    It.IsAny<CreateChannelOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(channelMock.Object);

            channelMock
                .Setup(x => x.QueueDeclareAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new QueueDeclareOk("q", 0, 0));

            channelMock
                .Setup(x => x.BasicQosAsync(
                    It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            channelMock
                .Setup(x => x.BasicConsumeAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                    It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object?>>(),
                    It.IsAny<IAsyncBasicConsumer>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("tag");

            var options = MsOptions.Create(new RpcOptions
            {
                HostName = "x", VirtualHost = "/", UserName = "u", Password = "p",
                ClientProvidedName = "c", Port = 5672, PrefetchCount = 5
            });

            var sut = new RabbitMqRpcTransportHost(
                providerMock.Object,
                options,
                NullLogger<RabbitMqRpcTransportHost>.Instance,
                new JsonRpcSerializer());

            await sut.StartAsync(
                (_, _) => Task.FromResult(new RpcResponse { Success = true }),
                new[] { "route.a", "route.b" });

            connectionMock.Verify(x => x.CreateChannelAsync(
                It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            channelMock.Verify(x => x.QueueDeclareAsync(
                "route.a", true, false, false,
                It.IsAny<IDictionary<string, object?>>(), false, false,
                It.IsAny<CancellationToken>()), Times.Once);

            channelMock.Verify(x => x.QueueDeclareAsync(
                "route.b", true, false, false,
                It.IsAny<IDictionary<string, object?>>(), false, false,
                It.IsAny<CancellationToken>()), Times.Once);

            channelMock.Verify(x => x.BasicQosAsync(
                0, (ushort)5, false, It.IsAny<CancellationToken>()),
                Times.Exactly(2));

            channelMock.Verify(x => x.BasicConsumeAsync(
                It.IsAny<string>(), false, It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [TestMethod]
        public async Task StartAsync_CalledTwice_NoOpOnSecondCall()
        {
            var providerMock = new Mock<IRabbitMqConnectionProvider>();
            var connectionMock = new Mock<IConnection>();
            var channelMock = new Mock<IChannel>();

            providerMock
                .Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(connectionMock.Object);
            connectionMock
                .Setup(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(channelMock.Object);
            channelMock
                .Setup(x => x.QueueDeclareAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new QueueDeclareOk("q", 0, 0));
            channelMock
                .Setup(x => x.BasicQosAsync(
                    It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            channelMock
                .Setup(x => x.BasicConsumeAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                    It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object?>>(),
                    It.IsAny<IAsyncBasicConsumer>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("tag");

            var sut = new RabbitMqRpcTransportHost(
                providerMock.Object,
                MsOptions.Create(new RpcOptions
                {
                    HostName = "x", VirtualHost = "/", UserName = "u", Password = "p",
                    ClientProvidedName = "c", Port = 5672, PrefetchCount = 1
                }),
                NullLogger<RabbitMqRpcTransportHost>.Instance,
                new JsonRpcSerializer());

            var handler = new Func<RpcRequest, CancellationToken, Task<RpcResponse>>(
                (_, _) => Task.FromResult(new RpcResponse { Success = true }));

            await sut.StartAsync(handler, new[] { "r" });
            await sut.StartAsync(handler, new[] { "r" });

            connectionMock.Verify(x => x.CreateChannelAsync(
                It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
