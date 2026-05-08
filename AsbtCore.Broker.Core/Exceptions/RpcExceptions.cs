namespace AsbtCore.Broker.Core.Exceptions;

/// <summary>
/// Thrown into a pending RPC <see cref="System.Threading.Tasks.TaskCompletionSource{TResult}"/>
/// when the underlying transport reconnects to the broker. The request may or may not have
/// reached the broker; caller decides whether to retry.
/// </summary>
public sealed class TransportReconnectedException : Exception
{
    public string RequestId { get; }

    public TransportReconnectedException(string requestId)
        : base($"RPC request '{requestId}' aborted: transport reconnected.")
    {
        RequestId = requestId;
    }
}

/// <summary>
/// Thrown when the broker rejected (nack/return) the publish of an RPC request.
/// Surfaces immediately, without waiting for the configured RPC timeout.
/// </summary>
public sealed class RpcPublishFailedException : Exception
{
    public string RequestId { get; }
    public string Reason { get; }

    public RpcPublishFailedException(string requestId, string reason, Exception? inner = null)
        : base($"RPC publish failed for '{requestId}': {reason}.", inner)
    {
        RequestId = requestId;
        Reason = reason;
    }
}
