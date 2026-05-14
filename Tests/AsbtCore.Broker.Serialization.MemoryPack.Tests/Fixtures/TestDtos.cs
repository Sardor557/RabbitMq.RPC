namespace AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class SimplePocoDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CollectionsDto
{
    public List<int> Numbers { get; set; } = new();
    public Dictionary<string, int> Map { get; set; } = new();
    public int? OptionalCount { get; set; }
    public SampleEnum Mode { get; set; }
}

public enum SampleEnum
{
    First = 0,
    Second = 1,
    Third = 2,
}

public sealed record RecordDto(int Id, string Title);

public sealed class InitOnlyDto
{
    public int Id { get; init; }
    public string Tag { get; init; } = string.Empty;
}

public sealed class GraphA
{
    public int Value { get; set; }
    public GraphB? Child { get; set; }
}

public sealed class GraphB
{
    public string Label { get; set; } = string.Empty;
    public GraphA? Parent { get; set; }
}

public abstract class AnimalBase
{
    public string Name { get; set; } = string.Empty;
}

public sealed class Cat : AnimalBase
{
    public bool IsIndoor { get; set; }
}

public sealed class Dog : AnimalBase
{
    public int BarksPerMinute { get; set; }
}

public sealed class HolderDto
{
    public AnimalBase? Animal { get; set; }
}

public sealed class NoUsableCtorDto
{
    public int Id { get; }
    private NoUsableCtorDto(int id) { Id = id; }
}
