using System.Buffers;
using AsbtCore.Broker.Serialization.XPacketRpc.Internal;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// Verifies that, under heavy concurrent first-touch, the cache builds each per-type
/// invoker exactly once. The fragment builder is wrapped in a <c>Lazy</c> with
/// <c>ExecutionAndPublication</c>, so the build-count must remain 1 even when many
/// threads race on the same type.
/// </summary>
public sealed class FragmentInvokerCacheConcurrencyTests
{
    [Test]
    public async Task SingleTypeUnderRace_BuildsOnce()
    {
        var built = 0;
        var cache = new FragmentInvokerCache(_ =>
        {
            // Sleep briefly to widen the race window so a non-Lazy implementation
            // would deterministically double-build.
            Thread.Sleep(25);
            Interlocked.Increment(ref built);
            return new FragmentInvoker
            {
                Write = (_, _) => { },
                Read = _ => null,
            };
        });

        const int threads = 100;
        using var startGate = new ManualResetEventSlim(false);
        var tasks = new Task[threads];
        for (int i = 0; i < threads; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                startGate.Wait();
                _ = cache.GetOrBuild(typeof(int));
            });
        }
        // Ensure all tasks have started and parked on the gate before releasing them.
        await Task.Delay(50);
        startGate.Set();
        await Task.WhenAll(tasks);

        await Assert.That(built).IsEqualTo(1);
        await Assert.That(cache.BuildCount).IsEqualTo(1);
    }

    [Test]
    public async Task DistinctTypes_BuildOncePerType()
    {
        var perTypeCounts = new System.Collections.Concurrent.ConcurrentDictionary<Type, int>();
        var cache = new FragmentInvokerCache(t =>
        {
            perTypeCounts.AddOrUpdate(t, 1, (_, c) => c + 1);
            return new FragmentInvoker
            {
                Write = (_, _) => { },
                Read = _ => null,
            };
        });

        var types = new[] { typeof(int), typeof(long), typeof(string), typeof(Guid) };
        const int threadsPerType = 25;
        using var startGate = new ManualResetEventSlim(false);
        var tasks = new List<Task>();
        foreach (var t in types)
        {
            for (int i = 0; i < threadsPerType; i++)
            {
                var captured = t;
                tasks.Add(Task.Run(() =>
                {
                    startGate.Wait();
                    _ = cache.GetOrBuild(captured);
                }));
            }
        }
        await Task.Delay(50);
        startGate.Set();
        await Task.WhenAll(tasks);

        foreach (var t in types)
        {
            await Assert.That(perTypeCounts[t]).IsEqualTo(1);
        }
        await Assert.That(cache.BuildCount).IsEqualTo(types.Length);
    }
}
