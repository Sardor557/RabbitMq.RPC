using System.Text.Json;

namespace AsbtCore.Broker.Core.Serialization
{
    /// <summary>
    /// Контракт сериализации RPC-сообщений.
    /// Границей сериализации/десериализации является <see cref="byte"/>[] у транспорта (RabbitMQ).
    /// Никаких промежуточных <see cref="string"/> между слоями.
    /// </summary>
    public interface IRpcSerializer
    {
        string ContentType { get;  }

        /// <summary>Сериализует значение напрямую в UTF-8 байты. Вызывается прямо перед BasicPublish.</summary>
        byte[] Serialize<T>(T value);

        /// <summary>Сериализует значение известного типа напрямую в UTF-8 байты.</summary>
        byte[] Serialize(object? value, Type type);

        /// <summary>Десериализует из тела сообщения (<see cref="BasicDeliverEventArgs.Body"/>) без UTF-8 string-хопа.</summary>
        T? Deserialize<T>(ReadOnlyMemory<byte> payload);

        /// <summary>Десериализует из тела сообщения в заранее известный runtime-тип.</summary>
        object? Deserialize(ReadOnlyMemory<byte> payload, Type type);

        // Полиморфные аргументы/результат — упаковываются как JsonElement внутрь envelope,
        // что исключает вложенное string-кодирование (JSON-внутри-JSON).

        RpcArgument PackArgument(Type type, object? value);
        object? UnpackArgument(RpcArgument argument);

        JsonElement? PackResult(object? value, Type resultType);
        T? UnpackResult<T>(JsonElement? element);
    }
}
