using System;
using System.Threading.Tasks;
using AsbtCore.Broker.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Server;

[TestClass]
public sealed class RpcServerMethodInvokerTests
{
    public sealed class Sample
    {
        public int Add(int a, int b) => a + b;

        public Task PingAsync() => Task.CompletedTask;

        public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);

        public async Task<string> EchoAsync(string s)
        {
            await Task.Yield();
            return s;
        }

        public Task<int> ThrowAsync()
            => throw new InvalidOperationException("boom");
    }

    [TestMethod]
    public async Task Build_SyncMethod_InvokesAndReturnsResult()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.Add))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), new object?[] { 3, 4 });

        Assert.AreEqual(7, result);
    }

    [TestMethod]
    public async Task Build_TaskMethod_ReturnsNull()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.PingAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), Array.Empty<object?>());

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Build_TaskOfTMethod_ReturnsValue()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.SumAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), new object?[] { 10, 20 });

        Assert.AreEqual(30, result);
    }

    [TestMethod]
    public async Task Build_AsyncTaskOfT_ReturnsValue()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.EchoAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), new object?[] { "hi" });

        Assert.AreEqual("hi", result);
    }

    [TestMethod]
    public async Task Build_ThrowingMethod_PropagatesException()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.ThrowAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            async () => await invoker(new Sample(), Array.Empty<object?>()));
    }
}
