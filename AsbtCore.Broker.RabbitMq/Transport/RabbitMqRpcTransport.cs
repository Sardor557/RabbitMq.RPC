using System.Collections.Concurrent;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsbtCore.Broker.RabbitMq.Transport;

public sealed class RabbitMqRpcTransport : IRpcTransport, IAsyncDisposable, IDisposable
{
    private readonly IRabbitMqConnectionProvider connectionProvider;
    private readonly ILogger<RabbitMqRpcTransport> logger;
    private readonly IRpcSerializer serializer;
    private readonly RpcOptions options;

    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> pending = new();

    private IChannel? publishChannel;
    private IChannel? replyChannel;
    private string? replyQueueName;
    private bool initialized;

    public RabbitMqRpcTransport(
        IRabbitMqConnectionProvider connectionProvider,
        ILogger<RabbitMqRpcTransport> logger,
        IRpcSerializer serializer,
        IOptions<RpcOptions> options)
    {
        this.connectionProvider = connectionProvider;
        this.logger = logger;
        this.serializer = serializer;
        this.options = options.Value;
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
            try
            {
                await publishChannel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: route,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                pending.TryRemove(request.RequestId, out _);
                throw new RpcPublishFailedException(request.RequestId, ex.GetType().Name, ex);
            }

            logger.LogDebug(
                "RPC request published. RequestId: {RequestId}, Route: {Route}, Method: {Method}",
                request.RequestId, route, request.MethodName);

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

            var publishChannelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            try
            {
                publishChannel = await connection.CreateChannelAsync(
                    options: publishChannelOptions,
                    cancellationToken: cancellationToken);
                replyChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

                var queueName = $"rpc-reply-{this.options.ClientProvidedName}-{Guid.NewGuid():N}";
                await replyChannel.QueueDeclareAsync(
                    queue: queueName,
                    durable: false,
                    exclusive: false,
                    autoDelete: true,
                    arguments: null,
                    passive: false,
                    noWait: false,
                    cancellationToken: cancellationToken);

                replyQueueName = queueName;

                if (replyChannel is IRecoverable recoverable)
                    recoverable.RecoveryAsync += OnRecoverySucceededAsync;

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
            catch
            {
                if (publishChannel is not null)
                {
                    await publishChannel.DisposeAsync();
                    publishChannel = null;
                }
                if (replyChannel is not null)
                {
                    await replyChannel.DisposeAsync();
                    replyChannel = null;
                }
                replyQueueName = null;
                throw;
            }
        }
        finally
        {
            initLock.Release();
        }
    }

    private Task OnResponseReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var correlationId = ea.BasicProperties.CorrelationId;
        if (string.IsNullOrWhiteSpace(correlationId))
            return Task.CompletedTask;

        if (!pending.TryRemove(correlationId, out var tcs))
            return Task.CompletedTask;

        try
        {
            var response = serializer.Deserialize<RpcResponse>(ea.Body);
            if (response is null)
                tcs.TrySetException(new RpcProtocolException("Empty RPC response."));
            else
                tcs.TrySetResult(response);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            logger.LogError(ex, "Error deserializing RPC response. CorrelationId: {Id}", correlationId);
        }

        return Task.CompletedTask;
    }

    private Task OnRecoverySucceededAsync(object? sender, AsyncEventArgs e)
    {
        foreach (var id in pending.Keys.ToArray())
        {
            if (pending.TryRemove(id, out var tcs))
                tcs.TrySetException(new TransportReconnectedException(id));
        }
        logger.LogWarning("RabbitMQ topology recovered. Pending RPC requests aborted.");
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
