using System.Collections.Generic;
using Contracts.Models;

namespace AsbtCore.Broker.Demo.Contracts
{
    public static class TestData
    {
        public static readonly List<UserDto> Users = new()
        {
            new UserDto { Id = 1, Name = "Alice" },
            new UserDto { Id = 2, Name = "Bob" },
            new UserDto { Id = 3, Name = "Charlie" },
            new UserDto { Id = 4, Name = "Diana" },
            new UserDto { Id = 5, Name = "Eve" }
        };

        public static UserDto GetUser(int id) => Users.Find(u => u.Id == id);

        public static List<UserDto> GetMany(int count)
        {
            var ls = new List<UserDto>();

            for (int i = 0; i < count; i++)
            {
                var baseUser = Users[i % Users.Count];
                ls.Add(new UserDto
                {
                    Id = baseUser.Id + (i / Users.Count) * Users.Count,
                    Name = baseUser.Name + "_" + (i / Users.Count + 1)
                });
            }

            return ls;
        }
    }
}
