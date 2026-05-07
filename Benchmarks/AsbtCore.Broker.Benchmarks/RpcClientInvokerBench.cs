using System.Reflection;
using AsbtCore.Broker.Client;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class RpcClientInvokerBench
{
    public interface ISample
    {
        Task PingAsync();
        Task<int> SumAsync(int a, int b);
    }

    private MethodInfo sumMethod = null!;
    private RpcClientInvocation cachedSum = null!;
    private RpcClient client = null!;

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        sumMethod = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;
        client = BenchmarkClientFactory.CreateInProcessClient();
        cachedSum = RpcClientInvokerCache.Get(sumMethod);
    }

    [Benchmark]
    public object SumAsync_Dispatch() => Mode switch
    {
        LegacyOrNew.Legacy => LegacyDispatch(sumMethod, new object[] { 1, 2 }),
        LegacyOrNew.New => cachedSum(client, typeof(ISample), new object[] { 1, 2 }, null, default),
        _ => throw new InvalidOperationException()
    };

    private object LegacyDispatch(MethodInfo method, object[] args)
    {
        var resultType = method.ReturnType.GetGenericArguments()[0];
        var generic = typeof(RpcClient)
            .GetMethod("InvokeGenericAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType);
        return generic.Invoke(client, new object?[] { typeof(ISample), method, args, null, CancellationToken.None })!;
    }
}
