# RabbitMq.RPC — Performance Optimization Pass (v2.0)

**Date:** 2026-05-07
**Status:** Design approved, awaiting plan
**Scope:** New major version (2.0). Old versions (1.0.x) not maintained. Wire-format and public API may break.

## 1. Goals

Eliminate allocations and runtime reflection from the RPC hot path:

1. Replace `SerializeToUtf8Bytes → JsonDocument.Parse → RootElement.Clone()` with a single `JsonSerializer.SerializeToElement` call (client and server).
2. Cache compiled delegates for client-side proxy invocation (replace `MakeGenericMethod` + `MethodInfo.Invoke` per call).
3. Cache compiled delegates for server-side method invocation (replace `MethodInfo.Invoke` + `task.GetType().GetProperty("Result").GetValue` per call).
4. Cache `Type.GetType(typeName)` results for argument deserialization on the server.
5. Remove the `SemaphoreSlim publishLock` around `BasicPublishAsync` (RabbitMQ.Client v7 `IChannel.BasicPublishAsync` is thread-safe).

In-scope critical bug fixes:

- `RpcDispatchProxy.timeout` is hardcoded to 10 s; must come from `RpcOptions.DefaultTimeoutSeconds`.
- `Test.Client/Program.cs` — null-deref on `b.Trim()` when input is null.
- `RabbitMqRpcTransport.SendAsync` — pending `TaskCompletionSource` is not explicitly cancelled on cancel/timeout.

Out of scope (separate spec): publisher confirms, reply-queue topology recovery, DLQ for poison messages, AOT/trimming compatibility, MSTest → xUnit migration, .NET dependencies upgrade beyond runtime, `IRpcSerializer` API extensions.

## 2. Non-goals

- AOT / trimming compatibility (DispatchProxy, runtime STJ, `Expression.Compile` are JIT-only — acceptable).
- Reliability features (publisher confirms, DLQ).
- Wire-format compatibility with v1.x.
- Backward-compatible public API.

## 3. Constraints

- Target framework: **net10.0**, `<Nullable>enable</Nullable>` across all projects.
- Private fields: no `_` prefix; access via `this.fooService` (per project style).
- `IRpcSerializer` keeps only `Serialize<T>(T value)` and `Deserialize<T>(ReadOnlyMemory<byte> payload)`. Argument/result serialization bypasses the abstraction and uses `System.Text.Json` directly.
- Tests: update existing where signatures change; delete obsolete; add new tests for new components.
- Benchmarks: BenchmarkDotNet project required, before/after metrics per optimization plus end-to-end round-trip.

## 4. Architecture

### 4.1 Component map (after change)

| Layer | Component | Status |
|-------|-----------|--------|
| Core | `RpcContracts` (`RpcRequest`, `RpcResponse`, `RpcArgument`, `RpcError`, `RpcJson`) | unchanged |
| Core | `IRpcSerializer`, `JsonRpcSerializer` | unchanged |
| Core | `RpcOptions` | unchanged (already has `DefaultTimeoutSeconds`) |
| Core | `DefaultRpcRouteResolver` | unchanged |
| Core | `RpcSerializationHelper` (internal static) | **new** |
| Core | `TypeNameCache` (internal static) | **new** |
| Client | `RpcClient` | refactor: use `RpcClientInvokerCache`, `SerializeToElement` |
| Client | `RpcClientInvokerCache` (internal) | **new** |
| Client | `RpcProxyFactory`, `RpcDispatchProxy` | refactor: timeout from `RpcOptions` via DI |
| Server | `RpcRequestDispatcher` | refactor: invoker cache, type cache, `SerializeToElement` |
| Server | `RpcServerDescriptor` | extended: stores invoker delegate per method |
| Server | `RpcServerRegistry` (`BuildMethodMap`) | extended: builds invoker delegate |
| Server | `RpcServerMethodInvoker` (internal static) | **new** (delegate factory) |
| RabbitMq | `RabbitMqRpcTransport` | refactor: drop `publishLock`, explicit TCS cancel |
| RabbitMq | `RabbitMqRpcTransportHost` | unchanged (only `Nullable enable` cleanup) |

### 4.2 New components

#### `RpcSerializationHelper` (Core, internal)

```csharp
internal static class RpcSerializationHelper
{
    public static JsonElement ToElement(object? value, Type type)
        => JsonSerializer.SerializeToElement(value, type, RpcJson.Options);

    public static object? FromElement(JsonElement element, Type type)
        => element.Deserialize(type, RpcJson.Options);
}
```

Purpose: single-allocation `JsonElement` creation and consumption for arguments and results. Replaces the legacy `SerializeToUtf8Bytes → JsonDocument.Parse → RootElement.Clone` pipeline.

#### `TypeNameCache` (Core, internal)

```csharp
internal static class TypeNameCache
{
    private static readonly ConcurrentDictionary<string, Type> cache =
        new(StringComparer.Ordinal);

    public static Type Resolve(string typeName) =>
        cache.GetOrAdd(typeName, n => Type.GetType(n, throwOnError: true)!);
}
```

Purpose: O(1) lookup for `AssemblyQualifiedName` → `Type` after first parse.

#### `RpcClientInvokerCache` (Client, internal)

Caches a compiled delegate per `MethodInfo` that dispatches to `InvokeVoidAsync` (for `Task` return) or to the closed-generic `InvokeGenericAsync<T>` (for `Task<T>`). The cache hides the `Task` vs `Task<T>` branch and the `MakeGenericMethod` cost behind a one-time compile.

Signature:

```csharp
internal delegate object RpcClientInvocation(
    RpcClient client,
    Type interfaceType,
    object[] args,
    TimeSpan? timeout,
    CancellationToken cancellationToken);

internal static class RpcClientInvokerCache
{
    public static RpcClientInvocation Get(MethodInfo method);
}
```

Implementation: `Expression.Lambda<RpcClientInvocation>(...).Compile()`. The compiled body:
- For `Task` return: `client.InvokeVoidAsync(interfaceType, method, args, timeout, ct)`.
- For `Task<T>` return: `client.InvokeGenericAsync<T>(interfaceType, method, args, timeout, ct)` with `T` baked in via `MakeGenericMethod` once at build time.
- Other return types: factory throws `NotSupportedException` (no caching of failed entries; throws are rare and reflect contract bugs).

Storage: `ConcurrentDictionary<MethodInfo, RpcClientInvocation>`.

#### `RpcServerMethodInvoker` (Server, internal)

Builds a `Func<object, object?[], Task<object?>>` per `MethodInfo`. The compiled lambda:
- Casts `instance` to the implementation type.
- Casts each `args[i]` to the parameter type.
- Calls the target method.
- Awaits the resulting `Task` (if any) and returns its result boxed as `object?`, or `null` for non-generic `Task`/`void`.
- Synchronous methods return `Task.FromResult(result)`.

Signature:

```csharp
internal delegate Task<object?> RpcMethodInvocation(object instance, object?[] args);

internal static class RpcServerMethodInvoker
{
    public static RpcMethodInvocation Build(MethodInfo method, Type implementationType);
}
```

Implementation: `Expression.Lambda<RpcMethodInvocation>` with an `async` outer wrapper produced via `Expression.Block` + helper bridge methods (since `Expression.Lambda` does not natively emit `async`/`await`). Practical approach:

- Compile a synchronous delegate `Func<object, object?[], object?>` that calls the method and returns its raw return value.
- Wrap that synchronous delegate in a static async helper that handles three return shapes:
  - `void` / sync non-task → `Task.FromResult(returnValue)`
  - `Task` (non-generic) → `await task; return null`
  - `Task<T>` → `await ((Task<T>)task)`; box `T` as `object?`. The `T` extractor is a closed-generic helper looked up once per `MethodInfo`.

Storage: per-`MethodInfo` invoker stored in `RpcServerDescriptor` alongside the existing method map.

### 4.3 Modified components

| Component | Change |
|-----------|--------|
| `RpcClient.InvokeProxy` | Look up `RpcClientInvocation` from `RpcClientInvokerCache.Get(method)` and invoke it. No more `MakeGenericMethod` + `MethodInfo.Invoke` per call. |
| `RpcClient.BuildRequest` | Replace `SerializeToUtf8Bytes` + `JsonDocument.Parse` + `Clone` with `RpcSerializationHelper.ToElement(arg, paramType)`. |
| `RpcClient.SendAsync<T>` | Result deserialization via `response.Result.Value.Deserialize<T>(RpcJson.Options)` (already correct — keep). |
| `RpcRequestDispatcher.DispatchAsync` | (a) Argument types via `TypeNameCache.Resolve`. (b) Argument payload via `RpcSerializationHelper.FromElement`. (c) Method invocation via cached `RpcMethodInvocation` from descriptor. (d) Result envelope via `RpcSerializationHelper.ToElement`. |
| `RpcServerDescriptor` | Internal map becomes `Dictionary<string, RpcMethodEntry>` where `RpcMethodEntry` is `internal sealed record(MethodInfo Method, RpcMethodInvocation Invoker)`. `TryGetMethod` signature changes to `bool TryGetMethod(string methodName, IReadOnlyList<string> parameterTypeNames, out RpcMethodEntry entry)`. Callers use `entry.Invoker`. |
| `RpcServerRegistry.BuildMethodMap` | For each interface method, build the invoker via `RpcServerMethodInvoker.Build(targetMethod, implementationType)` and store. |
| `RabbitMqRpcTransport` | Remove `publishLock` SemaphoreSlim and the wait/release around `BasicPublishAsync`. |
| `RabbitMqRpcTransport.SendAsync` | On `OperationCanceledException` from `WaitAsync`, ensure the pending `TaskCompletionSource` is set cancelled before removal: replace bare `tcs.Task.WaitAsync(linkedCts.Token)` flow with explicit cancellation via `linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token))`. |
| `RpcDispatchProxy` | Inject `IOptions<RpcOptions>` (or take `TimeSpan` from `RpcProxyFactory`); remove hardcoded 10 s. |
| `RpcProxyFactory` | Constructor takes `IOptions<RpcOptions>`; passes `TimeSpan.FromSeconds(options.Value.DefaultTimeoutSeconds)` to the proxy on configure. |
| `Test.Client/Program.cs` | Null-check `a` and `b` before `Trim()`. |

### 4.4 csproj changes

All projects:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

Package versions are bumped to the matching `Microsoft.Extensions.*` 10.x line where available; `RabbitMQ.Client` stays on 7.x.

NuGet packages (`AsbtCore.Broker.Client`, `AsbtCore.Broker.Server`) — version bump to **2.0.0**.

## 5. Data flow (target)

### 5.1 Client send

```
proxy method call
  → RpcDispatchProxy.Invoke
  → RpcClient.InvokeProxy(method, args)
  → RpcClientInvokerCache.Get(method)         [cache hit, no MakeGenericMethod]
  → compiled RpcClientInvocation
      → InvokeVoidAsync OR InvokeGenericAsync<T>
  → BuildRequest
      foreach arg:
         payload = RpcSerializationHelper.ToElement(arg, paramType)
         request.Arguments.Add(new RpcArgument { TypeName, Payload = payload })
  → IRpcSerializer.Serialize(request)         [byte[] envelope]
  → IRpcTransport.SendAsync → BasicPublishAsync (no semaphore)
  → tcs.Task.WaitAsync(linkedCts.Token)
  → response.Result.Value.Deserialize<T>(opts)
```

### 5.2 Server dispatch

```
AsyncEventingBasicConsumer.ReceivedAsync
  → IRpcSerializer.Deserialize<RpcRequest>(body)
  → registry.TryGet(InterfaceName) → RpcServerDescriptor
  → descriptor.TryGetMethod(name, paramTypeNames, out RpcMethodEntry entry)
  → scope = scopeFactory.CreateAsyncScope()
  → instance = scope.GetRequiredService(implementationType)
  → foreach arg:
      type = TypeNameCache.Resolve(arg.TypeName)
      args[i] = RpcSerializationHelper.FromElement(arg.Payload, type)
  → result = await entry.Invoker(instance, args)
  → resultElement = RpcSerializationHelper.ToElement(result, logicalType)
  → response → IRpcSerializer.Serialize → BasicPublishAsync (replyTo)
  → BasicAck
```

### 5.3 Allocation summary (per request)

| Step | Before | After |
|------|--------|-------|
| Per-arg serialize (client) | `byte[]` + `JsonDocument` + `Clone` | 1× `JsonElement` |
| Per-result serialize (server) | `byte[]` + `JsonDocument` + `Clone` | 1× `JsonElement` |
| Per-arg type lookup | `Type.GetType(AQN)` parse | dict lookup |
| Client invoker | `MakeGenericMethod` + `MethodInfo.Invoke` + boxed `object[]` | direct delegate call |
| Server invoker | `MethodInfo.Invoke` + `task.GetType().GetProperty("Result").GetValue` | direct delegate call |
| Publish lock | `SemaphoreSlim.WaitAsync` + `Release` | gone |

## 6. Error handling

### 6.1 Client

- `RpcClientInvokerCache.Get(method)` factory throws `NotSupportedException` for return types that are not `Task` or `Task<T>`. Subsequent calls retry the factory (no negative caching).
- `RpcSerializationHelper.ToElement` failures wrap into `InvalidOperationException` with method name and argument index in the message.
- Cancellation/timeout: `linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token))` ensures the pending entry is settled before `pending.TryRemove`. The awaiting code observes `OperationCanceledException`.

### 6.2 Server

- Unknown `TypeName` → `RpcResponse { Success = false, Error.Code = "type_not_found", Error.Message = "Type '{name}' not resolvable." }`. Not 500 / no exception escape.
- Argument deserialization error → `Error.Code = "deserialization_error"`, message references argument index.
- `RpcServerMethodInvoker.Build` failure during registration is fatal (logged at startup, host fails to start).
- Existing `TargetInvocationException` handling retained (`Error.Code = "invocation_error"`).
- Existing generic `Exception` catch retained (`Error.Code = "server_error"`).

## 7. Testing

### 7.1 Existing tests

**Update**:

- `RpcClientTests` — assert that the same `MethodInfo` is compiled once across N invocations (counter via test factory hook); both `Task` and `Task<T>` paths exercise the cache.
- `RpcClientTests` — `RpcDispatchProxy` timeout sourced from `RpcOptions.DefaultTimeoutSeconds`.
- `RpcRequestDispatcherTests` — assert the cached `RpcMethodInvocation` is used (spy on it); `TypeNameCache` hit on repeated `TypeName`; deserialization failure returns `Error.Code = "deserialization_error"`; unknown type returns `Error.Code = "type_not_found"`.
- `RabbitMqRpcTransportTests` — concurrent `SendAsync` not serialised by a semaphore (parallel publish observable); cancellation cancels the pending TCS and removes it from the dictionary; timeout cancels and removes likewise.

**Delete** (if present):

- Tests asserting the legacy `byte[] + JsonDocument.Parse` intermediate form.
- Tests asserting `RpcDispatchProxy.timeout == TimeSpan.FromSeconds(10)`.
- Tests asserting `publishLock` semantics.

### 7.2 New tests

- `RpcSerializationHelperTests` — round-trip primitives, nested DTO, list, null.
- `TypeNameCacheTests` — concurrent resolution, throw on unknown type.
- `RpcClientInvokerCacheTests` — build for `Task`, `Task<int>`, `Task<UserDto>`; throw on `void`/non-task return; idempotency (same `MethodInfo` returns the same delegate instance).
- `RpcServerMethodInvokerTests` — build for `Task PingAsync()`, `Task<int> SumAsync(int,int)`, `Task<UserDto> GetByIdAsync(int)`, sync `int Add(int,int)`; correct result extraction; exception in target method surfaces via the awaited task.

### 7.3 Framework

MSTest 3.6 retained for now. xUnit migration is out of scope.

## 8. Benchmarks (BenchmarkDotNet)

### 8.1 Project

`Benchmarks/AsbtCore.Broker.Benchmarks` — net10.0, Release, `BenchmarkDotNet` latest. Solution folder `Benchmarks`.

### 8.2 Benchmark suite

| # | Class | Optimization | Variants | Diagnosers |
|---|-------|--------------|----------|------------|
| 1 | `JsonElementCreationBench` | #1 | small DTO, nested DTO, list (3 sizes) | `MemoryDiagnoser` |
| 2 | `RpcClientInvokerBench` | #2 | `Task`, `Task<int>`, `Task<UserDto>` | `MemoryDiagnoser` |
| 3 | `RpcServerInvokerBench` | #3 | sync, `Task`, `Task<T>` | `MemoryDiagnoser` |
| 4 | `TypeResolutionBench` | #4 | N=1, N=10, N=1000 | `MemoryDiagnoser` |
| 5 | `PublishConcurrencyBench` | #5 | concurrency 1, 4, 16, 64 | `MemoryDiagnoser`, `ThreadingDiagnoser` |
| 6 | `RpcRoundTripBench` | end-to-end | `PingAsync`, `SumAsync`, `GetByIdAsync` | `MemoryDiagnoser`, `ThreadingDiagnoser` |

Each per-optimization benchmark exposes a `[Params]` switch (`Legacy` vs `New`) so before/after numbers come from the same harness.

### 8.3 Run

```
dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter '*'
```

`Benchmarks/README.md` documents how to run and how to interpret results.

### 8.4 Acceptance criteria

| # | Metric | Target |
|---|--------|--------|
| 1 | allocations | ↓ ≥ 40 % |
| 1 | wall time | ↓ ≥ 25 % |
| 2 | wall time after first call | ↓ ≥ 70 % |
| 3 | wall time | ↓ ≥ 60 % |
| 4 | wall time after first hit | ↓ ≥ 95 % |
| 5 | throughput at concurrency 16 | ↑ ≥ 3× |
| 6 | round-trip allocations | ↓ ≥ 30 % |
| 6 | round-trip wall time | ↓ ≥ 20 % |

If any target is missed the implementation is iterated before declaring the work complete.

## 9. Risks

- `Expression.Lambda` for `RpcServerMethodInvoker` requires careful handling of async unwrap. Mitigation: use a pair of static generic helpers (`Func<object, object?[], object?>` plus an `async Task<object?>` adaptor that switches on return shape) rather than emitting async IL directly.
- Removing `publishLock` exposes any latent assumption about per-channel ordering. Mitigation: validate via `PublishConcurrencyBench` and an integration test that asserts no deadlocks/races under concurrent `SendAsync`.
- `TypeNameCache` retains all `Type` references for the process lifetime. Acceptable in RPC server scenarios; not a leak in practice (closed type set).
- net8 → net10 bump touches every csproj. Mitigation: single PR, CI run, no functional changes mixed in.

## 10. Out-of-scope (tracked for later)

- Publisher confirms.
- Reply-queue topology recovery on connection recovery.
- DLQ + retry-count headers for poison messages.
- AOT / trimming compatibility.
- xUnit v3 migration.
- Secrets management (RabbitMQ credentials currently in `appsettings.json`).
