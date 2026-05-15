using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Tests.Fixtures;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.Core.Tests.Transport;

public sealed class ConsumerDispatchConcurrencyTests
{
    private Mock<IRabbitMqConnectionProvider> providerMock = null!;
    private Mock<IConnection> connectionMock = null!;
    private Mock<IChannel> channelMock = null!;
    private TestJsonRpcSerializer serializer = null!;

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
            .Setup(x => x.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("tag");
    }

    private RabbitMqRpcTransportHost CreateSut(RpcOptions options) =>
        new(providerMock.Object,
            MsOptions.Create(options),
            NullLogger<RabbitMqRpcTransportHost>.Instance,
            serializer);

    private static RpcOptions BuildOptions(ushort prefetch, ushort? dispatch) => new()
    {
        HostName = "x",
        VirtualHost = "/",
        UserName = "u",
        Password = "p",
        ClientProvidedName = "c",
        Port = 5672,
        PrefetchCount = prefetch,
        ConsumerDispatchConcurrency = dispatch,
    };

    [Test]
    public async Task StartAsync_PassesConsumerDispatchConcurrencyToChannelOptions()
    {
        var sut = CreateSut(BuildOptions(prefetch: 4, dispatch: 8));

        try
        {
            await sut.StartAsync(
                (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }),
                ["test.route"]);

            connectionMock.Verify(
                x => x.CreateChannelAsync(
                    It.Is<CreateChannelOptions?>(o => o != null && o.ConsumerDispatchConcurrency == 8),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Test]
    public async Task StartAsync_FallsBackToPrefetchCount_WhenConcurrencyUnset()
    {
        var sut = CreateSut(BuildOptions(prefetch: 4, dispatch: null));

        try
        {
            await sut.StartAsync(
                (_, _) => Task.FromResult(new RpcResponse { RequestId = "r", Success = true }),
                ["test.route"]);

            connectionMock.Verify(
                x => x.CreateChannelAsync(
                    It.Is<CreateChannelOptions?>(o => o != null && o.ConsumerDispatchConcurrency == 4),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }
}
