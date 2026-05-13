# MemoryPack Serialization Adapter — Design Spec

**Date:** 2026-05-13  
**Status:** Approved

## Overview

Add a MemoryPack-backed `IRpcSerializer` to the AsbtCore.Broker framework, ship it as a new NuGet package `RabbitRpc.Serialization.MemoryPack`, and migrate the Demo apps from XPacketRpc to MemoryPack.

## Goals

- New project `AsbtCore.Broker.Serialization.MemoryPack` implementing `IRpcSerializer` via MemoryPack.
- `AsbtCore.Broker.Core` stays MemoryPack-free (no `[MemoryPackable]` attributes, no MemoryPack dependency).
- Demo apps (`Test.Broker.API` + `Test.Client`) and shared contracts (`Test.Contracts`) migrated to MemoryPack.
- Unit test project `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests` covering the new adapter.

## Architecture

### New project structure

```
AsbtCore.Broker.Serialization.MemoryPack/
  AsbtCore.Broker.Serialization.MemoryPack.csproj
  MemoryPackRpcSerializer.cs
  Formatters/
    RpcContractFormatters.cs
  DependencyInjection/
    MemoryPackRpcServiceCollectionExtensions.cs
  README.md
```

### Dependencies (csproj)

| Dependency | Notes |
|---|---|
| `MemoryPack` NuGet | Runtime serialization API |
| `MemoryPack.Generator` as Analyzer | Source gen (needed only if this project uses `[MemoryPackable]` itself; included for consistency) |
| `AsbtCore.Broker.Core` `PrivateAssets="all"` | Bundled into NuGet output |
| `AsbtCore.Broker.Client` `PrivateAssets="all"` | Bundled |
| `AsbtCore.Broker.Server` `PrivateAssets="all"` | Bundled |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | DI extensions |

## Components

### MemoryPackRpcSerializer

Implements `IRpcSerializer`. ContentType = `"application/x-memorypack-rpc"`.

Static constructor registers four custom formatters (one-per-process, idempotent-safe):

```csharp
static MemoryPackRpcSerializer()
{
    MemoryPackFormatterProvider.Register(new RpcRequestFormatter());
    MemoryPackFormatterProvider.Register(new RpcArgumentFormatter());
    MemoryPackFormatterProvider.Register(new RpcResponseFormatter());
    MemoryPackFormatterProvider.Register(new RpcErrorFormatter());
}
```

Method routing:

| Method | Implementation |
|---|---|
| `Serialize<T>(T)` | `MemoryPackSerializer.Serialize<T>(value)` |
| `Deserialize<T>(ReadOnlyMemory<byte>)` | `MemoryPackSerializer.Deserialize<T>(payload.Span)` |
| `SerializeFragment(object?, Type)` | `MemoryPackSerializer.Serialize(type, value)` |
| `DeserializeFragment(ReadOnlyMemory<byte>, Type)` | `MemoryPackSerializer.Deserialize(type, payload.Span)` |

### Formatters — RpcContractFormatters.cs

Hand-written `IMemoryPackFormatter<T>` for each Core wire type. Core is not modified.

**Types and their fields:**

`RpcRequest`: `RequestId (string)`, `InterfaceName (string)`, `MethodName (string)`, `Arguments (List<RpcArgument>)`

`RpcArgument`: `TypeName (string)`, `Payload (ReadOnlyMemory<byte>)`

`RpcResponse`: `RequestId (string)`, `Success (bool)`, `ResultTypeName (string?)`, `Result (ReadOnlyMemory<byte>?)`, `Error (RpcError?)`

`RpcError`: `Code (string)`, `Message (string)`, `Details (string?)`, `ExceptionType (string?)`

`ReadOnlyMemory<byte>` fields serialized as length-prefixed byte sequences via `MemoryPackWriter`. Nullable reference/value types use MemoryPack's standard null tag byte convention.

### DependencyInjection

```csharp
public static class MemoryPackRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseMemoryPackRpcSerialization(this RpcServerBuilder builder);
    public static RpcClientBuilder UseMemoryPackRpcSerialization(this RpcClientBuilder builder);
}
```

Both overloads call `TryAddSingleton<IRpcSerializer>(_ => new MemoryPackRpcSerializer())`.

## Test project

**`Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests`** — TUnit + Moq, run via `dotnet run`.

Test coverage:

| Group | Scenarios |
|---|---|
| ContentType | `== "application/x-memorypack-rpc"` |
| RpcRequest round-trip | All fields preserved after serialize→deserialize |
| RpcResponse round-trip (success) | `Success=true`, `Result` bytes set, `Error=null` |
| RpcResponse round-trip (error) | `Success=false`, `Result=null`, `Error` fully populated |
| Nullable fields | `ResultTypeName=null`, `RpcError.Details=null`, `RpcError.ExceptionType=null` |
| Fragment round-trip | `int`, `string`, `bool` via `SerializeFragment`/`DeserializeFragment` |
| Formatter idempotency | Second `new MemoryPackRpcSerializer()` does not throw — `MemoryPackFormatterProvider.Register` silently overwrites on duplicate, which is safe |

## Demo migration

### Test.Contracts

| Change | Detail |
|---|---|
| Delete `GeneratorTouchSites.cs` | XPacketRpc-specific module initializer, no longer needed |
| Remove `XPacketRpc.Generators` Analyzer ref | From `Contracts.csproj` |
| Add `MemoryPack` NuGet + `MemoryPack.Generator` Analyzer | To `Contracts.csproj` |
| Add `[MemoryPackable]` to `UserDto` | Required for MemoryPack source-gen codecs |

### Test.Broker.API

- `Broker.API.csproj`: remove XPacketRpc project ref, add `AsbtCore.Broker.Serialization.MemoryPack` project ref
- `Program.cs`: `UseXPacketRpcSerialization()` → `UseMemoryPackRpcSerialization()`

### Test.Client

- `Client.csproj`: same ref swap as above
- `Program.cs`: same method swap

### Solution file

Add to `RabbitMq.RPC.sln`:
- `AsbtCore.Broker.Serialization.MemoryPack`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests`

## Error handling

`SerializeFragment`/`DeserializeFragment` for a DTO without `[MemoryPackable]` throws `MemoryPackSerializationException` at first call — fail-fast, no silent fallback.

## Wire format note

ContentType `"application/x-memorypack-rpc"` is distinct from `"application/json"` and `"application/x-xpacket-rpc"`. Client and server must use the same adapter or messages are rejected at deserialization.
