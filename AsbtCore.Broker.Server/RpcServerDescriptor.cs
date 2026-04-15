using System.Reflection;

namespace AsbtCore.Broker.Server
{
    public sealed class RpcServerDescriptor
    {
        private readonly Dictionary<string, MethodInfo> methods;

        public Type InterfaceType { get; }
        public Type ImplementationType { get; }
        public string InterfaceName { get; }
        public string Route { get; }

        public RpcServerDescriptor(
            Type interfaceType,
            Type implementationType,
            string route,
            Dictionary<string, MethodInfo> methods)
        {
            InterfaceType = interfaceType;
            ImplementationType = implementationType;
            InterfaceName = interfaceType.FullName
                ?? throw new InvalidOperationException($"Interface {interfaceType} has no FullName.");
            Route = route;
            this.methods = methods;
        }

        public bool TryGetMethod(string methodName, IReadOnlyList<string> parameterTypeNames, out MethodInfo method)
        {
            var key = BuildMethodKey(methodName, parameterTypeNames);
            return methods.TryGetValue(key, out method!);
        }

        public static string BuildMethodKey(string methodName, IEnumerable<string> parameterTypeNames)
        {
            return $"{methodName}|{string.Join(";", parameterTypeNames)}";
        }
    }
}
