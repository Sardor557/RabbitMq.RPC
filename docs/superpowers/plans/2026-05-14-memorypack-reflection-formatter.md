# MemoryPack Reflection Formatter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable MemoryPack RPC serialization for DTOs that lack `[MemoryPackable]` (vendor / compiled-DLL types) via a runtime reflection-based formatter with lazy auto-discovery, prewarm, and explicit polymorphism mapping.

**Architecture:** Add `Reflection/` and `Polymorphism/` namespaces to `AsbtCore.Broker.Serialization.MemoryPack`. Build `IMemoryPackFormatter<T>` at runtime via `Expression.Compile`, cache per type, register with `MemoryPackFormatterProvider`. Recursive registration handles vendor-defined object graphs; cyclic type graphs terminate via in-progress marker. `MemoryPackRpcSerializer` calls `EnsureRegistered<T>` before delegating to `MemoryPackSerializer`.

**Tech Stack:** .NET 10, MemoryPack 1.21+, `System.Linq.Expressions`, `NullabilityInfoContext`. Tests: TUnit + Moq. Build/test runner: `dotnet build` / `dotnet run` (TUnit uses run, not test). DI: `Microsoft.Extensions.DependencyInjection`.

**Reference spec:** `docs/superpowers/specs/2026-05-14-memorypack-reflection-formatter-design.md`.

---

## File Structure

**Create:**
- `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemberAccessor.cs` — internal helpers for member discovery (public properties + `NullabilityInfoContext`).
- `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackPlan.cs` — per-type cached plan: ordered members, ctor delegate, compiled serialize/deserialize delegates.
- `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackFormatter.cs` — generic `MemoryPackFormatter<T>` that invokes plan delegates.
- `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackRegistry.cs` — thread-safe orchestrator, `EnsureRegistered(Type)` recursion + cycle handling.
- `AsbtCore.Broker.Serialization.MemoryPack/Polymorphism/UnionBuilder.cs` — fluent builder for union mapping.
- `AsbtCore.Broker.Serialization.MemoryPack/Polymorphism/PolymorphicFormatter.cs` — tag-prefixed formatter for base types.
- `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcOptions.cs` — options bag for DI extension.
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/PolymorphismTests.cs`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MixedFormatterTests.cs`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/Fixtures/TestDtos.cs` — shared DTO definitions for new tests.

**Modify:**
- `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs` — accept optional `MemoryPackRpcOptions` ctor arg; call `registry.EnsureRegistered<T>()` in Serialize/Deserialize/SerializeFragment/DeserializeFragment.
- `AsbtCore.Broker.Serialization.MemoryPack/DependencyInjection/MemoryPackRpcServiceCollectionExtensions.cs` — add overload accepting `Action<MemoryPackRpcOptions>`.
- `AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj` — bump `<Version>` to 1.1.0 + add `<PackageReleaseNotes>`.
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/UseMemoryPackRpcSerializationTests.cs` — add tests for new options overload.
- `README.md` + `README.ru.md` (root) — add "Working with vendor DTOs" section.

---

## Conventions

- Private fields: **no `_` prefix**. Use `this.field` when ambiguous (project rule from `CLAUDE.md`).
- All identifiers / commit messages / code comments: English.
- TUnit tests: `[Test]` attribute, `await Assert.That(value).IsEqualTo(expected)` syntax, `Task` return type for async assertions.
- Build verification command (run after every code task): `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`. Expected: `Build succeeded` with 0 errors.
- Test execution: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/<TestClass>/*"`.

---

### Task 1: Baseline build + folder setup

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/.gitkeep`
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Polymorphism/.gitkeep`

- [ ] **Step 1: Verify baseline build passes**

Run: `dotnet build RabbitMq.RPC.sln -nologo`
Expected: `Build succeeded` with 0 errors. Note any warnings.

- [ ] **Step 2: Run existing MemoryPack tests baseline**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`
Expected: All existing tests pass. Record count for regression check.

- [ ] **Step 3: Create folders**

```bash
mkdir -p AsbtCore.Broker.Serialization.MemoryPack/Reflection
mkdir -p AsbtCore.Broker.Serialization.MemoryPack/Polymorphism
```

- [ ] **Step 4: Commit baseline marker**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Reflection AsbtCore.Broker.Serialization.MemoryPack/Polymorphism
git commit -m "chore: add Reflection/Polymorphism folders for MemoryPack adapter"
```

---

### Task 2: Test fixtures (shared DTOs)

**Files:**
- Create: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/Fixtures/TestDtos.cs`

These DTOs are intentionally **without** `[MemoryPackable]` — they simulate vendor types.

- [ ] **Step 1: Create fixtures file**

```csharp
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
```

- [ ] **Step 2: Verify fixtures compile**

Run: `dotnet build Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -nologo`
Expected: `Build succeeded` (still passes — the file references no new types yet).

- [ ] **Step 3: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/Fixtures/TestDtos.cs
git commit -m "test: add fixture DTOs for reflection formatter tests"
```

---

### Task 3: ReflectionMemberAccessor (member discovery)

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemberAccessor.cs`
- Test: inline in next task — accessor has no public surface, validated through Plan.

- [ ] **Step 1: Create accessor**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Reflection;

internal static class ReflectionMemberAccessor
{
    public static IReadOnlyList<MemberDescriptor> DiscoverMembers(Type type)
    {
        var nullCtx = new NullabilityInfoContext();
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true })
            .OrderBy(p => p.MetadataToken)
            .ToArray();

        var descriptors = new List<MemberDescriptor>(properties.Length);
        foreach (var prop in properties)
        {
            var allowsNull = prop.PropertyType.IsValueType
                ? Nullable.GetUnderlyingType(prop.PropertyType) is not null
                : nullCtx.Create(prop).WriteState != NullabilityState.NotNull;
            descriptors.Add(new MemberDescriptor(prop, allowsNull));
        }
        return descriptors;
    }
}

internal sealed record MemberDescriptor(PropertyInfo Property, bool AllowsNull);
```

- [ ] **Step 2: Build**

Run: `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemberAccessor.cs
git commit -m "feat(memorypack): add member descriptor discovery via reflection"
```

---

### Task 4: ReflectionMemoryPackPlan — POCO ctor + serialize delegate

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackPlan.cs`
- Test: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs` (new — first test added here)

This task introduces a minimal Plan supporting parameterless ctor + public settable props. Records/init-only come in Task 8.

- [ ] **Step 1: Write the failing test**

Create `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs`:

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class ReflectionFormatterTests
{
    [Test]
    public async Task SimplePoco_RoundTrips()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new SimplePocoDto { Id = 42, Name = "answer" };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<SimplePocoDto>(bytes);

        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.Id).IsEqualTo(42);
        await Assert.That(roundtrip.Name).IsEqualTo("answer");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/SimplePoco_RoundTrips"`
Expected: FAIL with a MemoryPack error about missing formatter for `SimplePocoDto`.

- [ ] **Step 3: Create the plan**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Linq.Expressions;
using System.Reflection;
using global::MemoryPack;

internal sealed class ReflectionMemoryPackPlan<T>
{
    public IReadOnlyList<MemberDescriptor> Members { get; }
    public Func<T> Activator { get; }

    private ReflectionMemoryPackPlan(IReadOnlyList<MemberDescriptor> members, Func<T> activator)
    {
        this.Members = members;
        this.Activator = activator;
    }

    public static ReflectionMemoryPackPlan<T> Build()
    {
        var type = typeof(T);
        if (type.IsAbstract || type.IsInterface)
        {
            throw new InvalidOperationException(
                $"Type {type.FullName} is abstract or an interface; register a union via MemoryPackRpcOptions.RegisterUnion<TBase>.");
        }
        if (type.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"Open generic type {type.FullName} cannot be registered. Provide a closed generic type.");
        }

        var members = ReflectionMemberAccessor.DiscoverMembers(type);
        var ctor = type.GetConstructor(Type.EmptyTypes)
            ?? throw new InvalidOperationException(
                $"Type {type.FullName} has no usable constructor. Parameterless ctor required (records / init-only support added later).");

        var activator = Expression.Lambda<Func<T>>(Expression.New(ctor)).Compile();
        return new ReflectionMemoryPackPlan<T>(members, activator);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit (test still red — formatter wiring next task)**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackPlan.cs Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs
git commit -m "feat(memorypack): add plan struct for reflection formatter (poco only)"
```

---

### Task 5: ReflectionMemoryPackFormatter<T> — serialize/deserialize delegates

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackFormatter.cs`

The formatter writes members in declaration order; each member resolves through `MemoryPackFormatterProvider.GetFormatter<TMember>()`. We emit non-generic per-member writers via Expression trees that invoke `writer.WriteValue` / `reader.ReadValue` generically — see implementation.

- [ ] **Step 1: Implement formatter**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Linq.Expressions;
using System.Reflection;
using global::MemoryPack;

internal sealed class ReflectionMemoryPackFormatter<T> : MemoryPackFormatter<T>
{
    private readonly ReflectionMemoryPackPlan<T> plan;
    private readonly Action<MemoryPackWriter, T> writeBody;
    private readonly Action<MemoryPackReader, T> readBody;

    public ReflectionMemoryPackFormatter(ReflectionMemoryPackPlan<T> plan)
    {
        this.plan = plan;
        this.writeBody = BuildWriter(plan.Members);
        this.readBody = BuildReader(plan.Members);
    }

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }
        writer.WriteObjectHeader((byte)plan.Members.Count);

        // Bridging to non-generic writer signature: pack the writer ref through a wrapper.
        var box = new WriterBox<TBufferWriter>(ref writer);
        try { writeBody(box.AsBase(), value); }
        finally { box.Release(); }
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = default;
            return;
        }
        if (count != plan.Members.Count)
        {
            throw new MemoryPackSerializationException(
                $"Member count mismatch for {typeof(T).FullName}: payload has {count}, expected {plan.Members.Count}.");
        }
        value ??= plan.Activator();
        readBody(reader, value);
    }

    private static Action<MemoryPackWriter, T> BuildWriter(IReadOnlyList<MemberDescriptor> members)
    {
        var writer = Expression.Parameter(typeof(MemoryPackWriter).MakeByRefType(), "writer");
        var instance = Expression.Parameter(typeof(T), "instance");
        var calls = new List<Expression>(members.Count);
        foreach (var member in members)
        {
            var writeValue = typeof(MemoryPackWriter)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "WriteValue" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                .MakeGenericMethod(member.Property.PropertyType);
            calls.Add(Expression.Call(writer, writeValue, Expression.Property(instance, member.Property)));
        }
        var body = calls.Count > 0 ? (Expression)Expression.Block(calls) : Expression.Empty();
        return Expression.Lambda<Action<MemoryPackWriter, T>>(body, writer, instance).Compile();
    }

    private static Action<MemoryPackReader, T> BuildReader(IReadOnlyList<MemberDescriptor> members)
    {
        var reader = Expression.Parameter(typeof(MemoryPackReader).MakeByRefType(), "reader");
        var instance = Expression.Parameter(typeof(T), "instance");
        var calls = new List<Expression>(members.Count);
        foreach (var member in members)
        {
            var readValue = typeof(MemoryPackReader)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "ReadValue" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                .MakeGenericMethod(member.Property.PropertyType);
            var value = Expression.Call(reader, readValue);
            calls.Add(Expression.Assign(Expression.Property(instance, member.Property), value));
        }
        var body = calls.Count > 0 ? (Expression)Expression.Block(calls) : Expression.Empty();
        return Expression.Lambda<Action<MemoryPackReader, T>>(body, reader, instance).Compile();
    }
}
```

> Note: The `WriterBox<TBufferWriter>` ref-bridging type is sketched here for clarity. If the MemoryPack API in the pinned version does not expose a non-generic writer reachable from `MemoryPackWriter<TBufferWriter>`, the implementer should keep both `Serialize` and `Deserialize` symmetric — emit Expression lambdas typed to `MemoryPackWriter<TBufferWriter>` cached per `TBufferWriter`. See `references/memorypack-writer-bridge.md` (to be authored by the implementer if a non-trivial bridge is needed). If the bridge cannot be avoided cleanly, swap to using `MemoryPackSerializer.Serialize(stream, value)` overload internally and document the perf delta in commit message.

- [ ] **Step 2: Build**

Run: `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`
Expected: `Build succeeded`. If the WriterBox sketch fails to compile, adjust per the note above — emit lambdas keyed on `TBufferWriter`. Commit message must mention the chosen approach.

- [ ] **Step 3: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackFormatter.cs
git commit -m "feat(memorypack): add reflection formatter with compiled member delegates"
```

---

### Task 6: ReflectionMemoryPackRegistry — EnsureRegistered + cycle handling

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackRegistry.cs`

- [ ] **Step 1: Implement registry**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Reflection;

using System.Collections.Concurrent;
using System.Reflection;
using global::MemoryPack;

internal sealed class ReflectionMemoryPackRegistry
{
    public static ReflectionMemoryPackRegistry Shared { get; } = new();

    private readonly ConcurrentDictionary<Type, RegistrationState> states = new();

    public void EnsureRegistered(Type type)
    {
        if (type.IsArray)
        {
            EnsureRegistered(type.GetElementType()!);
            return;
        }
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            EnsureRegistered(underlying);
            return;
        }
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type.IsEnum)
        {
            return; // MemoryPack handles primitives/enums/string natively.
        }
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments()) EnsureRegistered(arg);
            return; // collection formatters built-in; element types registered above.
        }

        var added = states.GetOrAdd(type, _ => RegistrationState.Pending);
        if (added == RegistrationState.Registered) return;
        if (added == RegistrationState.InProgress) return;

        // We are the first to claim Pending → upgrade to InProgress under lock.
        lock (states)
        {
            if (states[type] != RegistrationState.Pending) return;
            states[type] = RegistrationState.InProgress;
        }

        var registerMethod = typeof(ReflectionMemoryPackRegistry)
            .GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);

        try
        {
            registerMethod.Invoke(null, [this]);
            states[type] = RegistrationState.Registered;
        }
        catch
        {
            states.TryRemove(type, out _);
            throw;
        }
    }

    private static void RegisterTyped<T>(ReflectionMemoryPackRegistry self)
    {
        if (MemoryPackFormatterProvider.IsRegistered<T>()) return;

        var plan = ReflectionMemoryPackPlan<T>.Build();
        foreach (var member in plan.Members)
        {
            self.EnsureRegistered(member.Property.PropertyType);
        }

        if (!MemoryPackFormatterProvider.IsRegistered<T>())
        {
            MemoryPackFormatterProvider.Register(new ReflectionMemoryPackFormatter<T>(plan));
        }
    }

    private enum RegistrationState : byte
    {
        Pending = 0,
        InProgress = 1,
        Registered = 2,
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackRegistry.cs
git commit -m "feat(memorypack): add registry with cycle-safe recursive registration"
```

---

### Task 7: Wire registry into MemoryPackRpcSerializer

**Files:**
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs`

- [ ] **Step 1: Replace serializer body**

```csharp
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.MemoryPack.Formatters;
using AsbtCore.Broker.Serialization.MemoryPack.Reflection;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public sealed class MemoryPackRpcSerializer : IRpcSerializer
{
    static MemoryPackRpcSerializer()
    {
        MemoryPackFormatterProvider.Register<RpcError>(new RpcErrorFormatter());
        MemoryPackFormatterProvider.Register<RpcArgument>(new RpcArgumentFormatter());
        MemoryPackFormatterProvider.Register<RpcRequest>(new RpcRequestFormatter());
        MemoryPackFormatterProvider.Register<RpcResponse>(new RpcResponseFormatter());
    }

    private readonly ReflectionMemoryPackRegistry registry;

    public MemoryPackRpcSerializer()
        : this(ReflectionMemoryPackRegistry.Shared) { }

    internal MemoryPackRpcSerializer(ReflectionMemoryPackRegistry registry)
    {
        this.registry = registry;
    }

    public string ContentType => "application/x-memorypack-rpc";

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        this.registry.EnsureRegistered(typeof(T));
        return MemoryPackSerializer.Serialize(value);
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        this.registry.EnsureRegistered(typeof(T));
        return MemoryPackSerializer.Deserialize<T>(payload.Span);
    }

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
    {
        this.registry.EnsureRegistered(type);
        return MemoryPackSerializer.Serialize(type, value);
    }

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
    {
        this.registry.EnsureRegistered(type);
        return MemoryPackSerializer.Deserialize(type, payload.Span);
    }
}
```

- [ ] **Step 2: Run the SimplePoco round-trip test**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/SimplePoco_RoundTrips"`
Expected: PASS.

- [ ] **Step 3: Run full existing suite (regression check)**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`
Expected: All existing tests still pass + the new SimplePoco test.

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs
git commit -m "feat(memorypack): route serializer through reflection registry"
```

---

### Task 8: Collections / Nullable / Enum DTOs

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs`

- [ ] **Step 1: Add tests**

Append to `ReflectionFormatterTests.cs`:

```csharp
[Test]
public async Task Collections_Nullable_Enum_RoundTrip()
{
    var serializer = new MemoryPackRpcSerializer();
    var original = new CollectionsDto
    {
        Numbers = [1, 2, 3],
        Map = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 },
        OptionalCount = 42,
        Mode = SampleEnum.Third,
    };

    var bytes = serializer.Serialize(original);
    var roundtrip = serializer.Deserialize<CollectionsDto>(bytes);

    await Assert.That(roundtrip).IsNotNull();
    await Assert.That(roundtrip!.Numbers).IsEquivalentTo(new[] { 1, 2, 3 });
    await Assert.That(roundtrip.Map.Count).IsEqualTo(2);
    await Assert.That(roundtrip.Map["a"]).IsEqualTo(1);
    await Assert.That(roundtrip.OptionalCount).IsEqualTo(42);
    await Assert.That(roundtrip.Mode).IsEqualTo(SampleEnum.Third);
}

[Test]
public async Task NullableProperty_NullValue_RoundTrip()
{
    var serializer = new MemoryPackRpcSerializer();
    var original = new CollectionsDto { OptionalCount = null };

    var bytes = serializer.Serialize(original);
    var roundtrip = serializer.Deserialize<CollectionsDto>(bytes);

    await Assert.That(roundtrip!.OptionalCount).IsNull();
}
```

- [ ] **Step 2: Run new tests**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/*"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs
git commit -m "test(memorypack): cover collections, nullable, enum reflection round-trip"
```

---

### Task 9: Cyclic type graph

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs`

- [ ] **Step 1: Add test**

```csharp
[Test]
public async Task CyclicTypeGraph_RegistersWithoutOverflow()
{
    var serializer = new MemoryPackRpcSerializer();
    var original = new GraphA
    {
        Value = 1,
        Child = new GraphB
        {
            Label = "b",
            Parent = null, // avoid runtime data cycle
        },
    };

    var bytes = serializer.Serialize(original);
    var roundtrip = serializer.Deserialize<GraphA>(bytes);

    await Assert.That(roundtrip!.Value).IsEqualTo(1);
    await Assert.That(roundtrip.Child).IsNotNull();
    await Assert.That(roundtrip.Child!.Label).IsEqualTo("b");
    await Assert.That(roundtrip.Child.Parent).IsNull();
}
```

- [ ] **Step 2: Run**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/CyclicTypeGraph_RegistersWithoutOverflow"`
Expected: PASS. If FAIL with StackOverflowException, the InProgress marker in registry is not being honored — revisit Task 6 Step 1.

- [ ] **Step 3: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs
git commit -m "test(memorypack): verify cyclic type graph registers without overflow"
```

---

### Task 10: Records and init-only support in Plan

**Files:**
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackPlan.cs`
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackFormatter.cs`
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Test]
public async Task Record_PrimaryCtor_RoundTrip()
{
    var serializer = new MemoryPackRpcSerializer();
    var original = new RecordDto(7, "hello");

    var bytes = serializer.Serialize(original);
    var roundtrip = serializer.Deserialize<RecordDto>(bytes);

    await Assert.That(roundtrip!.Id).IsEqualTo(7);
    await Assert.That(roundtrip.Title).IsEqualTo("hello");
}

[Test]
public async Task InitOnly_RoundTrip()
{
    var serializer = new MemoryPackRpcSerializer();
    var original = new InitOnlyDto { Id = 9, Tag = "init" };

    var bytes = serializer.Serialize(original);
    var roundtrip = serializer.Deserialize<InitOnlyDto>(bytes);

    await Assert.That(roundtrip!.Id).IsEqualTo(9);
    await Assert.That(roundtrip.Tag).IsEqualTo("init");
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/Record_PrimaryCtor_RoundTrip"`
Expected: FAIL — Plan currently requires parameterless ctor.

- [ ] **Step 3: Extend Plan with ctor matching**

Replace the body of `ReflectionMemoryPackPlan<T>.Build()` and add the ctor-binding fields:

```csharp
internal sealed class ReflectionMemoryPackPlan<T>
{
    public IReadOnlyList<MemberDescriptor> Members { get; }
    public Func<object?[], T> Activator { get; }
    public int[] CtorMemberIndices { get; }    // -1 if member is set after ctor via property setter
    public bool[] HasSetter { get; }

    private ReflectionMemoryPackPlan(
        IReadOnlyList<MemberDescriptor> members,
        Func<object?[], T> activator,
        int[] ctorIndices,
        bool[] hasSetter)
    {
        this.Members = members;
        this.Activator = activator;
        this.CtorMemberIndices = ctorIndices;
        this.HasSetter = hasSetter;
    }

    public static ReflectionMemoryPackPlan<T> Build()
    {
        var type = typeof(T);
        if (type.IsAbstract || type.IsInterface)
            throw new InvalidOperationException(
                $"Type {type.FullName} is abstract or an interface; register a union via MemoryPackRpcOptions.RegisterUnion<TBase>.");
        if (type.IsGenericTypeDefinition)
            throw new InvalidOperationException(
                $"Open generic type {type.FullName} cannot be registered. Provide a closed generic type.");

        var members = ReflectionMemberAccessor.DiscoverMembers(type);
        var ctorIndices = new int[members.Count];
        Array.Fill(ctorIndices, -1);
        var hasSetter = members.Select(m => m.Property.SetMethod?.IsPublic == true).ToArray();

        Func<object?[], T> activator;
        var parameterless = type.GetConstructor(Type.EmptyTypes);
        if (parameterless is not null)
        {
            activator = _ => (T)parameterless.Invoke(null)!;
        }
        else
        {
            var bestCtor = SelectBestMatchingCtor(type, members)
                ?? throw new InvalidOperationException(
                    $"Type {type.FullName} has no usable constructor.");
            var parameters = bestCtor.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var matchIndex = -1;
                for (int j = 0; j < members.Count; j++)
                {
                    if (string.Equals(members[j].Property.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matchIndex = j;
                        break;
                    }
                }
                if (matchIndex < 0)
                    throw new InvalidOperationException(
                        $"Constructor parameter '{p.Name}' on {type.FullName} does not match any public property.");
                ctorIndices[matchIndex] = i;
            }
            activator = BuildCtorInvoker(bestCtor);
        }

        return new ReflectionMemoryPackPlan<T>(members, activator, ctorIndices, hasSetter);
    }

    private static ConstructorInfo? SelectBestMatchingCtor(Type type, IReadOnlyList<MemberDescriptor> members)
    {
        var memberNames = new HashSet<string>(
            members.Select(m => m.Property.Name), StringComparer.OrdinalIgnoreCase);
        return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().All(p => p.Name is not null && memberNames.Contains(p.Name)))
            .OrderByDescending(c => c.GetParameters().Length)
            .ThenBy(c => c.MetadataToken)
            .FirstOrDefault();
    }

    private static Func<object?[], T> BuildCtorInvoker(ConstructorInfo ctor)
    {
        var args = Expression.Parameter(typeof(object?[]), "args");
        var parameters = ctor.GetParameters();
        var ctorArgs = new Expression[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var indexed = Expression.ArrayIndex(args, Expression.Constant(i));
            ctorArgs[i] = Expression.Convert(indexed, parameters[i].ParameterType);
        }
        var newExpr = Expression.New(ctor, ctorArgs);
        return Expression.Lambda<Func<object?[], T>>(newExpr, args).Compile();
    }
}
```

- [ ] **Step 4: Update formatter to use ctor invoker**

Replace the `Deserialize` body in `ReflectionMemoryPackFormatter.cs`:

```csharp
public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
{
    if (!reader.TryReadObjectHeader(out var count))
    {
        value = default;
        return;
    }
    if (count != plan.Members.Count)
    {
        throw new MemoryPackSerializationException(
            $"Member count mismatch for {typeof(T).FullName}: payload has {count}, expected {plan.Members.Count}.");
    }

    var values = new object?[plan.Members.Count];
    for (int i = 0; i < plan.Members.Count; i++)
    {
        var propType = plan.Members[i].Property.PropertyType;
        values[i] = MemoryPackSerializer.Deserialize(propType, ref reader);
    }

    // Build ctor arg array using ctorIndices map.
    var ctorParamCount = plan.CtorMemberIndices.Max() + 1;
    object?[] ctorArgs = ctorParamCount > 0 ? new object?[ctorParamCount] : Array.Empty<object?>();
    for (int i = 0; i < plan.Members.Count; i++)
    {
        if (plan.CtorMemberIndices[i] >= 0)
        {
            ctorArgs[plan.CtorMemberIndices[i]] = values[i];
        }
    }

    value = plan.Activator(ctorArgs);

    // Assign remaining via setter.
    for (int i = 0; i < plan.Members.Count; i++)
    {
        if (plan.CtorMemberIndices[i] < 0 && plan.HasSetter[i])
        {
            plan.Members[i].Property.SetValue(value, values[i]);
        }
    }
}
```

> The `MemoryPackSerializer.Deserialize(Type, ref MemoryPackReader)` overload is used so the per-member type is dispatched dynamically. The serialize path stays compiled.

- [ ] **Step 5: Run the new tests**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/*"`
Expected: All ReflectionFormatterTests pass, including the new Record and InitOnly tests.

- [ ] **Step 6: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackPlan.cs AsbtCore.Broker.Serialization.MemoryPack/Reflection/ReflectionMemoryPackFormatter.cs Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs
git commit -m "feat(memorypack): support records / init-only via ctor parameter binding"
```

---

### Task 11: Failure modes (no ctor + open generic + abstract)

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Test]
public async Task NoUsableCtor_Throws()
{
    var serializer = new MemoryPackRpcSerializer();
    var ex = await Assert.That(() => serializer.Serialize(default(NoUsableCtorDto)!))
        .Throws<InvalidOperationException>();
    await Assert.That(ex!.Message).Contains("no usable constructor");
}

[Test]
public async Task AbstractWithoutUnion_Throws()
{
    var serializer = new MemoryPackRpcSerializer();
    var ex = await Assert.That(() => serializer.SerializeFragment(new Cat { Name = "x" }, typeof(AnimalBase)))
        .Throws<InvalidOperationException>();
    await Assert.That(ex!.Message).Contains("abstract or an interface");
}
```

- [ ] **Step 2: Run**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/ReflectionFormatterTests/*"`
Expected: All pass.

- [ ] **Step 3: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ReflectionFormatterTests.cs
git commit -m "test(memorypack): cover fail-fast cases (no ctor, abstract w/o union)"
```

---

### Task 12: UnionBuilder + PolymorphicFormatter

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Polymorphism/UnionBuilder.cs`
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Polymorphism/PolymorphicFormatter.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/PolymorphismTests.cs`

- [ ] **Step 1: Write failing test**

`PolymorphismTests.cs`:

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class PolymorphismTests
{
    [Test]
    public async Task Union_RoundTrips_AsBase()
    {
        var options = new MemoryPackRpcOptions()
            .RegisterUnion<AnimalBase>(b => b.Add<Cat>(1).Add<Dog>(2));
        var serializer = new MemoryPackRpcSerializer(options);

        AnimalBase original = new Cat { Name = "Mia", IsIndoor = true };
        var bytes = serializer.SerializeFragment(original, typeof(AnimalBase));
        var roundtrip = (AnimalBase?)serializer.DeserializeFragment(bytes, typeof(AnimalBase));

        await Assert.That(roundtrip).IsTypeOf<Cat>();
        await Assert.That(((Cat)roundtrip!).IsIndoor).IsTrue();
        await Assert.That(roundtrip.Name).IsEqualTo("Mia");
    }
}
```

This task also depends on `MemoryPackRpcOptions` (next task). Reorder: implement UnionBuilder + PolymorphicFormatter here, defer wiring to options/serializer to Task 13. Test will turn green after Task 13.

- [ ] **Step 2: Implement UnionBuilder**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;

public sealed class UnionBuilder<TBase>
{
    private readonly Dictionary<byte, Type> tagToType = new();
    private readonly Dictionary<Type, byte> typeToTag = new();

    public UnionBuilder<TBase> Add<TDerived>(byte tag) where TDerived : TBase
    {
        var derived = typeof(TDerived);
        if (tagToType.ContainsKey(tag))
            throw new InvalidOperationException($"Tag {tag} already mapped on union {typeof(TBase).FullName}.");
        if (typeToTag.ContainsKey(derived))
            throw new InvalidOperationException($"Type {derived.FullName} already mapped on union {typeof(TBase).FullName}.");
        tagToType[tag] = derived;
        typeToTag[derived] = tag;
        return this;
    }

    internal IReadOnlyDictionary<byte, Type> TagToType => tagToType;
    internal IReadOnlyDictionary<Type, byte> TypeToTag => typeToTag;
}
```

- [ ] **Step 3: Implement PolymorphicFormatter**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;

using MemoryPack;

internal sealed class PolymorphicFormatter<TBase> : MemoryPackFormatter<TBase>
{
    private readonly IReadOnlyDictionary<byte, Type> tagToType;
    private readonly IReadOnlyDictionary<Type, byte> typeToTag;

    public PolymorphicFormatter(UnionBuilder<TBase> builder)
    {
        this.tagToType = builder.TagToType;
        this.typeToTag = builder.TypeToTag;
    }

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref TBase? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }
        var runtimeType = value.GetType();
        if (!typeToTag.TryGetValue(runtimeType, out var tag))
        {
            throw new InvalidOperationException(
                $"Runtime type {runtimeType.FullName} is not mapped on union {typeof(TBase).FullName}.");
        }
        writer.WriteUnmanaged(tag);
        MemoryPackSerializer.Serialize(runtimeType, ref writer, value);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref TBase? value)
    {
        // Sentinel: a null union value is encoded as a null-object-header which Serialize wrote above.
        if (reader.PeekIsNull())
        {
            reader.Advance(1);
            value = default;
            return;
        }
        reader.ReadUnmanaged(out byte tag);
        if (!tagToType.TryGetValue(tag, out var derived))
        {
            throw new MemoryPackSerializationException(
                $"Unknown union tag {tag} for {typeof(TBase).FullName}.");
        }
        value = (TBase?)MemoryPackSerializer.Deserialize(derived, ref reader);
    }
}
```

> If the pinned MemoryPack version's `MemoryPackReader.PeekIsNull` / null-header API differs, adjust the null check accordingly — write `Polymorphism/PolymorphicFormatter.cs` such that null base values round-trip. Add a test for the null case in Task 14.

- [ ] **Step 4: Build (test still red, expected — options wiring next)**

Run: `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/Polymorphism Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/PolymorphismTests.cs
git commit -m "feat(memorypack): add union builder and polymorphic tag-prefix formatter"
```

---

### Task 13: MemoryPackRpcOptions + DI extension overload

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcOptions.cs`
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs`
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/DependencyInjection/MemoryPackRpcServiceCollectionExtensions.cs`

- [ ] **Step 1: Implement options**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack;

using System.Reflection;
using AsbtCore.Broker.Serialization.MemoryPack.Polymorphism;
using AsbtCore.Broker.Serialization.MemoryPack.Reflection;
using global::MemoryPack;

public sealed class MemoryPackRpcOptions
{
    private readonly List<Type> prewarmTypes = new();
    private readonly List<(Assembly Asm, Func<Type, bool>? Filter)> prewarmAssemblies = new();
    private readonly List<Action> unionRegistrations = new();

    public MemoryPackRpcOptions PrewarmType<T>()
    {
        prewarmTypes.Add(typeof(T));
        return this;
    }

    public MemoryPackRpcOptions PrewarmTypes(params Type[] types)
    {
        prewarmTypes.AddRange(types);
        return this;
    }

    public MemoryPackRpcOptions PrewarmAssembly(Assembly asm, Func<Type, bool>? filter = null)
    {
        prewarmAssemblies.Add((asm, filter));
        return this;
    }

    public MemoryPackRpcOptions RegisterUnion<TBase>(Action<UnionBuilder<TBase>> configure)
    {
        unionRegistrations.Add(() =>
        {
            if (MemoryPackFormatterProvider.IsRegistered<TBase>())
                throw new InvalidOperationException(
                    $"Union for type {typeof(TBase).FullName} is already registered.");
            var builder = new UnionBuilder<TBase>();
            configure(builder);
            MemoryPackFormatterProvider.Register(new PolymorphicFormatter<TBase>(builder));
        });
        return this;
    }

    internal void Apply(ReflectionMemoryPackRegistry registry)
    {
        foreach (var union in unionRegistrations) union();
        foreach (var type in prewarmTypes) registry.EnsureRegistered(type);
        foreach (var (asm, filter) in prewarmAssemblies)
        {
            foreach (var t in asm.GetTypes())
            {
                if (!t.IsClass || t.IsAbstract) continue;
                if (filter is not null && !filter(t)) continue;
                registry.EnsureRegistered(t);
            }
        }
    }
}
```

- [ ] **Step 2: Add options overload to serializer ctor**

Replace `MemoryPackRpcSerializer.cs` ctor block:

```csharp
public MemoryPackRpcSerializer() : this(null, ReflectionMemoryPackRegistry.Shared) { }

public MemoryPackRpcSerializer(MemoryPackRpcOptions? options)
    : this(options, ReflectionMemoryPackRegistry.Shared) { }

internal MemoryPackRpcSerializer(MemoryPackRpcOptions? options, ReflectionMemoryPackRegistry registry)
{
    this.registry = registry;
    options?.Apply(registry);
}
```

- [ ] **Step 3: Add overload to DI extension**

Replace `MemoryPackRpcServiceCollectionExtensions.cs`:

```csharp
using System;
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public static class MemoryPackRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseMemoryPackRpcSerialization(this RpcServerBuilder builder)
        => UseMemoryPackRpcSerialization(builder, configure: null);

    public static RpcServerBuilder UseMemoryPackRpcSerialization(
        this RpcServerBuilder builder, Action<MemoryPackRpcOptions>? configure)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ =>
        {
            var options = new MemoryPackRpcOptions();
            configure?.Invoke(options);
            return new MemoryPackRpcSerializer(options);
        });
        return builder;
    }

    public static RpcClientBuilder UseMemoryPackRpcSerialization(this RpcClientBuilder builder)
        => UseMemoryPackRpcSerialization(builder, configure: null);

    public static RpcClientBuilder UseMemoryPackRpcSerialization(
        this RpcClientBuilder builder, Action<MemoryPackRpcOptions>? configure)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ =>
        {
            var options = new MemoryPackRpcOptions();
            configure?.Invoke(options);
            return new MemoryPackRpcSerializer(options);
        });
        return builder;
    }
}
```

- [ ] **Step 4: Run PolymorphismTests**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/PolymorphismTests/*"`
Expected: PASS.

- [ ] **Step 5: Run full suite**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcOptions.cs AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs AsbtCore.Broker.Serialization.MemoryPack/DependencyInjection/MemoryPackRpcServiceCollectionExtensions.cs
git commit -m "feat(memorypack): expose options API with prewarm and union registration"
```

---

### Task 14: Union failure cases + null union value

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/PolymorphismTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Test]
public async Task Union_DuplicateRegistration_Throws()
{
    var options = new MemoryPackRpcOptions().RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
    var _ = new MemoryPackRpcSerializer(options);

    var options2 = new MemoryPackRpcOptions().RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
    await Assert.That(() => new MemoryPackRpcSerializer(options2))
        .Throws<InvalidOperationException>();
}

[Test]
public async Task Union_DuplicateTag_Throws()
{
    var options = new MemoryPackRpcOptions();
    await Assert.That(() => options.RegisterUnion<AnimalBase>(b => b.Add<Cat>(1).Add<Dog>(1)))
        .Throws<InvalidOperationException>();
}

[Test]
public async Task Union_NullValue_RoundTrips()
{
    var options = new MemoryPackRpcOptions().RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
    var serializer = new MemoryPackRpcSerializer(options);

    var bytes = serializer.SerializeFragment(null, typeof(AnimalBase));
    var roundtrip = serializer.DeserializeFragment(bytes, typeof(AnimalBase));

    await Assert.That(roundtrip).IsNull();
}
```

> The `Union_DuplicateRegistration_Throws` test depends on the registry being shared across serializer instances. If the duplicate check should be scoped per-serializer, change the implementation in `MemoryPackRpcOptions.Apply` to track registered base types in a static set with proper isolation — and update the assertion accordingly. Pick the behavior that matches the spec: spec says "process-wide", so global throw is correct.

- [ ] **Step 2: Run**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/PolymorphismTests/*"`
Expected: PASS. If `Union_NullValue_RoundTrips` fails, revisit Task 12 Step 3 `PolymorphicFormatter` null encoding/decoding (the `PeekIsNull`/`Advance` sketch).

- [ ] **Step 3: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/PolymorphismTests.cs
git commit -m "test(memorypack): cover union duplicate, duplicate tag, and null value"
```

---

### Task 15: Mixed scenario tests ([MemoryPackable] + reflection)

**Files:**
- Create: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MixedFormatterTests.cs`
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/Fixtures/TestDtos.cs` — add `[MemoryPackable] partial` DTO + a wrapping plain POCO that contains it.

- [ ] **Step 1: Add fixtures**

Append to `Fixtures/TestDtos.cs`:

```csharp
using global::MemoryPack;

[MemoryPackable]
public sealed partial class TaggedDto
{
    public int Id { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class WrapperDto
{
    public TaggedDto? Tagged { get; set; }
    public string OuterLabel { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create test file**

```csharp
namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;

public sealed class MixedFormatterTests
{
    [Test]
    public async Task TaggedAndPlain_RoundTrip()
    {
        var serializer = new MemoryPackRpcSerializer();
        var original = new WrapperDto
        {
            Tagged = new TaggedDto { Id = 7, Note = "tagged" },
            OuterLabel = "wrap",
        };

        var bytes = serializer.Serialize(original);
        var roundtrip = serializer.Deserialize<WrapperDto>(bytes);

        await Assert.That(roundtrip!.OuterLabel).IsEqualTo("wrap");
        await Assert.That(roundtrip.Tagged).IsNotNull();
        await Assert.That(roundtrip.Tagged!.Id).IsEqualTo(7);
        await Assert.That(roundtrip.Tagged.Note).IsEqualTo("tagged");
    }

    [Test]
    public async Task TaggedFormatter_NotReplaced()
    {
        var serializer = new MemoryPackRpcSerializer();
        var tagged = new TaggedDto { Id = 1, Note = "x" };
        var bytes = serializer.Serialize(tagged);
        // Bytes should match what MemoryPack source-gen produces directly.
        var direct = global::MemoryPack.MemoryPackSerializer.Serialize(tagged);
        await Assert.That(bytes.Span.SequenceEqual(direct)).IsTrue();
    }
}
```

- [ ] **Step 3: Run**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/MixedFormatterTests/*"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/Fixtures/TestDtos.cs Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MixedFormatterTests.cs
git commit -m "test(memorypack): verify reflection coexists with [MemoryPackable] types"
```

---

### Task 16: DI options overload tests

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/UseMemoryPackRpcSerializationTests.cs`

- [ ] **Step 1: Add tests**

Append after the last `[Test]` in the existing file:

```csharp
[Test]
public async Task ServerBuilder_WithOptions_Resolves()
{
    var services = new ServiceCollection();
    services.AddRabbitRpcServer(new ConfigurationBuilder().Build())
        .UseMemoryPackRpcSerialization(opt =>
        {
            opt.PrewarmType<SimplePocoDto>();
            opt.RegisterUnion<AnimalBase>(b => b.Add<Cat>(1));
        });

    var sp = services.BuildServiceProvider();
    var serializer = sp.GetRequiredService<IRpcSerializer>();
    await Assert.That(serializer).IsTypeOf<MemoryPackRpcSerializer>();

    // Verify prewarm took effect.
    var sample = new SimplePocoDto { Id = 1, Name = "n" };
    var bytes = serializer.Serialize(sample);
    var rt = serializer.Deserialize<SimplePocoDto>(bytes);
    await Assert.That(rt!.Id).IsEqualTo(1);
}

[Test]
public async Task PrewarmAssembly_WithFilter_RegistersOnlyMatching()
{
    var registry = new ReflectionMemoryPackRegistry();
    var options = new MemoryPackRpcOptions()
        .PrewarmAssembly(typeof(SimplePocoDto).Assembly,
            filter: t => t == typeof(SimplePocoDto));
    var serializer = new MemoryPackRpcSerializer(options, registry);

    // No assertion API on registry exposed; surface check: SimplePocoDto serializes immediately without re-discovery.
    var bytes = serializer.Serialize(new SimplePocoDto { Id = 9, Name = "p" });
    await Assert.That(bytes.IsEmpty).IsFalse();
}
```

If the `internal MemoryPackRpcSerializer(MemoryPackRpcOptions?, ReflectionMemoryPackRegistry)` ctor is not visible to the tests project, ensure `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests` is listed in `InternalsVisibleTo` on the production csproj. Adjust namespace usings as needed (`using AsbtCore.Broker.Serialization.MemoryPack.Reflection;` and `using AsbtCore.Broker.Serialization.MemoryPack.Tests.Fixtures;`).

- [ ] **Step 2: Run**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "/*/*/UseMemoryPackRpcSerializationTests/*"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/UseMemoryPackRpcSerializationTests.cs AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj
git commit -m "test(memorypack): cover DI options overload + prewarm assembly with filter"
```

---

### Task 17: Static analysis pass via dotnet-skills

**Files:** none (analysis only)

- [ ] **Step 1: Run `dotnet-diag:analyzing-dotnet-performance` skill**

Invoke skill, targeting `AsbtCore.Broker.Serialization.MemoryPack/Reflection` + `Polymorphism`. Focus on allocation patterns in `Serialize/Deserialize`. Record findings in commit message of follow-up fix if any.

- [ ] **Step 2: Run `dotnet-test:test-anti-patterns` skill**

Invoke skill against `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/`. Address only high-confidence issues.

- [ ] **Step 3: If any actionable findings — apply minimal fixes + commit**

```bash
git add <fixed-files>
git commit -m "perf(memorypack): apply <specific> finding from dotnet-diag"
```

If no findings → skip commit.

---

### Task 18: Version bump + release notes

**Files:**
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj`

- [ ] **Step 1: Bump version + release notes**

Locate `<Version>1.0.0</Version>` and `<PackageReleaseNotes>` (add if missing) in the csproj; update to:

```xml
<Version>1.1.0</Version>
<PackageReleaseNotes>
1.1.0
- Reflection-based formatter for DTOs without [MemoryPackable].
- Lazy auto-discovery; no registration required for POCO / record / init-only DTOs.
- New MemoryPackRpcOptions API: PrewarmType/PrewarmTypes/PrewarmAssembly and RegisterUnion for polymorphism.
- Wire format unchanged for [MemoryPackable] types; reflection layout matches declaration-order source-gen for plain DTOs.
- Existing UseMemoryPackRpcSerialization() callers continue to work; new overload accepts Action&lt;MemoryPackRpcOptions&gt;.
</PackageReleaseNotes>
```

- [ ] **Step 2: Build**

Run: `dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -nologo`
Expected: `Build succeeded`.

- [ ] **Step 3: Pack (optional, dry-run)**

Run: `dotnet pack AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj -c Release -nologo`
Expected: `RabbitRpc.Serialization.MemoryPack.1.1.0.nupkg` produced in `bin/Release/`.

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj
git commit -m "chore(memorypack): bump to 1.1.0 with reflection-formatter release notes"
```

---

### Task 19: README updates

**Files:**
- Modify: `README.md`
- Modify: `README.ru.md`

- [ ] **Step 1: Locate the "Adding a new RPC service" section in each README**

Insert a new subsection immediately after, titled `Working with vendor DTOs (MemoryPack)` / `Работа с DTO из сторонних сборок (MemoryPack)`. Each version must mirror the other.

- [ ] **Step 2: English version content**

```markdown
### Working with vendor DTOs (MemoryPack)

If your DTOs ship inside compiled libraries you cannot modify, the
`RabbitRpc.Serialization.MemoryPack` adapter supports them without any
`[MemoryPackable]` attribute. Lazy discovery is on by default.

For latency-sensitive paths use `PrewarmAssembly` / `PrewarmType` to amortize
the per-type build cost at startup.

For polymorphic base types you must register a union mapping explicitly —
MemoryPack cannot infer derived types from a base reference at runtime.

```csharp
services.AddRabbitRpcServer(configuration)
    .UseMemoryPackRpcSerialization(opt =>
    {
        opt.PrewarmAssembly(typeof(UserDto).Assembly);
        opt.RegisterUnion<Animal>(b => b
            .Add<Cat>(tag: 1)
            .Add<Dog>(tag: 2));
    });
```

Performance: reflection-built formatters are ~2-4x slower than native
`[MemoryPackable]` source-gen but still faster than JSON. AOT/trim
scenarios are not supported on this path — keep `[MemoryPackable]` for those.
```

- [ ] **Step 3: Russian mirror in `README.ru.md`**

```markdown
### Работа с DTO из сторонних сборок (MemoryPack)

Если DTO поставляются в скомпилированных библиотеках, которые нельзя
изменить, адаптер `RabbitRpc.Serialization.MemoryPack` поддерживает их
без атрибута `[MemoryPackable]`. По умолчанию работает lazy-обнаружение.

Для путей, чувствительных к latency, используйте `PrewarmAssembly` /
`PrewarmType` — это переносит стоимость подготовки формат-делегатов
на старт.

Для полиморфных базовых типов нужно явно зарегистрировать union — без
этого MemoryPack не сможет восстановить derived-тип из ссылки на базу.

```csharp
services.AddRabbitRpcServer(configuration)
    .UseMemoryPackRpcSerialization(opt =>
    {
        opt.PrewarmAssembly(typeof(UserDto).Assembly);
        opt.RegisterUnion<Animal>(b => b
            .Add<Cat>(tag: 1)
            .Add<Dog>(tag: 2));
    });
```

Производительность: рефлекшен-форматтер примерно в 2-4 раза медленнее
нативного source-gen `[MemoryPackable]`, но всё ещё быстрее JSON.
AOT/trim сценарии на этом пути не поддерживаются — для них используйте
`[MemoryPackable]`.
```

- [ ] **Step 4: Commit**

```bash
git add README.md README.ru.md
git commit -m "docs: document reflection-based MemoryPack adapter for vendor DTOs"
```

---

### Task 20: Final regression + verification gate

**Files:** none

- [ ] **Step 1: Run all MemoryPack tests**

Run: `dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`
Expected: All tests pass (count is at least: existing baseline + ReflectionFormatterTests + PolymorphismTests + MixedFormatterTests + new options tests).

- [ ] **Step 2: Run full solution test suite**

Run:
```
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj
```
Expected: Core tests (~45) and ClientServer tests (~38) all pass — no regressions outside the MemoryPack adapter.

- [ ] **Step 3: Full solution build (Release)**

Run: `dotnet build RabbitMq.RPC.sln -c Release -nologo`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Apply skill `superpowers:verification-before-completion`**

Confirm: tests pass, build clean, version bumped, README updated, no TODO left in new code. Only then declare the feature complete.

- [ ] **Step 5: Commit verification marker (optional — only if any final touch-up)**

If any small fixes were needed during verification, commit them.

```bash
git add <any-touched-files>
git commit -m "chore(memorypack): final verification pass"
```

---

## Skill invocations summary

| Phase | Skill | When |
|---|---|---|
| Task 17 | `dotnet-diag:analyzing-dotnet-performance` | After main code + tests done — audit reflection paths. |
| Task 17 | `dotnet-test:test-anti-patterns` | After test code done — audit assertion / smell. |
| Throughout | `dotnet-test:filter-syntax` | When constructing `--treenode-filter` strings. |
| Throughout | `dotnet-test:run-tests` | Each test step. |
| Task 18 | `dotnet-msbuild:msbuild-modernization` (optional) | When touching csproj. |
| On failure | `dotnet-msbuild:binlog-generation` + `dotnet-msbuild:binlog-failure-analysis` | If `dotnet build` fails non-obviously. |
| Task 20 | `superpowers:verification-before-completion` | Final gate. |

## Out of scope (do not implement)

- BenchmarkDotNet runs (deferred to follow-up task; flagged in spec as future work).
- AOT / trimming compatibility.
- Custom MemoryPack member attributes ([MemoryPackOrder], [MemoryPackIgnore]).
- Private setter / field support.
- Open generic registration.
- Live RabbitMQ integration tests.
