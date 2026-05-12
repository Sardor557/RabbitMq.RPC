using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;

namespace AsbtCore.Broker.Benchmarks;

/// <summary>
/// In-process transport for bench setups: routes envelopes directly to a
/// <see cref="RpcRequestDispatcher"/> in the same process. The dispatcher
/// owns the <see cref="IRpcSerializer"/>, so this transport only forwards
/// the envelope unchanged.
/// </summary>
internal sealed class InMemoryTransport : IRpcTransport
{
    private readonly RpcRequestDispatcher dispatcher;

    public InMemoryTransport(RpcRequestDispatcher dispatcher) => this.dispatcher = dispatcher;

    public Task<RpcResponse> SendAsync(
        RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => dispatcher.DispatchAsync(request, cancellationToken);
}
