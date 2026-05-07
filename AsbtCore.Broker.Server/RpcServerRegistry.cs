using System.Reflection;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;

namespace AsbtCore.Broker.Server;

public sealed class RpcServerRegistry
{
    private readonly Dictionary<string, RpcServerDescriptor> map;

    public RpcServerRegistry(
        IEnumerable<RpcServerRegistration> registrations,
        IRpcRouteResolver routeResolver)
    {
        map = new Dictionary<string, RpcServerDescriptor>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            var interfaceType = registration.InterfaceType;
            var implementationType = registration.ImplementationType;

            var methods = BuildMethodMap(interfaceType, implementationType);
            var route = registration.ExplicitRoute ?? routeResolver.Resolve(interfaceType);

            var descriptor = new RpcServerDescriptor(
                interfaceType,
                implementationType,
                route,
                methods);

            map[descriptor.InterfaceName] = descriptor;
        }
    }

    public bool TryGet(string interfaceName, out RpcServerDescriptor descriptor)
        => map.TryGetValue(interfaceName, out descriptor!);

    public IReadOnlyCollection<string> GetRoutes()
        => map.Values
            .Select(x => x.Route)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, RpcMethodEntry> BuildMethodMap(Type interfaceType, Type implementationType)
    {
        var result = new Dictionary<string, RpcMethodEntry>(StringComparer.Ordinal);
        var map = implementationType.GetInterfaceMap(interfaceType);

        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var interfaceMethod = map.InterfaceMethods[i];
            var targetMethod = map.TargetMethods[i];

            var parameterTypeNames = interfaceMethod
                .GetParameters()
                .Select(p => StableTypeName.From(p.ParameterType))
                .ToArray();

            var key = RpcServerDescriptor.BuildMethodKey(interfaceMethod.Name, parameterTypeNames);
            var invoker = RpcServerMethodInvoker.Build(targetMethod);
            var logicalResultType = GetLogicalResultType(targetMethod.ReturnType);
            result[key] = new RpcMethodEntry(targetMethod, invoker, logicalResultType);
        }

        return result;
    }

    private static Type? GetLogicalResultType(Type returnType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task))
            return null;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return returnType.GetGenericArguments()[0];

        return returnType;
    }
}
