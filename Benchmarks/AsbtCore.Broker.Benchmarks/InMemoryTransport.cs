using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;

namespace AsbtCore.Broker.Benchmarks;

internal sealed class InMemoryTransport : IRpcTransport
{
    private readonly RpcRequestDispatcher dispatcher;

    public InMemoryTransport(RpcRequestDispatcher dispatcher) => this.dispatcher = dispatcher;

    public Task<RpcResponse> SendAsync(
        RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => dispatcher.DispatchAsync(request, cancellationToken);
}
