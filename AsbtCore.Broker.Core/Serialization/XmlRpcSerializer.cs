using System.Text;
using System.Xml.Serialization;

namespace AsbtCore.Broker.Core.Serialization
{
    public class XmlRpcSerializer : IRpcSerializer
    {
        public string ContentType => "application/xml";

        public byte[] Serialize<T>(T value)
        {
            var serializer = new XmlSerializer(typeof(T));
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, Encoding.UTF8);
            serializer.Serialize(writer, value);
            return ms.ToArray();
        }

        public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            var serializer = new XmlSerializer(typeof(T));
            using var ms = new MemoryStream(payload.ToArray());
            using var reader = new StreamReader(ms, Encoding.UTF8);
            return (T?)serializer.Deserialize(reader);
        }
    }
}