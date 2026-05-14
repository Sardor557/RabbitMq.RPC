# MemoryPack Reflection Formatter — Design Spec

**Date:** 2026-05-14
**Status:** Approved (design phase)
**Scope:** `AsbtCore.Broker.Serialization.MemoryPack`

## Problem

Consumers ship 1000+ DTO models inside already-compiled libraries (third-party / NuGet DLLs). MemoryPack requires `[MemoryPackable] partial` on every serializable type, applied via source generator at the DTO's own compile time. Consumers cannot:

- Add the attribute to types they don't own.
- Make 1000+ types `partial`.
- Recompile vendor assemblies.

Goal: enable MemoryPack-based RPC serialization for DTOs that lack `[MemoryPackable]` without modifying their assemblies.

## Solution overview

Add a runtime reflection-based formatter to `AsbtCore.Broker.Serialization.MemoryPack`. On first encounter of a type, build delegates via `Expression.Compile`, wrap them in `IMemoryPackFormatter<T>`, and register with `MemoryPackFormatterProvider`. Lazy auto-discovery is the default; explicit prewarm and polymorphism mapping are opt-in.

Tradeoff accepted: ~2-4x slower than native MemoryPack source-gen path, still binary-format-compatible.

## Out of scope

- Full AOT / trimming compatibility (`Expression.Compile` precludes this; consumers needing AOT should keep using `[MemoryPackable]`).
- Custom MemoryPack member attributes (`[MemoryPackOrder]`, `[MemoryPackIgnore]`) — only declaration order, all public read/write members.
- Private setters or fields.
- Open generic types (only closed generics are registered).
- Benchmarks (covered separately by `AsbtCore.Broker.Benchmarks`).
- Integration tests requiring a live RabbitMQ broker.

## Architecture

### File layout

```
AsbtCore.Broker.Serialization.MemoryPack/
├── Reflection/
│   ├── ReflectionMemoryPackRegistry.cs   // thread-safe registry of built formatters
│   ├── ReflectionMemoryPackFormatter.cs  // generic IMemoryPackFormatter<T>
│   ├── ReflectionMemoryPackPlan.cs       // cached ctor + member readers/writers
│   └── ReflectionMemberAccessor.cs       // per-member read/write delegates
├── Polymorphism/
│   ├── UnionBuilder.cs                   // fluent builder.Add<TDerived>(tag)
│   └── PolymorphicFormatter.cs           // tag-prefixed wire format
├── MemoryPackRpcOptions.cs               // options bag for DI extension
├── MemoryPackRpcSerializer.cs            // modified — EnsureRegistered<T> before Serialize/Deserialize
└── DependencyInjection/
    └── MemoryPackRpcServiceCollectionExtensions.cs  // extended with options overload
```

Existing wire-type formatters (`RpcRequestFormatter`, `RpcResponseFormatter`, `RpcArgumentFormatter`, `RpcErrorFormatter`) and their static-ctor registration remain unchanged.

### Components

**`ReflectionMemoryPackRegistry`**
- `ConcurrentDictionary<Type, byte>` tracks registered types (and in-progress entries during recursive registration).
- `EnsureRegistered(Type t)`:
  1. If `MemoryPackFormatterProvider.IsRegistered(t)` → skip (respects existing `[MemoryPackable]` source-gen formatters).
  2. If `t` is in-progress → return (breaks recursion for cyclic graphs).
  3. Mark in-progress.
  4. Build `ReflectionMemoryPackPlan` for `t`.
  5. Recursively call `EnsureRegistered` on every reference-type member type (collections unwrap to element type).
  6. Instantiate `ReflectionMemoryPackFormatter<T>`, register via `MemoryPackFormatterProvider.Register<T>(formatter)`.
  7. Mark as registered.

**`ReflectionMemoryPackPlan`**
- Captures for one type: chosen constructor (delegate), ordered list of members, per-member read delegate (`Func<T, object>`), per-member write delegate (`Action<T, object>`), nullability info per member.
- Constructor selection rules:
  1. Parameterless ctor if available.
  2. Otherwise the ctor whose parameter names case-insensitively match property names. If multiple ctors match → pick highest-arity; tiebreak deterministically by `MetadataToken` ascending. If none match → throw `RpcSerializationException("Type X has no usable constructor")`.
- Member set: all public instance properties with public getter; settable via `set`, `init`, or matching ctor parameter.
- Member order: declaration order (matches MemoryPack source-gen behavior — uses `MetadataToken`).

**`ReflectionMemoryPackFormatter<T> : MemoryPackFormatter<T>`**
- `Serialize(ref MemoryPackWriter writer, scoped ref T? value)`: null-check, write member count (matches source-gen layout), iterate plan members, for each → resolve `MemoryPackFormatterProvider.GetFormatter<TField>()` lazily on first call (cached) and write.
- `Deserialize(ref MemoryPackReader reader, scoped ref T? value)`: null-check, read member count, materialize values into temp buffer, invoke ctor delegate, assign remaining settable members.

**`UnionBuilder<TBase>`**
- `Add<TDerived>(int tag)` where `TDerived : TBase`. Stores `(tag, Type)` mapping.
- Validates: no duplicate tags, no duplicate derived types.

**`PolymorphicFormatter<TBase> : MemoryPackFormatter<TBase>`**
- On serialize: look up `value.GetType()` in union map → write `byte tag` → delegate to formatter of derived type.
- On deserialize: read `byte tag` → look up derived type → deserialize.
- Unknown tag / unmapped runtime type → throw `RpcSerializationException`.

**`MemoryPackRpcOptions`**
- Holds: list of types/assemblies to prewarm, list of union registrations.
- Applied during DI build: extension method registers `MemoryPackRpcSerializer` as singleton; on construction, the serializer runs `Apply(options)` which invokes registry for prewarms and instantiates `PolymorphicFormatter<TBase>` for each union.

**`MemoryPackRpcSerializer` (modified)**
- Keeps existing static-ctor wire-type registration.
- Constructor accepts optional `MemoryPackRpcOptions`; applies prewarms and unions exactly once.
- `Serialize<T>(value)`: `registry.EnsureRegistered(typeof(T))` then delegate to `MemoryPackSerializer.Serialize(value)`.
- `Deserialize<T>`, `SerializeFragment(Type)`, `DeserializeFragment(Type)`: same pattern.

## Public API

```csharp
services.AddRabbitRpcClient(configuration)
    .UseMemoryPackRpcSerialization();    // existing overload — lazy auto-discovery by default

services.AddRabbitRpcClient(configuration)
    .UseMemoryPackRpcSerialization(options =>
    {
        options.PrewarmType<UserDto>();
        options.PrewarmTypes(typeof(OrderDto), typeof(InvoiceDto));
        options.PrewarmAssembly(typeof(UserDto).Assembly);
        options.PrewarmAssembly(typeof(UserDto).Assembly,
            filter: t => t.Namespace?.StartsWith("Contracts.") == true);

        options.RegisterUnion<Animal>(b => b
            .Add<Cat>(tag: 1)
            .Add<Dog>(tag: 2));
    });
```

Same extension methods exist for `RpcServerBuilder`.

`MemoryPackRpcOptions` public surface:

```csharp
public sealed class MemoryPackRpcOptions
{
    public MemoryPackRpcOptions PrewarmType<T>();
    public MemoryPackRpcOptions PrewarmTypes(params Type[] types);
    public MemoryPackRpcOptions PrewarmAssembly(Assembly asm, Func<Type, bool>? filter = null);
    public MemoryPackRpcOptions RegisterUnion<TBase>(Action<UnionBuilder<TBase>> configure);
}

public sealed class UnionBuilder<TBase>
{
    public UnionBuilder<TBase> Add<TDerived>(byte tag) where TDerived : TBase;
}
```

## Wire compatibility

Reflection formatter emits the same byte layout as MemoryPack source-gen for plain classes (count-prefixed members in declaration order). Consequence: a service using `[MemoryPackable]` for DTO X can interoperate with a service using the reflection formatter for the same DTO X, provided property declaration order matches.

Mixed scenario behavior:

| Producer | Consumer | Outcome |
|---|---|---|
| `[MemoryPackable]` source-gen | `[MemoryPackable]` source-gen | Works (baseline). |
| Reflection formatter | Reflection formatter | Works. |
| `[MemoryPackable]` source-gen | Reflection formatter | Works if member declaration order matches. |
| Reflection formatter | `[MemoryPackable]` source-gen | Works under same constraint. |

Polymorphism wire format: 1 byte tag prefix + payload bytes of the resolved derived type. Not compatible with MemoryPack's native `[MemoryPackUnion]` (which uses varint tags). Documented as a separate format reserved for runtime unions.

## Edge cases

**Thread safety.** Registry uses `ConcurrentDictionary` + double-check via `MemoryPackFormatterProvider.IsRegistered<T>()` before `Register<T>(...)`. Race between two threads registering the same type is safe — both build identical Plans; second `Register` call is suppressed under `IsRegistered` check inside critical section.

**Process-wide registry collision.** `MemoryPackFormatterProvider` is a static singleton. Two DI scopes registering different `RegisterUnion<TBase>` mappings → throw `InvalidOperationException("Union for type X is already registered")` on the second attempt.

**Cyclic type graphs (`A { B b }`, `B { A a }`).** Plans built once per type. Each member access resolves its field formatter lazily through `MemoryPackFormatterProvider.GetFormatter<TField>()` at runtime — by which point both A and B are registered. Recursion in `EnsureRegistered` terminates via the in-progress marker.

**Cyclic object graphs (runtime data with cycles).** Not supported. Stack overflow at serialize, same as MemoryPack itself. Documented limitation.

**Nullable reference types.** Members are inspected via `NullabilityInfoContext`. When `WriteState == Nullable`, formatter prepends a null-bit. If the assembly was compiled without `<Nullable>enable</Nullable>`, all reference-type members are treated as nullable (safe default).

**Generic types.** Closed generics (`Foo<int>`) are registered as concrete types — the Plan for `Foo<int>` is independent of `Foo<string>`. Open generics (`typeof(Foo<>)`) are rejected with `RpcSerializationException`.

**Failure modes (all fail fast, all surfaced as `RpcSerializationException` with type name in message):**

- Type has no public ctor.
- Ctor parameter cannot be matched to any property.
- Type is `interface` / `abstract` and no `RegisterUnion<T>` was configured.
- `MemoryPackFormatterProvider` already contains a conflicting formatter (defensive — should not happen under normal flow).
- Polymorphic value's runtime type is not in the union map.
- Union tag read during deserialization is not in the union map.

**Performance.** Cold-start cost per type (Expression.Compile): ~5-50ms depending on member count. `PrewarmAssembly` amortizes this at startup. Hot-path overhead: ~2-4x vs source-gen MemoryPack, ~3-5x faster than `System.Text.Json`. No boxing for value-typed members (Expression trees emit type-correct delegates).

## Test plan

To be implemented in `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/`. **Test code is created only after explicit confirmation per project rule.**

**New file `ReflectionFormatterTests.cs`:**
- POCO round-trip (public properties + parameterless ctor).
- Record with primary ctor round-trip.
- Class with init-only properties round-trip.
- DTO containing `List<int>`, `Dictionary<string, int>`, `int?`, `MyEnum`.
- Cyclic type graph (`A` references `B`, `B` references `A`) registers without recursion overflow.
- Lazy auto-discovery — DTO never declared in `PrewarmType` serializes correctly.
- Type without public ctor → throws `RpcSerializationException`.
- Open generic type → throws `RpcSerializationException`.

**New file `PolymorphismTests.cs`:**
- `RegisterUnion<Base>` + `Add<Derived>(tag)` → round-trip a base-typed reference.
- Multiple derived types in same union round-trip.
- Duplicate union registration for same base type → throws.
- Polymorphic value with no `RegisterUnion` → throws with informative message.
- Runtime type missing from union map → throws.

**Extend `UseMemoryPackRpcSerializationTests.cs`:**
- New `options` overload binds `MemoryPackRpcOptions` correctly.
- `PrewarmAssembly` with filter registers only matching types.
- Existing parameterless overload continues to work (lazy mode).

**New file `MixedFormatterTests.cs`:**
- DTO with `[MemoryPackable]` referenced by field of a DTO without the attribute → both serialize.
- Reflection-serialized payload deserialized by a fresh serializer instance round-trips.

**Not in scope:**
- Live RabbitMQ integration tests.
- Performance benchmarks (deferred to `AsbtCore.Broker.Benchmarks`).

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| `Expression.Compile` not AOT-safe | Documented; consumers needing AOT keep `[MemoryPackable]`. |
| Declaration order varies across compilers | `MetadataToken` is stable per compilation. Cross-compilation drift is a known MemoryPack limitation, not introduced here. |
| Hidden cost of recursive registration on first call | `PrewarmAssembly` API. Document `Prewarm` as recommended for latency-sensitive paths. |
| Conflict with future MemoryPack versions changing wire format | Adapter is internal; pin MemoryPack version in `AsbtCore.Broker.Serialization.MemoryPack.csproj`. |

## Implementation skills (dotnet plugins)

The implementation plan should invoke these skills at the listed phases. Each skill is referenced by its full ID for direct `Skill` tool invocation.

| Phase | Skill | Purpose |
|---|---|---|
| Before touching csproj | `dotnet-msbuild:msbuild-antipatterns` | Sanity-check edits to `AsbtCore.Broker.Serialization.MemoryPack.csproj` (PackageReleaseNotes bump, version, dependencies). |
| When changing csproj | `dotnet-msbuild:msbuild-modernization` | Ensure project file follows modern SDK conventions. |
| During code authoring | `dotnet-diag:analyzing-dotnet-performance` | Audit reflection/Expression.Compile code paths for known .NET anti-patterns before merging. Skip the hot-path async/LINQ checks — focus on allocation patterns in `ReflectionMemoryPackFormatter.Serialize/Deserialize`. |
| Test authoring | `dotnet-test:test-anti-patterns` | Audit new test files for assertion / smell issues before commit. TUnit + Moq idioms. |
| Test execution | `dotnet-test:run-tests` | Run TUnit suites via `dotnet run` (project convention — NOT `dotnet test`). |
| Test filtering during dev | `dotnet-test:filter-syntax` | Build `--treenode-filter` strings when iterating on a single failing test. |
| Build verification | `dotnet-msbuild:incremental-build` | Confirm builds stay fast and incremental after new files are added. |
| If build issues arise | `dotnet-msbuild:binlog-generation` + `dotnet-msbuild:binlog-failure-analysis` | Capture and diagnose any MSBuild errors during pack/publish. |
| Post-implementation (optional, deferred) | `dotnet-diag:microbenchmarking` | Add a BenchmarkDotNet run inside `AsbtCore.Broker.Benchmarks` comparing native vs reflection formatter. Not in core scope — flagged for follow-up. |

The implementation agent (writing-plans → executing-plans) is expected to invoke `dotnet-test:run-tests` after each phase that touches test code and `dotnet-msbuild:incremental-build` after each phase that touches csproj or adds new `.cs` files.

## Migration

Existing consumers (using current `UseMemoryPackRpcSerialization()` overload with no arguments) need no changes. New capability is additive: types without `[MemoryPackable]` now serialize automatically.

Consumers wanting polymorphism or cold-start reduction adopt the new `options` overload.

`PackageReleaseNotes` in `AsbtCore.Broker.Serialization.MemoryPack.csproj` is bumped to 1.1.0 with note describing reflection formatter + lazy/prewarm/union APIs. README sections (RU + EN) get a "Working with vendor DTOs" subsection.
