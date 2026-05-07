using System.Reflection;

namespace AsbtCore.Broker.Server;

internal sealed record RpcMethodEntry(MethodInfo Method, RpcMethodInvocation Invoker);

public sealed class RpcServerDescriptor
{
    private readonly Dictionary<string, RpcMethodEntry> methods;

    public Type InterfaceType { get; }
    public Type ImplementationType { get; }
    public string InterfaceName { get; }
    public string Route { get; }

    internal RpcServerDescriptor(
        Type interfaceType,
        Type implementationType,
        string route,
        Dictionary<string, RpcMethodEntry> methods)
    {
        InterfaceType = interfaceType;
        ImplementationType = implementationType;
        InterfaceName = interfaceType.FullName
            ?? throw new InvalidOperationException($"Interface {interfaceType} has no FullName.");
        Route = route;
        this.methods = methods;
    }

    internal bool TryGetMethod(string methodName, IReadOnlyList<string> parameterTypeNames, out RpcMethodEntry entry)
    {
        var key = BuildMethodKey(methodName, parameterTypeNames);
        return methods.TryGetValue(key, out entry!);
    }

    public static string BuildMethodKey(string methodName, IEnumerable<string> parameterTypeNames)
        => $"{methodName}|{string.Join(";", parameterTypeNames)}";
}
