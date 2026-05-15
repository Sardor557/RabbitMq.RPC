using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
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

        private readonly List<(IChannel Channel, string ConsumerTag)> consumers = new();
        private readonly object stateLock = new();

        private int inFlightCount;
        private volatile bool stopping;
        private TaskCompletionSource? drained;

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

            var dispatchConcurrency = options.ConsumerDispatchConcurrency ?? options.PrefetchCount;

            foreach (var route in routes)
            {
                var channelOptions = new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true,
                    consumerDispatchConcurrency: dispatchConcurrency);

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

                var consumerTag = await channel.BasicConsumeAsync(
                    queue: route,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: cancellationToken);

                consumers.Add((channel, consumerTag));

                logger.LogInformation("RPC route is listening: {Route}", route);
            }

            started = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!started)
                return;

            lock (stateLock)
            {
                stopping = true;
                if (inFlightCount > 0 && drained is null)
                {
                    drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            foreach (var (channel, consumerTag) in consumers)
            {
                try
                {
                    await channel.BasicCancelAsync(consumerTag, noWait: false, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cancel consumer {Tag}", consumerTag);
                }
            }

            TaskCompletionSource? toAwait;
            lock (stateLock)
            {
                toAwait = inFlightCount > 0 ? drained : null;
            }

            if (toAwait is not null)
            {
                try
                {
                    await toAwait.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning(
                        "StopAsync cancelled before all in-flight handlers drained. Remaining: {Count}",
                        inFlightCount);
                }
            }

            await DisposeChannelsAsync();
        }

        private async Task HandleIncomingAsync(
            IChannel channel,
            BasicDeliverEventArgs ea,
            Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler,
            string route,
            string deadRoute,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref inFlightCount);
            try
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
            finally
            {
                if (Interlocked.Decrement(ref inFlightCount) == 0 && stopping)
                {
                    TaskCompletionSource? toSignal;
                    lock (stateLock)
                    {
                        toSignal = drained;
                    }
                    toSignal?.TrySetResult();
                }
            }
        }

        private async Task DisposeChannelsAsync()
        {
            foreach (var (channel, _) in consumers)
            {
                try
                {
                    await channel.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing channel.");
                }
            }
            consumers.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeChannelsAsync();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
