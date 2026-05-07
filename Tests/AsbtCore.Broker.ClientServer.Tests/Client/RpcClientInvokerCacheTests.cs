using System;
using System.Threading.Tasks;
using AsbtCore.Broker.Client;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

public sealed class RpcClientInvokerCacheTests
{
    public interface ISample
    {
        Task PingAsync();
        Task<int> SumAsync(int a, int b);
    }

    [Test]
    public async Task Get_TaskMethod_ReturnsDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.PingAsync))!;

        var del = RpcClientInvokerCache.Get(method);

        await Assert.That(del).IsNotNull();
    }

    [Test]
    public async Task Get_TaskOfTMethod_ReturnsDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;

        var del = RpcClientInvokerCache.Get(method);

        await Assert.That(del).IsNotNull();
    }

    [Test]
    public async Task Get_SameMethodTwice_ReturnsSameDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;

        var first = RpcClientInvokerCache.Get(method);
        var second = RpcClientInvokerCache.Get(method);

        await Assert.That(second).IsSameReferenceAs(first);
    }

    [Test]
    public async Task Get_NonTaskReturn_Throws()
    {
        var method = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

        await Assert.ThrowsAsync<NotSupportedException>(() =>
        {
            RpcClientInvokerCache.Get(method);
            return Task.CompletedTask;
        });
    }
}
