using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsbtCore.Broker.RabbitMq.Transport
{
    public sealed class RabbitMqRpcTransportHost : IRpcTransportHost, IAsyncDisposable, IDisposable
    {
        private readonly IRabbitMqConnectionProvider connectionProvider;
        private readonly RpcOptions options;
        private readonly ILogger<RabbitMqRpcTransportHost> logger;
        private readonly IRpcSerializer serializer;

        private readonly List<IChannel> channels = new();
        private bool started;

        public RabbitMqRpcTransportHost(
            IRabbitMqConnectionProvider connectionProvider,
            IOptions<RpcOptions> options,
            ILogger<RabbitMqRpcTransportHost> logger,
            IRpcSerializer serializer)
        {
            this.connectionProvider = connectionProvider;
            this.options = options.Value;
            this.logger = logger;
            this.serializer = serializer;
        }

        public async Task StartAsync(
            Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler,
            IReadOnlyCollection<string> routes,
            CancellationToken cancellationToken = default)
        {
            if (started)
                return;

            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);

            foreach (var route in routes)
            {
                var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

                await channel.QueueDeclareAsync(
                    queue: route,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    passive: false,
                    noWait: false,
                    cancellationToken: cancellationToken);

                await channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: options.PrefetchCount,
                    global: false,
                    cancellationToken: cancellationToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    await HandleIncomingAsync(channel, ea, handler, cancellationToken);
                };

                await channel.BasicConsumeAsync(
                    queue: route,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: cancellationToken);

                channels.Add(channel);

                logger.LogInformation("RPC route is listening: {Route}", route);
            }

            started = true;
        }

        private async Task HandleIncomingAsync(
            IChannel channel,
            BasicDeliverEventArgs ea,
            Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler,
            CancellationToken cancellationToken)
        {
            try
            {
                // Единственная точка десериализации: ReadOnlyMemory<byte> → RpcRequest.
                var request = serializer.Deserialize<RpcRequest>(ea.Body)
                              ?? throw new InvalidOperationException("Invalid RPC request payload.");

                var response = await handler(request, cancellationToken);

                var replyTo = ea.BasicProperties.ReplyTo;
                if (!string.IsNullOrWhiteSpace(replyTo))
                {
                    // Единственная точка сериализации ответа: RpcResponse → byte[].
                    var responseBytes = serializer.Serialize(response);

                    var props = new BasicProperties
                    {
                        CorrelationId = ea.BasicProperties.CorrelationId,
                        ContentType = ea.BasicProperties.ContentType,
                    };

                    await channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: replyTo,
                        mandatory: false,
                        basicProperties: props,
                        body: responseBytes,
                        cancellationToken: cancellationToken);
                }

                await channel.BasicAckAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while handling incoming RPC request.");
                await channel.BasicNackAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken: cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var channel in channels)
            {
                await channel.DisposeAsync();
            }

            channels.Clear();
        }

        public void Dispose()
        {

        }
    }
}
