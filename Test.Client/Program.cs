using AsbtCore.Broker.Client;
using AsbtCore.Broker.Demo.Contracts;
using AsbtCore.Broker.Serialization.XPacketRpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AsbtCore.Broker.Demo.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddSerilog((services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext();
            });

            builder.Services
                .AddRabbitRpcClient(builder.Configuration)
                .UseXPacketRpcSerialization()
                .AddProxy<IUserService>();

            using var host = builder.Build();

            var userService = host.Services.GetRequiredService<IUserService>();

            var user = await userService.GetByIdAsync(1);
            Console.WriteLine(user is null
                ? "User not found"
                : $"User: {user.Id}, {user.Name}");
            
            await userService.PingAsync();
            Console.WriteLine("Ping completed.");

            while (true)
            {
                Console.WriteLine("Enter two numbers to sum (or 'exit' to quit):");

                var a = Console.ReadLine();
                var b = Console.ReadLine();

                if (string.Equals(a?.Trim(), "exit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b?.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!int.TryParse(a, out var ai) || !int.TryParse(b, out var bi))
                {
                    Console.WriteLine("Invalid input — please enter integers.");
                    continue;
                }

                var sum = await userService.SumAsync(ai, bi);
                Console.WriteLine($"Sum = {sum}");
            }
        }
    }
}
