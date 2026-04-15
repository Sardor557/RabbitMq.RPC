namespace AsbtCore.Broker.Core.Abstractions
{
    public interface IRpcTransport
    {
        Task<RpcResponse> SendAsync(RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    }
}
