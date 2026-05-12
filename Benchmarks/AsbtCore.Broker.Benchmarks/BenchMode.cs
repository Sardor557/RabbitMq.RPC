namespace AsbtCore.Broker.Benchmarks;

/// <summary>
/// BenchmarkDotNet parameter axis used by benches that compare a pre-v4.0
/// legacy code path (reflection / JsonElement-based) against the new
/// post-v4.0 path (compiled invokers / fragment serializer).
/// </summary>
public enum LegacyOrNew
{
    Legacy,
    New
}
