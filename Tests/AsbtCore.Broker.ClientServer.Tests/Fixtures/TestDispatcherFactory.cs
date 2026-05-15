using System.Reflection;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AsbtCore.Broker.ClientServer.Tests.Fixtures;

internal static class TestDispatcherFactory
{
    public static RpcRequestDispatcher Create<TInterface, TImpl>()
        where TInterface : class
        where TImpl : class, TInterface
    {
        var routeMock = new Mock<IRpcRouteResolver>();
        routeMock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => "rpc." + t.FullName);

        var registry = new RpcServerRegistry(
            new[] { new RpcServerRegistration(typeof(TInterface), typeof(TImpl)) },
            routeMock.Object);

        var services = new ServiceCollection();
        services.AddScoped<TImpl>();
        services.AddScoped<TInterface>(sp => sp.GetRequiredService<TImpl>());
        var sp = services.BuildServiceProvider();

        return new RpcRequestDispatcher(registry, sp.GetRequiredService<IServiceScopeFactory>(), new TestSerializer());
    }

    /// <summary>
    /// Creates a dispatcher whose registry has a method entry keyed with a bogus type name.
    /// This allows testing the type_not_found error path, where method lookup succeeds
    /// but TypeNameCache.Resolve fails for the argument's TypeName.
    /// </summary>
    public static (RpcRequestDispatcher Dispatcher, string BogusTypeName) CreateWithBogusArgTypeName<TInterface, TImpl>(
        string methodName)
        where TInterface : class
        where TImpl : class, TInterface
    {
        const string bogusTypeName = "Bogus.NonExistent.Type, BogusAssembly";

        var routeMock = new Mock<IRpcRouteResolver>();
        routeMock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => "rpc." + t.FullName);

        // Build a method map with a key using the bogus type name
        var interfaceMethod = typeof(TInterface).GetMethod(methodName)!;
        var implMethod = typeof(TImpl).GetMethod(methodName)!;
        var invoker = RpcServerMethodInvoker.Build(implMethod);
        var entry = new RpcMethodEntry(interfaceMethod, invoker, null);

        var methodKey = RpcServerDescriptor.BuildMethodKey(methodName, new[] { bogusTypeName });
        var methodMap = new Dictionary<string, RpcMethodEntry> { [methodKey] = entry };

        var descriptor = new RpcServerDescriptor(
            typeof(TInterface),
            typeof(TImpl),
            "rpc." + typeof(TInterface).FullName,
            methodMap);

        var registryMap = new Dictionary<string, RpcServerDescriptor>(StringComparer.Ordinal)
        {
            [typeof(TInterface).FullName!] = descriptor
        };

        // Use reflection to build a registry with our custom map
        var registry = CreateRegistryFromMap(registryMap);

        var services = new ServiceCollection();
        services.AddScoped<TImpl>();
        services.AddScoped<TInterface>(sp => sp.GetRequiredService<TImpl>());
        var sp = services.BuildServiceProvider();

        return (new RpcRequestDispatcher(registry, sp.GetRequiredService<IServiceScopeFactory>(), new TestSerializer()), bogusTypeName);
    }

    private static RpcServerRegistry CreateRegistryFromMap(Dictionary<string, RpcServerDescriptor> map)
    {
        // RpcServerRegistry has no public constructor that accepts a pre-built map,
        // so we construct one normally and then replace its internal map via reflection.
        var routeMock = new Mock<IRpcRouteResolver>();
        routeMock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => "rpc." + t.FullName);

        // Build an empty registry then inject our map
        var registry = new RpcServerRegistry(Array.Empty<RpcServerRegistration>(), routeMock.Object);

        var mapField = typeof(RpcServerRegistry)
            .GetField("map", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var internalMap = (Dictionary<string, RpcServerDescriptor>)mapField.GetValue(registry)!;
        foreach (var kv in map)
            internalMap[kv.Key] = kv.Value;

        return registry;
    }
}
