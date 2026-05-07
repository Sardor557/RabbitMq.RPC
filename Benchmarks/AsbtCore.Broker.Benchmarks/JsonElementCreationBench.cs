using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

public enum LegacyOrNew { Legacy, New }

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class JsonElementCreationBench
{
    public sealed record Small(int Id, string Name);
    public sealed record Nested(int Id, Small Inner, string[] Tags);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private Small smallValue = null!;
    private Nested nestedValue = null!;
    private List<Small> listValue = null!;

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        smallValue = new Small(42, "x");
        nestedValue = new Nested(1, new Small(2, "n"), new[] { "a", "b", "c" });
        listValue = Enumerable.Range(0, 50).Select(i => new Small(i, $"n{i}")).ToList();
    }

    [Benchmark]
    public JsonElement Small_Element() => Run(smallValue, typeof(Small));

    [Benchmark]
    public JsonElement Nested_Element() => Run(nestedValue, typeof(Nested));

    [Benchmark]
    public JsonElement List_Element() => Run(listValue, typeof(List<Small>));

    private JsonElement Run(object value, Type type) => Mode switch
    {
        LegacyOrNew.Legacy => LegacyToElement(value, type),
        LegacyOrNew.New => JsonSerializer.SerializeToElement(value, type, Options),
        _ => throw new InvalidOperationException()
    };

    private static JsonElement LegacyToElement(object value, Type type)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type, Options);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
}
