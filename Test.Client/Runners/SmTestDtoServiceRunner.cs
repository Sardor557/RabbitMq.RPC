using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace Client.Runners
{
    public static class SmTestDtoServiceRunner
    {
        public static async Task RunAsync(IHost host)
        {
            var service = host.Services.GetRequiredService<ITestDtoService>();

            const int iterations = 1000;
            const int parallelThreads = 5;

            long totalCalls = 0;
            long callsUnder1Sec = 0;

            var tasks = Enumerable.Range(0, parallelThreads).Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var data = await service.GetItemsAsync(3);
                    sw.Stop();

                    Interlocked.Increment(ref totalCalls);

                    if (sw.Elapsed.TotalSeconds < 1.0)
                        Interlocked.Increment(ref callsUnder1Sec);

                    Console.WriteLine($"[Thread {threadId}] Iter {i + 1}: Count={data.LineItems.Count}, Elapsed={sw.Elapsed.TotalMilliseconds:F1}ms");
                }
            }));

            await Task.WhenAll(tasks);

            Console.WriteLine();
            Console.WriteLine($"=== Results ===");
            Console.WriteLine($"Total calls:              {totalCalls}");
            Console.WriteLine($"Calls completed < 1 sec:  {callsUnder1Sec}");
            Console.WriteLine($"Calls completed >= 1 sec: {totalCalls - callsUnder1Sec}");
        }
    }
}