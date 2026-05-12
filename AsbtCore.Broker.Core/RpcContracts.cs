namespace AsbtCore.Broker.Core
{
    public sealed class RpcRequest
    {
        public string RequestId { get; set; } = default!;
        public string InterfaceName { get; set; } = default!;
        public string MethodName { get; set; } = default!;
        public List<RpcArgument> Arguments { get; set; } = new();
    }

    public sealed class RpcArgument
    {
        public string TypeName { get; set; } = default!;
        public ReadOnlyMemory<byte> Payload { get; set; }
    }

    public sealed class RpcResponse
    {
        public string RequestId { get; set; } = default!;
        public bool Success { get; set; }
        public string? ResultTypeName { get; set; }
        public ReadOnlyMemory<byte>? Result { get; set; }
        public RpcError? Error { get; set; }
    }

    public sealed class RpcError
    {
        public string Code { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string? Details { get; set; }
        public string? ExceptionType { get; set; }
    }
}
