using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Tests.Fixtures;

namespace AsbtCore.Broker.Core.Tests.Routing;

public class DefaultRpcRouteResolverTests
{
    private DefaultRpcRouteResolver Create(string prefix = "rpc.")
        => new(Microsoft.Extensions.Options.Options.Create(new RpcOptions
        {
            HostName = "x",
            VirtualHost = "/",
            UserName = "u",
            Password = "p",
            ClientProvidedName = "c",
            RoutePrefix = prefix,
            Port = 5672
        }));

    [Test]
    public async Task Resolve_Interface_UsesFullNameWithPrefix()
    {
        var sut = Create("rpc.");

        var route = sut.Resolve(typeof(ITestService));

        await Assert.That(route).IsEqualTo("rpc." + typeof(ITestService).FullName);
    }

    [Test]
    public async Task Resolve_StringOverload_ReturnsSameResult()
    {
        var sut = Create("rpc.");
        var expected = sut.Resolve(typeof(ITestService));

        var actual = sut.Resolve(typeof(ITestService).FullName!);

        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task Resolve_NestedTypePlus_ReplacedWithDot()
    {
        var sut = Create("rpc.");

        var route = sut.Resolve("Some.NS+Nested");

        await Assert.That(route).IsEqualTo("rpc.Some.NS.Nested");
    }

    [Test]
    public async Task Resolve_SameInterfaceTwice_ReturnsCachedInstance()
    {
        var sut = Create("rpc.");
        var a = sut.Resolve("Same.Type");
        var b = sut.Resolve("Same.Type");

        await Assert.That(b).IsSameReferenceAs(a);
    }
}
