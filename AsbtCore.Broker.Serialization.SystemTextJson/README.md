# RabbitRpc.Serialization.SystemTextJson

`System.Text.Json` `IRpcSerializer` adapter for [RabbitRpc](https://github.com/Sardor557/AsbtCore.Broker). One of three serialization adapters available for RabbitRpc 4.x — pick this one for JSON on the wire (debugging, legacy interop, or compatibility with v3 JSON payload shape). For high-throughput production traffic prefer a binary adapter.

## Installation

```bash
dotnet add package RabbitRpc.Serialization.SystemTextJson
dotnet add package RabbitRpc.Client     # or RabbitRpc.Server
```

This package is a sidecar to `RabbitRpc.Client` / `RabbitRpc.Server` — install one (or both) of those alongside it.

## Configuration

Server:

```csharp
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.SystemTextJson;

services.AddRabbitRpcServer(configuration)
        .UseJsonRpcSerialization()
        .Register<IGreeter, GreeterService>();
```

Client:

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Serialization.SystemTextJson;

services.AddRabbitRpcClient(configuration)
        .UseJsonRpcSerialization()
        .AddProxy<IGreeter>();
```

`ContentType` published on `BasicProperties` is `application/json`. Argument and result fragments are emitted as base64-encoded JSON strings.

### Custom options

Both `UseJsonRpcSerialization` extensions accept an optional configure callback for tuning `JsonSerializerOptions`:

```csharp
.UseJsonRpcSerialization(o =>
{
    o.WriteIndented = false;
    o.Converters.Add(new MyDomainConverter());
});
```

Base options are produced by `RpcJson.Build()` — camelCase, case-insensitive property matching, null-write skipped, plus the `ReadOnlyMemoryByteJsonConverter`. Call `RpcJson.Build()` directly if you need the defaults without DI.

## DTO requirements

None beyond what `System.Text.Json` already requires. DTOs are reflected at runtime — plain classes, `record`s, init-only properties, and `IEnumerable<T>` collections all work. No source generator, no `Touch<T>` call sites, no AOT-time codegen step.

## Wire format & performance

- JSON envelope, `application/json` content type.
- Per-argument and per-result fragments are **base64-encoded JSON strings** — adds ~33 % size overhead vs raw bytes.
- Deserialization allocates a fresh `byte[]` per `ReadOnlyMemory<byte>` property; the contract guarantees decoded payloads remain valid after the source buffer is reused.
- Not zero-copy. Throughput-sensitive workloads should use a binary adapter.

## Limitations

- Base64-on-the-wire makes payloads larger and somewhat slower than v3 raw JSON — this is intentional, since v4 fragment payloads are arbitrary bytes (`ReadOnlyMemory<byte>`), and base64 is the canonical JSON-safe encoding.
- AOT/trim require manual `JsonSerializerContext` registration through the configure callback.

## See Also

- [RabbitRpc.Client](https://www.nuget.org/packages/RabbitRpc.Client) — client-side library with typed proxies.
- [RabbitRpc.Server](https://www.nuget.org/packages/RabbitRpc.Server) — server-side library that hosts RPC implementations.
- [RabbitRpc.Serialization.XPacketRpc](https://www.nuget.org/packages/RabbitRpc.Serialization.XPacketRpc) — binary adapter (default since v4.0).
- [RabbitRpc.Serialization.MemoryPack](https://www.nuget.org/packages/RabbitRpc.Serialization.MemoryPack) — MemoryPack binary adapter with reflection-friendly DTO discovery.

## License

MIT
