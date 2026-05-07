using System.Reflection;
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public sealed class RpcProxyFactory
{
    private readonly RpcClient client;
    private readonly TimeSpan defaultTimeout;

    public RpcProxyFactory(RpcClient client, IOptions<RpcOptions> options)
    {
        this.client = client;
        this.defaultTimeout = TimeSpan.FromSeconds(options.Value.DefaultTimeoutSeconds);
    }

    public T CreateProxy<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, RpcDispatchProxy>();

        if (proxy is not RpcDispatchProxy dispatchProxy)
            throw new InvalidOperationException("Failed to create DispatchProxy.");

        dispatchProxy.Configure(client, typeof(T), defaultTimeout);
        return (T)(object)dispatchProxy;
    }
}

internal class RpcDispatchProxy : DispatchProxy
{
    private RpcClient client = default!;
    private Type interfaceType = default!;
    private TimeSpan timeout;

    public void Configure(RpcClient client, Type interfaceType, TimeSpan timeout)
    {
        this.client = client;
        this.interfaceType = interfaceType;
        this.timeout = timeout;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        return client.InvokeProxy(interfaceType, targetMethod, args ?? Array.Empty<object>()!, timeout);
    }
}
