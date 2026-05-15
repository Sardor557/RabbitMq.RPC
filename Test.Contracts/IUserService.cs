using AsbtCore.Broker.Demo.Contracts;

namespace Contracts
{
   
    public interface IUserService
    {
        Task<UserDto> GetByIdAsync(int id);
        Task<int> SumAsync(int a, int b);
        Task<int> SumAllAsync(RqModel rq);
        Task PingAsync();
    }
}
