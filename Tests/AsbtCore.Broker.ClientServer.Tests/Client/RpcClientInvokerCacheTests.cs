using System;
using System.Threading.Tasks;
using AsbtCore.Broker.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

[TestClass]
public sealed class RpcClientInvokerCacheTests
{
    public interface ISample
    {
        Task PingAsync();
        Task<int> SumAsync(int a, int b);
    }

    [TestMethod]
    public void Get_TaskMethod_ReturnsDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.PingAsync))!;

        var del = RpcClientInvokerCache.Get(method);

        Assert.IsNotNull(del);
    }

    [TestMethod]
    public void Get_TaskOfTMethod_ReturnsDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;

        var del = RpcClientInvokerCache.Get(method);

        Assert.IsNotNull(del);
    }

    [TestMethod]
    public void Get_SameMethodTwice_ReturnsSameDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;

        var first = RpcClientInvokerCache.Get(method);
        var second = RpcClientInvokerCache.Get(method);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Get_NonTaskReturn_Throws()
    {
        var method = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

        Assert.ThrowsException<NotSupportedException>(() => RpcClientInvokerCache.Get(method));
    }
}
