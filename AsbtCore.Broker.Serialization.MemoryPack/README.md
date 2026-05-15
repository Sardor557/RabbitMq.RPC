# RabbitRpc.Serialization.MemoryPack

[MemoryPack](https://github.com/Cysharp/MemoryPack) binary `IRpcSerializer` adapter for [RabbitRpc](https://github.com/Sardor557/AsbtCore.Broker). One of three serialization adapters available for RabbitRpc 4.x — pick this one when you need a compact binary wire format **and** want to keep working with vendor DTOs that you cannot decorate with `[MemoryPackable]`.

## Installation

```bash
dotnet add package RabbitRpc.Serialization.MemoryPack
dotnet add package RabbitRpc.Client     # or RabbitRpc.Server
```

This package is a sidecar to `RabbitRpc.Client` / `RabbitRpc.Server` — install one (or both) of those alongside it.

## Configuration

Server:

```csharp
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.MemoryPack;

services.AddRabbitRpcServer(configuration)
        .UseMemoryPackRpcSerialization()
        .Register<IMyService, MyServiceImpl>();
```

Client:

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Serialization.MemoryPack;

services.AddRabbitRpcClient(configuration)
        .UseMemoryPackRpcSerialization()
        .AddProxy<IMyService>();
```

`ContentType` published on `BasicProperties` is `application/x-memorypack`.

## DTO requirements

Two supported paths — mix freely in the same contract:

**1. Source-generated (`[MemoryPackable]`) — recommended for hot paths.** Declare DTOs `partial` and decorate; MemoryPack emits codecs at compile time. AOT/trim-safe.

```csharp
[MemoryPackable]
public sealed partial class UserDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
```

**2. Reflection-built (no attribute).** Plain DTOs, records, and init-only POCOs are auto-discovered on first use. Wire layout matches the declaration-order source-gen for compatible types. Useful for vendor DTOs you cannot modify. Not AOT/trim-safe.

For polymorphism, register the union explicitly — MemoryPack cannot infer derived types from a base reference at runtime:

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

## Wire format & performance

- Binary, length-prefixed; identical bytes for `[MemoryPackable]` and reflection-built paths for the same logical type.
- `[MemoryPackable]` source-gen: zero-allocation hot paths, fastest of the three adapters for known-shape DTOs.
- Reflection-built: estimated **~2-4× slower** than source-gen on the same DTO, **still measurably faster than JSON**. (Architectural estimate; no benchmark on this codepath yet.)
- `PrewarmType` / `PrewarmAssembly` move the per-type formatter build off the hot path to startup.

## Limitations

- Reflection-built path is **not** AOT or trim safe. Compile with `PublishAot=true` only when every DTO is `[MemoryPackable]`.
- Polymorphic base types **must** be registered with `RegisterUnion<T>(...)`; runtime discovery cannot infer the union table.
- Cycle detection uses thread-static state; no cross-thread aliasing in a single serialization call (this matters only for custom transports).

## See Also

- [RabbitRpc.Client](https://www.nuget.org/packages/RabbitRpc.Client) — client-side library with typed proxies.
- [RabbitRpc.Server](https://www.nuget.org/packages/RabbitRpc.Server) — server-side library that hosts RPC implementations.
- [RabbitRpc.Serialization.XPacketRpc](https://www.nuget.org/packages/RabbitRpc.Serialization.XPacketRpc) — sibling binary adapter, source-gen via XPacketRpc.
- [RabbitRpc.Serialization.SystemTextJson](https://www.nuget.org/packages/RabbitRpc.Serialization.SystemTextJson) — JSON adapter for v3 wire compatibility and debugging.

## License

MIT
