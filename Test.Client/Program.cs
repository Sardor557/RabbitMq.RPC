using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Demo.Contracts;
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

            //builder.Services.AddRpcSerialization<JsonRpcSerializer>();  
            builder.Services.AddRpcSerialization<MessagePackRpcSerializer>();
            builder.Services
                .AddRabbitRpcClient(builder.Configuration)
                .AddRpcProxy<IUserService>();

            using var host = builder.Build();

            var serializer = host.Services.GetRequiredService<IRpcSerializer>();
            Console.WriteLine($">>> Using serializer: {serializer.GetType().Name}");
            Console.WriteLine($">>> ContentType: {serializer.ContentType}");

            var userService = host.Services.GetRequiredService<IUserService>();

            var user = await userService.GetByIdAsync(1);
            Console.WriteLine(user is null
                ? "User not found"
                : $"User: {user.Id}, {user.Name}");
            
            await userService.PingAsync();
            Console.WriteLine("Ping completed.");

            while (true)
            {
              
                //var sum = await userService.SumAsync(Convert.ToInt32(a), Convert.ToInt32(b));
                //Console.WriteLine($"Sum = {sum}");


                var ls = userService.GetManyAsync(1000);

            }
        }
    }
}
