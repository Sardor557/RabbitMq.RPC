using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Benchmarks;

internal static class BenchmarkClientFactory
{
    public static RpcClient CreateInProcessClient()
    {
        var options = Options.Create(new RpcOptions
        {
            HostName = "localhost", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "bench", Port = 5672, DefaultTimeoutSeconds = 30
        });
        var transport = new InProcessTransport();
        var resolver = new DefaultRpcRouteResolver(options);
        return new RpcClient(transport, resolver, new JsonRpcSerializer(), options);
    }

    private sealed class InProcessTransport : IRpcTransport
    {
        public Task<RpcResponse> SendAsync(
            RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var response = new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Result = new JsonRpcSerializer().PackPayload(0, typeof(int))
            };
            return Task.FromResult(response);
        }
    }
}
