using AsbtCore.Broker.Demo.Contracts;
using Contracts;

namespace Server.Services
{
    public sealed class UserService : IUserService
    {
        public Task<UserDto> GetByIdAsync(int id)
        {
            UserDto user = id switch
            {
                1 => new UserDto { Id = 1, Name = "Ali" },
                2 => new UserDto { Id = 2, Name = "Vali" },
                _ => null
            };

            return Task.FromResult(user);
        }

        public Task<int> SumAsync(int a, int b)
        {
            return Task.FromResult(a + b);
        }

        public Task PingAsync()
        {
            Console.WriteLine("Ping received on server.");
            return Task.CompletedTask;
        }

        public Task<int> SumAllAsync(RqModel rq)
        {
            return Task.FromResult(rq.a + rq.b + rq.c);
        }
    }
}
