namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// DTO roundtrips. The DTOs themselves are registered by the source generator (driven by
/// the <c>Touch&lt;T&gt;</c> sites in <c>TestDtos.cs</c>'s module initializer); the
/// adapter routes fragment writes/reads through the cached invoker.
/// </summary>
public sealed class DtoRoundtripTests
{
    [Test]
    public async Task UserDto_Roundtrip()
    {
        var sut = new XPacketRpcSerializer();
        var u = new UserDto(7, "Alice", Guid.NewGuid(), new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var bytes = sut.SerializeFragment(u, typeof(UserDto));
        var back = (UserDto?)sut.DeserializeFragment(bytes, typeof(UserDto));
        await Assert.That(back).IsEqualTo(u);
    }

    [Test]
    public async Task OrderDto_NestedDto_Roundtrip()
    {
        var sut = new XPacketRpcSerializer();
        var customer = new UserDto(42, "Bob", Guid.NewGuid(), DateTime.UtcNow);
        var order = new OrderDto(Guid.NewGuid(), 999.99m, customer);
        var bytes = sut.SerializeFragment(order, typeof(OrderDto));
        var back = (OrderDto?)sut.DeserializeFragment(bytes, typeof(OrderDto));
        await Assert.That(back).IsEqualTo(order);
    }
}
