namespace AsbtCore.Broker.Core.Exceptions;

public sealed class RpcProtocolException : Exception
{
    public RpcProtocolException(string message) : base(message) { }
    public RpcProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
