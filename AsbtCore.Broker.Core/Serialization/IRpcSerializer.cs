namespace AsbtCore.Broker.Core.Serialization
{
    /// <summary>
    /// Контракт сериализации RPC-сообщений.
    /// Границей сериализации/десериализации является <see cref="byte"/>[] у транспорта (RabbitMQ).
    /// Никаких промежуточных <see cref="string"/> между слоями.
    /// </summary>
    public interface IRpcSerializer
    {
        string ContentType { get; }

        /// <summary>Сериализует значение напрямую в UTF-8 байты. Вызывается прямо перед BasicPublish.</summary>
        byte[] Serialize<T>(T value);

        /// <summary>Десериализует из тела сообщения (<see cref="BasicDeliverEventArgs.Body"/>) без UTF-8 string-хопа.</summary>
        T? Deserialize<T>(ReadOnlyMemory<byte> payload);

        /// <summary>Упаковывает значение аргумента/результата в динамический <see cref="RpcPayload"/>.</summary>
        RpcPayload PackPayload(object? value, Type type);

        /// <summary>Распаковывает значение аргумента/результата из <see cref="RpcPayload"/> в целевой тип.</summary>
        object? UnpackPayload(RpcPayload payload, Type type);
    }
}
