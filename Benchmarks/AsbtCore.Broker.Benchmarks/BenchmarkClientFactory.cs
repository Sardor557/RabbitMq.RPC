using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Serialization.SystemTextJson;
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
        var serializer = new JsonRpcSerializer();
        var transport = new InProcessTransport(serializer);
        var resolver = new DefaultRpcRouteResolver(options);
        return new RpcClient(transport, resolver, serializer, options);
    }

    /// <summary>
    /// Fake transport that always returns a successful response with a serialized
    /// <c>0</c> result. Used by bench client-invoker measurements that don't need a
    /// real server.
    /// </summary>
    private sealed class InProcessTransport : IRpcTransport
    {
        private readonly IRpcSerializer serializer;

        public InProcessTransport(IRpcSerializer serializer) => this.serializer = serializer;

        public Task<RpcResponse> SendAsync(
            RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var response = new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Result = serializer.SerializeFragment(0, typeof(int))
            };
            return Task.FromResult(response);
        }
    }
}
