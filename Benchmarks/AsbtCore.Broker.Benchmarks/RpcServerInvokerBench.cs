using System.Reflection;
using AsbtCore.Broker.Server;
using BenchmarkDotNet.Attributes;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
public class RpcServerInvokerBench
{
    public sealed class Sample
    {
        public int Add(int a, int b) => a + b;
        public Task PingAsync() => Task.CompletedTask;
        public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);
    }

    private Sample instance = null!;
    private MethodInfo addMethod = null!;
    private MethodInfo pingMethod = null!;
    private MethodInfo sumMethod = null!;
    private RpcMethodInvocation addInvoker = null!;
    private RpcMethodInvocation pingInvoker = null!;
    private RpcMethodInvocation sumInvoker = null!;

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        instance = new Sample();
        addMethod = typeof(Sample).GetMethod(nameof(Sample.Add))!;
        pingMethod = typeof(Sample).GetMethod(nameof(Sample.PingAsync))!;
        sumMethod = typeof(Sample).GetMethod(nameof(Sample.SumAsync))!;
        addInvoker = RpcServerMethodInvoker.Build(addMethod);
        pingInvoker = RpcServerMethodInvoker.Build(pingMethod);
        sumInvoker = RpcServerMethodInvoker.Build(sumMethod);
    }

    [Benchmark]
    public async Task<object?> SumAsync_Invoke() => Mode switch
    {
        LegacyOrNew.Legacy => await LegacyInvokeAsync(sumMethod, new object?[] { 3, 4 }),
        LegacyOrNew.New => await sumInvoker(instance, new object?[] { 3, 4 }),
        _ => throw new InvalidOperationException()
    };

    [Benchmark]
    public async Task<object?> Add_Invoke() => Mode switch
    {
        LegacyOrNew.Legacy => await LegacyInvokeAsync(addMethod, new object?[] { 3, 4 }),
        LegacyOrNew.New => await addInvoker(instance, new object?[] { 3, 4 }),
        _ => throw new InvalidOperationException()
    };

    [Benchmark]
    public async Task<object?> Ping_Invoke() => Mode switch
    {
        LegacyOrNew.Legacy => await LegacyInvokeAsync(pingMethod, Array.Empty<object?>()),
        LegacyOrNew.New => await pingInvoker(instance, Array.Empty<object?>()),
        _ => throw new InvalidOperationException()
    };

    private async Task<object?> LegacyInvokeAsync(MethodInfo method, object?[] args)
    {
        var result = method.Invoke(instance, args);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return task.GetType().GetProperty("Result")!.GetValue(task);
            }
            return null;
        }
        return result;
    }
}
