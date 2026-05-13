using MemoryPack;

namespace AsbtCore.Broker.Demo.Contracts
{
    [MemoryPackable]
    public sealed partial class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public interface IUserService
    {
        Task<UserDto> GetByIdAsync(int id);
        Task<int> SumAsync(int a, int b);
        Task PingAsync();
    }
}
