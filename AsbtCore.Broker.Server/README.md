# RabbitRpc.Server

Server-side library for RabbitMQ RPC on .NET 10. Register implementations of contract interfaces with DI; a hosted service listens on the configured RabbitMQ route and dispatches each incoming request to the matching method.

**v4.0** introduces a pluggable serialization layer — you must pair this package with **one** of the adapter packages:

- [`RabbitRpc.Serialization.XPacketRpc`](https://www.nuget.org/packages/RabbitRpc.Serialization.XPacketRpc) — binary, source-gen, recommended default.
- [`RabbitRpc.Serialization.MemoryPack`](https://www.nuget.org/packages/RabbitRpc.Serialization.MemoryPack) — binary, MemoryPack-backed; works with vendor DTOs without `[MemoryPackable]`.
- [`RabbitRpc.Serialization.SystemTextJson`](https://www.nuget.org/packages/RabbitRpc.Serialization.SystemTextJson) — JSON, for debugging and v3 wire compatibility.

## Installation

```bash
dotnet add package RabbitRpc.Server
dotnet add package RabbitRpc.Serialization.XPacketRpc      # or .MemoryPack / .SystemTextJson
```

## Pack for NuGet

```bash
dotnet pack -c Release
```

## Configuration

Add the `RabbitMqRpc` section to `appsettings.json`:

```json
{
  "RabbitMqRpc": {
    "HostName": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest",
    "ClientProvidedName": "rabbit-rpc-server",
    "RoutePrefix": "rpc.",
    "PrefetchCount": 1,
    "ConsumerDispatchConcurrency": null,
    "DefaultTimeoutSeconds": 30
  }
}
```

`ConsumerDispatchConcurrency` defaults to `PrefetchCount`; handlers must be thread-safe when it is `> 1`. Set it to `1` for the v3.0 sequential-dispatch behaviour.

## Usage

Define a contract interface (shared between client and server) and implement it on the server:

```csharp
public interface IMathService
{
    Task<int>     AddAsync(int a, int b);
    Task<UserDto> GetUserAsync(Guid id);
}

public sealed class MathService : IMathService
{
    public Task<int>     AddAsync(int a, int b) => Task.FromResult(a + b);
    public Task<UserDto> GetUserAsync(Guid id)  => Task.FromResult(new UserDto(id, "Alice"));
}
```

Register the server and handlers in `Program.cs`:

```csharp
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.XPacketRpc;   // pick one adapter

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcServer(builder.Configuration)    // returns RpcServerBuilder
    .UseXPacketRpcSerialization()                 // required in v4.0
    .Register<IMathService, MathService>();

await builder.Build().RunAsync();
```

The hosted service declares one durable queue per RPC route (`rpc.<InterfaceFullName>`), one companion dead-letter queue (`<route>.dead`), and starts consuming. Server-side exceptions are serialized and rethrown on the client as `RpcRemoteException`. Poison messages (deserialization failure, unresolvable type, dispatcher error) are routed to the DLQ after **a single attempt** — no requeue loops; monitor `*.dead` depth for alerting.

## Migration v3.x → v4.0

| Was (v3.x)                              | Now (v4.0)                                                       |
|-----------------------------------------|------------------------------------------------------------------|
| `AddRabbitRpcServer(cfg)` returns `RpcServerBuilder`, defaults JSON | returns `RpcServerBuilder`, NO default serializer                 |
| `services.AddRpcSerialization()`        | `builder.UseXxxSerialization()` from an adapter package          |
| `JsonElement` result in `RpcResponse`   | `ReadOnlyMemory<byte>?`                                          |
| `RpcRequestDispatcher` ctor with 2 args | ctor takes `IRpcSerializer` as a third parameter                 |

Startup throws `OptionsValidationException` with a helpful message if no `IRpcSerializer` is registered. See the [repo migration guide](https://github.com/Sardor557/AsbtCore.Broker#migration-v31--v40) for the full break list.

## See Also

- [RabbitRpc.Client](https://www.nuget.org/packages/RabbitRpc.Client) — client-side library with typed proxies.
- [RabbitRpc.Serialization.XPacketRpc](https://www.nuget.org/packages/RabbitRpc.Serialization.XPacketRpc) — binary adapter (default since v4.0).
- [RabbitRpc.Serialization.MemoryPack](https://www.nuget.org/packages/RabbitRpc.Serialization.MemoryPack) — MemoryPack binary adapter with reflection-friendly DTO discovery.
- [RabbitRpc.Serialization.SystemTextJson](https://www.nuget.org/packages/RabbitRpc.Serialization.SystemTextJson) — JSON adapter for v3 wire compatibility.
