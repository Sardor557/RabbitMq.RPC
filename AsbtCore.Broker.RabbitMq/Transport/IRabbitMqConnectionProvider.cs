using RabbitMQ.Client;

namespace AsbtCore.Broker.RabbitMq.Transport
{
    public interface IRabbitMqConnectionProvider : IAsyncDisposable
    {
        Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
    }
}
