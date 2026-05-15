using MemoryPack;

namespace Contracts
{
    [MemoryPackable]
    public partial class RqModel
    {
        public int a { get; set; }
        public int b { get; set; }
        public int c { get; set; }
    }
}
