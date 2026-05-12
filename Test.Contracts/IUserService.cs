namespace AsbtCore.Broker.Demo.Contracts
{
    public sealed class UserDto
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
