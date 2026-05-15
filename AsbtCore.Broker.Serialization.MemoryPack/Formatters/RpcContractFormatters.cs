//using AsbtCore.Broker.Core;
//using MemoryPack;

//namespace AsbtCore.Broker.Serialization.MemoryPack.Formatters;

//internal sealed class RpcErrorFormatter : MemoryPackFormatter<RpcError>
//{
//    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcError? value)
//    {
//        if (value is null) { writer.WriteNullObjectHeader(); return; }
//        writer.WriteObjectHeader(4);
//        writer.WriteString(value.Code);
//        writer.WriteString(value.Message);
//        writer.WriteString(value.Details);
//        writer.WriteString(value.ExceptionType);
//    }

//    public override void Deserialize(ref MemoryPackReader reader, scoped ref RpcError? value)
//    {
//        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
//        var code = reader.ReadString();
//        var message = reader.ReadString();
//        var details = reader.ReadString();
//        var exceptionType = reader.ReadString();
//        value = new RpcError { Code = code!, Message = message!, Details = details, ExceptionType = exceptionType };
//    }
//}

//internal sealed class RpcArgumentFormatter : MemoryPackFormatter<RpcArgument>
//{
//    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcArgument? value)
//    {
//        if (value is null) { writer.WriteNullObjectHeader(); return; }
//        writer.WriteObjectHeader(2);
//        writer.WriteString(value.TypeName);
//        byte[]? bytes = value.Payload.ToArray();
//        writer.WriteValue(in bytes);
//    }

//    public override void Deserialize(ref MemoryPackReader reader, scoped ref RpcArgument? value)
//    {
//        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
//        var typeName = reader.ReadString();
//        var bytes = reader.ReadValue<byte[]>();
//        value = new RpcArgument { TypeName = typeName!, Payload = bytes ?? Array.Empty<byte>() };
//    }
//}

//internal sealed class RpcRequestFormatter : MemoryPackFormatter<RpcRequest>
//{
//    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcRequest? value)
//    {
//        if (value is null) { writer.WriteNullObjectHeader(); return; }
//        writer.WriteObjectHeader(4);
//        writer.WriteString(value.RequestId);
//        writer.WriteString(value.InterfaceName);
//        writer.WriteString(value.MethodName);
//        List<RpcArgument>? args = value.Arguments;
//        writer.WriteValue(in args);
//    }

//    public override void Deserialize(ref MemoryPackReader reader, scoped ref RpcRequest? value)
//    {
//        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
//        var requestId = reader.ReadString();
//        var interfaceName = reader.ReadString();
//        var methodName = reader.ReadString();
//        var args = reader.ReadValue<List<RpcArgument>>();
//        value = new RpcRequest
//        {
//            RequestId = requestId!,
//            InterfaceName = interfaceName!,
//            MethodName = methodName!,
//            Arguments = args ?? []
//        };
//    }
//}

//internal sealed class RpcResponseFormatter : MemoryPackFormatter<RpcResponse>
//{
//    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcResponse? value)
//    {
//        if (value is null) { writer.WriteNullObjectHeader(); return; }
//        writer.WriteObjectHeader(5);
//        writer.WriteString(value.RequestId);
//        writer.WriteUnmanaged(value.Success);
//        writer.WriteString(value.ResultTypeName);
//        byte[]? resultBytes = value.Result?.ToArray();
//        writer.WriteValue(in resultBytes);
//        RpcError? error = value.Error;
//        writer.WriteValue(in error);
//    }

//    public override void Deserialize(ref MemoryPackReader reader, scoped ref RpcResponse? value)
//    {
//        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
//        var requestId = reader.ReadString();
//        reader.ReadUnmanaged(out bool success);
//        var resultTypeName = reader.ReadString();
//        var resultBytes = reader.ReadValue<byte[]>();
//        var error = reader.ReadValue<RpcError>();
//        value = new RpcResponse
//        {
//            RequestId = requestId!,
//            Success = success,
//            ResultTypeName = resultTypeName,
//            Result = resultBytes is null ? (ReadOnlyMemory<byte>?)null : resultBytes,
//            Error = error
//        };
//    }
//}
