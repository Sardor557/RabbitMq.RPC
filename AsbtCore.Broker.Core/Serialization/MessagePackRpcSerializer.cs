//using AsbtCore.Broker.Core;
//using AsbtCore.Broker.Core.Serialization;
//using MessagePack;
//using MessagePack.Resolvers;

//namespace AsbtCore.Broker.Core.Serialization
//{

//    public sealed class MessagePackRpcSerializer : IRpcSerializer
//    {
//        public string ContentType => "application/msgpack-hybrid";

//        private readonly MessagePackSerializerOptions msgpackOptions;

//        public byte[] Serialize<T>(T value)
//        {
//            throw new NotImplementedException();
//        }

//        public byte[] Serialize(object value, Type type)
//        {
//            throw new NotImplementedException();
//        }

//        public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
//        {
//            throw new NotImplementedException();
//        }

//        public object Deserialize(ReadOnlyMemory<byte> payload, Type type)
//        {
//            throw new NotImplementedException();
//        }

//        public RpcArgument PackArgument(Type type, object value)
//        {
//            throw new NotImplementedException();
//        }

//        public object UnpackArgument(RpcArgument argument)
//        {
//            throw new NotImplementedException();
//        }

//        public JsonElement? PackResult(object value, Type resultType)
//        {
//            throw new NotImplementedException();
//        }

//        public T? UnpackResult<T>(JsonElement? element)
//        {
//            throw new NotImplementedException();
//        }
//    }
//}