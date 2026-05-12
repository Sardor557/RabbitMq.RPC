# Binary Serialization Migration — Implementation Plan

> **For Claude:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `System.Text.Json` + `JsonElement` payloads in `RpcRequest`/`RpcResponse` with format-agnostic `ReadOnlyMemory<byte>` payloads, ship two adapter packages (`RabbitRpc.Serialization.SystemTextJson` and `RabbitRpc.Serialization.XPacketRpc`), and cut `RabbitRpc.Client`/`Server` 4.0.0.

**Architecture:** Two-level serializer contract — envelope-level (`Serialize<T>` / `Deserialize<T>`) plus fragment-level (`SerializeFragment(value, type)` / `DeserializeFragment(bytes, type)`). `RpcArgument.Payload` and `RpcResponse.Result` become `ReadOnlyMemory<byte>`. Core ships zero serializer; user picks an adapter package and calls `.UseXxxSerialization()` on the builder — DI startup validator fails with a helpful message otherwise.

**Tech Stack:** .NET 10, TUnit + Moq, RabbitMQ.Client 7.x, XPacketRpc (source generator), System.Text.Json (adapter only).

**Spec reference:** `docs/superpowers/specs/2026-05-13-binary-serialization-design.md`

**Parallel execution:** Phases map to 7 agents (A–G) across 4 waves — see Chunk 7 (Execution Handoff) for dispatch order.

---

## Conventions used throughout this plan

- All paths are **repo-relative**.
- File excerpts marked `existing` are quoted from the current codebase; do not retype, modify in place.
- Test framework is **TUnit** (already used by both test projects). Test methods are `async Task` returning, use `await Assert.That(actual).IsEqualTo(expected)` style — see existing tests for the convention. Do **not** introduce xUnit/NUnit syntax.
- **TDD loop** for every behavior change: write failing test → run, observe red → minimal impl → run, observe green → commit.
- Test run command (TUnit projects are exe, not `dotnet test`):
  ```
  dotnet run --project Tests/<Project>/<Project>.csproj
  ```
  Single test filter:
  ```
  dotnet run --project Tests/<Project>/<Project>.csproj -- --treenode-filter "/*/*/<ClassName>/<TestName>"
  ```
- After every task: `git status` must be clean; commits are small and focused.
- Skip commit if a task is "rename / move" only and the next task depends on the moved file — commit at the next stable point.

---

## Chunk 1: Phase 1 — Core Contracts Refactor (Agent A)

> **Wave 1 — blocks all subsequent agents.** This chunk freezes the new `IRpcSerializer` and `RpcContracts` surface. Agents B / C / D start the moment this is merged.

### Files touched in this chunk

| Action | Path |
|---|---|
| Create | `AsbtCore.Broker.Core/Abstractions/IRpcSerializer.cs` |
| Create | `AsbtCore.Broker.Core/Abstractions/IRpcSerializerInterfaceWarmup.cs` |
| Modify | `AsbtCore.Broker.Core/RpcContracts.cs` |
| Modify | `AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj` (remove no-longer-needed package refs — verify after deletes) |
| Delete | `AsbtCore.Broker.Core/Serialization/IRpcSerializer.cs` (moved) |
| Delete | `AsbtCore.Broker.Core/Serialization/JsonRpcSerializer.cs` |
| Delete | `AsbtCore.Broker.Core/Serialization/RpcSerializationHelper.cs` |
| Delete | `AsbtCore.Broker.Core/Serialization/RpcSerializationServiceCollectionExtensions.cs` |
| Delete | `Tests/AsbtCore.Broker.Core.Tests/Serialization/JsonRpcSerializerTests.cs` |
| Delete | `Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationHelperTests.cs` |
| Delete | `Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationServiceCollectionExtensionsTests.cs` |
| Move | `Tests/AsbtCore.Broker.Core.Tests/RpcRequestSerializationTests.cs` → goes to SystemTextJson.Tests in Phase 2 (delete here, Phase 2 recreates) |
| Modify | `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs` — change `using AsbtCore.Broker.Core.Serialization;` → `using AsbtCore.Broker.Core.Abstractions;` |
| Modify | `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs` — same `using` change |
| Verify | `Tests/AsbtCore.Broker.Core.Tests/Transport/*Tests.cs` (5 files) — they do not reference `JsonElement`/`RpcJson` (confirmed via grep). No source edits required; they must compile + pass once RabbitMq compiles. |

> **Plan erratum (corrected after Agent A's BLOCKED feedback):** `Tests/AsbtCore.Broker.Core.Tests` has a `<ProjectReference>` to `AsbtCore.Broker.RabbitMq`, and the 5 transport tests directly construct `RabbitMqRpcTransport` / `RabbitMqRpcTransportHost`. Phase 1 therefore expands minimally to also fix the two `using` lines in RabbitMq so Core.Tests builds. This is justified because the `IRpcSerializer` signature change (`byte[]` → `ReadOnlyMemory<byte>` for `Serialize<T>`) is wire-compatible with all RabbitMq call sites — `RabbitMQ.Client 7.x`'s `BasicPublishAsync` already accepts `ReadOnlyMemory<byte>`.

---

### Task 1.1: Create the new `IRpcSerializer` contract

**Files:**
- Create: `AsbtCore.Broker.Core/Abstractions/IRpcSerializer.cs`

- [ ] **Step 1: Create the file with the new 4-method interface**

```csharp
namespace AsbtCore.Broker.Core.Abstractions;

/// <summary>
/// RPC message serialization contract.
/// The byte boundary is <see cref="System.ReadOnlyMemory{T}"/> of <see cref="byte"/> at the transport (RabbitMQ body).
/// No intermediate <see cref="string"/> between layers.
/// </summary>
public interface IRpcSerializer
{
    /// <summary>Wire identifier — written to <c>BasicProperties.ContentType</c> on publish.</summary>
    string ContentType { get; }

    /// <summary>Serializes a whole envelope (RpcRequest / RpcResponse) directly to bytes for BasicPublish.</summary>
    ReadOnlyMemory<byte> Serialize<T>(T value);

    /// <summary>Deserializes a whole envelope from the message body without an intermediate string hop.</summary>
    T? Deserialize<T>(ReadOnlyMemory<byte> payload);

    /// <summary>
    /// Serializes a single typed RPC argument or method result.
    /// Called once per RpcArgument on the client and once per result on the server.
    /// </summary>
    ReadOnlyMemory<byte> SerializeFragment(object? value, Type type);

    /// <summary>Deserializes a single typed fragment back into a CLR value.</summary>
    object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type);
}
```

- [ ] **Step 2: Build Core to confirm no compiler errors yet (old interface still exists in Serialization/)**

Run: `dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj`
Expected: PASS (we have two `IRpcSerializer` types temporarily — they're in different namespaces; Core/Serialization users still pick up the old one).

- [ ] **Step 3: Do not commit yet — next task adds the warmup interface.**

---

### Task 1.2: Create `IRpcSerializerInterfaceWarmup`

**Files:**
- Create: `AsbtCore.Broker.Core/Abstractions/IRpcSerializerInterfaceWarmup.cs`

- [ ] **Step 1: Add the public optional contract**

```csharp
namespace AsbtCore.Broker.Core.Abstractions;

/// <summary>
/// Optional capability surface for <see cref="IRpcSerializer"/> implementations that need
/// to pre-register types from RPC interface signatures (e.g. source-generated binary serializers).
/// AddRpcProxy and Register call <see cref="Prewarm"/> on startup if the registered serializer
/// implements this interface; implementations that do not need warm-up should not implement it.
/// </summary>
public interface IRpcSerializerInterfaceWarmup
{
    /// <summary>
    /// Walks all methods of <paramref name="interfaceType"/>, unwraps Task/ValueTask return types,
    /// and recursively registers every parameter and return type with the underlying format.
    /// </summary>
    void Prewarm(Type interfaceType);
}
```

- [ ] **Step 2: Commit (intermediate but coherent surface)**

```
git add AsbtCore.Broker.Core/Abstractions/IRpcSerializer.cs AsbtCore.Broker.Core/Abstractions/IRpcSerializerInterfaceWarmup.cs
git commit -m "feat(core): introduce 4-method IRpcSerializer and IRpcSerializerInterfaceWarmup in Abstractions/"
```

---

### Task 1.3: Migrate `RpcContracts` to `ReadOnlyMemory<byte>`

**Files:**
- Modify: `AsbtCore.Broker.Core/RpcContracts.cs` (full rewrite — small file)

- [ ] **Step 1: Write a failing test that asserts the new contract shape**

Create temporary `Tests/AsbtCore.Broker.Core.Tests/Contracts/RpcContractsShapeTests.cs`:

```csharp
namespace AsbtCore.Broker.Core.Tests.Contracts;

public class RpcContractsShapeTests
{
    [Test]
    public async Task RpcArgument_Payload_IsReadOnlyMemoryOfByte()
    {
        var prop = typeof(RpcArgument).GetProperty(nameof(RpcArgument.Payload))!;
        await Assert.That(prop.PropertyType).IsEqualTo(typeof(ReadOnlyMemory<byte>));
    }

    [Test]
    public async Task RpcResponse_Result_IsNullableReadOnlyMemoryOfByte()
    {
        var prop = typeof(RpcResponse).GetProperty(nameof(RpcResponse.Result))!;
        await Assert.That(prop.PropertyType).IsEqualTo(typeof(ReadOnlyMemory<byte>?));
    }

    [Test]
    public async Task RpcRequest_DoesNotReference_JsonElement()
    {
        var refs = typeof(RpcRequest).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();
        // System.Text.Json may still be transitively reachable, but RpcRequest itself
        // must not depend on JsonElement: check no public property type lives in System.Text.Json.
        var jsonElementUsages = typeof(RpcRequest).GetProperties()
            .Concat(typeof(RpcArgument).GetProperties())
            .Concat(typeof(RpcResponse).GetProperties())
            .Where(p => p.PropertyType.Namespace?.StartsWith("System.Text.Json") == true)
            .ToArray();
        await Assert.That(jsonElementUsages.Length).IsEqualTo(0);
    }
}
```

- [ ] **Step 2: Run — observe red**

Run: `dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj -- --treenode-filter "/*/*/RpcContractsShapeTests/*"`
Expected: 3 failures (current `Payload` is `JsonElement`, `Result` is `JsonElement?`).

- [ ] **Step 3: Rewrite `RpcContracts.cs`**

```csharp
namespace AsbtCore.Broker.Core
{
    public sealed class RpcRequest
    {
        public string RequestId { get; set; } = default!;
        public string InterfaceName { get; set; } = default!;
        public string MethodName { get; set; } = default!;
        public List<RpcArgument> Arguments { get; set; } = new();
    }

    public sealed class RpcArgument
    {
        public string TypeName { get; set; } = default!;
        public ReadOnlyMemory<byte> Payload { get; set; }
    }

    public sealed class RpcResponse
    {
        public string RequestId { get; set; } = default!;
        public bool Success { get; set; }
        public string? ResultTypeName { get; set; }
        public ReadOnlyMemory<byte>? Result { get; set; }
        public RpcError? Error { get; set; }
    }

    public sealed class RpcError
    {
        public string Code { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string? Details { get; set; }
        public string? ExceptionType { get; set; }
    }
}
```

`using System.Text.Json` is removed. `RpcJson` (the static `JsonSerializerOptions` holder) is **deleted** — it moves to the SystemTextJson adapter in Phase 2.

- [ ] **Step 4: Build expectations — Core won't compile cleanly yet**

`AsbtCore.Broker.Core/Serialization/*.cs` still references the old shape (e.g., `RpcSerializationHelper.ToElement` returns `JsonElement`). Build will fail. **That's expected** — Task 1.4 deletes those files.

Skip a build step here; proceed directly to deletion.

- [ ] **Step 5: Do not commit yet** — partial-state file is still in the working tree.

---

### Task 1.4: Delete legacy serialization files from Core

**Files:**
- Delete: `AsbtCore.Broker.Core/Serialization/IRpcSerializer.cs`
- Delete: `AsbtCore.Broker.Core/Serialization/JsonRpcSerializer.cs`
- Delete: `AsbtCore.Broker.Core/Serialization/RpcSerializationHelper.cs`
- Delete: `AsbtCore.Broker.Core/Serialization/RpcSerializationServiceCollectionExtensions.cs`

- [ ] **Step 1: Remove the four files**

```
git rm AsbtCore.Broker.Core/Serialization/IRpcSerializer.cs
git rm AsbtCore.Broker.Core/Serialization/JsonRpcSerializer.cs
git rm AsbtCore.Broker.Core/Serialization/RpcSerializationHelper.cs
git rm AsbtCore.Broker.Core/Serialization/RpcSerializationServiceCollectionExtensions.cs
```

- [ ] **Step 2: Build Core**

Run: `dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj`
Expected: **Compile errors only in `AsbtCore.Broker.RabbitMq` / `AsbtCore.Broker.Client` / `AsbtCore.Broker.Server`** referencing `RpcSerializationHelper` and `RpcJson`. Core itself should be clean.

To confirm Core in isolation:
```
dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj --no-dependencies
```
Expected: PASS.

- [ ] **Step 3: Do not commit yet** — Phase 1 tests still need updates.

---

### Task 1.5: Delete the now-orphaned test files in Core.Tests

**Files:**
- Delete: `Tests/AsbtCore.Broker.Core.Tests/Serialization/JsonRpcSerializerTests.cs`
- Delete: `Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationHelperTests.cs`
- Delete: `Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationServiceCollectionExtensionsTests.cs`
- Delete: `Tests/AsbtCore.Broker.Core.Tests/RpcRequestSerializationTests.cs` (its assertions live in the JSON adapter from Phase 2)

- [ ] **Step 1: Remove the four test files**

```
git rm Tests/AsbtCore.Broker.Core.Tests/Serialization/JsonRpcSerializerTests.cs
git rm Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationHelperTests.cs
git rm Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationServiceCollectionExtensionsTests.cs
git rm Tests/AsbtCore.Broker.Core.Tests/RpcRequestSerializationTests.cs
```

- [ ] **Step 2: If `Tests/AsbtCore.Broker.Core.Tests/Serialization/` is now empty, remove the directory**

```
rmdir Tests/AsbtCore.Broker.Core.Tests/Serialization 2>/dev/null || true
```

Note: keep `Tests/AsbtCore.Broker.Core.Tests/Serialization/StableTypeNameTests.cs` if it lives there — `StableTypeName` stays internal in Core, its tests stay too. **Re-verify** before removing the directory.

---

### Task 1.6: Fix RabbitMq `using` statements + verify transport tests pass unchanged

**Plan erratum:** Agent A's initial pass discovered that the 5 transport tests in `Core.Tests/Transport/` do not contain `JsonElement` / `RpcJson` references (`grep` returned zero matches) — they test RabbitMq transport behavior, not serialization. They need no source edits. However, `Tests/AsbtCore.Broker.Core.Tests` has a `<ProjectReference>` to `AsbtCore.Broker.RabbitMq`, whose 2 transport files reference the deleted `Core.Serialization` namespace. Phase 1 minimally fixes this so Core.Tests can build.

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs`
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs`

- [ ] **Step 1: Update both files' `using` statements**

In each of the two RabbitMq transport files, change:
```csharp
using AsbtCore.Broker.Core.Serialization;
```
to:
```csharp
using AsbtCore.Broker.Core.Abstractions;
```

Both files inject `IRpcSerializer` via constructor. The interface moved namespace; no other change is required at the call sites — `serializer.Serialize(request)` now returns `ReadOnlyMemory<byte>` instead of `byte[]`, but `RabbitMQ.Client 7.x`'s `BasicPublishAsync(body: ...)` already accepts `ReadOnlyMemory<byte>` (since 7.0). Variable type-inference via `var body = serializer.Serialize(...)` adapts automatically.

- [ ] **Step 2: Build RabbitMq in isolation**

```
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj
```
Expected: green. If you get a "cannot convert ReadOnlyMemory<byte> to byte[]" error at a call site, that means RabbitMQ.Client did NOT auto-accept. Fix it by appending `.ToArray()` to the affected call — but verify first that the call site really needs `byte[]`.

- [ ] **Step 3: Build Core.Tests, then run**

```
dotnet build Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
```
Expected: all tests PASS — the 5 transport tests, the new `RpcContractsShapeTests`, `StableTypeNameTests`, and others. If any transport test fails, it indicates RabbitMq behavior changed implicitly via the signature swap — investigate that specific test (do not paper over it).

- [ ] **Step 4: Commit**

```
git add -A
git commit -m "refactor(core)!: replace JsonElement with ReadOnlyMemory<byte> in RpcContracts; delete legacy serialization; rewire RabbitMq using to Core.Abstractions"
```

> **!** in commit type denotes breaking change per Conventional Commits.

---

### Task 1.7: Mark `AsbtCore.Broker.Core.Tests/Contracts/RpcContractsShapeTests.cs` as permanent

The contract-shape test was added in Task 1.3 as a TDD driver. Keep it — it prevents accidental regressions (someone re-adding `JsonElement` to `RpcContracts.cs`).

- [ ] **Step 1: Verify the test still passes**

```
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj -- --treenode-filter "/*/*/RpcContractsShapeTests/*"
```
Expected: 3 PASS.

- [ ] **Step 2: No commit needed — file was added in Task 1.3 commit.**

---

### Phase 1 acceptance — Agent A done when:

1. `dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj --no-dependencies` — green.
2. `dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj` — green (after `using` fix in Task 1.6).
3. `dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj` — green.
4. `grep -rn "JsonElement\|RpcJson\|RpcSerializationHelper" AsbtCore.Broker.Core/` — zero matches.
5. `grep -rn "Core\.Serialization" AsbtCore.Broker.RabbitMq/` — zero matches.
6. New files exist: `Core/Abstractions/IRpcSerializer.cs`, `Core/Abstractions/IRpcSerializerInterfaceWarmup.cs`.
7. Working tree clean; commits on the worktree branch.

**Hand-off note for Wave 2 agents:** `AsbtCore.Broker.Client` and `AsbtCore.Broker.Server` do **not** build yet — they still reference `RpcSerializationHelper` / `RpcJson` / `AddRpcSerialization`. That is expected and is the explicit scope of Agent C in Phase 3. `AsbtCore.Broker.RabbitMq` **does** build (fixed in Phase 1).

---

## Chunk 2: Phase 2 — SystemTextJson Adapter (Agent B)

> **Wave 2 — parallel with C and D.** Depends only on Phase 1 contracts. Does not touch Client/Server projects.

### Files touched in this chunk

| Action | Path |
|---|---|
| Create | `AsbtCore.Broker.Serialization.SystemTextJson/AsbtCore.Broker.Serialization.SystemTextJson.csproj` |
| Create | `AsbtCore.Broker.Serialization.SystemTextJson/RpcJson.cs` |
| Create | `AsbtCore.Broker.Serialization.SystemTextJson/JsonRpcSerializer.cs` |
| Create | `AsbtCore.Broker.Serialization.SystemTextJson/Converters/ReadOnlyMemoryByteJsonConverter.cs` |
| Create | `AsbtCore.Broker.Serialization.SystemTextJson/DependencyInjection/JsonRpcServiceCollectionExtensions.cs` |
| Create | `AsbtCore.Broker.Serialization.SystemTextJson/README.md` |
| Create | `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj` |
| Create | `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/Converters/ReadOnlyMemoryByteJsonConverterTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/JsonRpcSerializerTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/RpcRequestSerializationTests.cs` (the one moved from Core.Tests) |
| Create | `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/LifetimeContractTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/UseJsonRpcSerializationTests.cs` |
| Modify | `RabbitMq.RPC.sln` (add 2 projects — main project + test project) |

---

### Task 2.1: Scaffold adapter project + add to solution

**Files:**
- Create: `AsbtCore.Broker.Serialization.SystemTextJson/AsbtCore.Broker.Serialization.SystemTextJson.csproj`

- [ ] **Step 1: Create the directory and csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <PackageId>RabbitRpc.Serialization.SystemTextJson</PackageId>
    <Title>RabbitRpc.Serialization.SystemTextJson</Title>
    <Description>System.Text.Json adapter for RabbitRpc — provides IRpcSerializer over System.Text.Json with a ReadOnlyMemory&lt;byte&gt; base64 converter.</Description>
    <PackageTags>rabbitmq;rpc;dotnet;json;serialization</PackageTags>

    <Version>1.0.0</Version>
    <Authors>AsbtCore</Authors>
    <Company>AsbtCore</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/Sardor557/AsbtCore.Broker</PackageProjectUrl>
    <RepositoryUrl>https://github.com/Sardor557/AsbtCore.Broker</RepositoryUrl>
    <RepositoryType>git</RepositoryType>

    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AsbtCore.Broker.Core\AsbtCore.Broker.Core.csproj" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="AsbtCore.Broker.Serialization.SystemTextJson.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

```
dotnet sln RabbitMq.RPC.sln add AsbtCore.Broker.Serialization.SystemTextJson/AsbtCore.Broker.Serialization.SystemTextJson.csproj --solution-folder "Serialization"
```

If solution folder must be created manually, edit `RabbitMq.RPC.sln` and add the project under a new `"Serialization"` folder (GUID matching the existing pattern — copy a `{2150E333-...}` folder block).

- [ ] **Step 3: Build expectations — won't link yet, no source files exist**

`dotnet build AsbtCore.Broker.Serialization.SystemTextJson/AsbtCore.Broker.Serialization.SystemTextJson.csproj`
Expected: PASS (empty assembly, only references).

---

### Task 2.2: Add `RpcJson` (static JsonSerializerOptions holder) — TDD

**Files:**
- Create: `AsbtCore.Broker.Serialization.SystemTextJson/RpcJson.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj`
- Create: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/JsonRpcSerializerTests.cs` (initially with RpcJson sanity tests)

- [ ] **Step 1: Scaffold the test project**

`Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" Version="*" />
    <PackageReference Include="Moq" Version="*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\AsbtCore.Broker.Serialization.SystemTextJson\AsbtCore.Broker.Serialization.SystemTextJson.csproj" />
  </ItemGroup>
</Project>
```

> Copy exact `TUnit` and `Moq` versions from `Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj` — don't drift versions.

Add to solution:
```
dotnet sln RabbitMq.RPC.sln add Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj --solution-folder "Tests"
```

- [ ] **Step 2: Write the failing test**

`Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/JsonRpcSerializerTests.cs`:

```csharp
using System.Text.Json;
using AsbtCore.Broker.Serialization.SystemTextJson;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class RpcJsonOptionsTests
{
    [Test]
    public async Task Options_UseCamelCase()
    {
        await Assert.That(RpcJson.Options.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task Options_IgnoreNullsOnWrite()
    {
        await Assert.That(RpcJson.Options.DefaultIgnoreCondition).IsEqualTo(JsonIgnoreCondition.WhenWritingNull);
    }

    [Test]
    public async Task Options_AreCaseInsensitive()
    {
        await Assert.That(RpcJson.Options.PropertyNameCaseInsensitive).IsTrue();
    }
}
```

- [ ] **Step 3: Run — observe red (RpcJson does not exist)**

```
dotnet run --project Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj
```
Expected: compile error `The type or namespace 'RpcJson' could not be found`.

- [ ] **Step 4: Implement `RpcJson`**

`AsbtCore.Broker.Serialization.SystemTextJson/RpcJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using AsbtCore.Broker.Serialization.SystemTextJson.Converters;

namespace AsbtCore.Broker.Serialization.SystemTextJson;

/// <summary>
/// Default <see cref="JsonSerializerOptions"/> for RabbitRpc JSON envelopes.
/// Pre-registers <see cref="ReadOnlyMemoryByteJsonConverter"/> so that
/// <c>RpcArgument.Payload</c> / <c>RpcResponse.Result</c> round-trip through base64.
/// </summary>
public static class RpcJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    /// <summary>Builds a fresh, mutable options instance preconfigured for RabbitRpc.</summary>
    public static JsonSerializerOptions Build() => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ReadOnlyMemoryByteJsonConverter() }
    };
}
```

(`ReadOnlyMemoryByteJsonConverter` doesn't exist yet — fix in Task 2.3. Until then, leave the converter line out and re-add it after Task 2.3 lands.)

- [ ] **Step 5: Temporarily comment out the converter line, run tests**

```
dotnet run --project Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj -- --treenode-filter "/*/*/RpcJsonOptionsTests/*"
```
Expected: 3 PASS.

- [ ] **Step 6: Do not commit yet — converter follows.**

---

### Task 2.3: `ReadOnlyMemoryByteJsonConverter` — TDD

**Files:**
- Create: `AsbtCore.Broker.Serialization.SystemTextJson/Converters/ReadOnlyMemoryByteJsonConverter.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/Converters/ReadOnlyMemoryByteJsonConverterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using AsbtCore.Broker.Serialization.SystemTextJson.Converters;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests.Converters;

public class ReadOnlyMemoryByteJsonConverterTests
{
    private static JsonSerializerOptions OptionsWithConverter()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new ReadOnlyMemoryByteJsonConverter());
        return o;
    }

    [Test]
    public async Task Write_EmitsBase64String()
    {
        var bytes = new ReadOnlyMemory<byte>(new byte[] { 0x01, 0x02, 0x03 });
        var json = JsonSerializer.Serialize(bytes, OptionsWithConverter());
        await Assert.That(json).IsEqualTo("\"AQID\""); // base64("\x01\x02\x03") = "AQID"
    }

    [Test]
    public async Task Read_ParsesBase64String()
    {
        var bytes = JsonSerializer.Deserialize<ReadOnlyMemory<byte>>("\"AQID\"", OptionsWithConverter());
        await Assert.That(bytes.Length).IsEqualTo(3);
        await Assert.That(bytes.Span[0]).IsEqualTo((byte)0x01);
        await Assert.That(bytes.Span[1]).IsEqualTo((byte)0x02);
        await Assert.That(bytes.Span[2]).IsEqualTo((byte)0x03);
    }

    [Test]
    public async Task Roundtrip_PreservesArbitraryBytes()
    {
        var original = new byte[256];
        for (int i = 0; i < 256; i++) original[i] = (byte)i;
        var json = JsonSerializer.Serialize<ReadOnlyMemory<byte>>(original, OptionsWithConverter());
        var roundtrip = JsonSerializer.Deserialize<ReadOnlyMemory<byte>>(json, OptionsWithConverter());
        await Assert.That(roundtrip.Span.SequenceEqual(original)).IsTrue();
    }
}
```

- [ ] **Step 2: Run — observe red (type missing)**

```
dotnet run --project Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj
```
Expected: compile error.

- [ ] **Step 3: Implement the converter**

`AsbtCore.Broker.Serialization.SystemTextJson/Converters/ReadOnlyMemoryByteJsonConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Converters;

public sealed class ReadOnlyMemoryByteJsonConverter : JsonConverter<ReadOnlyMemory<byte>>
{
    public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetBytesFromBase64();

    public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<byte> value, JsonSerializerOptions options)
        => writer.WriteBase64StringValue(value.Span);
}
```

- [ ] **Step 4: Re-enable the converter line in `RpcJson.Build()`** (un-comment from Task 2.2 Step 4).

- [ ] **Step 5: Run all SystemTextJson.Tests**

Expected: 6 PASS (3 RpcJson + 3 converter).

- [ ] **Step 6: Commit**

```
git add AsbtCore.Broker.Serialization.SystemTextJson Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests RabbitMq.RPC.sln
git commit -m "feat(serialization-systemtextjson): scaffold adapter with RpcJson options and ReadOnlyMemory<byte> converter"
```

---

### Task 2.4: `JsonRpcSerializer` envelope round-trip — TDD

**Files:**
- Create: `AsbtCore.Broker.Serialization.SystemTextJson/JsonRpcSerializer.cs`
- Modify: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/JsonRpcSerializerTests.cs` (extend)

- [ ] **Step 1: Write the failing envelope round-trip test**

Append to `JsonRpcSerializerTests.cs`:

```csharp
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class JsonRpcSerializerEnvelopeTests
{
    private static IRpcSerializer NewSerializer() => new JsonRpcSerializer();

    [Test]
    public async Task ContentType_IsSystemTextJson()
    {
        await Assert.That(NewSerializer().ContentType).IsEqualTo("application/json");
    }

    [Test]
    public async Task Serialize_Deserialize_RpcRequest_Roundtrip()
    {
        var sut = NewSerializer();
        var request = new RpcRequest
        {
            RequestId = "rid",
            InterfaceName = "IFoo",
            MethodName = "M",
            Arguments =
            {
                new RpcArgument { TypeName = "System.Int32, System.Private.CoreLib", Payload = new byte[] { 1, 2, 3 } }
            }
        };

        var bytes = sut.Serialize(request);
        var roundtrip = sut.Deserialize<RpcRequest>(bytes);

        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.RequestId).IsEqualTo("rid");
        await Assert.That(roundtrip.Arguments.Count).IsEqualTo(1);
        await Assert.That(roundtrip.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_NullResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse { RequestId = "rid", Success = true, Result = null };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Result.HasValue).IsFalse();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_WithResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse { RequestId = "rid", Success = true, Result = new byte[] { 9, 8, 7 } };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Result!.Value.Span.SequenceEqual(new byte[] { 9, 8, 7 })).IsTrue();
    }
}
```

- [ ] **Step 2: Run — observe red**

Expected: compile error (`JsonRpcSerializer` missing).

- [ ] **Step 3: Implement envelope methods**

`AsbtCore.Broker.Serialization.SystemTextJson/JsonRpcSerializer.cs`:

```csharp
using System.Text.Json;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.Serialization.SystemTextJson;

public sealed class JsonRpcSerializer : IRpcSerializer
{
    public string ContentType => "application/json";

    private readonly JsonSerializerOptions options;

    public JsonRpcSerializer() : this(RpcJson.Options) { }

    public JsonRpcSerializer(JsonSerializerOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, options);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Deserialize<T>(payload.Span, options);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
        => JsonSerializer.SerializeToUtf8Bytes(value, type, options);

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
        => JsonSerializer.Deserialize(payload.Span, type, options);
}
```

- [ ] **Step 4: Run all SystemTextJson.Tests**

Expected: all PASS (envelope + converter + RpcJson tests).

---

### Task 2.5: `JsonRpcSerializer.SerializeFragment` — TDD edge cases

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/JsonRpcSerializerTests.cs`

- [ ] **Step 1: Add failing fragment-level tests**

```csharp
public class JsonRpcSerializerFragmentTests
{
    private record UserDto(Guid Id, string Name);

    private static IRpcSerializer NewSerializer() => new JsonRpcSerializer();

    [Test]
    public async Task Fragment_Roundtrips_Int()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(42, typeof(int));
        var value = (int)sut.DeserializeFragment(bytes, typeof(int))!;
        await Assert.That(value).IsEqualTo(42);
    }

    [Test]
    public async Task Fragment_Roundtrips_Dto()
    {
        var sut = NewSerializer();
        var original = new UserDto(Guid.NewGuid(), "Alice");
        var bytes = sut.SerializeFragment(original, typeof(UserDto));
        var value = (UserDto)sut.DeserializeFragment(bytes, typeof(UserDto))!;
        await Assert.That(value).IsEqualTo(original);
    }

    [Test]
    public async Task Fragment_Roundtrips_NullableInt_WithValue()
    {
        var sut = NewSerializer();
        int? original = 7;
        var bytes = sut.SerializeFragment(original, typeof(int?));
        var value = (int?)sut.DeserializeFragment(bytes, typeof(int?));
        await Assert.That(value).IsEqualTo(7);
    }

    [Test]
    public async Task Fragment_Roundtrips_NullReference()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(null, typeof(string));
        var value = sut.DeserializeFragment(bytes, typeof(string));
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Fragment_Roundtrips_List()
    {
        var sut = NewSerializer();
        var list = new List<int> { 1, 2, 3 };
        var bytes = sut.SerializeFragment(list, typeof(List<int>));
        var value = (List<int>)sut.DeserializeFragment(bytes, typeof(List<int>))!;
        await Assert.That(value.SequenceEqual(list)).IsTrue();
    }
}
```

- [ ] **Step 2: Run — expect all PASS (the implementation from 2.4 already covers fragments)**

If any fail, the bug is in `JsonRpcSerializer.SerializeFragment` / `DeserializeFragment`. Fix and re-run.

- [ ] **Step 3: Commit**

```
git add AsbtCore.Broker.Serialization.SystemTextJson Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests
git commit -m "feat(serialization-systemtextjson): JsonRpcSerializer with envelope and fragment round-trip"
```

---

### Task 2.6: `RpcRequestSerializationTests` (carried over from Core.Tests) — TDD

**Files:**
- Create: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/RpcRequestSerializationTests.cs`

- [ ] **Step 1: Port the two original tests, adapt to new contract**

```csharp
using System.Text.Json;
using AsbtCore.Broker.Core;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class RpcRequestSerializationTests
{
    [Test]
    public async Task Deserialize_PreservesProvidedRequestId()
    {
        var json = """{"requestId":"abc","interfaceName":"I","methodName":"M","arguments":[]}""";
        var request = JsonSerializer.Deserialize<RpcRequest>(json, RpcJson.Options);
        await Assert.That(request).IsNotNull();
        await Assert.That(request!.RequestId).IsEqualTo("abc");
    }

    [Test]
    public async Task Deserialize_MissingRequestId_LeavesItNull()
    {
        var json = """{"interfaceName":"I","methodName":"M","arguments":[]}""";
        var request = JsonSerializer.Deserialize<RpcRequest>(json, RpcJson.Options);
        await Assert.That(request).IsNotNull();
        await Assert.That(request!.RequestId).IsNull();
    }

    [Test]
    public async Task Serialize_RpcArgument_PayloadIsBase64()
    {
        var arg = new RpcArgument { TypeName = "X", Payload = new byte[] { 0xAA, 0xBB } };
        var json = JsonSerializer.Serialize(arg, RpcJson.Options);
        await Assert.That(json).Contains("\"payload\":\"qrs=\"");
    }
}
```

- [ ] **Step 2: Run — expect 3 PASS**

---

### Task 2.7: Lifetime contract test

**Files:**
- Create: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/LifetimeContractTests.cs`

The contract from spec §9 risk 1: `RpcArgument.Payload` returned by `Deserialize<RpcRequest>` must remain valid after the source buffer is overwritten. JSON-adapter naturally satisfies this because `JsonSerializer.Deserialize<RpcRequest>` allocates new arrays for base64-decoded `byte[]` / `ReadOnlyMemory<byte>` properties.

- [ ] **Step 1: Write the test**

```csharp
using System.Text.Json;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Serialization.SystemTextJson;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class LifetimeContractTests
{
    [Test]
    public async Task Deserialized_Payload_Survives_BufferOverwrite()
    {
        var sut = new JsonRpcSerializer();
        var original = new RpcRequest
        {
            RequestId = "rid",
            InterfaceName = "I",
            MethodName = "M",
            Arguments = { new RpcArgument { TypeName = "T", Payload = new byte[] { 7, 8, 9 } } }
        };

        var bytes = sut.Serialize(original).ToArray(); // own buffer
        var deserialized = sut.Deserialize<RpcRequest>(bytes)!;

        // Overwrite the source buffer with zeroes
        Array.Clear(bytes, 0, bytes.Length);

        // Payload must still be intact
        await Assert.That(deserialized.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 7, 8, 9 })).IsTrue();
    }
}
```

- [ ] **Step 2: Run — expect PASS**

- [ ] **Step 3: Commit**

```
git add Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests
git commit -m "test(serialization-systemtextjson): port RpcRequest serialization + add lifetime contract"
```

---

### Task 2.8: `UseJsonRpcSerialization` DI extensions

**Files:**
- Create: `AsbtCore.Broker.Serialization.SystemTextJson/DependencyInjection/JsonRpcServiceCollectionExtensions.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/UseJsonRpcSerializationTests.cs`

> **Open dependency from Phase 3:** `IRpcClientBuilder` and `IRpcServerBuilder` (or the existing `RpcServerBuilder` concrete class — see Chunk 3 Task 3.1) must exist before these extensions can be written. **However**, Agent B writes the extensions against the **expected surface** declared in the spec; if Phase 3 hasn't materialized the builders yet, Agent B uses the concrete `RpcServerBuilder` (already exists) and a `RpcClientBuilder` placeholder shim that Phase 3 will replace.

Concrete plan for adapter B: target `RpcServerBuilder` (exists) and `RpcClientBuilder` (will be created by Agent C). Agent B does **not** declare the client builder type — it just writes extensions; Agent C in Phase 3 adds matching client-builder DI plumbing.

- [ ] **Step 1: Write the failing test**

```csharp
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.SystemTextJson;
using AsbtCore.Broker.Server;          // RpcServerBuilder
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class UseJsonRpcSerializationTests
{
    [Test]
    public async Task ServerBuilder_RegistersJsonRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        builder.UseJsonRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<JsonRpcSerializer>();
    }

    [Test]
    public async Task UseJsonRpcSerialization_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        bool configureCalled = false;
        builder.UseJsonRpcSerialization(o =>
        {
            configureCalled = true;
            o.WriteIndented = true;
        });
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(configureCalled).IsTrue();
        await Assert.That(serializer).IsNotNull();
    }
}
```

- [ ] **Step 2: Run — observe red**

Expected: compile error (`UseJsonRpcSerialization` missing).

- [ ] **Step 3: Implement the extensions**

`AsbtCore.Broker.Serialization.SystemTextJson/DependencyInjection/JsonRpcServiceCollectionExtensions.cs`:

```csharp
using System.Text.Json;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.SystemTextJson;

public static class JsonRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseJsonRpcSerialization(this RpcServerBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer());
        return builder;
    }

    public static RpcServerBuilder UseJsonRpcSerialization(
        this RpcServerBuilder builder,
        Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = RpcJson.Build();
        configure(options);
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new JsonRpcSerializer(options));
        return builder;
    }

    // Client-builder overloads added in Phase 3 by Agent C — same signatures, different builder type.
    // Agent C is responsible for keeping the surfaces symmetric.
}
```

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Commit**

```
git add AsbtCore.Broker.Serialization.SystemTextJson/DependencyInjection Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/UseJsonRpcSerializationTests.cs
git commit -m "feat(serialization-systemtextjson): UseJsonRpcSerialization extensions for RpcServerBuilder"
```

---

### Task 2.9: README

**Files:**
- Create: `AsbtCore.Broker.Serialization.SystemTextJson/README.md`

- [ ] **Step 1: Write package-level README**

Required content:
- One-paragraph summary (JSON adapter for RabbitRpc, primary use cases: debugging, legacy interop, tests).
- Install snippet: `dotnet add package RabbitRpc.Serialization.SystemTextJson`.
- Usage snippet (server + client) showing `.UseJsonRpcSerialization()`.
- Section "Performance note": fragments encoded as base64 (~33% size overhead vs raw bytes); no zero-copy on deserialize. Use `RabbitRpc.Serialization.XPacketRpc` for production throughput.
- Section "Custom options" with the `Action<JsonSerializerOptions>` overload.

Keep ≤ 80 lines; this is the NuGet readme.

- [ ] **Step 2: Commit**

```
git add AsbtCore.Broker.Serialization.SystemTextJson/README.md
git commit -m "docs(serialization-systemtextjson): package README"
```

---

### Phase 2 acceptance — Agent B done when:

1. `dotnet build AsbtCore.Broker.Serialization.SystemTextJson/AsbtCore.Broker.Serialization.SystemTextJson.csproj` green.
2. `dotnet run --project Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj` — all PASS (~15 tests).
3. `JsonRpcSerializer.SerializeFragment`/`DeserializeFragment` covered for: int, string, Guid, DTO record, `Nullable<int>`, null reference, `List<int>`.
4. Lifetime contract test green.
5. `UseJsonRpcSerialization()` and `UseJsonRpcSerialization(configure)` register `IRpcSerializer` singleton on `RpcServerBuilder`.
6. README written; csproj has `PackageReadmeFile`.
7. 4 commits on master.

---

## Chunk 3: Phase 3 — Client/Server Integration (Agent C)

> **Wave 2 — parallel with B and D.** Depends only on Phase 1. Introduces `RpcClientBuilder` (breaking) and rewires `RpcClient` / `RpcRequestDispatcher` to use injected `IRpcSerializer`.

### Files touched in this chunk

| Action | Path |
|---|---|
| Create | `AsbtCore.Broker.Client/RpcClientBuilder.cs` |
| Modify | `AsbtCore.Broker.Client/DependencyInjection.cs` (rename `ClientPackageExtensions` if needed; change `AddRabbitRpcClient` return type) |
| Modify | `AsbtCore.Broker.Client/RpcClient.cs` (ctor + BuildRequest + SendAsync) |
| Modify | `AsbtCore.Broker.Server/RpcRequestDispatcher.cs` (ctor + dispatch) |
| Modify | `AsbtCore.Broker.Server/ServerPackageExtensions.cs` (remove `AddRpcSerialization()`) |
| Modify | `AsbtCore.Broker.Server/RpcServerBuilder.cs` (add interface registration on Register) |
| Modify | `AsbtCore.Broker.Server/RpcServerHostedService.cs` (trigger prewarm on start) |
| Create | `AsbtCore.Broker.Core/Internal/RpcInterfaceRegistration.cs` |
| Create | `AsbtCore.Broker.Core/Internal/RpcSerializerStartupValidator.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Fixtures/TestSerializer.cs` (NEW file — fixture fake) |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientInvokerCacheTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcProxyFactoryTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Client/ClientPackageExtensionsTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcRequestDispatcherTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerBuilderTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerHostedServiceTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerMethodInvokerTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Server/ServerDependencyInjectionTests.cs` |
| Modify | `Tests/AsbtCore.Broker.ClientServer.Tests/Fixtures/TestDispatcherFactory.cs` (pass IRpcSerializer) |

---

### Task 3.1: Introduce `RpcClientBuilder`

**Files:**
- Create: `AsbtCore.Broker.Client/RpcClientBuilder.cs`

This makes `AddRabbitRpcClient` symmetric to `AddRabbitRpcServer`. Breaking — declared in v4.0 migration notes.

- [ ] **Step 1: Write the failing test**

`Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientBuilderTests.cs` (NEW):

```csharp
using AsbtCore.Broker.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

public class RpcClientBuilderTests
{
    [Test]
    public async Task Services_PropertyExposesUnderlyingCollection()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        await Assert.That(builder.Services).IsEqualTo(services);
    }

    [Test]
    public async Task AddProxy_RegistersProxyOnce()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        builder.AddProxy<IDummyContract>();
        var count = services.Count(d => d.ServiceType == typeof(IDummyContract));
        await Assert.That(count).IsEqualTo(1);
    }

    public interface IDummyContract { Task DoAsync(); }
}
```

- [ ] **Step 2: Run — observe red**

Expected: `RpcClientBuilder` missing.

- [ ] **Step 3: Implement**

`AsbtCore.Broker.Client/RpcClientBuilder.cs`:

```csharp
using AsbtCore.Broker.Core.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Client;

public sealed class RpcClientBuilder
{
    public IServiceCollection Services { get; }

    public RpcClientBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public RpcClientBuilder AddProxy<TInterface>() where TInterface : class
    {
        Services.TryAddSingleton<TInterface>(sp =>
            sp.GetRequiredService<RpcProxyFactory>()
              .CreateProxy<TInterface>());

        Services.AddSingleton(new RpcInterfaceRegistration(typeof(TInterface)));
        return this;
    }
}
```

`RpcInterfaceRegistration` doesn't exist yet — created in Task 3.2.

- [ ] **Step 4: Do not commit yet** — depends on 3.2.

---

### Task 3.2: `RpcInterfaceRegistration` marker type

**Files:**
- Create: `AsbtCore.Broker.Core/Internal/RpcInterfaceRegistration.cs`

- [ ] **Step 1: Implement (no test needed — pure data carrier)**

```csharp
namespace AsbtCore.Broker.Core.Internal;

/// <summary>
/// DI marker — collected as <c>IEnumerable&lt;RpcInterfaceRegistration&gt;</c> by both the client
/// (RpcClientBuilder.AddProxy) and the server (RpcServerBuilder.Register) so that the
/// serializer can prewarm types from RPC interface signatures on host startup.
/// </summary>
public sealed class RpcInterfaceRegistration
{
    public RpcInterfaceRegistration(Type interfaceType) => InterfaceType = interfaceType;
    public Type InterfaceType { get; }
}
```

- [ ] **Step 2: Build and run `RpcClientBuilderTests`**

```
dotnet build AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj -- --treenode-filter "/*/*/RpcClientBuilderTests/*"
```
Expected: 2 PASS.

- [ ] **Step 3: Do not commit yet** — DI extension rewrite is next.

---

### Task 3.3: Rewrite `AddRabbitRpcClient` to return `RpcClientBuilder`

**Files:**
- Modify: `AsbtCore.Broker.Client/DependencyInjection.cs`

The current file is named `DependencyInjection.cs` in the existing code but lives in class `ClientPackageExtensions`. Keep both names; only the return type changes.

- [ ] **Step 1: Update the failing test for the new shape**

Edit `Tests/AsbtCore.Broker.ClientServer.Tests/Client/ClientPackageExtensionsTests.cs`. Change assertions on the return type from `IServiceCollection` to `RpcClientBuilder`. The `.AddRpcProxy<T>()` chain must now go through the builder: `AddRabbitRpcClient(cfg).AddProxy<T>()` not `AddRabbitRpcClient(cfg).AddRpcProxy<T>()`. (Renamed: extension on `IServiceCollection` removed, method on builder added in 3.1.)

- [ ] **Step 2: Run — observe red** (extension `.AddRpcProxy<T>()` on IServiceCollection no longer exists once 3.3 lands).

- [ ] **Step 3: Rewrite extensions**

```csharp
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Internal;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public static class ClientPackageExtensions
{
    public static RpcClientBuilder AddRabbitRpcClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RpcOptions>().Bind(configuration.GetSection("RabbitMqRpc"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RpcOptions>()
            .Services.AddSingleton<IValidateOptions<RpcOptions>, RpcSerializerStartupValidator>();

        services.TryAddSingleton<IRpcRouteResolver, DefaultRpcRouteResolver>();
        services.TryAddSingleton<RpcClient>();
        services.TryAddSingleton<RpcProxyFactory>();

        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.TryAddSingleton<IRpcTransport, RabbitMqRpcTransport>();
        services.TryAddSingleton<IRpcTransportHost, RabbitMqRpcTransportHost>();

        return new RpcClientBuilder(services);
    }

    // OBSOLETE: kept temporarily? — NO. Hard remove per v4 breaking change. Users must call `.AddProxy<T>` on the builder.
}
```

> `AddRpcSerialization()` call is **gone**. `RpcSerializerStartupValidator` is added in Task 3.5.

- [ ] **Step 4: Do not commit yet** — `RpcClient` still references the deleted `RpcSerializationHelper`. Move directly to 3.4.

---

### Task 3.4: `RpcClient.cs` — inject `IRpcSerializer`, switch to fragment API

**Files:**
- Modify: `AsbtCore.Broker.Client/RpcClient.cs`

- [ ] **Step 1: Write a failing dispatcher-side test**

Edit `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientTests.cs` (or create new section):

```csharp
[Test]
public async Task BuildRequest_CallsSerializeFragment_OncePerArgument()
{
    var serializer = new Moq.Mock<IRpcSerializer>(Moq.MockBehavior.Strict);
    serializer.SetupGet(s => s.ContentType).Returns("test/raw");
    serializer.Setup(s => s.SerializeFragment(Moq.It.IsAny<object?>(), Moq.It.IsAny<Type>()))
              .Returns<object?, Type>((v, t) => new byte[] { (byte)t.MetadataToken.GetHashCode() }); // dummy unique-ish
    // ... wire transport mock, route resolver, options
    // assert serializer.Verify(SerializeFragment, Times.Exactly(2));
}
```

Detail: see "TestSerializer" fake in Task 3.7. Use the fake there rather than a raw mock if it's already available. The test verifies the **count** of `SerializeFragment` calls — one per parameter — and that `DeserializeFragment` is called once for the response.

- [ ] **Step 2: Run — observe red**

- [ ] **Step 3: Rewrite `RpcClient` constructor and methods**

```csharp
using System.Reflection;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public sealed class RpcClient
{
    private readonly IRpcTransport transport;
    private readonly IRpcRouteResolver routeResolver;
    private readonly IRpcSerializer serializer;
    private readonly RpcOptions options;

    public RpcClient(
        IRpcTransport transport,
        IRpcRouteResolver routeResolver,
        IRpcSerializer serializer,
        IOptions<RpcOptions> options)
    {
        this.transport = transport;
        this.routeResolver = routeResolver;
        this.serializer = serializer;
        this.options = options.Value;
    }

    // InvokeProxy / InvokeVoidAsync / InvokeGenericAsync: unchanged signatures, body unchanged.

    private async Task<T?> SendAsync<T>(
        Type interfaceType,
        MethodInfo method,
        object[] args,
        TimeSpan? timeout,
        bool expectsResult,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(interfaceType, method, args);
        var route = routeResolver.Resolve(interfaceType);

        var response = await transport.SendAsync(
            request,
            route,
            timeout ?? TimeSpan.FromSeconds(options.DefaultTimeoutSeconds),
            cancellationToken);

        if (!response.Success)
        {
            throw new RpcRemoteException(
                response.Error?.Message ?? "Remote call failed.",
                response.Error?.Code,
                response.Error?.ExceptionType,
                response.Error?.Details);
        }

        if (!expectsResult)
            return default;

        if (response.Result is null)
            return default;

        return (T?)serializer.DeserializeFragment(response.Result.Value, typeof(T));
    }

    private RpcRequest BuildRequest(Type interfaceType, MethodInfo method, object[] args)
    {
        var interfaceName = interfaceType.FullName
            ?? throw new InvalidOperationException($"Type {interfaceType} has no FullName.");

        var parameters = method.GetParameters();
        args ??= Array.Empty<object>();

        if (parameters.Length != args.Length)
            throw new InvalidOperationException(
                $"Argument count mismatch for method '{method.Name}'. Expected {parameters.Length}, got {args.Length}.");

        var request = new RpcRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            InterfaceName = interfaceName,
            MethodName = method.Name
        };

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;

            if (parameterType == typeof(CancellationToken))
                throw new NotSupportedException(
                    $"Method '{method.Name}' contains CancellationToken parameter. Use timeout on transport/client level.");

            var typeName = StableTypeName.From(parameterType);

            request.Arguments.Add(new RpcArgument
            {
                TypeName = typeName,
                Payload  = serializer.SerializeFragment(args[i], parameterType)
            });
        }

        return request;
    }
}
```

> Note: `StableTypeName.From` is `internal` to Core. `AsbtCore.Broker.Core.csproj` already has `<InternalsVisibleTo Include="AsbtCore.Broker.Client" />` — confirmed in source. Keep as-is.

- [ ] **Step 4: Run client tests — expect green after `TestSerializer` from Task 3.7 lands**

If `TestSerializer` isn't yet written, you'll see tests still failing for missing fake. Skip them — track in Task 3.7.

- [ ] **Step 5: Do not commit yet** — server still broken.

---

### Task 3.5: `RpcSerializerStartupValidator`

**Files:**
- Create: `AsbtCore.Broker.Core/Internal/RpcSerializerStartupValidator.cs`

- [ ] **Step 1: Write the failing test**

`Tests/AsbtCore.Broker.ClientServer.Tests/Client/ClientPackageExtensionsTests.cs` (append):

```csharp
[Test]
public async Task BuildHost_WithoutSerializer_ThrowsOptionsValidationException()
{
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new[]
    {
        new KeyValuePair<string,string?>("RabbitMqRpc:HostName","x"),
        new KeyValuePair<string,string?>("RabbitMqRpc:Port","5672"),
        new KeyValuePair<string,string?>("RabbitMqRpc:VirtualHost","/"),
        new KeyValuePair<string,string?>("RabbitMqRpc:UserName","u"),
        new KeyValuePair<string,string?>("RabbitMqRpc:Password","p"),
        new KeyValuePair<string,string?>("RabbitMqRpc:ClientProvidedName","cn"),
    }).Build());
    services.AddRabbitRpcClient(services.BuildServiceProvider().GetRequiredService<IConfiguration>());

    using var sp = services.BuildServiceProvider();
    // ValidateOnStart triggers on first IOptions resolution
    var thrown = false;
    try { _ = sp.GetRequiredService<IOptions<RpcOptions>>().Value; }
    catch (OptionsValidationException ex)
    {
        thrown = true;
        await Assert.That(ex.Message).Contains("IRpcSerializer");
    }
    await Assert.That(thrown).IsTrue();
}
```

- [ ] **Step 2: Run — observe red**

- [ ] **Step 3: Implement validator**

```csharp
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Core.Internal;

internal sealed class RpcSerializerStartupValidator : IValidateOptions<RpcOptions>
{
    private readonly IServiceProvider services;

    public RpcSerializerStartupValidator(IServiceProvider services)
    {
        this.services = services;
    }

    public ValidateOptionsResult Validate(string? name, RpcOptions options)
        => services.GetService<IRpcSerializer>() is not null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "No IRpcSerializer is registered. Call .UseXPacketRpcSerialization() " +
                "or .UseJsonRpcSerialization() on the builder, or register a custom IRpcSerializer in DI.");
}
```

- [ ] **Step 4: Wire the validator in both `AddRabbitRpcClient` and `AddRabbitRpcServer`** (already wired in 3.3 for client; mirror in 3.6 for server).

- [ ] **Step 5: Run — expect green**

---

### Task 3.6: `RpcRequestDispatcher.cs` + server DI

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcRequestDispatcher.cs`
- Modify: `AsbtCore.Broker.Server/ServerPackageExtensions.cs`

- [ ] **Step 1: Rewrite `RpcRequestDispatcher`**

```csharp
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Server;

public sealed class RpcRequestDispatcher
{
    private readonly RpcServerRegistry registry;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IRpcSerializer serializer;

    public RpcRequestDispatcher(
        RpcServerRegistry registry,
        IServiceScopeFactory scopeFactory,
        IRpcSerializer serializer)
    {
        this.registry = registry;
        this.scopeFactory = scopeFactory;
        this.serializer = serializer;
    }

    public async Task<RpcResponse> DispatchAsync(RpcRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!registry.TryGet(request.InterfaceName, out var descriptor))
                return CreateError(request.RequestId, "service_not_found",
                    $"Service '{request.InterfaceName}' not found.");

            var parameterTypeNames = request.Arguments.Select(x => x.TypeName).ToArray();

            if (!descriptor.TryGetMethod(request.MethodName, parameterTypeNames, out var entry))
                return CreateError(request.RequestId, "method_not_found",
                    $"Method '{request.MethodName}' with specified signature was not found.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService(descriptor.ImplementationType);

            var args = new object?[request.Arguments.Count];
            for (int i = 0; i < request.Arguments.Count; i++)
            {
                var arg = request.Arguments[i];
                Type type;
                try { type = StableTypeName.Resolve(arg.TypeName); }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "type_not_found",
                        $"Type '{arg.TypeName}' (argument {i}) could not be resolved.", ex);
                }
                try { args[i] = serializer.DeserializeFragment(arg.Payload, type); }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "deserialization_error",
                        $"Failed to deserialize argument {i} of method '{request.MethodName}'.", ex);
                }
            }

            object? result;
            try { result = await entry.Invoker(service, args); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { return CreateError(request.RequestId, "invocation_error", ex.Message, ex); }

            var logicalResultType = entry.LogicalResultType;

            return new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ResultTypeName = logicalResultType is null ? null : StableTypeName.From(logicalResultType),
                Result = logicalResultType is null ? null : serializer.SerializeFragment(result, logicalResultType)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CreateError(request.RequestId, "server_error", ex.Message, ex);
        }
    }

    private static RpcResponse CreateError(string requestId, string code, string message, Exception? exception = null)
        => new()
        {
            RequestId = requestId,
            Success = false,
            Error = new RpcError
            {
                Code = code,
                Message = message,
                Details = exception?.ToString(),
                ExceptionType = exception?.GetType().FullName
            }
        };
}
```

- [ ] **Step 2: Rewrite `ServerPackageExtensions`**

```csharp
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Internal;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.RabbitMq.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Server;

public static class DependencyInjection
{
    public static RpcServerBuilder AddRabbitRpcServer(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<RpcOptions>().Bind(configuration.GetSection("RabbitMqRpc"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RpcOptions>, RpcSerializerStartupValidator>();

        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.TryAddSingleton<IRpcTransport, RabbitMqRpcTransport>();
        services.TryAddSingleton<IRpcTransportHost, RabbitMqRpcTransportHost>();
        services.TryAddSingleton<IRpcRouteResolver, DefaultRpcRouteResolver>();
        services.TryAddSingleton<RpcServerRegistry>();
        services.TryAddSingleton<RpcRequestDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RpcServerHostedService>());

        return new RpcServerBuilder(services);
    }
}
```

- [ ] **Step 3: Build Client + Server + RabbitMq**

```
dotnet build AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj
dotnet build AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj
```
Expected: green.

---

### Task 3.7: Server `Register<TI,TImpl>` records interface for prewarm

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcServerBuilder.cs`
- Modify: `AsbtCore.Broker.Server/RpcServerHostedService.cs`

- [ ] **Step 1: Update `RpcServerBuilder.Register` to record `RpcInterfaceRegistration`**

```csharp
public RpcServerBuilder Register<TInterface, TImplementation>(
    ServiceLifetime lifetime = ServiceLifetime.Scoped,
    string route = null)
    where TInterface : class
    where TImplementation : class, TInterface
{
    Services.TryAdd(new ServiceDescriptor(typeof(TImplementation), typeof(TImplementation), lifetime));
    Services.TryAdd(new ServiceDescriptor(typeof(TInterface), sp => sp.GetRequiredService<TImplementation>(), lifetime));

    Services.AddSingleton(new RpcServerRegistration(
        typeof(TInterface),
        typeof(TImplementation),
        route));

    Services.AddSingleton(new RpcInterfaceRegistration(typeof(TInterface)));   // NEW
    return this;
}
```

- [ ] **Step 2: `RpcServerHostedService` invokes warmup on `StartAsync`**

```csharp
// In RpcServerHostedService.StartAsync, before BasicConsume:
var warmup = sp.GetService<IRpcSerializer>() as IRpcSerializerInterfaceWarmup;
if (warmup is not null)
{
    foreach (var reg in sp.GetServices<RpcInterfaceRegistration>())
        warmup.Prewarm(reg.InterfaceType);
}
```

For the client side, the warmup runs lazily on first proxy creation — `RpcProxyFactory.CreateProxy<T>()` calls warmup too:

```csharp
public T CreateProxy<T>() where T : class
{
    (serializer as IRpcSerializerInterfaceWarmup)?.Prewarm(typeof(T));
    // ... existing CreateProxy logic
}
```

`RpcProxyFactory` now takes `IRpcSerializer` in constructor — update accordingly.

- [ ] **Step 3: Run server tests** — expect green.

---

### Task 3.8: `TestSerializer` fake fixture

**Files:**
- Create: `Tests/AsbtCore.Broker.ClientServer.Tests/Fixtures/TestSerializer.cs`

- [ ] **Step 1: Implement deterministic fake**

```csharp
using System.Text;
using System.Text.Json;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.ClientServer.Tests.Fixtures;

/// <summary>
/// Deterministic test fake — envelope uses JSON for readability; fragments are
/// wire-encoded as <c>"TYPE:VALUE"</c> UTF-8 so tests can assert payload contents
/// without depending on a real binary format.
/// Tracks call counts so tests can verify behavior.
/// </summary>
internal sealed class TestSerializer : IRpcSerializer
{
    public string ContentType => "application/test";

    public int SerializeFragmentCalls;
    public int DeserializeFragmentCalls;
    public List<(object? Value, Type Type)> SerializeFragmentArgs = new();
    public List<(ReadOnlyMemory<byte> Payload, Type Type)> DeserializeFragmentArgs = new();

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => JsonSerializer.Deserialize<T>(payload.Span);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
    {
        Interlocked.Increment(ref SerializeFragmentCalls);
        lock (SerializeFragmentArgs) SerializeFragmentArgs.Add((value, type));
        var text = $"{type.FullName}:{value?.ToString() ?? "null"}";
        return Encoding.UTF8.GetBytes(text);
    }

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
    {
        Interlocked.Increment(ref DeserializeFragmentCalls);
        lock (DeserializeFragmentArgs) DeserializeFragmentArgs.Add((payload, type));
        var text = Encoding.UTF8.GetString(payload.Span);
        var idx = text.IndexOf(':');
        var valueText = idx < 0 ? text : text[(idx + 1)..];
        if (valueText == "null") return null;
        return type == typeof(string) ? valueText : System.Convert.ChangeType(valueText, Nullable.GetUnderlyingType(type) ?? type);
    }
}
```

- [ ] **Step 2: Update `TestDispatcherFactory.cs` to accept `IRpcSerializer`**

Existing helper currently constructs `RpcRequestDispatcher(registry, scopeFactory)`. Update to inject a default `TestSerializer` unless one is provided.

- [ ] **Step 3: Run all ClientServer tests**

```
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj
```
Expected: green for all tests that were updated; tests still using `JsonElement` / `RpcJson` in their bodies need updates per Task 3.9.

---

### Task 3.9: Update existing ClientServer.Tests files

**Files:**
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientInvokerCacheTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcProxyFactoryTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Client/ClientPackageExtensionsTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcRequestDispatcherTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerBuilderTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerHostedServiceTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerMethodInvokerTests.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/ServerDependencyInjectionTests.cs`

For each file:

- [ ] **Step 1: Replace `JsonElement` / `RpcJson` references**

Common changes:
- `JsonSerializer.SerializeToElement(x, RpcJson.Options)` → use `TestSerializer.SerializeFragment(x, typeof(X))` or pre-computed bytes.
- `arg.Payload.GetRawText()` → `Encoding.UTF8.GetString(arg.Payload.Span)`.
- `Response.Result.Value.Deserialize<T>(RpcJson.Options)` → cast via `(T)TestSerializer.DeserializeFragment(response.Result.Value, typeof(T))`.

- [ ] **Step 2: Replace `.AddRpcProxy<T>()` extension on `IServiceCollection`** with `.AddRabbitRpcClient(cfg).AddProxy<T>()` calls.

- [ ] **Step 3: Verify each file compiles individually**

- [ ] **Step 4: Full test run**

```
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj
```
Expected: all 38+ tests PASS (count may grow slightly with new builder tests).

- [ ] **Step 5: Commit (single big-bang commit for Phase 3)**

```
git add -A
git commit -m "refactor(client,server)!: inject IRpcSerializer; introduce RpcClientBuilder; remove default JsonRpcSerializer wiring"
```

---

### Phase 3 acceptance — Agent C done when:

1. `dotnet build AsbtCore.Broker.Client/...` and `AsbtCore.Broker.Server/...` green.
2. `ClientServer.Tests` all PASS.
3. `grep -rn "RpcJson\|RpcSerializationHelper\|JsonRpcSerializer\|AddRpcSerialization" AsbtCore.Broker.Client/ AsbtCore.Broker.Server/` zero matches.
4. `RpcClientBuilder` exists; `AddRabbitRpcClient` returns it; `.AddProxy<T>()` works.
5. `RpcSerializerStartupValidator` triggers `OptionsValidationException` if no serializer registered.
6. `IRpcSerializerInterfaceWarmup.Prewarm(typeof(IFoo))` is called from `RpcServerHostedService.StartAsync` and from `RpcProxyFactory.CreateProxy<T>()` for serializers that implement the interface.

---

## Chunk 4: Phase 4 — XPacketRpc Adapter (Agent D)

> **Wave 2 — parallel with B and C.** Largest implementation chunk. Self-contained.

### Files touched in this chunk

| Action | Path |
|---|---|
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/AsbtCore.Broker.Serialization.XPacketRpc.csproj` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/XPacketRpcSerializer.cs` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/Internal/FragmentInvokerCache.cs` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/Internal/TouchCache.cs` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/Internal/RpcTypeRegistry.cs` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/DependencyInjection/XPacketRpcServiceCollectionExtensions.cs` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/Exceptions/RpcSerializationException.cs` |
| Create | `AsbtCore.Broker.Serialization.XPacketRpc/README.md` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests.csproj` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/XPacketRpcSerializerTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/FragmentInvokerCacheTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/RpcTypeRegistryTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/LifetimeContractTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/UseXPacketRpcSerializationTests.cs` |
| Create | `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/Fixtures/Dtos.cs` |
| Modify | `RabbitMq.RPC.sln` (2 new projects) |

---

### Task 4.1: Scaffold adapter project + add to solution

Same as Task 2.1, but for XPacketRpc:

- [ ] **Step 1: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <PackageId>RabbitRpc.Serialization.XPacketRpc</PackageId>
    <Title>RabbitRpc.Serialization.XPacketRpc</Title>
    <Description>XPacketRpc binary adapter for RabbitRpc — high-throughput serializer with source-generated codecs and zero-copy fragments.</Description>
    <PackageTags>rabbitmq;rpc;dotnet;binary;serialization;xpacketrpc</PackageTags>

    <Version>1.0.0</Version>
    <Authors>AsbtCore</Authors>
    <Company>AsbtCore</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/Sardor557/AsbtCore.Broker</PackageProjectUrl>
    <RepositoryUrl>https://github.com/Sardor557/AsbtCore.Broker</RepositoryUrl>
    <RepositoryType>git</RepositoryType>

    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="XPacketRpc" Version="7.*" />
    <PackageReference Include="XPacketRpc.Generators" Version="7.*">
      <PrivateAssets>analyzers;build</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AsbtCore.Broker.Core\AsbtCore.Broker.Core.csproj" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="AsbtCore.Broker.Serialization.XPacketRpc.Tests" />
  </ItemGroup>
</Project>
```

> Verify the actual published version range for `XPacketRpc` and `XPacketRpc.Generators` packages — if no public NuGet exists yet, the adapter will need to consume the project as a local `<ProjectReference>` until package is published. Adjust accordingly.

- [ ] **Step 2: Add to sln** (same as 2.1).

---

### Task 4.2: `TouchCache` — TDD

**Files:**
- Create: `AsbtCore.Broker.Serialization.XPacketRpc/Internal/TouchCache.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/Fixtures/Dtos.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/RpcTypeRegistryTests.cs` (will extend later)

- [ ] **Step 1: Test DTOs**

```csharp
namespace AsbtCore.Broker.Serialization.XPacketRpc.Tests.Fixtures;

public sealed record UserDto(Guid Id, string Name);
public sealed record OrderDto(Guid Id, UserDto Owner, List<int> Items);
public sealed record SelfRef(int Value, SelfRef? Next);
```

- [ ] **Step 2: Failing test for `TouchCache`**

```csharp
[Test]
public async Task TouchCache_ReturnsCachedDelegate_OnSecondCall()
{
    var d1 = TouchCache.Get(typeof(UserDto));
    var d2 = TouchCache.Get(typeof(UserDto));
    await Assert.That(ReferenceEquals(d1, d2)).IsTrue();
}
```

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Concurrent;
using System.Reflection;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Internal;

internal static class TouchCache
{
    private static readonly ConcurrentDictionary<Type, Action> cache = new();
    private static readonly MethodInfo touchGeneric = typeof(XPRpc)
        .GetMethod(nameof(XPRpc.Touch), BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("XPRpc.Touch<T>() not found — XPacketRpc package incompatible.");

    public static Action Get(Type type) => cache.GetOrAdd(type, BuildTouch);

    private static Action BuildTouch(Type type)
    {
        var closed = touchGeneric.MakeGenericMethod(type);
        return closed.CreateDelegate<Action>();
    }
}
```

- [ ] **Step 4: Run test — expect PASS**

---

### Task 4.3: `RpcTypeRegistry` — TDD

**Files:**
- Create: `AsbtCore.Broker.Serialization.XPacketRpc/Internal/RpcTypeRegistry.cs`
- Modify: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/RpcTypeRegistryTests.cs`

- [ ] **Step 1: Tests**

```csharp
[Test]
public async Task EnsureRegistered_RegistersOnce()
{
    var t = typeof(UserDto);
    RpcTypeRegistry.EnsureRegistered(t);
    var before = TouchCache.CountOf(t); // expose for tests via InternalsVisibleTo
    RpcTypeRegistry.EnsureRegistered(t);
    var after = TouchCache.CountOf(t);
    await Assert.That(after).IsEqualTo(before);
}

[Test]
public async Task EnsureRegistered_RecursesIntoProperties()
{
    RpcTypeRegistry.EnsureRegistered(typeof(OrderDto));
    // OrderDto.Owner is UserDto — should be registered as a side effect
    await Assert.That(RpcTypeRegistry.IsRegistered(typeof(UserDto))).IsTrue();
    await Assert.That(RpcTypeRegistry.IsRegistered(typeof(List<int>))).IsTrue();
}

[Test]
public async Task EnsureRegistered_HandlesSelfReference_WithoutStackOverflow()
{
    RpcTypeRegistry.EnsureRegistered(typeof(SelfRef));
    await Assert.That(RpcTypeRegistry.IsRegistered(typeof(SelfRef))).IsTrue();
}

[Test]
public async Task RegisterInterfaceSignatures_CoversAllParameterAndReturnTypes()
{
    RpcTypeRegistry.RegisterInterfaceSignatures(typeof(IMyService));
    await Assert.That(RpcTypeRegistry.IsRegistered(typeof(UserDto))).IsTrue();
    await Assert.That(RpcTypeRegistry.IsRegistered(typeof(int))).IsTrue();
}

public interface IMyService
{
    Task<UserDto> GetUserAsync(Guid id);
    Task<int> AddAsync(int a, int b);
}
```

- [ ] **Step 2: Implement**

```csharp
using System.Collections.Concurrent;
using System.Reflection;

namespace AsbtCore.Broker.Serialization.XPacketRpc.Internal;

internal static class RpcTypeRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> seen = new();

    public static bool IsRegistered(Type type) => seen.ContainsKey(type);

    public static void EnsureRegistered(Type type)
    {
        if (!seen.TryAdd(type, 0)) return;
        if (IsPrimitiveOrBuiltin(type)) return;

        TouchCache.Get(type).Invoke();

        foreach (var nested in EnumerateNested(type))
            EnsureRegistered(nested);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            EnsureRegistered(prop.PropertyType);
    }

    public static void RegisterInterfaceSignatures(Type interfaceType)
    {
        foreach (var method in interfaceType.GetMethods())
        {
            foreach (var param in method.GetParameters())
                EnsureRegistered(param.ParameterType);

            var unwrapped = UnwrapTask(method.ReturnType);
            if (unwrapped is not null) EnsureRegistered(unwrapped);
        }
    }

    private static bool IsPrimitiveOrBuiltin(Type t)
        => t.IsPrimitive || t == typeof(string) || t.IsEnum || t == typeof(decimal)
        || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
        || t == typeof(TimeSpan);

    private static IEnumerable<Type> EnumerateNested(Type t)
    {
        if (t.IsArray) { yield return t.GetElementType()!; yield break; }
        if (Nullable.GetUnderlyingType(t) is { } u) { yield return u; yield break; }
        if (t.IsGenericType)
            foreach (var ga in t.GetGenericArguments()) yield return ga;
    }

    private static Type? UnwrapTask(Type t)
    {
        if (t == typeof(Task) || t == typeof(ValueTask)) return null;
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Task<>) || def == typeof(ValueTask<>))
                return t.GetGenericArguments()[0];
        }
        return t;
    }
}
```

- [ ] **Step 3: Run tests** — expect PASS.

---

### Task 4.4: `FragmentInvokerCache` — TDD with concurrency

**Files:**
- Create: `AsbtCore.Broker.Serialization.XPacketRpc/Internal/FragmentInvokerCache.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/FragmentInvokerCacheTests.cs`

- [ ] **Step 1: Tests**

```csharp
[Test]
public async Task GetWriter_ReturnsSameInstance_OnSubsequentCalls()
{
    var w1 = FragmentInvokerCache.GetWriter(typeof(int));
    var w2 = FragmentInvokerCache.GetWriter(typeof(int));
    await Assert.That(ReferenceEquals(w1, w2)).IsTrue();
}

[Test]
public async Task GetWriter_Concurrent_BuildsOnce()
{
    // Use a fresh test-only type to avoid cache contamination from other tests.
    var t = new { x = 1 }.GetType(); // anonymous type — unique per test
    Func<object?, ReadOnlyMemory<byte>>[] results = new Func<object?, ReadOnlyMemory<byte>>[100];
    Parallel.For(0, 100, i => results[i] = FragmentInvokerCache.GetWriter(t));
    for (int i = 1; i < 100; i++)
        await Assert.That(ReferenceEquals(results[0], results[i])).IsTrue();
}

[Test]
public async Task Writer_Roundtrips_Int_Through_XPacketRpc()
{
    var writer = FragmentInvokerCache.GetWriter(typeof(int));
    var reader = FragmentInvokerCache.GetReader(typeof(int));
    var bytes = writer(42);
    var value = (int)reader(bytes)!;
    await Assert.That(value).IsEqualTo(42);
}
```

- [ ] **Step 2: Implement** — see spec §5 `FragmentInvokerCache`. Verbatim from spec.

- [ ] **Step 3: Run** — expect PASS.

---

### Task 4.5: `XPacketRpcSerializer` + `RpcSerializationException`

**Files:**
- Create: `AsbtCore.Broker.Serialization.XPacketRpc/Exceptions/RpcSerializationException.cs`
- Create: `AsbtCore.Broker.Serialization.XPacketRpc/XPacketRpcSerializer.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/XPacketRpcSerializerTests.cs`

- [ ] **Step 1: Exception type (no test — pure data)**

```csharp
namespace AsbtCore.Broker.Serialization.XPacketRpc;

public sealed class RpcSerializationException : Exception
{
    public RpcSerializationException(string message, Exception? inner = null) : base(message, inner) { }
}
```

- [ ] **Step 2: Failing tests for envelope + fragment + warmup + content type**

```csharp
[Test]
public async Task ContentType_IsXPacketRpc()
{
    await Assert.That(new XPacketRpcSerializer().ContentType).IsEqualTo("application/x-xpacket-rpc");
}

[Test]
public async Task RpcRequest_Envelope_Roundtrips()
{
    var sut = new XPacketRpcSerializer();
    var original = new RpcRequest
    {
        RequestId = "rid",
        InterfaceName = "I",
        MethodName = "M",
        Arguments = { new RpcArgument { TypeName = "T", Payload = new byte[] { 1,2,3 } } }
    };
    var bytes = sut.Serialize(original);
    var roundtrip = sut.Deserialize<RpcRequest>(bytes)!;
    await Assert.That(roundtrip.RequestId).IsEqualTo("rid");
    await Assert.That(roundtrip.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 1,2,3 })).IsTrue();
}

[Test]
public async Task Fragment_Roundtrips_Dto()
{
    var sut = new XPacketRpcSerializer();
    var user = new UserDto(Guid.NewGuid(), "Alice");
    var bytes = sut.SerializeFragment(user, typeof(UserDto));
    var back = (UserDto)sut.DeserializeFragment(bytes, typeof(UserDto))!;
    await Assert.That(back).IsEqualTo(user);
}

[Test]
public async Task Prewarm_RegistersAllSignatureTypes()
{
    var sut = new XPacketRpcSerializer();
    sut.Prewarm(typeof(IMyService));
    await Assert.That(RpcTypeRegistry.IsRegistered(typeof(UserDto))).IsTrue();
}
```

- [ ] **Step 3: Implement `XPacketRpcSerializer`** — verbatim from spec §5.

- [ ] **Step 4: Run tests** — expect PASS.

---

### Task 4.6: Lifetime contract test

**Files:**
- Create: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/LifetimeContractTests.cs`

Implementation must ensure that deserialized `RpcRequest.Arguments[i].Payload` survives source-buffer overwrite. XPacketRpc generated readers typically copy length-prefixed bytes into new arrays for `byte[]` / `ReadOnlyMemory<byte>` properties, so the test should pass naturally. If it fails — the adapter must add an explicit copy step in `Deserialize<RpcRequest>` (clone all fragment payloads).

- [ ] **Step 1: Write the test (identical structure to Phase 2 Task 2.7)**

- [ ] **Step 2: If red — add fragment-copy step inside `XPacketRpcSerializer.Deserialize<T>` (only when `T == RpcRequest` or `T == RpcResponse`):**

```csharp
public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
{
    RpcTypeRegistry.EnsureRegistered(typeof(T));
    var result = XPRpc.Read<T>(payload.Span);

    // Contract: fragment payloads must survive source-buffer reuse.
    if (result is RpcRequest req)
        for (int i = 0; i < req.Arguments.Count; i++)
            req.Arguments[i].Payload = req.Arguments[i].Payload.ToArray();
    else if (result is RpcResponse resp && resp.Result.HasValue)
        resp.Result = resp.Result.Value.ToArray();

    return result;
}
```

(If the XPacketRpc-generated reader already copies, this fallback is a no-cost defensive measure. Only the lifetime test reveals which path is needed.)

- [ ] **Step 3: Re-run — expect PASS.**

---

### Task 4.7: `UseXPacketRpcSerialization` extensions

**Files:**
- Create: `AsbtCore.Broker.Serialization.XPacketRpc/DependencyInjection/XPacketRpcServiceCollectionExtensions.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/UseXPacketRpcSerializationTests.cs`

- [ ] **Step 1: Tests (mirror Phase 2 Task 2.8)**

- [ ] **Step 2: Implement**

```csharp
using AsbtCore.Broker.Client;        // RpcClientBuilder (created in Phase 3)
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;        // RpcServerBuilder
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.XPacketRpc;

public static class XPacketRpcServiceCollectionExtensions
{
    public static RpcClientBuilder UseXPacketRpcSerialization(this RpcClientBuilder b)
    {
        b.Services.TryAddSingleton<IRpcSerializer, XPacketRpcSerializer>();
        return b;
    }

    public static RpcServerBuilder UseXPacketRpcSerialization(this RpcServerBuilder b)
    {
        b.Services.TryAddSingleton<IRpcSerializer, XPacketRpcSerializer>();
        return b;
    }
}
```

> Same dependency caveat as Phase 2 Task 2.8: `RpcClientBuilder` must exist (created by Agent C in Task 3.1). If Agent C is still in flight, Agent D defers writing `UseXPacketRpcSerialization(RpcClientBuilder)` until C completes — the server overload is independent and can land first.

- [ ] **Step 3: Run tests** — expect PASS.

---

### Task 4.8: README + commit

- [ ] **Step 1: Write README** (≤ 100 lines), structurally identical to Phase 2 Task 2.9 — emphasize zero-copy on deserialize, source-generator requirement (DTO project must reference `XPacketRpc.Generators`), no polymorphism caveat.

- [ ] **Step 2: Commit Phase 4 in one go**

```
git add AsbtCore.Broker.Serialization.XPacketRpc Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests RabbitMq.RPC.sln
git commit -m "feat(serialization-xpacketrpc): adapter package with FragmentInvokerCache, RpcTypeRegistry, IRpcSerializerInterfaceWarmup"
```

---

### Phase 4 acceptance — Agent D done when:

1. `dotnet build AsbtCore.Broker.Serialization.XPacketRpc/...` green.
2. `dotnet run --project Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/...` all PASS.
3. Concurrency test for `FragmentInvokerCache` proves single-build per type under 100-thread load.
4. Lifetime contract test green.
5. `Prewarm(typeof(IFoo))` registers all signature types.

---

## Chunk 5: Phase 5 — Docs, Demo Apps, Version Bump (Agent E)

> **Wave 3 — parallel with F.** Requires B, C, D merged.

### Tasks

- [ ] **5.1: Confirm `RabbitMq.RPC.sln` contains 4 new project entries** (added incrementally by B & D; verify here).
- [ ] **5.2: Update `coverage.runsettings`** — add 2 `<ModulePath>` entries for new adapter DLLs.
  ```xml
  <ModulePath>.*AsbtCore\.Broker\.Serialization\.SystemTextJson\.dll$</ModulePath>
  <ModulePath>.*AsbtCore\.Broker\.Serialization\.XPacketRpc\.dll$</ModulePath>
  ```
- [ ] **5.3: Migrate `Test.Broker.API` and `Test.Client` to XPacketRpc adapter.**
  - In `Test.Broker.API/Program.cs`: add `<PackageReference Include="RabbitRpc.Serialization.XPacketRpc" />` (or `<ProjectReference>` for in-repo demo), call `.UseXPacketRpcSerialization()` after `AddRabbitRpcServer(...)`.
  - In `Test.Client/Program.cs`: same.
  - DTOs in `Test.Contracts/Contracts.csproj`: add `<PackageReference Include="XPacketRpc.Generators" />` so source generator visits them.
- [ ] **5.4: Update `README.md`.** Add "Migration v3.1 → v4.0" section after the existing "Migration v3.0 → v3.1" section. Content mirrors spec §7. Update the package structure mermaid diagram to add `Serialization.SystemTextJson` and `Serialization.XPacketRpc` as side nodes. Update Quick Start to include `.UseXPacketRpcSerialization()`.
- [ ] **5.5: Update `README.ru.md`.** Translate the migration section; keep diagrams in sync.
- [ ] **5.6: Bump versions in csproj files.**
  - `AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj` → `<Version>4.0.0</Version>` + `<PackageReleaseNotes>v4.0.0 — Binary serialization. Breaking change.</PackageReleaseNotes>` (link to migration section).
  - `AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj` → same.
- [ ] **5.7: Commit.** `git commit -m "docs(release)!: v4.0 binary serialization migration notes, demo apps on XPacketRpc, version bump"`

---

## Chunk 6: Phase 6 — Benchmarks (Agent F)

> **Wave 3 — parallel with E.** Requires B, C, D merged. Updates existing benches + adds comparison.

### Tasks

- [ ] **6.1: Update `Benchmarks/AsbtCore.Broker.Benchmarks/InMemoryTransport.cs`** to accept `IRpcSerializer` and route through fragment API.
- [ ] **6.2: Update `BenchmarkClientFactory.cs`, `RpcRoundTripBench.cs`, `RpcClientInvokerBench.cs`, `RpcServerInvokerBench.cs`, `JsonElementCreationBench.cs`, `PublishConcurrencyBench.cs`, `TypeResolutionBench.cs`** so they compile against the new contract. `JsonElementCreationBench` likely becomes meaningless — delete or repurpose as `FragmentCreationBench`.
- [ ] **6.3: Create `SerializerComparisonBench.cs`** running envelope + fragment size & throughput for `JsonRpcSerializer` vs `XPacketRpcSerializer` on a representative DTO graph (e.g., `OrderDto` with 10 line items). BenchmarkDotNet `[Params]` over both adapters.
- [ ] **6.4: Run baselines:** `dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter "*SerializerComparison*"`. Capture results to `Benchmarks/results/v4.0-baseline.md` for posterity (gitignored otherwise — `git add -f`).
- [ ] **6.5: Commit.** `git commit -m "bench: update existing benches to new IRpcSerializer; add SerializerComparisonBench"`

---

## Chunk 7: Phase 7 — Integration Verification (Agent G)

> **Wave 4 — final, sequential.** Runs after E and F complete.

### Tasks

- [ ] **7.1: Full solution build.**
  ```
  dotnet build RabbitMq.RPC.sln -c Release
  ```
  Expected: zero errors, zero warnings (or document acceptable warning baseline).
- [ ] **7.2: Run every unit-test project sequentially.**
  ```
  dotnet run -c Release --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
  dotnet run -c Release --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj
  dotnet run -c Release --project Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests/AsbtCore.Broker.Serialization.SystemTextJson.Tests.csproj
  dotnet run -c Release --project Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests/AsbtCore.Broker.Serialization.XPacketRpc.Tests.csproj
  ```
  Expected: all PASS.
- [ ] **7.3: Coverage with runsettings.**
  ```
  dotnet test RabbitMq.RPC.sln --settings coverage.runsettings -c Release
  ```
  Expected: coverage of Core ≥ 100%, Client ≥ 97%, Server ≥ 95%, adapters ≥ 90%. RabbitMq transport stays around 40% (no live broker).
- [ ] **7.4: Smoke benchmark.** `dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter "*RpcRoundTrip*" --job short` — ensure it completes without exceptions.
- [ ] **7.5: Migration scenario manual check.**
  - Spin up RabbitMQ container (`docker run -d --rm -p 5672:5672 rabbitmq:3.13-alpine`).
  - `dotnet run --project Test.Broker.API` (server).
  - `dotnet run --project Test.Client` (client) — verify a few RPC calls succeed end-to-end via XPacketRpc.
- [ ] **7.6: Final commit (if any verification artifacts were checked in).** No commit if pure verification.
- [ ] **7.7: Cut tag `v4.0.0` on master.** Not done by Agent G — surface to human reviewer for explicit confirmation.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-13-binary-serialization.md`.

This is a parallel multi-agent execution plan. The recommended dispatch order:

**Wave 1 (sequential, blocks everything):**
- Agent A (Phase 1) — Core contracts refactor.

**Wave 2 (parallel, after A merges):**
- Agent B (Phase 2) — SystemTextJson adapter.
- Agent C (Phase 3) — Client/Server integration.
- Agent D (Phase 4) — XPacketRpc adapter.

**Wave 3 (parallel, after B + C + D merge):**
- Agent E (Phase 5) — docs, demo apps, version bump.
- Agent F (Phase 6) — benchmarks.

**Wave 4 (sequential, final):**
- Agent G (Phase 7) — integration verification + smoke.

Use `superpowers:subagent-driven-development` to dispatch each agent on a fresh worktree with this plan as input. Each agent runs in isolation and submits a PR / merge-back when its acceptance criteria are green.

For solo execution (no subagents): use `superpowers:executing-plans` and proceed phase-by-phase in the current session.

---

## Status

- [x] Plan written and saved.
- [ ] Plan reviewed by user.
- [ ] Wave 1 (Agent A) dispatched.
- [ ] Wave 2 dispatched.
- [ ] Wave 3 dispatched.
- [ ] Wave 4 dispatched.
- [ ] v4.0.0 tagged.
