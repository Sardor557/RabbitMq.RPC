using MemoryPack;

namespace AsbtCore.Broker.Demo.Contracts
{
    [MemoryPackable]
    public sealed partial class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
