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
            const int parallelThreads = 30;

            long totalCalls = 0;
            long callsUnder1Sec = 0;
            long callsPerSecond = 0;

            using var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (_, _) =>
            {
                long calls = Interlocked.Exchange(ref callsPerSecond, 0);
                Console.WriteLine($"[Timer] Calls per second: {calls}");
            };
            timer.Start();

            var tasks = Enumerable.Range(0, parallelThreads).Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var data = await service.GetItemsAsync(10);
                    sw.Stop();

                    Interlocked.Increment(ref totalCalls);
                    Interlocked.Increment(ref callsPerSecond);

                    if (sw.Elapsed.TotalSeconds < 1.0)
                        Interlocked.Increment(ref callsUnder1Sec);

                   // Console.WriteLine($"[Thread {threadId}] Iter {i + 1}: Count={data.LineItems.Count}, Elapsed={sw.Elapsed.TotalMilliseconds:F1}ms");
                }
            }));

            await Task.WhenAll(tasks);
            timer.Stop();

            Console.WriteLine();
            Console.WriteLine($"=== Results ===");
            Console.WriteLine($"Total calls:              {totalCalls}");
            Console.WriteLine($"Calls completed < 1 sec:  {callsUnder1Sec}");
            Console.WriteLine($"Calls completed >= 1 sec: {totalCalls - callsUnder1Sec}");
        }
    }
}