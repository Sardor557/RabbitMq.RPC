# RPC Reliability v3.0.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `AsbtCore.Broker` v3.0.0 with reply-queue reconnect resilience, publisher confirms, per-route DLQ, and stable wire-level type identity.

**Architecture:** Self-contained changes inside `AsbtCore.Broker.RabbitMq` (transport reliability) and `AsbtCore.Broker.Core` (`StableTypeName` replacing `TypeNameCache`, plus public exception types). `RpcClient`, `RpcServerRegistry`, `RpcRequestDispatcher` are re-wired to use `StableTypeName`. No new public abstractions on transport interfaces.

**Tech Stack:** .NET 10, C# 13, RabbitMQ.Client v7, System.Text.Json, MSTest. Wire payload serialized as JSON (`RpcRequest`/`RpcResponse`).

**Spec:** [docs/superpowers/specs/2026-05-07-rpc-reliability-v3-design.md](../specs/2026-05-07-rpc-reliability-v3-design.md)

**Per the user's explicit decision recorded in the spec (§12):**
- **No new tests are written. No existing tests are updated.**
- **Existing tests that break because of the v3 wire-format change are deleted** (not commented, not stubbed).
- **Benchmarks are not re-run.**

Verification therefore relies on `dotnet build` (compile-time correctness) and a manual smoke run against a local RabbitMQ. This is unusual; documented here so the executor doesn't add tests "to be safe" — the user owns this decision.

---

## Branch and Setup

### Task 0: Create feature branch

**Files:** none (git only)

- [ ] **Step 1: Create and switch to v3 feature branch**

```bash
git checkout -b feature/rpc-reliability-v3
```

Expected: `Switched to a new branch 'feature/rpc-reliability-v3'`

- [ ] **Step 2: Verify clean tree**

```bash
git status --short
```

Expected: empty (clean working tree).

---

## Phase 1: Core foundation (StableTypeName + public exceptions)

These tasks introduce the new wire-format type-name machinery and the new public exception types. All v3 wire and behavior changes depend on them.

### Task 1: Public exception types

**Files:**
- Create: `AsbtCore.Broker.Core/Exceptions/RpcExceptions.cs`

If the file already exists with other exception types, add the two new types to it without disturbing existing types. (Verify by listing the directory first.)

- [ ] **Step 1: Inspect existing exceptions directory**

```bash
ls AsbtCore.Broker.Core/Exceptions/ 2>/dev/null || echo "no Exceptions/ folder"
```

Expected: either an existing folder listing, or "no Exceptions/ folder".

- [ ] **Step 2: Create exception types file**

If the directory doesn't exist, create it. Then create the file with this exact content:

`AsbtCore.Broker.Core/Exceptions/RpcExceptions.cs`:
```csharp
namespace AsbtCore.Broker.Core.Exceptions;

/// <summary>
/// Thrown into a pending RPC <see cref="System.Threading.Tasks.TaskCompletionSource{TResult}"/>
/// when the underlying transport reconnects to the broker. The request may or may not have
/// reached the broker; caller decides whether to retry.
/// </summary>
public sealed class TransportReconnectedException : Exception
{
    public string RequestId { get; }

    public TransportReconnectedException(string requestId)
        : base($"RPC request '{requestId}' aborted: transport reconnected.")
    {
        RequestId = requestId;
    }
}

/// <summary>
/// Thrown when the broker rejected (nack/return) the publish of an RPC request.
/// Surfaces immediately, without waiting for the configured RPC timeout.
/// </summary>
public sealed class RpcPublishFailedException : Exception
{
    public string RequestId { get; }
    public string Reason { get; }

    public RpcPublishFailedException(string requestId, string reason, Exception? inner = null)
        : base($"RPC publish failed for '{requestId}': {reason}.", inner)
    {
        RequestId = requestId;
        Reason = reason;
    }
}
```

- [ ] **Step 3: Build to confirm compilation**

```bash
dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Core/Exceptions/RpcExceptions.cs
git commit -m "feat(core): add TransportReconnectedException and RpcPublishFailedException"
```

---

### Task 2: StableTypeName component

**Files:**
- Create: `AsbtCore.Broker.Core/Serialization/StableTypeName.cs`

This replaces the existing `TypeNameCache` (deletion is in Task 6, after all consumers are switched over).

- [ ] **Step 1: Confirm current TypeNameCache location**

```bash
ls AsbtCore.Broker.Core/Serialization/
```

Expected: list includes `TypeNameCache.cs` and `RpcSerializationHelper.cs` and `RpcJson.cs`.

- [ ] **Step 2: Create StableTypeName.cs**

`AsbtCore.Broker.Core/Serialization/StableTypeName.cs`:
```csharp
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace AsbtCore.Broker.Core.Serialization;

/// <summary>
/// Builds and resolves wire-stable type identifiers of the form
/// <c>Namespace.Type, AssemblySimpleName</c> (no <c>Version</c>/<c>Culture</c>/<c>PublicKeyToken</c>).
/// Generic instances are encoded as
/// <c>Namespace.OpenType`N[[arg1],[arg2]], AssemblySimpleName</c>.
/// Replaces the v2 <c>TypeNameCache</c>.
/// </summary>
internal static class StableTypeName
{
    private static readonly ConcurrentDictionary<Type, string> writeCache = new();
    private static readonly ConcurrentDictionary<string, Type> readCache  = new(StringComparer.Ordinal);

    internal static string From(Type type) => writeCache.GetOrAdd(type, static t => Build(t));

    internal static Type Resolve(string name) =>
        readCache.GetOrAdd(name, static n =>
            Type.GetType(
                typeName:         n,
                assemblyResolver: ResolveAssembly,
                typeResolver:     ResolveType,
                throwOnError:     true)!);

    private static string Build(Type t)
    {
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            var def  = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            var sb   = new StringBuilder(def.FullName!).Append('[');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(Build(args[i])).Append(']');
            }
            return sb.Append("], ").Append(def.Assembly.GetName().Name).ToString();
        }
        return $"{t.FullName}, {t.Assembly.GetName().Name}";
    }

    private static Assembly? ResolveAssembly(AssemblyName name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.Ordinal))
        ?? Assembly.Load(name.Name!);

    private static Type? ResolveType(Assembly? assembly, string typeName, bool ignoreCase) =>
        assembly?.GetType(typeName, throwOnError: false, ignoreCase) ?? Type.GetType(typeName);
}
```

- [ ] **Step 3: Build to confirm compilation**

```bash
dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Core/Serialization/StableTypeName.cs
git commit -m "feat(core): add StableTypeName for wire-stable type identity"
```

---

### Task 3: Wire StableTypeName into RpcClient.BuildRequest

**Files:**
- Modify: `AsbtCore.Broker.Client/RpcClient.cs` (the `BuildRequest` method, around line 105–110)

- [ ] **Step 1: Open the file and locate the call site**

Open `AsbtCore.Broker.Client/RpcClient.cs`. Find the loop in `BuildRequest` (around line 105–115):

```csharp
var typeName = parameterType.AssemblyQualifiedName
    ?? parameterType.FullName
    ?? throw new InvalidOperationException(...);
```

- [ ] **Step 2: Add the using and replace the type-name expression**

Add at the top of the file (next to the existing `using AsbtCore.Broker.Core...` lines):

```csharp
using AsbtCore.Broker.Core.Serialization;
```

Replace the `var typeName = parameterType.AssemblyQualifiedName ?? parameterType.FullName ?? ...;` line with:

```csharp
var typeName = StableTypeName.From(parameterType);
```

- [ ] **Step 3: Build the client project**

```bash
dotnet build AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors. (Server/Tests projects may still fail at this point — that is OK; they're updated in later tasks.)

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Client/RpcClient.cs
git commit -m "refactor(client): use StableTypeName for parameter type names"
```

---

### Task 4: Wire StableTypeName into RpcServerRegistry

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcServerRegistry.cs` (around line 50–60, the `Select` projection that builds parameter type names)

- [ ] **Step 1: Open the file and locate the projection**

Open `AsbtCore.Broker.Server/RpcServerRegistry.cs`. Find the projection (around line 53–60):

```csharp
.Select(p => p.ParameterType.AssemblyQualifiedName
             ?? p.ParameterType.FullName
             ?? throw new InvalidOperationException(...))
```

- [ ] **Step 2: Add the using and replace the projection**

Add at the top of the file:

```csharp
using AsbtCore.Broker.Core.Serialization;
```

Replace the projection body with:

```csharp
.Select(p => StableTypeName.From(p.ParameterType))
```

- [ ] **Step 3: Build the server project**

```bash
dotnet build AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors. (Tests project may still fail at this point — that is OK; the broken tests are deleted in Task 7.)

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Server/RpcServerRegistry.cs
git commit -m "refactor(server): use StableTypeName for method registry keys"
```

---

### Task 5: Wire StableTypeName into RpcRequestDispatcher

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcRequestDispatcher.cs` — replace `TypeNameCache.Resolve` calls with `StableTypeName.Resolve`; replace the `ResultTypeName` write to use `StableTypeName.From`.

- [ ] **Step 1: Open the file and locate the two call sites**

Open `AsbtCore.Broker.Server/RpcRequestDispatcher.cs`. The two call sites are:

1. The inner loop deserializing arguments (around line ~50–65), currently calling `TypeNameCache.Resolve(typeName)`.
2. The `ResultTypeName` assignment when building the success response (around line 81), currently:
   ```csharp
   ResultTypeName = logicalResultType?.AssemblyQualifiedName ?? logicalResultType?.FullName,
   ```

- [ ] **Step 2: Replace `TypeNameCache.Resolve` with `StableTypeName.Resolve`**

For each `TypeNameCache.Resolve(...)` call inside the dispatcher, replace with `StableTypeName.Resolve(...)`. Keep the same argument expression. (Both methods have the identical signature `(string) → Type`.)

- [ ] **Step 3: Replace ResultTypeName**

Replace:
```csharp
ResultTypeName = logicalResultType?.AssemblyQualifiedName ?? logicalResultType?.FullName,
```
with:
```csharp
ResultTypeName = logicalResultType is null ? null : StableTypeName.From(logicalResultType),
```

- [ ] **Step 4: Build the server project**

```bash
dotnet build AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj -c Release
```

Expected: `Build succeeded.` with 0 errors. The line still using `TypeNameCache.Resolve` elsewhere in the codebase, if any, will be caught by the full-solution build in Task 7.

- [ ] **Step 5: Commit**

```bash
git add AsbtCore.Broker.Server/RpcRequestDispatcher.cs
git commit -m "refactor(server): use StableTypeName in dispatcher resolution and ResultTypeName"
```

---

### Task 6: Delete TypeNameCache

**Files:**
- Delete: `AsbtCore.Broker.Core/Serialization/TypeNameCache.cs`

- [ ] **Step 1: Confirm there are no remaining references**

```bash
git grep "TypeNameCache" -- ":!docs/" ":!*.md"
```

Expected: empty output. If any matches remain inside source files (excluding docs and markdown), update them to `StableTypeName` before proceeding.

- [ ] **Step 2: Delete the file**

```bash
git rm AsbtCore.Broker.Core/Serialization/TypeNameCache.cs
```

- [ ] **Step 3: Build the Core project**

```bash
dotnet build AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(core): remove TypeNameCache (replaced by StableTypeName)"
```

---

### Task 7: Full-solution build, delete tests pinned to v2 wire format

**Files:**
- Delete: any test files in `AsbtCore.Broker.Tests` whose assertions or fixtures encode the v2 AQN-based wire format (`TypeNameCache` tests, `RpcServerRegistry` tests that hard-code AQN-shaped strings, `RpcRequestDispatcher` tests that build `RpcRequest` payloads with AQN type names).

This is the gate where v2-pinned tests are removed.

- [ ] **Step 1: Full solution build to enumerate failures**

```bash
dotnet build -c Release 2>&1 | tee build.log
```

Expected outcome: `AsbtCore.Broker.Core`, `AsbtCore.Broker.Client`, `AsbtCore.Broker.Server`, `AsbtCore.Broker.RabbitMq` all succeed. `AsbtCore.Broker.Tests` may have errors referring to `TypeNameCache`, AQN-shaped string constants, or removed members — those are the deletion candidates. (The transport classes are not yet modified, so their public surface still compiles.)

- [ ] **Step 2: Identify failing test files**

```bash
grep -E "error " build.log | sed -E 's/^([^(]+\.cs).*$/\1/' | sort -u
```

Expected: a short list of files inside `AsbtCore.Broker.Tests/`. Each is a candidate for deletion.

- [ ] **Step 3: Delete each candidate file**

For each candidate file `<path>` from Step 2:

```bash
git rm <path>
```

Repeat until the list is empty. **Only delete files in the `AsbtCore.Broker.Tests/` directory.** If a build error appears in production code, that is a real bug — fix it instead.

- [ ] **Step 4: Re-run the full-solution build**

```bash
dotnet build -c Release
```

Expected: `Build succeeded.` with 0 errors. Warnings about unused usings or nullable annotations are acceptable.

- [ ] **Step 5: Run the remaining tests**

```bash
dotnet test -c Release --no-build
```

Expected: any remaining tests pass. The total count is lower than before — that's the point.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test: delete unit tests pinned to v2 AQN wire format"
```

---

## Phase 2: Connection topology recovery

### Task 8: Enable topology recovery

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqConnectionProvider.cs:69`

- [ ] **Step 1: Open the file**

Read `AsbtCore.Broker.RabbitMq/Transport/RabbitMqConnectionProvider.cs`. Around line 60–70 you see:

```csharp
var factory = new ConnectionFactory
{
    HostName = vars.HostName,
    ...
    AutomaticRecoveryEnabled = true,
    TopologyRecoveryEnabled = false   // ← line 69
};
```

- [ ] **Step 2: Flip the flag**

Change `TopologyRecoveryEnabled = false` to `TopologyRecoveryEnabled = true`.

- [ ] **Step 3: Build**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqConnectionProvider.cs
git commit -m "fix(transport): enable RabbitMQ TopologyRecoveryEnabled"
```

---

## Phase 3: Reply queue resilience

### Task 9: Reply queue with stable name (non-exclusive)

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs` — `EnsureInitializedAsync`, around lines 109–128 (queue declaration block).

- [ ] **Step 1: Open the file and locate the declaration block**

In `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs`, lines 109–128 currently read:

```csharp
var declareOk = await replyChannel.QueueDeclareAsync(
    queue: string.Empty,
    durable: false,
    exclusive: true,
    autoDelete: true,
    arguments: null,
    passive: false,
    noWait: false,
    cancellationToken: cancellationToken);

replyQueueName = declareOk.QueueName;
```

We also need access to `RpcOptions` for `ClientProvidedName`. Inspect the constructor — currently takes `IRabbitMqConnectionProvider`, `ILogger<RabbitMqRpcTransport>`, `IRpcSerializer`. We must inject `IOptions<RpcOptions>` too.

- [ ] **Step 2: Add `IOptions<RpcOptions>` to the constructor**

Top of the file, ensure these usings exist (add what's missing):

```csharp
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.Options;
```

Add a private field next to the existing fields:

```csharp
private readonly RpcOptions options;
```

Update the constructor signature and body:

```csharp
public RabbitMqRpcTransport(
    IRabbitMqConnectionProvider connectionProvider,
    ILogger<RabbitMqRpcTransport> logger,
    IRpcSerializer serializer,
    IOptions<RpcOptions> options)
{
    this.connectionProvider = connectionProvider;
    this.logger = logger;
    this.serializer = serializer;
    this.options = options.Value;
}
```

- [ ] **Step 3: Replace the declaration block**

Replace the lines from Step 1 with:

```csharp
var queueName = $"rpc-reply-{this.options.ClientProvidedName}-{Guid.NewGuid():N}";
await replyChannel.QueueDeclareAsync(
    queue: queueName,
    durable: false,
    exclusive: false,
    autoDelete: true,
    arguments: null,
    passive: false,
    noWait: false,
    cancellationToken: cancellationToken);

replyQueueName = queueName;
```

- [ ] **Step 4: Build the RabbitMq project**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.` (DI registration in the consuming app may need an updated overload — leave that for the smoke test; the framework itself compiles.)

- [ ] **Step 5: Search for transport DI registration sites and update them**

```bash
git grep -n "new RabbitMqRpcTransport(" -- "*.cs"
```

For each match outside the `AsbtCore.Broker.RabbitMq/` folder (typically a `ServiceCollection` extension or `Test.Client`), pass `IOptions<RpcOptions>` from the DI container, e.g.:

```csharp
new RabbitMqRpcTransport(
    sp.GetRequiredService<IRabbitMqConnectionProvider>(),
    sp.GetRequiredService<ILogger<RabbitMqRpcTransport>>(),
    sp.GetRequiredService<IRpcSerializer>(),
    sp.GetRequiredService<IOptions<RpcOptions>>())
```

If the registration uses constructor injection (e.g., `services.AddSingleton<RabbitMqRpcTransport>()` without a factory), no change is needed — DI resolves the new parameter automatically.

- [ ] **Step 6: Full solution build**

```bash
dotnet build -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(transport): named non-exclusive reply queue (rpc-reply-{client}-{guid})"
```

---

### Task 10: RecoverySucceededAsync hook fails pending TCS

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs` — `EnsureInitializedAsync` (subscribe to recovery event), add `OnRecoverySucceededAsync` method.

- [ ] **Step 1: Add the using for the exception type**

At the top of the file, add:

```csharp
using AsbtCore.Broker.Core.Exceptions;
```

- [ ] **Step 2: Subscribe to the recovery event after queue declaration**

In `EnsureInitializedAsync`, immediately after `replyQueueName = queueName;` (the assignment from Task 9) and before the consumer registration, add:

```csharp
if (replyChannel is IRecoverable recoverable)
    recoverable.RecoverySucceededAsync += OnRecoverySucceededAsync;
```

`IRecoverable` is in the `RabbitMQ.Client` namespace, already imported.

- [ ] **Step 3: Add the handler method**

Add this method to the class (after `OnResponseReceivedAsync` is a natural place):

```csharp
private Task OnRecoverySucceededAsync(object? sender, AsyncEventArgs e)
{
    foreach (var id in pending.Keys.ToArray())
    {
        if (pending.TryRemove(id, out var tcs))
            tcs.TrySetException(new TransportReconnectedException(id));
    }
    logger.LogWarning("RabbitMQ topology recovered. Pending RPC requests aborted.");
    return Task.CompletedTask;
}
```

`AsyncEventArgs` is in the `RabbitMQ.Client.Events` namespace — add `using RabbitMQ.Client.Events;` if not already present.

- [ ] **Step 4: Build**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs
git commit -m "feat(transport): fail pending RPC tasks on broker reconnect"
```

---

## Phase 4: Publisher confirms

### Task 11: Publisher confirms on the client publish channel

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs` — `EnsureInitializedAsync`, the `publishChannel = await connection.CreateChannelAsync(...)` line (~line 106).

- [ ] **Step 1: Replace the publish channel creation**

Find:

```csharp
publishChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
```

Replace with:

```csharp
var publishChannelOptions = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,
    publisherConfirmationTrackingEnabled: true);

publishChannel = await connection.CreateChannelAsync(
    options: publishChannelOptions,
    cancellationToken: cancellationToken);
```

`CreateChannelOptions` is in the `RabbitMQ.Client` namespace, already imported.

- [ ] **Step 2: Build**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs
git commit -m "feat(transport): enable publisher confirms on client publish channel"
```

---

### Task 12: Map publish failure to RpcPublishFailedException

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs` — `SendAsync`, the `try { await publishChannel.BasicPublishAsync(...); ... }` block (~lines 69–86).

- [ ] **Step 1: Locate the publish block**

Currently:

```csharp
try
{
    await publishChannel.BasicPublishAsync(
        exchange: string.Empty,
        routingKey: route,
        mandatory: false,
        basicProperties: properties,
        body: body,
        cancellationToken: linkedCts.Token);

    logger.LogDebug(
        "RPC request published. RequestId: {RequestId}, Route: {Route}, Method: {Method}",
        request.RequestId, route, request.MethodName);

    return await tcs.Task;
}
finally
{
    pending.TryRemove(request.RequestId, out _);
}
```

- [ ] **Step 2: Wrap the publish in its own try/catch and map non-cancellation exceptions**

Replace the block with:

```csharp
try
{
    try
    {
        await publishChannel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: route,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: linkedCts.Token);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        pending.TryRemove(request.RequestId, out _);
        throw new RpcPublishFailedException(request.RequestId, ex.GetType().Name, ex);
    }

    logger.LogDebug(
        "RPC request published. RequestId: {RequestId}, Route: {Route}, Method: {Method}",
        request.RequestId, route, request.MethodName);

    return await tcs.Task;
}
finally
{
    pending.TryRemove(request.RequestId, out _);
}
```

- [ ] **Step 3: Add the using for the exception type (if not already added in Task 10)**

```csharp
using AsbtCore.Broker.Core.Exceptions;
```

- [ ] **Step 4: Build**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs
git commit -m "feat(transport): map broker publish failure to RpcPublishFailedException"
```

---

### Task 13: Publisher confirms on each server consumer channel

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs` — `StartAsync`, the per-route `channel = await connection.CreateChannelAsync(...)` line (~line 46).

- [ ] **Step 1: Replace channel creation**

Find:

```csharp
var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
```

Replace with:

```csharp
var channelOptions = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,
    publisherConfirmationTrackingEnabled: true);

var channel = await connection.CreateChannelAsync(
    options: channelOptions,
    cancellationToken: cancellationToken);
```

- [ ] **Step 2: Build**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs
git commit -m "feat(transport): enable publisher confirms on server reply channel"
```

---

## Phase 5: DLQ

### Task 14: Declare DLQ queue per route

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs` — `StartAsync`, the per-route loop (~lines 44–79).

- [ ] **Step 1: Add DLQ declaration after the main queue declaration**

In the `foreach (var route in routes)` loop, immediately after the existing `await channel.QueueDeclareAsync(...)` for the main queue (around line 48–56) and **before** the `await channel.BasicQosAsync(...)` call, insert:

```csharp
var deadRoute = $"{route}.dead";

await channel.QueueDeclareAsync(
    queue: deadRoute,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null,
    passive: false,
    noWait: false,
    cancellationToken: cancellationToken);
```

- [ ] **Step 2: Pass `deadRoute` into the consumer callback**

The existing consumer registration is:

```csharp
var consumer = new AsyncEventingBasicConsumer(channel);
consumer.ReceivedAsync += async (_, ea) =>
{
    await HandleIncomingAsync(channel, ea, handler, cancellationToken);
};
```

Update the lambda to pass `deadRoute` and `route` (we will widen `HandleIncomingAsync`'s signature in Task 15):

```csharp
var consumer = new AsyncEventingBasicConsumer(channel);
var capturedRoute = route;
var capturedDeadRoute = deadRoute;
consumer.ReceivedAsync += async (_, ea) =>
{
    await HandleIncomingAsync(channel, ea, handler, capturedRoute, capturedDeadRoute, cancellationToken);
};
```

(`capturedRoute`/`capturedDeadRoute` locals avoid closure-captures-loop-variable surprises across `foreach` iterations.)

- [ ] **Step 3: Build**

The build will fail because `HandleIncomingAsync` doesn't accept `route, deadRoute` yet — that's expected and will be fixed in Task 15.

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: build error mentioning argument count for `HandleIncomingAsync`. **Do not commit yet** — proceed to Task 15.

---

### Task 15: Replace BasicNackAsync with publish-to-DLQ + ack

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs` — `HandleIncomingAsync` method (~lines 84–133).

- [ ] **Step 1: Replace the entire HandleIncomingAsync method**

Replace the existing method with:

```csharp
private async Task HandleIncomingAsync(
    IChannel channel,
    BasicDeliverEventArgs ea,
    Func<RpcRequest, CancellationToken, Task<RpcResponse>> handler,
    string route,
    string deadRoute,
    CancellationToken cancellationToken)
{
    try
    {
        var request = serializer.Deserialize<RpcRequest>(ea.Body)
                      ?? throw new InvalidOperationException("Invalid RPC request payload.");

        var response = await handler(request, cancellationToken);

        var replyTo = ea.BasicProperties.ReplyTo;
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            var responseBytes = serializer.Serialize(response);

            var props = new BasicProperties
            {
                CorrelationId = ea.BasicProperties.CorrelationId,
                ContentType   = ea.BasicProperties.ContentType,
            };

            try
            {
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: replyTo,
                    mandatory: false,
                    basicProperties: props,
                    body: responseBytes,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Failed to publish RPC reply. CorrelationId: {Id}. Original delivery acked anyway.",
                    ea.BasicProperties.CorrelationId);
            }
        }

        await channel.BasicAckAsync(
            deliveryTag: ea.DeliveryTag,
            multiple: false,
            cancellationToken: cancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Poison RPC message → DLQ {DeadRoute}", deadRoute);

        var deadProps = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["x-rpc-error"]     = ex.GetType().FullName,
                ["x-rpc-error-msg"] = ex.Message,
                ["x-rpc-original"]  = route,
                ["x-rpc-failed-at"] = DateTimeOffset.UtcNow.ToString("o"),
            },
        };

        try
        {
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: deadRoute,
                mandatory: false,
                basicProperties: deadProps,
                body: ea.Body,
                cancellationToken: cancellationToken);

            await channel.BasicAckAsync(
                deliveryTag: ea.DeliveryTag,
                multiple: false,
                cancellationToken: cancellationToken);
        }
        catch (Exception dlqEx) when (dlqEx is not OperationCanceledException)
        {
            logger.LogError(dlqEx,
                "DLQ publish failed for {DeadRoute}; dropping original message.", deadRoute);

            await channel.BasicNackAsync(
                deliveryTag: ea.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken: cancellationToken);
        }
    }
}
```

Note: per the user's `_`-prefix rule from CLAUDE.md, the field accesses use `this.` style (e.g., `this.serializer.Deserialize`). The existing class uses bare names because the fields don't shadow parameters in this method; we keep the existing convention here unchanged.

- [ ] **Step 2: Build**

```bash
dotnet build AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit Task 14 + 15 together**

```bash
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs
git commit -m "feat(transport): poison messages → per-route DLQ ({route}.dead)"
```

---

## Phase 6: Versioning, docs, final verification

### Task 16: Bump package versions to 3.0.0

**Files:**
- Modify: `AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj`
- Modify: `AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj`

- [ ] **Step 1: Update Client csproj**

In `AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj`, find the `<Version>2.0.0</Version>` element and change to `<Version>3.0.0</Version>`. Update or add a `<PackageReleaseNotes>` element with:

```xml
<PackageReleaseNotes>
v3.0.0 — Reliability release. Wire-format breaking change (stable type identity);
reply queue resilience on reconnect; publisher confirms; per-route DLQ.
v3.x is not interoperable with v2.x. See README "Migration v2 → v3".
</PackageReleaseNotes>
```

- [ ] **Step 2: Update Server csproj**

Apply the same `<Version>` and `<PackageReleaseNotes>` changes to `AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj`.

- [ ] **Step 3: Build to confirm**

```bash
dotnet build -c Release
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj
git commit -m "chore(release): bump client and server packages to 3.0.0"
```

---

### Task 17: README migration section

**Files:**
- Modify: `README.md` (append a section)

- [ ] **Step 1: Open README.md and find the end**

```bash
ls README.md
```

Expected: file exists.

- [ ] **Step 2: Append the migration section**

Append the following section to the end of the README:

```markdown
## Migration v2 → v3

v3.0.0 is a reliability-focused release with breaking wire and behavior changes. v2.x and v3.x are not interoperable; upgrade clients and servers in lockstep.

### Wire-format change

Parameter and result type names on the wire now use the stable form
`Namespace.TypeName, AssemblySimpleName`, dropping `Version`, `Culture`, and `PublicKeyToken`.
This means routine version bumps of your contract assemblies no longer break the wire format.

A v2 client cannot talk to a v3 server (and vice versa) — the server's method-key lookup will fail with `method_not_found`.

### Behavior changes

- **`TransportReconnectedException`** (new public type in `AsbtCore.Broker.Core.Exceptions`):
  thrown into pending RPC tasks when the broker reconnects. Caller decides whether to retry.
  In v2, pending tasks would hang until process exit.
- **`RpcPublishFailedException`** (new public type): thrown immediately when the broker rejects
  (nack/return) the publish of an RPC request. In v2, callers only saw `OperationCanceledException`
  after the configured RPC timeout elapsed.
- **Per-route DLQ**: each RPC route now has a companion durable queue `{route}.dead`. Poison
  messages (malformed payload, dispatcher-internal failure, reply-publish failure) move there
  after a single attempt. v2 used `BasicNackAsync(requeue: true)`, producing infinite loops on
  deterministically broken messages. Plan ops alerting on `*.dead` queue depth.
- **Reply queue naming**: reply queues are now declared as `rpc-reply-{ClientProvidedName}-{guid}`
  (durable: false, auto-delete: true, non-exclusive). Update any monitoring filters that matched
  the old `amq.gen-*` pattern.

### Operator action items

1. Upgrade client and server packages in lockstep.
2. Expect new queues named `*.dead` per RPC route in your broker.
3. Add `catch (TransportReconnectedException)` and/or `catch (RpcPublishFailedException)`
   clauses where your code awaits proxy methods, if you want to handle these explicitly.
4. Set broker policies (TTL, max length) on the new `*.dead` queues if you don't drain them
   manually.
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: add Migration v2 → v3 section to README"
```

---

### Task 18: Final verification and tag

**Files:** none (verification only)

- [ ] **Step 1: Full clean build**

```bash
dotnet build -c Release
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 2: Run remaining tests**

```bash
dotnet test -c Release --no-build
```

Expected: all remaining tests pass (test count is reduced because v2-pinned tests were deleted in Task 7).

- [ ] **Step 3: Pack the NuGet packages locally**

```bash
dotnet pack AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj -c Release -o ./artifacts/nupkg
dotnet pack AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj -c Release -o ./artifacts/nupkg
```

Expected: `AsbtCore.Broker.Client.3.0.0.nupkg` and `AsbtCore.Broker.Server.3.0.0.nupkg` produced under `./artifacts/nupkg/`.

- [ ] **Step 4: Manual smoke test against a local broker (OPTIONAL but recommended)**

If `docker` and a local RabbitMQ are available:

```bash
docker run -d --rm --name rmq-smoke -p 5672:5672 -p 15672:15672 rabbitmq:4-management
```

Run `Test.Client` and `Test.Server` (or whichever sample apps the repo provides) and verify a round-trip RPC succeeds. Then in the RabbitMQ management UI (http://localhost:15672, guest/guest), confirm:

- A reply queue named `rpc-reply-*-*` exists while the client is running.
- A queue `<route>.dead` exists per registered RPC route.
- The publish channel shows confirmed messages (Channels → confirm count > 0).

Stop the broker with `docker stop rmq-smoke` when done.

- [ ] **Step 5: Tag the release**

```bash
git tag -a v3.0.0 -m "v3.0.0 — RPC reliability release"
```

- [ ] **Step 6: Show the final log**

```bash
git log --oneline feature/rpc-reliability-v3 ^master | head -30
```

Expected: ~17 commits since branching from `master`, ending at `v3.0.0` tag.

---

## Self-Review

Spec coverage check:

| Spec section | Implemented in |
|--------------|----------------|
| §5 Reply queue resilience | Task 8 (topology recovery), Task 9 (named non-exclusive queue), Task 10 (recovery hook → fail pending) |
| §6 Publisher confirms (client) | Task 11 |
| §6 Publisher confirms (client failure mapping) | Task 12 |
| §6 Publisher confirms (server reply channel) | Task 13 |
| §7 DLQ declare per route | Task 14 |
| §7 DLQ publish on poison + DLQ-publish-fallback nack | Task 15 |
| §8 StableTypeName component | Task 2 |
| §8 Client wire write | Task 3 |
| §8 Server registry key | Task 4 |
| §8 Dispatcher resolution + ResultTypeName | Task 5 |
| §8 TypeNameCache deletion | Task 6 |
| §9 Public exception types | Task 1 |
| §10 Migration / breaking surface (csproj versions, release notes) | Task 16 |
| §10 Migration (README) | Task 17 |
| §11 File map | Tasks 1–17 each cite their files |
| §12 Tests deleted (no new tests, no benchmarks) | Task 7 |

No gaps.
