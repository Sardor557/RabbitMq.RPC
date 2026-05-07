using AsbtCore.Broker.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsbtCore.Broker.Server
{
    public sealed class RpcServerHostedService : IHostedService
    {
        private readonly IRpcTransportHost transportHost;
        private readonly RpcServerRegistry registry;
        private readonly RpcRequestDispatcher dispatcher;
        private readonly ILogger<RpcServerHostedService> logger;

        public RpcServerHostedService(
            IRpcTransportHost transportHost,
            RpcServerRegistry registry,
            RpcRequestDispatcher dispatcher,
            ILogger<RpcServerHostedService> logger)
        {
            this.transportHost = transportHost;
            this.registry = registry;
            this.dispatcher = dispatcher;
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var routes = registry.GetRoutes();

            await transportHost.StartAsync(dispatcher.DispatchAsync, routes, cancellationToken);

            logger.LogInformation("RPC server started. Routes: {Routes}", string.Join(", ", routes));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("RPC server stopping.");
            await transportHost.StopAsync(cancellationToken);
            logger.LogInformation("RPC server stopped.");
        }
    }
}
