using System.Linq;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Server
{
    [TestClass]
    public class RpcServerBuilderTests
    {
        [TestMethod]
        public void Register_AddsImplementationAndInterfaceToDi()
        {
            var services = new ServiceCollection();
            var sut = new RpcServerBuilder(services);

            sut.Register<ITestService, TestServiceImpl>();

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            Assert.IsInstanceOfType(scope.ServiceProvider.GetRequiredService<ITestService>(), typeof(TestServiceImpl));
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<TestServiceImpl>());
        }

        [TestMethod]
        public void Register_AddsRpcServerRegistration()
        {
            var services = new ServiceCollection();
            var sut = new RpcServerBuilder(services);

            sut.Register<ITestService, TestServiceImpl>(route: "custom.route");

            var sp = services.BuildServiceProvider();
            var registrations = sp.GetServices<RpcServerRegistration>().ToList();

            Assert.AreEqual(1, registrations.Count);
            Assert.AreEqual(typeof(ITestService), registrations[0].InterfaceType);
            Assert.AreEqual(typeof(TestServiceImpl), registrations[0].ImplementationType);
            Assert.AreEqual("custom.route", registrations[0].ExplicitRoute);
        }

        [TestMethod]
        public void Register_Singleton_UsesSingletonLifetime()
        {
            var services = new ServiceCollection();
            var sut = new RpcServerBuilder(services);

            sut.Register<ITestService, TestServiceImpl>(ServiceLifetime.Singleton);

            var sp = services.BuildServiceProvider();
            var a = sp.GetRequiredService<ITestService>();
            var b = sp.GetRequiredService<ITestService>();

            Assert.AreSame(a, b);
        }
    }
}
