namespace AsbtCore.Broker.Core.Tests.Fixtures
{
    public interface ITestService
    {
        Task<int> AddAsync(int a, int b);
        Task NotifyAsync(string message);
        Task<UserDto> GetUserAsync(Guid id);
    }

    public sealed record UserDto(Guid Id, string Name);
}
