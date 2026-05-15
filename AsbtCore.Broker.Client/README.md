# RabbitRpc.Client

Client-side library for RabbitMQ RPC on .NET 10. Define a contract interface, register a typed proxy in DI, and call its methods like any local service — the request is sent via RabbitMQ and the response is awaited from the server.

**v4.0** introduces a pluggable serialization layer — you must pair this package with **one** of the adapter packages:

- [`RabbitRpc.Serialization.XPacketRpc`](https://www.nuget.org/packages/RabbitRpc.Serialization.XPacketRpc) — binary, source-gen, recommended default.
- [`RabbitRpc.Serialization.MemoryPack`](https://www.nuget.org/packages/RabbitRpc.Serialization.MemoryPack) — binary, MemoryPack-backed; works with vendor DTOs without `[MemoryPackable]`.
- [`RabbitRpc.Serialization.SystemTextJson`](https://www.nuget.org/packages/RabbitRpc.Serialization.SystemTextJson) — JSON, for debugging and v3 wire compatibility.

## Installation

```bash
dotnet add package RabbitRpc.Client
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
    "ClientProvidedName": "rabbit-rpc-client",
    "RoutePrefix": "rpc.",
    "PrefetchCount": 1,
    "DefaultTimeoutSeconds": 30
  }
}
```

## Usage

Define a contract interface (shared between client and server, no `CancellationToken` parameters — timeouts live on the transport):

```csharp
public interface IMathService
{
    Task<int>     AddAsync(int a, int b);
    Task<UserDto> GetUserAsync(Guid id);
}

public sealed record UserDto(Guid Id, string Name);
```

Register the client and a typed proxy in `Program.cs`:

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Serialization.XPacketRpc;   // pick one adapter

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcClient(builder.Configuration)    // returns RpcClientBuilder
    .UseXPacketRpcSerialization()                 // required in v4.0
    .AddProxy<IMathService>();                    // was AddRpcProxy<T> in v3
```

Inject and use the proxy like any other DI service:

```csharp
public sealed class CalculatorController(IMathService math) : ControllerBase
{
    [HttpGet("add")]
    public Task<int> Add(int a, int b) => math.AddAsync(a, b);
}
```

The per-call timeout defaults to `RpcOptions.DefaultTimeoutSeconds`; expired calls surface as `TaskCanceledException`. Server-side exceptions arrive as `RpcRemoteException` with `RemoteCode` / `RemoteExceptionType` / `RemoteDetails` populated.

## Migration v3.x → v4.0

| Was (v3.x)                              | Now (v4.0)                                                       |
|-----------------------------------------|------------------------------------------------------------------|
| `AddRabbitRpcClient(cfg)` returns `IServiceCollection` | returns `RpcClientBuilder`                                       |
| `services.AddRpcProxy<T>()` extension   | `builder.AddProxy<T>()` on `RpcClientBuilder`                    |
| Default JSON wired automatically        | Adapter package + `.UseXxxSerialization()` is **required**       |
| `JsonElement` payload in `RpcRequest`   | `ReadOnlyMemory<byte>` (only matters for custom transports)      |

See the [repo migration guide](https://github.com/Sardor557/AsbtCore.Broker#migration-v31--v40) for the full break list.

## See Also

- [RabbitRpc.Server](https://www.nuget.org/packages/RabbitRpc.Server) — server-side library that hosts RPC implementations.
- [RabbitRpc.Serialization.XPacketRpc](https://www.nuget.org/packages/RabbitRpc.Serialization.XPacketRpc) — binary adapter (default since v4.0).
- [RabbitRpc.Serialization.MemoryPack](https://www.nuget.org/packages/RabbitRpc.Serialization.MemoryPack) — MemoryPack binary adapter with reflection-friendly DTO discovery.
- [RabbitRpc.Serialization.SystemTextJson](https://www.nuget.org/packages/RabbitRpc.Serialization.SystemTextJson) — JSON adapter for v3 wire compatibility.
