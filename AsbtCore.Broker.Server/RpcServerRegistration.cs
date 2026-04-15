namespace AsbtCore.Broker.Server
{
    public sealed class RpcServerRegistration
    {
        public Type InterfaceType { get; }
        public Type ImplementationType { get; }
        public string ExplicitRoute { get; }

        public RpcServerRegistration(Type interfaceType, Type implementationType, string explicitRoute = null)
        {
            InterfaceType = interfaceType;
            ImplementationType = implementationType;
            ExplicitRoute = explicitRoute;
        }
    }
}
