namespace AsbtCore.Broker.Core.Abstractions;

/// <summary>
/// Optional capability surface for <see cref="IRpcSerializer"/> implementations that need
/// to pre-register types from RPC interface signatures (e.g. source-generated binary serializers).
/// AddRpcProxy and Register call <see cref="Prewarm"/> on startup if the registered serializer
/// implements this interface; implementations that do not need warm-up should not implement it.
/// </summary>
public interface IRpcSerializerInterfaceWarmup
{
    /// <summary>
    /// Walks all methods of <paramref name="interfaceType"/>, unwraps Task/ValueTask return types,
    /// and recursively registers every parameter and return type with the underlying format.
    /// </summary>
    void Prewarm(Type interfaceType);
}
