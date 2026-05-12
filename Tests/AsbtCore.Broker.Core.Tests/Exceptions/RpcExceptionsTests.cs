namespace AsbtCore.Broker.Core.Tests.Exceptions;

using AsbtCore.Broker.Core.Exceptions;

public sealed class RpcExceptionsTests
{
    [Test]
    public async Task TransportReconnectedException_MessageContainsRequestId()
    {
        var ex = new TransportReconnectedException("req-42");

        await Assert.That(ex.RequestId).IsEqualTo("req-42");
        await Assert.That(ex.Message).Contains("req-42");
    }

    [Test]
    public async Task TransportReconnectedException_InheritsException()
    {
        var ex = new TransportReconnectedException("r");

        await Assert.That(ex).IsAssignableTo<Exception>();
    }

    [Test]
    public async Task RpcPublishFailedException_PropertiesSet()
    {
        var ex = new RpcPublishFailedException("req-1", "nack");

        await Assert.That(ex.RequestId).IsEqualTo("req-1");
        await Assert.That(ex.Reason).IsEqualTo("nack");
        await Assert.That(ex.Message).Contains("req-1");
        await Assert.That(ex.Message).Contains("nack");
    }

    [Test]
    public async Task RpcPublishFailedException_WithInner_SetsInnerException()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new RpcPublishFailedException("req-2", "returned", inner);

        await Assert.That(ex.InnerException).IsSameReferenceAs(inner);
    }

    [Test]
    public async Task RpcPublishFailedException_InheritsException()
    {
        var ex = new RpcPublishFailedException("r", "x");

        await Assert.That(ex).IsAssignableTo<Exception>();
    }
}
