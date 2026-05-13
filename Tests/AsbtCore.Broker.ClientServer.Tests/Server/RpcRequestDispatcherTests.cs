using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AsbtCore.Broker.ClientServer.Tests.Server;

public sealed class RpcRequestDispatcherTests
{
    private static string Stn(Type t) => $"{t.FullName}, {t.Assembly.GetName().Name}";
    private static readonly JsonRpcSerializer Serializer = new();

    private static RpcArgument Arg<T>(T value) => new()
    {
        TypeName = Stn(typeof(T)),
        Payload = Serializer.PackPayload(value, typeof(T))
    };

    private static (RpcServerRegistry registry, RpcRequestDispatcher dispatcher) BuildSut(
        params (Type iface, Type impl)[] registrations)
    {
        var route = new Mock<IRpcRouteResolver>();
        route.Setup(x => x.Resolve(It.IsAny<Type>())).Returns<Type>(t => t.FullName!);

        var regs = registrations.Select(r => new RpcServerRegistration(r.iface, r.impl)).ToArray();
        var registry = new RpcServerRegistry(regs, route.Object);

        var services = new ServiceCollection();
        foreach (var (_, impl) in registrations)
            services.AddScoped(impl);

        var sp = services.BuildServiceProvider();
        var dispatcher = new RpcRequestDispatcher(
            registry,
            sp.GetRequiredService<IServiceScopeFactory>(),
            Serializer);

        return (registry, dispatcher);
    }

    [Test]
    public async Task DispatchAsync_ServiceNotFound_ReturnsServiceNotFoundError()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = "No.Such.Interface",
            MethodName = "Foo",
            Arguments = []
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error!.Code).IsEqualTo("service_not_found");
    }

    [Test]
    public async Task DispatchAsync_MethodNotFound_ReturnsMethodNotFoundError()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = "NoSuchMethod",
            Arguments = []
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error!.Code).IsEqualTo("method_not_found");
    }

    [Test]
    public async Task DispatchAsync_VoidMethod_ReturnsSuccessWithNullResult()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.NotifyAsync),
            Arguments = [Arg("hello")]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Result).IsNull();
    }

    [Test]
    public async Task DispatchAsync_TypedMethod_ReturnsSuccessWithResult()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.AddAsync),
            Arguments = [Arg(3), Arg(4)]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        var result = (int?)Serializer.UnpackPayload(response.Result!, typeof(int));
        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task DispatchAsync_InvocationThrows_ReturnsInvocationError()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(ThrowingServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.AddAsync),
            Arguments = [Arg(1), Arg(2)]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error!.Code).IsEqualTo("invocation_error");
        await Assert.That(response.Error.Message).Contains("boom");
    }

    [Test]
    public async Task DispatchAsync_BadPayloadForKnownType_ReturnsDeserializationError()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.AddAsync),
            Arguments =
            [
                new RpcArgument
                {
                    TypeName = Stn(typeof(int)),
                    Payload = Serializer.PackPayload("not-a-number", typeof(string))
                },
                Arg(2)
            ]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Error!.Code).IsEqualTo("deserialization_error");
    }

    [Test]
    public async Task DispatchAsync_GetUserAsync_ReturnsComplexResult()
    {
        var (_, dispatcher) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));
        var id = Guid.NewGuid();

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.GetUserAsync),
            Arguments = [Arg(id)]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.ResultTypeName).IsNotNull();
    }
}
