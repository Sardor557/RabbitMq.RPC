using System.Collections.Concurrent;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsbtCore.Broker.RabbitMq.Transport;

public sealed class RabbitMqRpcTransport : IRpcTransport, IAsyncDisposable, IDisposable
{
    private readonly IRabbitMqConnectionProvider connectionProvider;
    private readonly ILogger<RabbitMqRpcTransport> logger;
    private readonly IRpcSerializer serializer;

    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> pending = new();

    private IChannel? publishChannel;
    private IChannel? replyChannel;
    private string? replyQueueName;
    private bool initialized;

    public RabbitMqRpcTransport(
        IRabbitMqConnectionProvider connectionProvider,
        ILogger<RabbitMqRpcTransport> logger,
        IRpcSerializer serializer)
    {
        this.connectionProvider = connectionProvider;
        this.logger = logger;
        this.serializer = serializer;
    }

    public async Task<RpcResponse> SendAsync(
        RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (publishChannel is null || replyQueueName is null)
            throw new InvalidOperationException("Transport is not initialized.");

        var properties = new BasicProperties
        {
            CorrelationId = request.RequestId,
            ReplyTo = replyQueueName,
            ContentType = serializer.ContentType
        };

        var body = serializer.Serialize(request);

        var tcs = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!pending.TryAdd(request.RequestId, tcs))
            throw new InvalidOperationException($"Duplicate request id '{request.RequestId}'.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
            linkedCts.CancelAfter(timeout.Value);

        await using var registration = linkedCts.Token.Register(
            static state =>
            {
                var (tcs, token) = ((TaskCompletionSource<RpcResponse>, CancellationToken))state!;
                tcs.TrySetCanceled(token);
            },
            (tcs, linkedCts.Token));

        try
        {
            await publishChannel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: route,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: linkedCts.Token);

            logger.LogDebug(
                "RPC request published. RequestId: {RequestId}, Route: {Route}, Method: {Method}",
                request.RequestId,
                route,
                request.MethodName);

            return await tcs.Task;
        }
        finally
        {
            pending.TryRemove(request.RequestId, out _);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        await initLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;

            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);

            publishChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            replyChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            var declareOk = await replyChannel.QueueDeclareAsync(
                queue: string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: true,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken);

            replyQueueName = declareOk.QueueName;

            var consumer = new AsyncEventingBasicConsumer(replyChannel);
            consumer.ReceivedAsync += OnResponseReceivedAsync;

            await replyChannel.BasicConsumeAsync(
                queue: replyQueueName,
                autoAck: true,
                consumer: consumer,
                cancellationToken: cancellationToken);

            initialized = true;

            logger.LogInformation("RabbitMQ RPC transport initialized. Reply queue: {ReplyQueue}", replyQueueName);
        }
        finally
        {
            initLock.Release();
        }
    }

    private Task OnResponseReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var correlationId = ea.BasicProperties.CorrelationId;

            if (string.IsNullOrWhiteSpace(correlationId))
                return Task.CompletedTask;

            var response = serializer.Deserialize<RpcResponse>(ea.Body);

            if (response is null)
                return Task.CompletedTask;

            if (pending.TryRemove(correlationId, out var tcs))
                tcs.TrySetResult(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while handling RPC response.");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (replyChannel is not null)
            await replyChannel.DisposeAsync();

        if (publishChannel is not null)
            await publishChannel.DisposeAsync();

        initLock.Dispose();
    }

    public void Dispose()
    {
        replyChannel?.Dispose();
        publishChannel?.Dispose();
        initLock.Dispose();
    }
}
