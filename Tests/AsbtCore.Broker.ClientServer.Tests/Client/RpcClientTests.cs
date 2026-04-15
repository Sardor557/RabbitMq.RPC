using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.ClientServer.Tests.Fixtures;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AsbtCore.Broker.ClientServer.Tests.Client
{
    [TestClass]
    public class RpcClientTests
    {
        private Mock<IRpcTransport> transportMock = null!;
        private Mock<IRpcRouteResolver> routeMock = null!;
        private IRpcSerializer serializer = null!;
        private RpcOptions options = null!;

        [TestInitialize]
        public void Init()
        {
            transportMock = new Mock<IRpcTransport>();
            routeMock = new Mock<IRpcRouteResolver>();
            routeMock.Setup(x => x.Resolve(It.IsAny<Type>())).Returns("rpc.route");
            serializer = new JsonRpcSerializer();
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

        [TestMethod]
        public async Task Invoke_TaskOfT_ReturnsDeserializedResult()
        {
            transportMock
                .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), "rpc.route", It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RpcResponse
                {
                    RequestId = "r",
                    Success = true,
                    Result = serializer.PackResult(42, typeof(int))
                });

            var sut = CreateSut();
            var task = (Task<int>)sut.GetType()
                .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1, 2 }, null })!;

            var result = await task;

            Assert.AreEqual(42, result);
        }

        [TestMethod]
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

        [TestMethod]
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

            var ex = await Assert.ThrowsExceptionAsync<RpcRemoteException>(() => task);
            Assert.AreEqual("bad", ex.Message);
            Assert.AreEqual("E", ex.RemoteCode);
            Assert.AreEqual("T", ex.RemoteExceptionType);
            Assert.AreEqual("d", ex.RemoteDetails);
        }

        [TestMethod]
        public async Task Invoke_ArgumentCountMismatch_ThrowsInvalidOperation()
        {
            var sut = CreateSut();
            var task = (Task<int>)sut.GetType()
                .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1 }, null })!;

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => task);
        }

        [TestMethod]
        public async Task Invoke_MethodWithCancellationToken_ThrowsNotSupported()
        {
            var sut = CreateSut();
            var method = typeof(ICancellableService).GetMethod(nameof(ICancellableService.DoAsync))!;

            var task = (Task)sut.GetType()
                .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(sut, new object?[] { typeof(ICancellableService), method, new object[] { CancellationToken.None }, null })!;

            await Assert.ThrowsExceptionAsync<NotSupportedException>(() => task);
        }

        [TestMethod]
        public void Invoke_NonTaskReturn_ThrowsNotSupported()
        {
            var sut = CreateSut();
            var method = typeof(ISyncService).GetMethod(nameof(ISyncService.Add))!;

            var ex = Assert.ThrowsException<TargetInvocationException>(() =>
                sut.GetType()
                    .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(sut, new object?[] { typeof(ISyncService), method, new object[] { 1, 2 }, null }));

            Assert.IsInstanceOfType(ex.InnerException, typeof(NotSupportedException));
        }

        [TestMethod]
        public async Task Invoke_NoExplicitTimeout_UsesDefaultFromOptions()
        {
            TimeSpan? captured = null;
            transportMock
                .Setup(x => x.SendAsync(It.IsAny<RpcRequest>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .Callback<RpcRequest, string, TimeSpan?, CancellationToken>((_, _, t, _) => captured = t)
                .ReturnsAsync(new RpcResponse { RequestId = "r", Success = true, Result = serializer.PackResult(0, typeof(int)) });

            var sut = CreateSut();
            var task = (Task<int>)sut.GetType()
                .GetMethod("InvokeProxy", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(sut, new object?[] { typeof(ITestService), Method(nameof(ITestService.AddAsync)), new object[] { 1, 2 }, null })!;
            await task;

            Assert.AreEqual(TimeSpan.FromSeconds(7), captured);
        }
    }
}
