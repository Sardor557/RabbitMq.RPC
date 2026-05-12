using System.Text;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AsbtCore.Broker.ClientServer.Tests.Server;

public sealed class RpcRequestDispatcherTests
{
    private static string Stn(Type t) => $"{t.FullName}, {t.Assembly.GetName().Name}";

    private static RpcArgument Arg<T>(T value) => new()
    {
        TypeName = Stn(typeof(T)),
        Payload = TestSerializer.BuildFragment(value, typeof(T))
    };

    private static (RpcServerRegistry registry, RpcRequestDispatcher dispatcher, TestSerializer serializer) BuildSut(
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
        var serializer = new TestSerializer();
        var dispatcher = new RpcRequestDispatcher(registry, sp.GetRequiredService<IServiceScopeFactory>(), serializer);

        return (registry, dispatcher, serializer);
    }

    [Test]
    public async Task DispatchAsync_ServiceNotFound_ReturnsServiceNotFoundError()
    {
        var (_, dispatcher, _) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

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
        var (_, dispatcher, _) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

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
        var (_, dispatcher, _) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.NotifyAsync),
            Arguments = [Arg("hello")]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        await Assert.That(response.Result.HasValue).IsFalse();
    }

    [Test]
    public async Task DispatchAsync_TypedMethod_ReturnsSuccessWithResult()
    {
        var (_, dispatcher, serializer) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.AddAsync),
            Arguments = [Arg(3), Arg(4)]
        };

        var response = await dispatcher.DispatchAsync(request);

        await Assert.That(response.Success).IsTrue();
        var result = (int)serializer.DeserializeFragment(response.Result!.Value, typeof(int))!;
        await Assert.That(result).IsEqualTo(7);
    }

    [Test]
    public async Task DispatchAsync_InvocationThrows_ReturnsInvocationError()
    {
        var (_, dispatcher, _) = BuildSut((typeof(ITestService), typeof(ThrowingServiceImpl)));

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
        var (_, dispatcher, _) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));

        // TestSerializer encodes payloads as "TYPE:VALUE". A bytes payload that cannot be
        // converted to int (e.g., string text "not-a-number" without a numeric body) triggers
        // ChangeType failure.
        var badPayload = Encoding.UTF8.GetBytes($"{typeof(int).FullName}:not-a-number");

        var request = new RpcRequest
        {
            InterfaceName = typeof(ITestService).FullName!,
            MethodName = nameof(ITestService.AddAsync),
            Arguments =
            [
                new RpcArgument
                {
                    TypeName = Stn(typeof(int)),
                    Payload = badPayload
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
        var (_, dispatcher, _) = BuildSut((typeof(ITestService), typeof(TestServiceImpl)));
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
