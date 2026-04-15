using System.Collections.Generic;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Client
{
    [TestClass]
    public class ClientPackageExtensionsTests
    {
        private static IConfiguration BuildConfig() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RabbitMqRpc:HostName"] = "localhost",
                    ["RabbitMqRpc:Port"] = "5672",
                    ["RabbitMqRpc:VirtualHost"] = "/",
                    ["RabbitMqRpc:UserName"] = "guest",
                    ["RabbitMqRpc:Password"] = "guest",
                    ["RabbitMqRpc:ClientProvidedName"] = "test",
                    ["RabbitMqRpc:RoutePrefix"] = "rpc.",
                })
                .Build();

        [TestMethod]
        public void AddRabbitRpcClient_RegistersCoreServices()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddRabbitRpcClient(BuildConfig());
            var sp = services.BuildServiceProvider();

            Assert.IsNotNull(sp.GetService<IRpcSerializer>());
            Assert.IsNotNull(sp.GetService<IRpcRouteResolver>());
            Assert.IsNotNull(sp.GetService<RpcClient>());
            Assert.IsNotNull(sp.GetService<RpcProxyFactory>());
            Assert.IsNotNull(sp.GetService<IRabbitMqConnectionProvider>());
        }

        [TestMethod]
        public void AddRpcProxy_ResolvesAsImplementationOfInterface()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRabbitRpcClient(BuildConfig());
            services.AddRpcProxy<ITestService>();
            var sp = services.BuildServiceProvider();

            var proxy = sp.GetService<ITestService>();

            Assert.IsNotNull(proxy);
        }
    }
}
