using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace AsbtCore.Broker.RabbitMq.Transport
{
    public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider, IAsyncDisposable, IDisposable
    {
        private readonly RpcOptions vars;
        private readonly ILogger<RabbitMqConnectionProvider> logger;
        private readonly SemaphoreSlim @lock = new(1, 1);

        private Task<IConnection>? connectionTask;
        private bool disposed;

        public RabbitMqConnectionProvider(
            IOptions<RpcOptions> options,
            ILogger<RabbitMqConnectionProvider> logger)
        {
            this.vars = options.Value;
            this.logger = logger;
        }

        public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(RabbitMqConnectionProvider));

            var existingTask = connectionTask;
            if (existingTask is not null)
            {
                var existingConnection = await existingTask.ConfigureAwait(false);
                if (existingConnection.IsOpen)
                    return existingConnection;
            }

            await @lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                existingTask = connectionTask;
                if (existingTask is not null)
                {
                    var existingConnection = await existingTask.ConfigureAwait(false);
                    if (existingConnection.IsOpen)
                        return existingConnection;
                }

                connectionTask = CreateConnectionAsync(cancellationToken);
                return await connectionTask.ConfigureAwait(false);
            }
            finally
            {
                @lock.Release();
            }
        }

        private async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = vars.HostName,
                Port = vars.Port,
                VirtualHost = vars.VirtualHost,
                UserName = vars.UserName,
                Password = vars.Password,
                ClientProvidedName = vars.ClientProvidedName,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            logger.LogInformation(
                "Creating RabbitMQ connection to {Host}:{Port}, vhost {VirtualHost}, client name {ClientName}",
                vars.HostName,
                vars.Port,
                vars.VirtualHost,
                vars.ClientProvidedName);

            return await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;

            disposed = true;

            if (connectionTask is not null)
            {
                var connection = await connectionTask.ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            @lock.Dispose();
        }

        public void Dispose()
        {
            
        }
    }
}
