using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Tests.Fixtures;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.Core.Tests.Transport;

public sealed class PoisonReplyTests
{
    private Mock<IRabbitMqConnectionProvider> providerMock = null!;
    private Mock<IConnection> connectionMock = null!;
    private Mock<IChannel> publishChannelMock = null!;
    private Mock<IChannel> replyChannelMock = null!;
    private RabbitMqRpcTransport sut = null!;
    private TestJsonRpcSerializer serializer = null!;

    [After(Test)]
    public async ValueTask Cleanup() => await sut.DisposeAsync();

    [Before(Test)]
    public void Init()
    {
        providerMock = new Mock<IRabbitMqConnectionProvider>();
        connectionMock = new Mock<IConnection>();
        publishChannelMock = new Mock<IChannel>();
        replyChannelMock = new Mock<IChannel>();
        serializer = new TestJsonRpcSerializer();

        providerMock
            .Setup(x => x.GetConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionMock.Object);

        // Transport calls CreateChannelAsync twice: first for publish, second for reply.
        var channelQueue = new Queue<IChannel>(new[] { publishChannelMock.Object, replyChannelMock.Object });
        connectionMock
            .Setup(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(channelQueue.Dequeue);

        replyChannelMock
            .Setup(x => x.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("rpc-reply-test", 0, 0));

        sut = new RabbitMqRpcTransport(
            providerMock.Object,
            NullLogger<RabbitMqRpcTransport>.Instance,
            serializer,
            MsOptions.Create(new RpcOptions
            {
                ClientProvidedName = "test",
                DefaultTimeoutSeconds = 30
            }));
    }

    [Test]
    public async Task SendAsync_FailsFast_WhenReplyBodyIsMalformedJson()
    {
        AsyncEventingBasicConsumer? replyConsumer = null;
        string? capturedCorrelationId = null;

        replyChannelMock
            .Setup(x => x.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => replyConsumer = consumer as AsyncEventingBasicConsumer)
            .ReturnsAsync("reply-tag");

        publishChannelMock
            .Setup(x => x.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, _, props, _, _) => capturedCorrelationId = props.CorrelationId)
            .Returns(ValueTask.CompletedTask);

        var request = new RpcRequest
        {
            RequestId = "req-poison",
            InterfaceName = "I",
            MethodName = "M",
            Arguments = []
        };

        var sendTask = sut.SendAsync(request, "test.route", TimeSpan.FromSeconds(30));

        // Wait until both consumer and correlationId have been captured.
        var captureWait = Stopwatch.StartNew();
        while ((replyConsumer is null || capturedCorrelationId is null) &&
               captureWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        await Assert.That(replyConsumer).IsNotNull();
        await Assert.That(capturedCorrelationId).IsNotNull();

        var props = new Mock<IReadOnlyBasicProperties>();
        props.Setup(p => p.CorrelationId).Returns(capturedCorrelationId);

        var malformedBody = Encoding.UTF8.GetBytes("{not-json").AsMemory();

        var sw = Stopwatch.StartNew();

        await replyConsumer!.HandleBasicDeliverAsync(
            "reply-tag", 1, false, "", "test.route",
            props.Object, malformedBody);

        var exceptionThrown = false;
        try
        {
            await sendTask.WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch
        {
            exceptionThrown = true;
        }

        sw.Stop();

        await Assert.That(exceptionThrown).IsTrue();
        await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }
}
