using MemoryPack;
using System;
using System.Collections.Generic;
using System.Text;

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
