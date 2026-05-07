using Contracts.Models;

namespace AsbtCore.Broker.Demo.Contracts
{

    public interface IUserService
    {
        Task<UserDto> GetByIdAsync(int id);
        Task<int> SumAsync(int a, int b);
        Task PingAsync();

        Task<List<UserDto>> GetManyAsync(int count);

    }
}
