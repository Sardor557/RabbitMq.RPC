namespace AsbtCore.Broker.Core.Abstractions
{
    public interface IRpcTransportHost : IAsyncDisposable
    {
        Task StartAsync(
            Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler,
            IReadOnlyCollection<string> routes,
            CancellationToken cancellationToken = default);

        // Default implementation keeps backward compatibility for third-party transports.
        // Concrete transports should override to provide proper drain semantics.
        Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
