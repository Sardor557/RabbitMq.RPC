namespace AsbtCore.Broker.Core.Internal;

/// <summary>
/// DI marker — collected as <c>IEnumerable&lt;RpcInterfaceRegistration&gt;</c> by both the client
/// (RpcClientBuilder.AddProxy) and the server (RpcServerBuilder.Register) so that the
/// serializer can prewarm types from RPC interface signatures on host startup.
/// </summary>
public sealed class RpcInterfaceRegistration
{
    public RpcInterfaceRegistration(Type interfaceType) => InterfaceType = interfaceType;

    public Type InterfaceType { get; }
}
