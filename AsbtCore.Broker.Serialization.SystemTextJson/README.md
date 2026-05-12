# RabbitRpc.Serialization.SystemTextJson

System.Text.Json adapter for [RabbitRpc](https://github.com/Sardor557/AsbtCore.Broker).
Implements `IRpcSerializer` on top of `System.Text.Json` with a `ReadOnlyMemory<byte>`
base64 converter so that RPC payload fragments round-trip through JSON envelopes.
Primary use cases: debugging, legacy interop, and tests. For high-throughput
production traffic prefer `RabbitRpc.Serialization.XPacketRpc`.

## Install

```
dotnet add package RabbitRpc.Serialization.SystemTextJson
```

## Usage

Server:

```csharp
services.AddRabbitRpcServer(opts => { /* ... */ })
        .UseJsonRpcSerialization()
        .Register<IGreeter, GreeterService>();
```

Client (v4.0+):

```csharp
services.AddRabbitRpcClient(opts => { /* ... */ })
        .UseJsonRpcSerialization();
```

Once registered, every published `RpcRequest` / `RpcResponse` carries
`ContentType: application/json` on the AMQP `BasicProperties`. Argument and
result fragments are emitted as base64-encoded JSON strings.

## Performance note

This adapter is **not zero-copy**. Each fragment is base64-encoded on the wire,
adding ~33% size overhead vs raw bytes. Deserialize allocates a fresh
`byte[]` for every `ReadOnlyMemory<byte>` property. The contract guarantee is
that decoded payloads remain valid after the source buffer is reused — handy,
but it costs an allocation per argument.

For production hot paths where throughput matters, switch to
`RabbitRpc.Serialization.XPacketRpc`.

## Custom options

Both `UseJsonRpcSerialization` extensions accept an optional configure callback
for tuning `JsonSerializerOptions`:

```csharp
.UseJsonRpcSerialization(o =>
{
    o.WriteIndented = false;
    o.Converters.Add(new MyDomainConverter());
})
```

The base options are produced by `RpcJson.Build()` — camelCase, case-insensitive
property matching, null-write skipped, plus the `ReadOnlyMemoryByteJsonConverter`.
Call `RpcJson.Build()` directly if you need the defaults without DI.

## License

MIT
