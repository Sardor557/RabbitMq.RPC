using BenchmarkDotNet.Attributes;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class PublishConcurrencyBench
{
    private SemaphoreSlim semaphore = null!;

    [Params(1, 4, 16, 64)]
    public int Concurrency { get; set; }

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup() => semaphore = new SemaphoreSlim(1, 1);

    [Benchmark]
    public async Task ParallelPublish()
    {
        var tasks = new Task[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Mode switch
            {
                LegacyOrNew.Legacy => LegacyPublishAsync(),
                LegacyOrNew.New => NewPublishAsync(),
                _ => throw new InvalidOperationException()
            };
        }
        await Task.WhenAll(tasks);
    }

    private async Task LegacyPublishAsync()
    {
        await semaphore.WaitAsync();
        try { await SimulatePublishAsync(); }
        finally { semaphore.Release(); }
    }

    private static Task NewPublishAsync() => SimulatePublishAsync();

    private static async Task SimulatePublishAsync()
    {
        await Task.Yield();
    }
}
