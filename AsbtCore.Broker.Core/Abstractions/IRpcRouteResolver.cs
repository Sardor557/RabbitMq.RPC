namespace AsbtCore.Broker.Core.Abstractions
{
    public interface IRpcRouteResolver
    {
        string Resolve(Type interfaceType);
        string Resolve(string interfaceName);
    }
}
