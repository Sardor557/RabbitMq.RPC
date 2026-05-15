using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Server;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Server.Services;

namespace Server
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
                .AddRabbitRpcServer(builder.Configuration)
                .UseMemoryPackRpcSerialization()
                .Register<IUserService, UserService>(ServiceLifetime.Scoped)
                .Register<ITestDtoService, TestDtoService>(ServiceLifetime.Scoped);

            using var host = builder.Build();

            await host.RunAsync();
        }
    }
}
