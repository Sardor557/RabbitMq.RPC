using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsbtCore.Broker.Server
{
    public sealed class RpcServerHostedService : IHostedService
    {
        private readonly IRpcTransportHost transportHost;
        private readonly RpcServerRegistry registry;
        private readonly RpcRequestDispatcher dispatcher;
        private readonly IRpcSerializer? serializer;
        private readonly IEnumerable<RpcInterfaceRegistration> interfaceRegistrations;
        private readonly ILogger<RpcServerHostedService> logger;

        public RpcServerHostedService(
            IRpcTransportHost transportHost,
            RpcServerRegistry registry,
            RpcRequestDispatcher dispatcher,
            ILogger<RpcServerHostedService> logger,
            IRpcSerializer? serializer = null,
            IEnumerable<RpcInterfaceRegistration>? interfaceRegistrations = null)
        {
            this.transportHost = transportHost;
            this.registry = registry;
            this.dispatcher = dispatcher;
            this.serializer = serializer;
            this.interfaceRegistrations = interfaceRegistrations ?? Array.Empty<RpcInterfaceRegistration>();
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (serializer is IRpcSerializerInterfaceWarmup warmup)
            {
                foreach (var reg in interfaceRegistrations)
                    warmup.Prewarm(reg.InterfaceType);
            }

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
