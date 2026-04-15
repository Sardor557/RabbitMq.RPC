using System.Reflection;

namespace AsbtCore.Broker.Client
{
    public class RpcProxyFactory
    {
        private readonly RpcClient client;

        public RpcProxyFactory(RpcClient client)
        {
            this.client = client;
        }

        public T CreateProxy<T>()
            where T : class
        {
            var proxy = DispatchProxy.Create<T, RpcDispatchProxy>();

            if (proxy is not RpcDispatchProxy dispatchProxy)
                throw new InvalidOperationException("Failed to create DispatchProxy.");

            dispatchProxy.Configure(client, typeof(T));
            return (T)(object)dispatchProxy;
        }
    }

    internal class RpcDispatchProxy : DispatchProxy
    {
        private RpcClient client = default!;
        private Type interfaceType = default!;
        private TimeSpan? timeout = TimeSpan.FromSeconds(10);

        public void Configure(RpcClient client, Type interfaceType)
        {
            this.client = client;
            this.interfaceType = interfaceType;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod is null)
                throw new ArgumentNullException(nameof(targetMethod));

            return client.InvokeProxy(interfaceType, targetMethod, args, timeout);
        }
    }
}
