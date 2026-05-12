using XPacketRpc;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests;

/// <summary>
/// DTOs and Touch sites used by the adapter test suite. The Touch sites are no-ops at
/// runtime; they exist so the XPacketRpc source generator (referenced as an Analyzer in
/// this csproj) emits codecs for these closed generic <c>T</c>s.
/// </summary>
public sealed record UserDto(int Id, string Name, Guid Token, DateTime Joined);

public sealed record OrderDto(Guid OrderId, decimal Total, UserDto Customer);

public interface ISampleService
{
    Task<UserDto> GetUserAsync(int id);
    Task<int> AddAsync(int a, int b);
    Task<string> GetNameAsync(Guid token);
    Task<OrderDto> GetOrderAsync(Guid orderId);
}

internal static class GeneratorTouchSites
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void TouchAll()
    {
        XPRpc.Touch<UserDto>();
        XPRpc.Touch<OrderDto>();
    }
}
