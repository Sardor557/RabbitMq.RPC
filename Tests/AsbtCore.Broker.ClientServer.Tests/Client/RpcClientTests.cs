using System.Reflection;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

public class RpcClientTests
{
    private Mock<IRpcTransport> transportMock = null!;
    private Mock<IRpcRouteResolver> routeMock = null!;
    private TestSerializer serializer = null!;
    private RpcOptions options = null!;

    [Before(Test)]
    public void Init()
    {
        transportMock = new Mock<IRpcTransport>();
        routeMock = new Mock<IRpcRouteResolver>();
        routeMock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns("rpc.route");
        serializer = new TestSerializer();
        options = new RpcOptions
        {
            HostName = "x", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "c", Port = 5672, DefaultTimeoutSeconds = 7
        };
    }

    private RpcClient CreateSut()
        => new(transportMock.Object, routeMock.Object, serializer, MsOptions.Create(options));

    private static MethodInfo Method(string name)
        => typeof(ITestService).GetMethod(name)!;

    private static ReadOnlyMemory<byte>? PackResult(object? value, Type type)
        => TestSerializer.BuildFragment(value, type);

    [Test]
    public async Task Invoke_TaskOfT_ReturnsDeserializedResult()
    {
        transportMock
            .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), "rpc.route", It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpcResponse
            {
                RequestId = "r",
                Success = true,
                Result = PackResult(42, typeof(int))
            });

        var sut = CreateSut();
        var task = (Task<int>)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1, 2 }, null })!;

        var result = await task;

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task Invoke_TaskVoid_CompletesSuccessfully()
    {
        transportMock
            .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpcResponse { RequestId = "r", Success = true });

        var sut = CreateSut();
        var task = (Task)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.NotifyAsync)), new object[] { "hi" }, null })!;

        await task;
    }

    [Test]
    public async Task Invoke_ResponseNotSuccess_ThrowsRpcRemoteException()
    {
        transportMock
            .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpcResponse
            {
                RequestId = "r",
                Success = false,
                Error = new RpcError { Code = "E", Message = "bad", ExceptionType = "T", Details = "d" }
            });

        var sut = CreateSut();
        var task = (Task<int>)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1, 2 }, null })!;

        var ex = await Assert.ThrowsAsync<RpcRemoteException>(() => task);
        await Assert.That(ex!.Message).IsEqualTo("bad");
        await Assert.That(ex.RemoteCode).IsEqualTo("E");
        await Assert.That(ex.RemoteExceptionType).IsEqualTo("T");
        await Assert.That(ex.RemoteDetails).IsEqualTo("d");
    }

    [Test]
    public async Task Invoke_ArgumentCountMismatch_ThrowsInvalidOperation()
    {
        var sut = CreateSut();
        var task = (Task<int>)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1 }, null })!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Test]
    public async Task Invoke_MethodWithCancellationToken_ThrowsNotSupported()
    {
        var sut = CreateSut();
        var method = typeof(ICancellableService).GetMethod(nameof(ICancellableService.DoAsync))!;

        var task = (Task)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ICancellableService), method, new object[] { CancellationToken.None }, null })!;

        await Assert.ThrowsAsync<NotSupportedException>(() => task);
    }

    [Test]
    public async Task Invoke_NonTaskReturn_ThrowsNotSupported()
    {
        var sut = CreateSut();
        var method = typeof(ISyncService).GetMethod(nameof(ISyncService.Add))!;

        var ex = await Assert.ThrowsAsync<TargetInvocationException>(() =>
        {
            sut.GetType()
                .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(sut, new object?[] { typeof(ISyncService), method, new object[] { 1, 2 }, null });
            return Task.CompletedTask;
        });

        await Assert.That(ex!.InnerException).IsAssignableTo<NotSupportedException>();
    }

    [Test]
    public async Task Invoke_NoExplicitTimeout_UsesDefaultFromOptions()
    {
        TimeSpan? captured = null;
        transportMock
            .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback<RpcRequest, string, TimeSpan?, CancellationToken>((_, _, t, _) => captured = t)
            .ReturnsAsync(new RpcResponse { RequestId = "r", Success = true, Result = PackResult(0, typeof(int)) });

        var sut = CreateSut();
        var task = (Task<int>)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1, 2 }, null })!;
        await task;

        await Assert.That(captured).IsEqualTo(TimeSpan.FromSeconds(7));
    }

    [Test]
    public async Task BuildRequest_CallsSerializeFragment_OncePerArgument()
    {
        transportMock
            .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RpcResponse
            {
                RequestId = "r",
                Success = true,
                Result = PackResult(42, typeof(int))
            });

        var sut = CreateSut();
        var task = (Task<int>)sut.GetType()
            .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1, 2 }, null })!;
        await task;

        // AddAsync has 2 parameters → 2 SerializeFragment calls; response result → 1 DeserializeFragment call.
        await Assert.That(serializer.SerializeFragmentCalls).IsEqualTo(2);
        await Assert.That(serializer.DeserializeFragmentCalls).IsEqualTo(1);
    }
}
