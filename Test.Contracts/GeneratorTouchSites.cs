using System.Runtime.CompilerServices;
using XPacketRpc;

namespace AsbtCore.Broker.Demo.Contracts
{
    /// <summary>
    /// Call sites observed by the XPacketRpc source generator. The Touch calls are no-ops at
    /// runtime; they exist purely so the generator emits writer/reader codecs into a module
    /// initializer for every DTO that travels the wire. Add a new <c>XPRpc.Touch&lt;T&gt;()</c>
    /// line here whenever you introduce a new DTO in this project.
    /// </summary>
    internal static class GeneratorTouchSites
    {
#pragma warning disable CA2255 // ModuleInitializer is the documented entry point for source-generator codec emission.
        [ModuleInitializer]
        internal static void TouchAll()
        {
            XPRpc.Touch<UserDto>();
        }
#pragma warning restore CA2255
    }
}
