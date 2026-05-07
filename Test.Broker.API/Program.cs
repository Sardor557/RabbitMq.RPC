using AsbtCore.Broker.API.Services;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Demo.Contracts;
using AsbtCore.Broker.Server;
using Microsoft.AspNetCore.HttpOverrides;
using Newtonsoft.Json.Serialization;
using Serilog;
using Serilog.Exceptions;
using Serilog.Settings.Configuration;

namespace AsbtCore.Broker.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration.AddEnvironmentVariables();

            var configurationAssemblies = new[]
            {
                typeof(ConsoleLoggerConfigurationExtensions).Assembly,
                typeof(FileLoggerConfigurationExtensions).Assembly,
            };

            var options = new ConfigurationReaderOptions(configurationAssemblies);

            Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithExceptionDetails()
                    .Enrich.WithProperty("Environment", builder.Environment)
                    .ReadFrom.Configuration(builder.Configuration, options)
                    .CreateLogger();

            builder.Services.AddControllers(o => { o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true; })
                            .AddNewtonsoftJson(o => { o.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver(); });              

            builder.Services.ApiMyVersion();
            builder.Services.AddMySwagger();
            builder.Services.AddSerilog();
            builder.Services.AddMyResponseCompression();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAllHeaders", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyHeader()
                           .AllowAnyMethod();
                });
            }); 

            builder.Services.AddMemoryCache();
            builder.Services.AddHttpContextAccessor();

            //builder.Services.AddRpcSerialization<JsonRpcSerializer> ();
            builder.Services.AddRabbitRpcServer(builder.Configuration)
                .Register<IUserService, UserService>(ServiceLifetime.Scoped);

            builder.Services.AddRpcSerialization<MessagePackRpcSerializer>();


            var app = builder.Build();
            app.UseMySwagger();
            app.UseMyStaticFiles();

            app.UseRouting();
            app.UseCors("AllowAllHeaders");
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
            app.MapControllers();

            app.UseSerilogRequestLogging();
            app.UseResponseCompression();

            app.Run();
        }
    }
}
