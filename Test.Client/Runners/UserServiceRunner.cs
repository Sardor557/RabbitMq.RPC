using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Client.Runners
{
    static class UserServiceRunner
    {
        static async Task RunAsync(IHost host)
        {
            // ── IUserService demo ─────────────────────────────────────────────
            var userService = host.Services.GetRequiredService<IUserService>();

            var user = await userService.GetByIdAsync(1);
            Console.WriteLine(user is null ? "User not found" : $"User: {user.Id}, {user.Name}");

            await userService.PingAsync();
            Console.WriteLine("Ping completed.");

            while (true)
            {
                Console.WriteLine("Enter three numbers to sum (or 'exit' to quit):");

                var a = Console.ReadLine();
                var b = Console.ReadLine();
                var c = Console.ReadLine();

                if (string.Equals(a?.Trim(), "exit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b?.Trim(), "exit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c?.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!int.TryParse(a, out var ai) || !int.TryParse(b, out var bi) || !int.TryParse(c, out var ci))
                {
                    Console.WriteLine("Invalid input — please enter integers.");
                    continue;
                }

                var sum = await userService.SumAllAsync(new RqModel { a = ai, b = bi, c = ci });
                Console.WriteLine($"Sum = {sum}");
            }

        }
    }
}