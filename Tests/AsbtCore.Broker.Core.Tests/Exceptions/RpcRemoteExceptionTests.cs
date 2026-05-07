using AsbtCore.Broker.Core.Exceptions;

namespace AsbtCore.Broker.Core.Tests.Exceptions;

public class RpcRemoteExceptionTests
{
    [Test]
    public async Task Ctor_AllFields_ArePopulated()
    {
        var ex = new RpcRemoteException("msg", "CODE", "System.Exception", "details");

        await Assert.That(ex.Message).IsEqualTo("msg");
        await Assert.That(ex.RemoteCode).IsEqualTo("CODE");
        await Assert.That(ex.RemoteExceptionType).IsEqualTo("System.Exception");
        await Assert.That(ex.RemoteDetails).IsEqualTo("details");
    }

    [Test]
    public async Task Ctor_OptionalArgsOmitted_DoesNotThrow()
    {
        var ex = new RpcRemoteException("msg");

        await Assert.That(ex.Message).IsEqualTo("msg");
        await Assert.That(ex.RemoteCode).IsNull();
        await Assert.That(ex.RemoteExceptionType).IsNull();
        await Assert.That(ex.RemoteDetails).IsNull();
    }
}
