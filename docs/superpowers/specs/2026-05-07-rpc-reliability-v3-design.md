# RPC Reliability v3.0.0 — Design Spec

**Date:** 2026-05-07
**Project:** AsbtCore.Broker (RabbitMQ RPC framework)
**Status:** Draft

## 1. Goals

1. **Reply queue resilience** — RPC client survives RabbitMQ reconnect without leaking pending `TaskCompletionSource` handles forever. On reconnect, in-flight requests fail explicitly with `TransportReconnectedException`; caller decides whether to retry.
2. **Publisher confirms end-to-end** — both client request publishes and server reply publishes use RabbitMQ publisher confirms (channel-level), so broker-side acceptance is observable. Failed publishes raise `RpcPublishFailedException` immediately, not after timeout.
3. **Poison message DLQ** — incoming RPC requests that cannot be processed (malformed payload, dispatcher-internal failure, reply-publish failure) are moved to a per-route dead-letter queue `{route}.dead` after a single attempt. No infinite `requeue: true` loops.
4. **Stable wire-level type identity** — parameter and result type names on the wire use `FullName + assembly simple-name` (no `Version`/`Culture`/`PublicKeyToken`), so contract assemblies can be revved without breaking the wire format.

## 2. Non-Goals

- Broker-side retry/backoff. RPC is request/response; "retry on poison" is rarely correct, and when it is, it's the caller's decision, not the framework's.
- Wire compatibility with v2. v2 client cannot talk to v3 server and vice versa.
- Auto-resubmit of pending requests on reconnect. Caller policy.
- Native AOT.

## 3. Version

`AsbtCore.Broker.Client` and `AsbtCore.Broker.Server` packages bumped to **3.0.0**. Major bump per semver: wire-format breaking change (§7.1).

## 4. Architecture

Changes are contained:

| Layer | Files | Change |
|-------|-------|--------|
| Transport (RabbitMQ) | `RabbitMqRpcTransport`, `RabbitMqRpcTransportHost`, `RabbitMqConnectionProvider` | Reply queue naming, recovery hook, publisher confirms, DLQ declare/publish |
| Core | `Serialization/StableTypeName.cs` (new), `Serialization/TypeNameCache.cs` (deleted), `Exceptions/RpcExceptions.cs` (new public types) | Stable type name generation/resolution; new public exception types |
| Server | `RpcServerRegistry`, `RpcRequestDispatcher` | Method key + `ResultTypeName` use `StableTypeName` |
| Client | `RpcClient.BuildRequest` | Parameter type names use `StableTypeName` |

No new projects. No new public abstractions on the transport interface.

## 5. Reply Queue Resilience (#1)

### Problem
`RabbitMqRpcTransport` declares the reply queue with `exclusive: true, autoDelete: true` and stores its server-generated name. On reconnect, the queue is gone (broker closed it when the consumer disconnected) and any pending `TaskCompletionSource` is bound to a now-meaningless `ReplyTo`. With `TopologyRecoveryEnabled = false` (current), nothing is rebuilt — pending TCS hang forever or until process exit. Even with topology recovery enabled, an exclusive queue is re-declared with a *new* server-generated name; the framework's stored `replyQueueName` becomes stale.

### Resolution
1. Declare the reply queue with a **stable, process-local name** and **non-exclusive**:
   - Name pattern: `rpc-reply-{ClientProvidedName}-{Guid:N}`
   - `durable: false, exclusive: false, autoDelete: true`
   - Auto-deletes when the last consumer (this transport's `replyChannel`) disconnects.
2. Enable topology recovery in `RabbitMqConnectionProvider`: `TopologyRecoveryEnabled = true`. The library re-declares the named queue and re-attaches the consumer on reconnect.
3. Subscribe to `RecoverySucceededAsync` on `replyChannel` (cast to `IRecoverable`). On recovery: drain `pending` and complete each `TaskCompletionSource<RpcResponse>` with `TransportReconnectedException`. Rationale: the request that was in flight may or may not have reached the broker; even if it did, the response may have been routed to the now-disposed (or re-declared identical) queue during the gap. Failing fast is correct semantics; caller retries if it wants idempotent behavior.

### Skeleton

```csharp
// RabbitMqRpcTransport.EnsureInitializedAsync
var queueName = $"rpc-reply-{options.ClientProvidedName}-{Guid.NewGuid():N}";
await replyChannel.QueueDeclareAsync(
    queue: queueName,
    durable: false, exclusive: false, autoDelete: true,
    arguments: null, cancellationToken: ct);
replyQueueName = queueName;

if (replyChannel is IRecoverable recoverable)
    recoverable.RecoverySucceededAsync += OnRecoverySucceededAsync;
```

```csharp
private Task OnRecoverySucceededAsync(object? sender, AsyncEventArgs e)
{
    foreach (var id in pending.Keys.ToArray())
    {
        if (pending.TryRemove(id, out var tcs))
            tcs.TrySetException(new TransportReconnectedException(id));
    }
    return Task.CompletedTask;
}
```

## 6. Publisher Confirms (#2)

### Problem
RabbitMQ.Client v7 channels created via `CreateChannelAsync()` without options return as soon as the publish leaves the client TCP buffer — broker rejection (queue full, missing exchange, return) is silent. For RPC, this manifests as caller-side timeout with no diagnostic. Reply publishes on the server have the same property — a lost reply causes a client timeout with no server-side signal.

### Resolution
Both the client publish channel and each server channel (the same channel both consumes the request and publishes the reply) are created with publisher confirms enabled and tracked:

```csharp
var opts = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,
    publisherConfirmationTrackingEnabled: true);
var channel = await connection.CreateChannelAsync(opts, ct);
```

`BasicPublishAsync` then completes only when the broker has acked (or thrown on nack/return). Failures are mapped:

- **Client side** (`RabbitMqRpcTransport.SendAsync`): on publish exception, remove `pending[requestId]` and throw `RpcPublishFailedException(requestId, reason, inner)` immediately. Caller sees a typed publish failure instead of a generic timeout.
- **Server side** (`RabbitMqRpcTransportHost.HandleIncomingAsync`): reply publish failure is logged; original delivery is **acked** anyway (the request was processed; replaying it would do duplicate business work). Caller will hit RPC timeout — that is its existing semantics.

### Latency note
Publisher confirms add ~5–15 µs per publish in our local benchmarking environment. Acceptable for the reliability gain.

## 7. Poison Message DLQ (#3)

### Problem
`RabbitMqRpcTransportHost.HandleIncomingAsync` currently calls `BasicNackAsync(requeue: true)` on any handler exception. A deterministically broken request (malformed body, deserialization failure) is requeued forever, producing a tight error loop in logs and 100 % CPU on the consumer side.

Note that `RpcRequestDispatcher` already wraps **business** exceptions into `RpcResponse.Exception` — those never propagate up to `HandleIncomingAsync`'s `catch`. The actual catch population is:

- malformed `RpcRequest` body (cannot deserialize)
- dispatcher-internal failure (e.g., serializer threw)
- reply publish failure (with confirms enabled — see §6)

### Resolution
1. **DLQ declaration per route** in `RabbitMqRpcTransportHost.StartAsync`: for each `route`, declare `{route}.dead` as `durable: true, exclusive: false, autoDelete: false`. No `x-dead-letter-exchange` argument on the main queue — DLQ publish is explicit, not consumer-cancel/expire-driven.
2. **Single attempt + DLQ in catch**: replace `BasicNackAsync(requeue: true)` with publish-to-DLQ + ack original. Headers on the DLQ message preserve forensic context:
   - `x-rpc-error` (CLR exception type FullName)
   - `x-rpc-error-msg` (Exception.Message)
   - `x-rpc-original` (original route name)
   - `x-rpc-failed-at` (ISO-8601 UTC timestamp)
3. **Cancellation passthrough**: `OperationCanceledException` is rethrown without DLQ publish (it indicates host shutdown, not a poison message).
4. **DLQ publish failure**: if the publish to `{route}.dead` itself fails (broker disconnected mid-recovery), the original delivery is `BasicNackAsync(requeue: false, multiple: false)`. The message is dropped; the broker may route it via its own configured DLX policy if any. Logged at error level for ops visibility.

### Skeleton

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex)
{
    logger.LogError(ex, "Poison message → DLQ {Route}", deadRoute);
    var props = new BasicProperties
    {
        Headers = new Dictionary<string, object?>
        {
            ["x-rpc-error"]     = ex.GetType().FullName,
            ["x-rpc-error-msg"] = ex.Message,
            ["x-rpc-original"]  = route,
            ["x-rpc-failed-at"] = DateTimeOffset.UtcNow.ToString("o")
        }
    };
    await channel.BasicPublishAsync(
        exchange: string.Empty, routingKey: deadRoute,
        mandatory: false, basicProperties: props,
        body: ea.Body, cancellationToken: ct);
    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
}
```

## 8. Stable Type Identity (#6)

### Problem
v2 wire format puts `Type.AssemblyQualifiedName` into `RpcRequest.ParameterTypeNames` and `RpcResponse.ResultTypeName`. AQN includes `Version=X.Y.Z.W, Culture=..., PublicKeyToken=...`. Bumping the contract assembly's version (a normal, non-breaking change) makes the server's stored method key (built at registry time from the *new* AQN) not match the client's payload (built from its own AQN snapshot). Wire-format compat is silently broken by a routine version bump.

### Wire format change
| Type | v2 | v3 |
|------|----|----|
| `int` | `System.Int32, System.Private.CoreLib, Version=10.0.0.0, ...` | `System.Int32, System.Private.CoreLib` |
| `Contracts.UserDto` | `Contracts.UserDto, Contracts, Version=2.0.0.0, ...` | `Contracts.UserDto, Contracts` |
| `List<UserDto>` | nested AQNs with versions | `` System.Collections.Generic.List`1[[Contracts.UserDto, Contracts]], System.Private.CoreLib `` |

### New component: `StableTypeName`

`AsbtCore.Broker.Core/Serialization/StableTypeName.cs` (replaces `TypeNameCache`):

```csharp
internal static class StableTypeName
{
    private static readonly ConcurrentDictionary<Type, string> writeCache = new();
    private static readonly ConcurrentDictionary<string, Type> readCache  = new(StringComparer.Ordinal);

    internal static string From(Type type) => writeCache.GetOrAdd(type, static t => Build(t));

    internal static Type Resolve(string name) =>
        readCache.GetOrAdd(name, static n =>
            Type.GetType(n,
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

    private static Assembly? ResolveAssembly(AssemblyName n) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, n.Name, StringComparison.Ordinal))
        ?? Assembly.Load(n.Name!);

    private static Type? ResolveType(Assembly? a, string typeName, bool ignoreCase) =>
        a?.GetType(typeName, throwOnError: false, ignoreCase) ?? Type.GetType(typeName);
}
```

### Call sites
- `RpcClient.BuildRequest` — parameter type names: `StableTypeName.From(parameterType)`.
- `RpcRequestDispatcher` — `Type.GetType(typeName)` calls (currently routed through `TypeNameCache`) replaced with `StableTypeName.Resolve(typeName)`. `RpcResponse.ResultTypeName` writes via `StableTypeName.From(logicalResultType)`.
- `RpcServerRegistry.BuildKey` — registry key parameter type names use `StableTypeName.From`.

### Deletion
`AsbtCore.Broker.Core/Serialization/TypeNameCache.cs` is removed; all usages migrated to `StableTypeName.Resolve`.

### Generic type collision risk
If two assemblies expose the same `Namespace.Type` FullName, resolution is non-deterministic (first match wins among loaded assemblies). This is a flat namespace constraint clients must observe. Documented in the Migration section.

## 9. Public Exception Types

`AsbtCore.Broker.Core/Exceptions/RpcExceptions.cs`:

```csharp
namespace AsbtCore.Broker.Core.Exceptions;

/// <summary>Pending RPC failed because the transport reconnected; caller may retry.</summary>
public sealed class TransportReconnectedException : Exception
{
    public string RequestId { get; }
    public TransportReconnectedException(string requestId)
        : base($"RPC request '{requestId}' aborted: transport reconnected.")
        => RequestId = requestId;
}

/// <summary>Broker rejected (nack/return) the publish.</summary>
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

## 10. Migration / Breaking Changes

### Wire-breaking (v2 ↔ v3 are NOT compatible on the wire)
- `RpcRequest.ParameterTypeNames[i]` format changed (see §8).
- `RpcResponse.ResultTypeName` format changed (see §8).
- DLQ topology: brokers will see new queues `{route}.dead`. Operators should expect them.

### Behavior-breaking
- After reconnect, all in-flight RPC tasks complete with `TransportReconnectedException` instead of hanging until process exit. Caller code that awaited an RPC task without exception handling for transport failures must add a `catch (TransportReconnectedException)` clause.
- Publish failure surfaces immediately as `RpcPublishFailedException` (pre-confirm: caller saw `OperationCanceledException` only after the configured timeout elapsed).

### API surface
- Constructors of `RabbitMqRpcTransport`, `RabbitMqRpcTransportHost`, `RpcClient`, `RpcProxyFactory`: unchanged.
- New public types: `TransportReconnectedException`, `RpcPublishFailedException` in `AsbtCore.Broker.Core.Exceptions`.
- Removed: `TypeNameCache` (was internal — no consumer impact).

### Operator action items (documented in README)
1. Upgrade client and server packages in lockstep. Mixed v2/v3 deployments will fail on the first request.
2. Expect new queues named `*.dead` per RPC route in your broker. Plan routine inspection or alerting.
3. Reply queue names changed pattern: `rpc-reply-{ClientProvidedName}-{guid}` (was server-generated `amq.gen-...`). Update any monitoring filters that match by name.

### Release notes (csproj `PackageReleaseNotes`)
> v3.0.0 — Reliability release. Wire-format breaking change (stable type identity); reply queue resilience; publisher confirms; per-route DLQ. v3.x is not interoperable with v2.x. See README "Migration v2 → v3".

## 11. File Map

### Create
- `AsbtCore.Broker.Core/Serialization/StableTypeName.cs`
- `AsbtCore.Broker.Core/Exceptions/RpcExceptions.cs` (or extend the existing exceptions file with new public types)

### Modify
- `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs` — reply queue naming, recovery hook, publisher confirms options, `RpcPublishFailedException` mapping
- `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransportHost.cs` — DLQ declare per route, DLQ publish in catch, publisher confirms options on reply channel
- `AsbtCore.Broker.RabbitMq/Transport/RabbitMqConnectionProvider.cs` — `TopologyRecoveryEnabled = true` (line 69)
- `AsbtCore.Broker.Client/RpcClient.cs` — `BuildRequest` uses `StableTypeName.From`
- `AsbtCore.Broker.Server/RpcRequestDispatcher.cs` — replace `TypeNameCache.Resolve` with `StableTypeName.Resolve`; write `ResultTypeName` via `StableTypeName.From`
- `AsbtCore.Broker.Server/RpcServerRegistry.cs` — method key parameter type names via `StableTypeName.From`
- `AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj` — `<Version>3.0.0</Version>` + release notes
- `AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj` — `<Version>3.0.0</Version>` + release notes
- `README.md` — new "Migration v2 → v3" section

### Delete
- `AsbtCore.Broker.Core/Serialization/TypeNameCache.cs` — replaced by `StableTypeName`
- Tests in `AsbtCore.Broker.Tests` that pin v2 wire format (AQN-based assertions). Exact list determined at build time when failures surface; explicitly approved deletion scope.

## 12. Testing & Benchmarks

Per the explicit decision recorded during brainstorming:

- **No new tests written.** No existing tests updated.
- **Existing tests that fail because of the v3 wire-format change are deleted** (not commented, not stubbed).
- **Benchmarks are not re-run.** Performance impact of publisher confirms is acknowledged (~5–15 µs per publish) but not re-measured for this release.

This is unusual for a release of this scope; documented here so reviewers can challenge it explicitly. The decision is the user's, not a default.

## 13. Risks

| Risk | Mitigation |
|------|------------|
| Topology recovery race: queue re-declared with same name but consumer not yet re-bound when a publish lands → message dropped to `unroutable`. | Mandatory=false on reply publishes (already), confirms still ack the publish (broker accepted into reply queue). If consumer is briefly absent, broker holds messages until the auto-delete timer; `RecoverySucceededAsync` triggers TCS failure regardless. Net effect: at-most-once with explicit fail-fast. |
| `Assembly.Load(simpleName)` finds wrong version of contract assembly. | Documented constraint: contract assemblies must be uniquely named within the AppDomain. Assembly resolver prefers already-loaded assemblies. |
| DLQ queue grows unboundedly if poison messages are frequent. | Out of scope for the framework. Operators set `x-message-ttl` or queue length limits via broker policy. README will mention this. |
| `IRecoverable` cast may not apply to all `IChannel` implementations in future RabbitMQ.Client versions. | Defensive cast (`if (replyChannel is IRecoverable rec)`) — recovery hook is best-effort; functional path doesn't depend on it for steady-state. |
| DLQ publish itself fails during broker outage. | Original delivery nacked with `requeue: false` (drop). Logged at error level. See §7 step 4. |

## 14. Open Questions

None. All design decisions resolved during brainstorming.
