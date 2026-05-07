using System;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.ClientServer.Tests.Fixtures;

internal sealed class TimeoutCapturingTransport : IRpcTransport
{
    public TimeSpan? LastTimeout { get; private set; }

    public Task<RpcResponse> SendAsync(
        RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        LastTimeout = timeout;
        return Task.FromResult(new RpcResponse
        {
            RequestId = request.RequestId,
            Success = true
        });
    }
}
