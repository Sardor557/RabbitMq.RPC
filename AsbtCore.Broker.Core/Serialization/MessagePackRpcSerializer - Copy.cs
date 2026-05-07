//using AsbtCore.Broker.Core;
//using AsbtCore.Broker.Core.Serialization;
//using MessagePack;
//using MessagePack.Resolvers;
//using System.Text.Json;

//namespace AsbtCore.Broker.Core.Serialization
//{

//    public sealed class MessagePackRpcSerializer : IRpcSerializer
//    {
//        public string ContentType => "application/msgpack-hybrid";

//        private readonly MessagePackSerializerOptions msgpackOptions;
//        private readonly JsonSerializerOptions jsonOptions;

//        public MessagePackRpcSerializer()
//        {
//            msgpackOptions = MessagePackSerializerOptions.Standard
//                .WithResolver(ContractlessStandardResolver.Instance);

//            jsonOptions = RpcJson.Options;
//        }

//        public byte[] Serialize<T>(T value)
//            => JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions);

//        public byte[] Serialize(object? value, Type type)
//            => JsonSerializer.SerializeToUtf8Bytes(value, type, jsonOptions);

//        public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
//            => JsonSerializer.Deserialize<T>(payload.Span, jsonOptions);

//        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
//            => JsonSerializer.Deserialize(payload.Span, type, jsonOptions);

//        public RpcArgument PackArgument(Type type, object? value)
//        {
//            var typeName = type.AssemblyQualifiedName
//                           ?? type.FullName
//                           ?? throw new InvalidOperationException(
//                               $"Cannot resolve type name for {type}.");

//            var msgpackBytes = MessagePackSerializer.Serialize(type, value, msgpackOptions);
//            var base64 = Convert.ToBase64String(msgpackBytes);

//            using var doc = JsonDocument.Parse($"\"{base64}\"");
//            return new RpcArgument
//            {
//                TypeName = typeName,
//                Payload = doc.RootElement.Clone()
//            };
//        }

//        public object? UnpackArgument(RpcArgument argument)
//        {
//            var type = Type.GetType(argument.TypeName, throwOnError: true)!;

//            if (argument.Payload.ValueKind == JsonValueKind.String)
//            {
//                var base64 = argument.Payload.GetString()!;
//                var bytes = Convert.FromBase64String(base64);
//                return MessagePackSerializer.Deserialize(type, bytes, msgpackOptions);
//            }

//            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(
//                argument.Payload.GetRawText());
//            return JsonSerializer.Deserialize(jsonBytes, type, jsonOptions);
//        }


//        public JsonElement? PackResult(object? value, Type resultType)
//        {
//            if (value is null)
//                return null;

//            if (resultType == typeof(void) || resultType == typeof(Task))
//                return null;

//            try
//            {
//                var msgpackBytes = MessagePackSerializer.Serialize(
//                    resultType, value, msgpackOptions);
//                var base64 = Convert.ToBase64String(msgpackBytes);
//                using var doc = JsonDocument.Parse($"\"{base64}\"");
//                return doc.RootElement.Clone();
//            }
//            catch
//            {
//                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(
//                    value, resultType, jsonOptions);
//                using var doc = JsonDocument.Parse(jsonBytes);
//                return doc.RootElement.Clone();
//            }
//        }

//        public T? UnpackResult<T>(JsonElement? element)
//        {
//            if (element is null || element.Value.ValueKind == JsonValueKind.Undefined)
//                return default;

//            if (element.Value.ValueKind == JsonValueKind.String)
//            {
//                var base64 = element.Value.GetString();
//                if (base64 is null) return default;

//                try
//                {
//                    var bytes = Convert.FromBase64String(base64);
//                    return MessagePackSerializer.Deserialize<T>(bytes, msgpackOptions);
//                }
//                catch
//                {

//                }
//            }

//            return element.Value.Deserialize<T>(jsonOptions);
//        }
//    }
//}