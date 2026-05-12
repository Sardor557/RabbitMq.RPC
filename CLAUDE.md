# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`AsbtCore.Broker` — a lightweight RPC framework over RabbitMQ for .NET 10. Type-safe contracts via C# interfaces, DI integration on both sides, JSON serialization, reply-queue pattern, publisher confirms, per-route dead-letter queues. Ships two consumer-facing NuGet packages: **`RabbitRpc.Client`** and **`RabbitRpc.Server`** (current version **3.1.0**).

Requires .NET 10 SDK and RabbitMQ 3.12+ / `RabbitMQ.Client` 7.x.

## Architecture

Four internal projects with a strict, one-way dependency graph:

```
AsbtCore.Broker.Client ─┐
                        ├─► AsbtCore.Broker.RabbitMq ─► AsbtCore.Broker.Core
AsbtCore.Broker.Server ─┘
```

- **`AsbtCore.Broker.Core`** — abstractions only (`IRpcTransport`, `IRpcTransportHost`, `IRpcSerializer`, `IRpcRouteResolver`), wire types (`RpcRequest` / `RpcResponse` in `RpcContracts.cs`), `RpcOptions`, exception hierarchy (`RpcRemoteException`, `RpcProtocolException`), `StableTypeName`, JSON serializer. `IsPackable=false` — never shipped directly.
- **`AsbtCore.Broker.RabbitMq`** — only RabbitMQ-specific code: `RabbitMqRpcTransport` (client side), `RabbitMqRpcTransportHost` (server side), `IRabbitMqConnectionProvider`. Also non-packable.
- **`AsbtCore.Broker.Client`** — `RpcClient`, `RpcProxyFactory` (built on `DispatchProxy`), `RpcClientInvokerCache`, DI extensions `AddRabbitRpcClient` / `AddRpcProxy<T>`. Packaged as `RabbitRpc.Client`.
- **`AsbtCore.Broker.Server`** — `RpcServerBuilder`, `RpcServerRegistry`, `RpcRequestDispatcher`, `RpcServerMethodInvoker`, `RpcServerHostedService`, DI extension `AddRabbitRpcServer`. Packaged as `RabbitRpc.Server`.

Both shipping packages reference `Core` and `RabbitMq` with `PrivateAssets="all"` and bundle their DLLs via the `IncludeReferencedProjectsInPackage` MSBuild target — consumers only ever see one package per side. When modifying `Core` or `RabbitMq` public surface, both shipping packages re-emit those DLLs.

### RPC call flow

Client `IFoo.BarAsync(args)` → `DispatchProxy` → `RpcClient.InvokeProxy` → `BasicPublish` to `rpc.IFoo` with `replyTo=rpc-reply-{name}-{guid}` and `correlationId=requestId` → server `RpcServerHostedService` receives → `RpcRequestDispatcher.DispatchAsync` → reflective invoke via `RpcServerMethodInvoker` → response published to the client's reply queue → proxy resolves the `Task<T>`.

- Routing key = `RpcOptions.RoutePrefix + typeof(TInterface).FullName` (default prefix `rpc.`). **Both sides must agree on prefix and interface namespace** or routes won't match.
- Each route has a companion durable DLQ `{route}.dead`. Poison messages (deserialization failures, unresolvable types, dispatcher errors) move there after a **single** attempt — there are no requeue loops. Monitor `*.dead` queue depth for alerts.
- Server-side exceptions are serialized and rethrown on the client as `RpcRemoteException` (`RemoteExceptionType`, `RemoteCode`, `RemoteDetails`). Timeouts surface as `TaskCanceledException` from `RpcOptions.DefaultTimeoutSeconds`.

### Critical version-specific behavior (v3.0 → v3.1)

- **Server message dispatch is parallel by default.** `RpcOptions.ConsumerDispatchConcurrency` defaults to `PrefetchCount` (which itself defaults to 1, so single-channel deployments stay sequential unless they raise `PrefetchCount`). When `ConsumerDispatchConcurrency > 1`, handlers must be thread-safe. Set it explicitly to `1` to force v3.0 sequential behavior.
- `RpcRequest.RequestId` is **not** auto-initialized — client code that constructs `RpcRequest` directly must set it explicitly.
- `IRpcTransportHost.StopAsync` (default interface method) drains in-flight handlers and `BasicCancel`s consumers before disposal. Custom transports should override it; prefer `DisposeAsync()` over `Dispose()`.
- Poison reply handling: `OnResponseReceivedAsync` surfaces deserialization errors to the awaiting caller (no silent timeout-wait).

## Configuration

`RpcOptions` is bound from configuration section `RabbitMqRpc`:

```json
{
  "RabbitMqRpc": {
    "HostName": "localhost", "Port": 5672, "VirtualHost": "/",
    "UserName": "guest", "Password": "guest",
    "ClientProvidedName": "my-app",
    "RoutePrefix": "rpc.",
    "PrefetchCount": 1,
    "ConsumerDispatchConcurrency": null,
    "DefaultTimeoutSeconds": 30
  }
}
```

All `[Required]` / `[Range]` annotations are validated on startup via `Microsoft.Extensions.Options.DataAnnotations`.

## Adding a new RPC service

1. Add interface (must return `Task` or `Task<T>`) + DTOs to a **shared contracts class library** that has **no `AsbtCore.Broker.*` references**.
2. Server: `services.AddRabbitRpcServer(configuration).Register<IMyService, MyServiceImpl>();`
3. Client: `services.AddRabbitRpcClient(configuration).AddRpcProxy<IMyService>();`
4. Restart both sides — server declares the route queue + `{route}.dead` DLQ on startup.

## Build, test, benchmark

```bash
# Build solution
dotnet build RabbitMq.RPC.sln

# Run all tests (TUnit-based — uses `dotnet run`, NOT `dotnet test`)
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj

# Run a single test (TUnit filter syntax)
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj -- --treenode-filter "/*/*/JsonRpcSerializerTests/*"

# Coverage (use the included runsettings — it excludes test DLLs)
dotnet test RabbitMq.RPC.sln --settings coverage.runsettings

# Benchmarks (BenchmarkDotNet — must be Release)
dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks
```

Tests use **TUnit + Moq** and run without a real RabbitMQ broker. Project layout: `Core.Tests` (~45 tests) covers serialization, routing, options, transport host behavior; `ClientServer.Tests` (~38 tests) covers proxy factory, client invoker cache, server dispatcher/registry/hosted service.

The `RabbitMqRpcTransport` / `RabbitMqConnectionProvider` classes need a live broker — they're integration-test territory and are not covered by the unit suite.

### Test infrastructure quirks

- Both shipping packages have `<InternalsVisibleTo Include="AsbtCore.Broker.ClientServer.Tests" />`. `Core` is `InternalsVisibleTo` for every other project including `AsbtCore.Broker.Benchmarks`. Internal types are deliberately exposed to tests/benches — don't widen visibility just to test something.
- `Tests/AsbtCore.Broker.ClientServer.Tests/Fixtures/` provides `TestContracts`, `TestDispatcherFactory`, `TimeoutCapturingTransport` — reuse these instead of building new test doubles.
- `coverage.runsettings` includes only the four production DLLs (`Core`, `Client`, `Server`, `RabbitMq`); do not add test projects to its `<Include>` list.

## Demo apps

`Test.Broker.API` (ASP.NET Core server host) + `Test.Client` (console client) + `Test.Contracts` (shared interfaces) are smoke-test apps, not production references. They require a running RabbitMQ instance reachable per `appsettings.json`. They are not part of the test suite and shouldn't be touched when modifying the framework unless verifying an end-to-end change.

## Conventions

- All projects target `net10.0` with `ImplicitUsings` and `Nullable` enabled.
- Namespaces match folder structure under `AsbtCore.Broker.{Core,RabbitMq,Client,Server}`.
- `Core` must stay broker-agnostic — if a change references `RabbitMQ.Client`, it belongs in `AsbtCore.Broker.RabbitMq`.
- Public-facing changes in shipping packages need a `<PackageReleaseNotes>` bump in both `AsbtCore.Broker.Client.csproj` and `AsbtCore.Broker.Server.csproj` and a corresponding "Migration" section in `README.md` / `README.ru.md` (both kept in sync).
