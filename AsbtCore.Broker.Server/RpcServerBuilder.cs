using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Server
{
    public sealed class RpcServerBuilder
    {
        public IServiceCollection Services { get; }

        public RpcServerBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public RpcServerBuilder Register<TInterface, TImplementation>(
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            string route = null)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            Services.TryAdd(new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
            Services.TryAdd(new ServiceDescriptor(typeof(TInterface), sp => sp.GetRequiredService<TImplementation>(), lifetime));

            Services.AddSingleton(new RpcServerRegistration(
                typeof(TInterface),
                typeof(TImplementation),
                route));

            return this;
        }
    }
}
