using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public sealed class RabbitMqRpcTransportHostShutdownTests
{
    private Mock<IRabbitMqConnectionProvider> providerMock = null!;
    private Mock<IConnection> connectionMock = null!;
    private Mock<IChannel> channelMock = null!;
    private TestJsonRpcSerializer serializer = null!;
    private RabbitMqRpcTransportHost sut = null!;

    [Before(Test)]
    public void Init()
    {
        providerMock = new Mock<IRabbitMqConnectionProvider>();
        connectionMock = new Mock<IConnection>();
        channelMock = new Mock<IChannel>();
        serializer = new TestJsonRpcSerializer();

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

        channelMock
            .Setup(x => x.BasicCancelAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        channelMock
            .Setup(x => x.DisposeAsync())
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
            .ReturnsAsync("consumer-tag");

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
    public async Task StopAsync_CallsBasicCancel_OnEachConsumer()
    {
        await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        await sut.StopAsync();

        channelMock.Verify(
            x => x.BasicCancelAsync("consumer-tag", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task StopAsync_WaitsForInFlightHandler_BeforeReturning()
    {
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = await StartAndCaptureConsumerAsync(async (_, _) =>
        {
            handlerEntered.SetResult();
            await handlerRelease.Task;
            return new RpcResponse { RequestId = "r", Success = true };
        });

        var body = serializer.Serialize(new RpcRequest
        {
            RequestId = "r",
            InterfaceName = "I",
            MethodName = "M",
            Arguments = []
        });

        var deliverTask = consumer.HandleBasicDeliverAsync(
            "consumer-tag", 1, false, "", "test.route",
            BuildProps(replyTo: null).Object, body);

        await handlerEntered.Task;

        var stopTask = sut.StopAsync();

        await Task.Delay(200);

        if (stopTask.IsCompleted)
        {
            throw new Exception("StopAsync returned before in-flight handler completed (no drain).");
        }

        handlerRelease.SetResult();

        await stopTask;
        await deliverTask;
    }

    [Test]
    public async Task StopAsync_ReturnsImmediately_WhenNoInFlightHandlers()
    {
        await StartAndCaptureConsumerAsync(
            (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }));

        var sw = Stopwatch.StartNew();
        await sut.StopAsync();
        sw.Stop();

        if (sw.Elapsed > TimeSpan.FromSeconds(1))
        {
            throw new Exception($"StopAsync took {sw.Elapsed.TotalSeconds:F1}s with no in-flight handlers.");
        }
    }
}
