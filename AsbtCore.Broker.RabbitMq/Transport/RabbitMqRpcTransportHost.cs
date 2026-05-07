using System;
using System.Collections.Generic;
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
                var channelOptions = new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true);

                var channel = await connection.CreateChannelAsync(
                    options: channelOptions,
                    cancellationToken: cancellationToken);

                await channel.QueueDeclareAsync(
                    queue: route,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    passive: false,
                    noWait: false,
                    cancellationToken: cancellationToken);

                var deadRoute = $"{route}.dead";

                await channel.QueueDeclareAsync(
                    queue: deadRoute,
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
                var capturedRoute = route;
                var capturedDeadRoute = deadRoute;
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    await HandleIncomingAsync(channel, ea, handler, capturedRoute, capturedDeadRoute, cancellationToken);
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
            string route,
            string deadRoute,
            CancellationToken cancellationToken)
        {
            try
            {
                var request = serializer.Deserialize<RpcRequest>(ea.Body)
                              ?? throw new InvalidOperationException("Invalid RPC request payload.");

                var response = await handler(request, cancellationToken);

                var replyTo = ea.BasicProperties.ReplyTo;
                if (!string.IsNullOrWhiteSpace(replyTo))
                {
                    var responseBytes = serializer.Serialize(response);

                    var props = new BasicProperties
                    {
                        CorrelationId = ea.BasicProperties.CorrelationId,
                        ContentType   = ea.BasicProperties.ContentType,
                    };

                    try
                    {
                        await channel.BasicPublishAsync(
                            exchange: string.Empty,
                            routingKey: replyTo,
                            mandatory: false,
                            basicProperties: props,
                            body: responseBytes,
                            cancellationToken: cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex,
                            "Failed to publish RPC reply. CorrelationId: {Id}. Original delivery acked anyway.",
                            ea.BasicProperties.CorrelationId);
                    }
                }

                await channel.BasicAckAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Poison RPC message → DLQ {DeadRoute}", deadRoute);

                var deadProps = new BasicProperties
                {
                    Headers = new Dictionary<string, object?>
                    {
                        ["x-rpc-error"]     = ex.GetType().FullName,
                        ["x-rpc-error-msg"] = ex.Message,
                        ["x-rpc-original"]  = route,
                        ["x-rpc-failed-at"] = DateTimeOffset.UtcNow.ToString("o"),
                    },
                };

                try
                {
                    await channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: deadRoute,
                        mandatory: false,
                        basicProperties: deadProps,
                        body: ea.Body,
                        cancellationToken: cancellationToken);

                    await channel.BasicAckAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        cancellationToken: cancellationToken);
                }
                catch (Exception dlqEx) when (dlqEx is not OperationCanceledException)
                {
                    logger.LogError(dlqEx,
                        "DLQ publish failed for {DeadRoute}; dropping original message.", deadRoute);

                    await channel.BasicNackAsync(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false,
                        requeue: false,
                        cancellationToken: cancellationToken);
                }
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
