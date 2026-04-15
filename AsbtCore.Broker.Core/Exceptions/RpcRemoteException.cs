namespace AsbtCore.Broker.Core.Exceptions
{
    public sealed class RpcRemoteException : Exception
    {
        public string RemoteCode { get; }
        public string RemoteExceptionType { get; }
        public string RemoteDetails { get; }

        public RpcRemoteException(
            string message,
            string remoteCode = null,
            string remoteExceptionType = null,
            string remoteDetails = null)
            : base(message)
        {
            RemoteCode = remoteCode;
            RemoteExceptionType = remoteExceptionType;
            RemoteDetails = remoteDetails;
        }
    }
}
