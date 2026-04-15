namespace AsbtCore.Broker.Core.Abstractions
{
    public interface IRpcTransportHost : IAsyncDisposable
    {
        Task StartAsync(
            Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler,
            IReadOnlyCollection<string> routes,
            CancellationToken cancellationToken = default);
    }
}
