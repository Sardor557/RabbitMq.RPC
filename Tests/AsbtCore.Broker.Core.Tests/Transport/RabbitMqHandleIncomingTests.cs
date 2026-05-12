using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.Core.Tests.Transport;

public sealed class RabbitMqHandleIncomingTests
{
    private Mock<IRabbitMqConnectionProvider> providerMock = null!;
    private Mock<IConnection> connectionMock = null!;
    private Mock<IChannel> channelMock = null!;
    private RabbitMqRpcTransportHost sut = null!;
    private JsonRpcSerializer serializer = null!;

    [After(Test)]
    public async ValueTask Cleanup() => await sut.DisposeAsync();

    [Before(Test)]
    public void Init()
    {
        providerMock = new Mock<IRabbitMqConnectionProvider>();
        connectionMock = new Mock<IConnection>();
        channelMock = new Mock<IChannel>();
        serializer = new JsonRpcSerializer();

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
            .Setup(x => x.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        channelMock
            .Setup(x => x.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        channelMock
            .Setup(x => x.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        sut = new RabbitMqRpcTransportHost(
            providerMock.Object,
            MsOptions.Create(new RpcOptions
            {
                HostName = "x", VirtualHost = "/", UserName = "u", Password = "p",
                ClientProvidedName = "c", Port = 5672, PrefetchCount = 1
            }),
            NullLogger<RabbitMqRpcTransportHost>.Instance,
            serializer);
    }

    private async Task<AsyncEventingBasicConsumer> StartAndCaptureConsumerAsync(
        Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler)
    {
        IAsyncBasicConsumer? captured = null;

        channelMock
            .Setup(x => x.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => captured = consumer)
            .ReturnsAsync("tag");

        await sut.StartAsync(handler, ["test.route"]);

        return (AsyncEventingBasicConsumer)captured!;
    }

    private static Mock<IReadOnlyBasicProperties> BuildProps(string? replyTo = null)
    {
        var props = new Mock<IReadOnlyBasicProperties>();
        props.Setup(p => p.ReplyTo).Returns(replyTo);
        props.Setup(p => p.CorrelationId).Returns("corr-1");
        props.Setup(p => p.ContentType).Returns("application/json");
        return props;
    }

    [Test]
    public async Task HandleIncomingAsync_ValidRequest_AcksMessage()
    {
        var consumer = await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var body = serializer.Serialize(new RpcRequest
        {
            RequestId = "r", InterfaceName = "I", MethodName = "M", Arguments = []
        });

        await consumer.HandleBasicDeliverAsync("tag", 1, false, "", "test.route",
            BuildProps().Object, body.AsMemory());

        channelMock.Verify(
            x => x.BasicAckAsync(1UL, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleIncomingAsync_WithReplyTo_PublishesReply()
    {
        var consumer = await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var body = serializer.Serialize(new RpcRequest
        {
            RequestId = "r", InterfaceName = "I", MethodName = "M", Arguments = []
        });

        await consumer.HandleBasicDeliverAsync("tag", 2, false, "", "test.route",
            BuildProps(replyTo: "amq.reply").Object, body.AsMemory());

        channelMock.Verify(
            x => x.BasicPublishAsync(
                string.Empty, "amq.reply", false,
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        channelMock.Verify(
            x => x.BasicAckAsync(2UL, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleIncomingAsync_NoReplyTo_DoesNotPublish()
    {
        var consumer = await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var body = serializer.Serialize(new RpcRequest
        {
            RequestId = "r", InterfaceName = "I", MethodName = "M", Arguments = []
        });

        await consumer.HandleBasicDeliverAsync("tag", 3, false, "", "test.route",
            BuildProps(replyTo: null).Object, body.AsMemory());

        channelMock.Verify(
            x => x.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task HandleIncomingAsync_PoisonMessage_PublishesToDlqAndAcks()
    {
        var consumer = await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var invalidBody = "not-json"u8.ToArray().AsMemory();

        await consumer.HandleBasicDeliverAsync("tag", 4, false, "", "test.route",
            BuildProps().Object, invalidBody);

        channelMock.Verify(
            x => x.BasicPublishAsync(
                string.Empty, "test.route.dead", false,
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        channelMock.Verify(
            x => x.BasicAckAsync(4UL, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleIncomingAsync_ReplyPublishFails_StillAcks()
    {
        channelMock
            .Setup(x => x.BasicPublishAsync(
                string.Empty, "amq.reply", false,
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("reply channel closed"));

        var consumer = await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var body = serializer.Serialize(new RpcRequest
        {
            RequestId = "r", InterfaceName = "I", MethodName = "M", Arguments = []
        });

        await consumer.HandleBasicDeliverAsync("tag", 6, false, "", "test.route",
            BuildProps(replyTo: "amq.reply").Object, body.AsMemory());

        channelMock.Verify(
            x => x.BasicAckAsync(6UL, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleIncomingAsync_PoisonMessageDlqFails_Nacks()
    {
        channelMock
            .Setup(x => x.BasicPublishAsync(
                string.Empty, "test.route.dead", false,
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("dlq publish failed"));

        var consumer = await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var invalidBody = "bad-json"u8.ToArray().AsMemory();

        await consumer.HandleBasicDeliverAsync("tag", 5, false, "", "test.route",
            BuildProps().Object, invalidBody);

        channelMock.Verify(
            x => x.BasicNackAsync(5UL, false, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
