using System.Runtime.CompilerServices;
using XPacketRpc;

namespace AsbtCore.Broker.Benchmarks;

/// <summary>
/// Closed-generic <c>XPRpc.Touch&lt;T&gt;</c> call sites used by the XPacketRpc source
/// generator to emit codecs for every DTO consumed by an XPacket bench parameter axis.
/// The calls are no-ops at runtime; they exist so the generator can scan the assembly
/// for the concrete <c>T</c>s it must materialize codecs for.
/// </summary>
internal static class GeneratorTouchSites
{
    [ModuleInitializer]
    internal static void TouchAll()
    {
        // FragmentCreationBench
        XPRpc.Touch<FragmentCreationBench.Small>();
        XPRpc.Touch<FragmentCreationBench.Nested>();
        XPRpc.Touch<List<FragmentCreationBench.Small>>();
        XPRpc.Touch<string[]>();

        // SerializerComparisonBench
        XPRpc.Touch<SerializerComparisonBench.OrderLineDto>();
        XPRpc.Touch<SerializerComparisonBench.OrderDto>();
        XPRpc.Touch<SerializerComparisonBench.OrderLineDto[]>();
    }
}
