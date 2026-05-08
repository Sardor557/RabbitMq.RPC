using Google.Protobuf;

namespace AsbtCore.Broker.Core.Serialization
{
    public class ProtobufRpcSerializer : IRpcSerializer
    {
        public string ContentType => "application/x-protobuf";

        public byte[] Serialize<T>(T value)
        {
            if (value is not IMessage message)
                throw new InvalidOperationException(
                    $"ProtobufRpcSerializer only supports IMessage types, got {typeof(T).Name}");

            return message.ToByteArray();
        }

        public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            var parserProperty = typeof(T).GetProperty("Parser");

            if (parserProperty == null)
                throw new InvalidOperationException(
                    $"Type {typeof(T).Name} does not contain a static Parser property");

            
            if (parserProperty.GetValue(null) is not MessageParser parser)
                throw new InvalidOperationException(
                    $"Invalid parser for {typeof(T).Name}");

            return (T) parser.ParseFrom(payload.Span);
        }
    }
}