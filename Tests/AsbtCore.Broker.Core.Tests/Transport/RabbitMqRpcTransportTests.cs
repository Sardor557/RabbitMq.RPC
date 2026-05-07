using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Transport
{
    [TestClass]
    public class RabbitMqRpcTransportTests
    {
        private Mock<IRabbitMqConnectionProvider> providerMock = null!;
        private Mock<IConnection> connectionMock = null!;
        private Mock<IChannel> publishChannelMock = null!;
        private Mock<IChannel> replyChannelMock = null!;
        private JsonRpcSerializer serializer = null!;

        [TestInitialize]
        public void Init()
        {
            providerMock = new Mock<IRabbitMqConnectionProvider>();
            connectionMock = new Mock<IConnection>();
            publishChannelMock = new Mock<IChannel>();
            replyChannelMock = new Mock<IChannel>();
            serializer = new JsonRpcSerializer();

            providerMock
                .Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(connectionMock.Object);

            // SUT calls CreateChannelAsync(cancellationToken: ct) — only CancellationToken param
            connectionMock
                .SetupSequence(x => x.CreateChannelAsync(
                    It.IsAny<CreateChannelOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(publishChannelMock.Object)
                .ReturnsAsync(replyChannelMock.Object);

            // SUT calls: QueueDeclareAsync(queue, durable, exclusive, autoDelete, arguments, passive, noWait, ct)
            replyChannelMock
                .Setup(x => x.QueueDeclareAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object?>>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new QueueDeclareOk("reply-q", 0, 0));

            // IChannel.BasicConsumeAsync full signature (extension 4-arg overload forwards here)
            replyChannelMock
                .Setup(x => x.BasicConsumeAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object?>>(),
                    It.IsAny<IAsyncBasicConsumer>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("consumer-tag");

            publishChannelMock
                .Setup(x => x.BasicPublishAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<BasicProperties>(),
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
        }

        private RabbitMqRpcTransport CreateSut()
            => new(providerMock.Object, NullLogger<RabbitMqRpcTransport>.Instance, serializer);

        [TestMethod]
        public async Task SendAsync_Timeout_ThrowsOperationCanceledException()
        {
            var sut = CreateSut();
            var request = new RpcRequest { InterfaceName = "I", MethodName = "M" };

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => sut.SendAsync(request, "route.x", TimeSpan.FromMilliseconds(50)));

            publishChannelMock.Verify(x => x.BasicPublishAsync(
                It.Is<string>(s => s == string.Empty),
                It.Is<string>(r => r == "route.x"),
                It.IsAny<bool>(),
                It.Is<BasicProperties>(p =>
                    p.CorrelationId == request.RequestId &&
                    p.ReplyTo == "reply-q" &&
                    p.ContentType == serializer.ContentType),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task SendAsync_CancellationRequested_ThrowsOperationCanceled()
        {
            var sut = CreateSut();
            var request = new RpcRequest { InterfaceName = "I", MethodName = "M" };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => sut.SendAsync(request, "route.x", TimeSpan.FromSeconds(10), cts.Token));
        }
    }
}
