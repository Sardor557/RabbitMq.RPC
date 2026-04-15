using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AsbtCore.Broker.Core.Routing
{
    public sealed class DefaultRpcRouteResolver : IRpcRouteResolver
    {
        private readonly string prefix;
        private readonly ConcurrentDictionary<string, string> cache = new();

        public DefaultRpcRouteResolver(IOptions<RpcOptions> options)
        {
            prefix = options.Value.RoutePrefix;
        }

        public string Resolve(Type interfaceType)
        {
            var interfaceName = interfaceType.FullName
                ?? throw new InvalidOperationException($"Type {interfaceType} has no FullName.");

            return Resolve(interfaceName);
        }

        public string Resolve(string interfaceName)
        {
            return cache.GetOrAdd(interfaceName, name =>
            {
                var normalized = name
                    .Replace('+', '.')
                    .Replace(',', '_')
                    .Replace(' ', '_');

                return $"{prefix}{normalized}";
            });
        }
    }
}
