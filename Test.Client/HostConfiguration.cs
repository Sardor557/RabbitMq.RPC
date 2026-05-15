using AsbtCore.Broker.Client;
using AsbtCore.Broker.Serialization. MemoryPack ;
using Contracts;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Client;

internal static class HostConfiguration
{
    /// <summary>
    /// Builds and configures the application host with all required services.
    /// </summary>
    internal static IHost BuildHost(string[] args)
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
            .UseMemoryPackRpcSerialization()                        
            .AddProxy<IUserService>()
            .AddProxy<ITestDtoService>();

        return builder.Build();
    }
}
