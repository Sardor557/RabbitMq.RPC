namespace AsbtCore.Broker.Core
{    
    public partial class RpcRequest
    {
        public string RequestId { get; set; } = default!;
        public string InterfaceName { get; set; } = default!;
        public string MethodName { get; set; } = default!;
        public List<RpcArgument> Arguments { get; set; } = new();
    }

    public partial class RpcArgument
    {
        public string TypeName { get; set; } = default!;
        public ReadOnlyMemory<byte> Payload { get; set; }
    }

    public partial class RpcResponse
    {
        public string RequestId { get; set; } = default!;
        public bool Success { get; set; }
        public string? ResultTypeName { get; set; }
        public ReadOnlyMemory<byte>? Result { get; set; }
        public RpcError? Error { get; set; }
    }

    public partial class RpcError
    {
        public string Code { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string? Details { get; set; }
        public string? ExceptionType { get; set; }
    }
}
