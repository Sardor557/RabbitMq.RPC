# AsbtCore.Broker — RabbitMQ RPC

[Русская версия](README.ru.md)

A lightweight RPC framework on top of RabbitMQ for .NET 8: type-safe contracts via C# interfaces, DI integration on client and server, JSON serialization, reply-queue pattern.

This repository ships two consumer-facing NuGet packages: **`AsbtCore.Broker.Client`** and **`AsbtCore.Broker.Server`**. Everything else (`Core`, `RabbitMq`) is pulled in transitively.

## Installation

In the consuming app, install the package you need. Dependencies (`AsbtCore.Broker.Core`, `AsbtCore.Broker.RabbitMq`) are resolved automatically.

**Client app** (calls remote services):

```bash
dotnet add package AsbtCore.Broker.Client
```

**Server app** (hosts implementations):

```bash
dotnet add package AsbtCore.Broker.Server
```

**Shared contracts project** — a plain class library with interfaces and DTOs, referenced by both sides. It does not need any `AsbtCore.Broker.*` reference.

Typical solution layout:

```
MySolution/
├─ MyApp.Contracts/        class library (interfaces + DTOs)
├─ MyApp.Server/           references AsbtCore.Broker.Server + Contracts
└─ MyApp.Client/           references AsbtCore.Broker.Client + Contracts
```

## Architecture

```
┌─────────────────┐        RabbitMQ         ┌─────────────────┐
│   Client host   │       (RPC exchange)    │   Server host   │
│                 │                         │                 │
│  IMyService ──► RpcProxy ──► RpcClient ──►│ request queue ──┼──► RpcRequestDispatcher
│                                           │                 │         │
│  result ◄── reply queue ◄── Transport ◄───┼── reply props   │         ▼
│                                           │                 │    impl.Method(args)
└─────────────────┘                         └─────────────────┘
```

### Packages (solution `RabbitMq.RPC.sln`)

| Project | Purpose |
|---|---|
| `AsbtCore.Broker.Core` | Contracts (`RpcRequest`/`RpcResponse`), `IRpcTransport`, `IRpcSerializer`, `IRpcRouteResolver`, `RpcOptions`, `RpcRemoteException`. |
| `AsbtCore.Broker.RabbitMq` | `RabbitMqRpcTransport` (client side), `RabbitMqRpcTransportHost` (server side), `IRabbitMqConnectionProvider`. |
| `AsbtCore.Broker.Client` | `RpcClient`, `RpcProxyFactory` (`DispatchProxy`), DI: `AddRabbitRpcClient` / `AddRpcProxy<T>`. |
| `AsbtCore.Broker.Server` | `RpcServerBuilder`, `RpcServerRegistry`, `RpcRequestDispatcher`, `RpcServerHostedService`, DI: `AddRabbitRpcServer`. |
| `Tests/*` | MSTest + Moq: 42 tests, isolated from real RabbitMQ. |

### Call flow

1. Client invokes a method on the proxy → `RpcProxyFactory` packs arguments into an `RpcRequest`.
2. `RpcClient` → `RabbitMqRpcTransport.SendAsync` publishes to the exchange using routing key `RoutePrefix + FullName(interface)`, sets `CorrelationId` and `ReplyTo` (client's exclusive queue).
3. Server: `RpcServerHostedService` starts `IRpcTransportHost`; for each incoming message it invokes `RpcRequestDispatcher`, which looks up the implementation in `RpcServerRegistry` and calls the method via reflection.
4. The result/exception → `RpcResponse`, published to `ReplyTo` with the same `CorrelationId`. Server exceptions surface on the client as `RpcRemoteException`.

## Configuration (`RpcOptions`, section `Rpc`)

```json
{
  "Rpc": {
    "HostName": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest",
    "ClientProvidedName": "my-app",
    "RoutePrefix": "rpc.",
    "PrefetchCount": 1,
    "DefaultTimeoutSeconds": 30
  }
}
```

## Usage example

### 1. Shared contract

```csharp
// Contracts.csproj
public interface ICalculatorService
{
    Task<int> AddAsync(int a, int b);
    Task<UserDto> GetUserAsync(Guid id);
}

public sealed record UserDto(Guid Id, string Name);
```

### 2. Server

```csharp
// Program.cs (ASP.NET / Worker)
using AsbtCore.Broker.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRabbitRpcServer(builder.Configuration)
    .Register<ICalculatorService, CalculatorService>();

var app = builder.Build();
app.Run();

public sealed class CalculatorService : ICalculatorService
{
    public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
    public Task<UserDto> GetUserAsync(Guid id) => Task.FromResult(new UserDto(id, "Alice"));
}
```

### 3. Client

```csharp
using AsbtCore.Broker.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcClient(builder.Configuration)
    .AddRpcProxy<ICalculatorService>();

var host = builder.Build();

var calc = host.Services.GetRequiredService<ICalculatorService>();
var sum  = await calc.AddAsync(2, 3);         // 5
var user = await calc.GetUserAsync(Guid.NewGuid());
```

## How to add RPC to your project

1. Create the shared `MyApp.Contracts` class library (plain, no broker references).
2. In `MyApp.Server`: `dotnet add package AsbtCore.Broker.Server` + reference `MyApp.Contracts`.
3. In `MyApp.Client`: `dotnet add package AsbtCore.Broker.Client` + reference `MyApp.Contracts`.
4. Add the `Rpc` section to `appsettings.json` on both sides (see [Configuration](#configuration-rpcoptions-section-rpc)).
5. Wire up DI (see [Usage example](#usage-example) below).

## How to add a new RPC service

1. **Contract** — add the interface (`Task` / `Task<T>` methods) and DTO types to the shared `*.Contracts` project. Payloads are serialized with `System.Text.Json`.
2. **Server** — implement the interface and register it:
   ```csharp
   services.AddRabbitRpcServer(configuration)
           .Register<IMyService, MyService>();
   ```
3. **Client** — register a proxy:
   ```csharp
   services.AddRabbitRpcClient(configuration)
           .AddRpcProxy<IMyService>();
   ```
4. Client and server must use the same `RoutePrefix` and interface namespace (routing key = `RoutePrefix + typeof(T).FullName`).

## Error handling

An exception thrown inside the server implementation is serialized and rethrown on the client as `RpcRemoteException`:

```csharp
try { await calc.AddAsync(1, 2); }
catch (RpcRemoteException ex)
{
    // ex.RemoteExceptionType, ex.RemoteCode, ex.RemoteDetails
}
```

Timeout: `DefaultTimeoutSeconds` → `TaskCanceledException`.

## Testing

```bash
dotnet test RabbitMq.RPC.sln
```

42 tests (Core.Tests — serialization / routing / transport via mocked `IChannel`; ClientServer.Tests — RpcClient / Proxy / Registry / Dispatcher / HostedService). No real RabbitMQ connection required.

## Requirements

- .NET 8
- RabbitMQ 3.12+ (RabbitMQ.Client 7.x)

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
