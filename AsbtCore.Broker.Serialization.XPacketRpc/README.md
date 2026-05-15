# RabbitRpc.Serialization.XPacketRpc

Binary `IRpcSerializer` adapter for [RabbitRpc](https://github.com/Sardor557/AsbtCore.Broker), backed by the [XPacketRpc](https://github.com/tdav/XProtokol) source-generated codec library. One of three serialization adapters available for RabbitRpc 4.x — this is the **default** binary adapter for new and high-throughput deployments. Ships a per-type compiled fragment-invoker cache and a primitive-codec bootstrap so that `int Add(int, int)`-style contracts work out of the box.

## Installation

```bash
dotnet add package RabbitRpc.Serialization.XPacketRpc
dotnet add package RabbitRpc.Client     # or RabbitRpc.Server
```

## Pack for NuGet

```bash
dotnet pack -c Release
```

This package is a sidecar to `RabbitRpc.Client` / `RabbitRpc.Server` — install one (or both) of those alongside it.

## Configuration

Server:

```csharp
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.XPacketRpc;

services.AddRabbitRpcServer(configuration)
        .UseXPacketRpcSerialization()
        .Register<IUserService, UserService>();
```

Client:

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Serialization.XPacketRpc;

services.AddRabbitRpcClient(configuration)
        .UseXPacketRpcSerialization()
        .AddProxy<IUserService>();
```

`ContentType` published on `BasicProperties` is `application/x-xpacket-rpc`.

### Why a bootstrap library?

The XPacketRpc source generator deliberately skips every type whose namespace starts with `System` (see `IsBuiltinSkipType` in `XPacketRpcGenerator.cs`). DTOs work because the generator emits *inline* reads/writes for primitive properties — but the top-level `XPRpc.Write<T>` entrypoint for primitive `T` would throw `MissingSerializerException`. `XPacketRpcSerializer` hand-registers byte-for-byte identical codecs for the following types on first construction (idempotent and thread-safe):

| Type | Wire format |
|---|---|
| `bool` | 1 byte (`0`/`1`) |
| `byte` / `sbyte` | 1 byte |
| `short` / `ushort` | 2 bytes LE |
| `int` / `uint` | 4 bytes LE |
| `long` / `ulong` | 8 bytes LE |
| `float` | 4 bytes LE (IEEE 754 single) |
| `double` | 8 bytes LE (IEEE 754 double) |
| `decimal` | 16 bytes LE (4 × `int` from `decimal.GetBits`) |
| `string` | LEB128 varint length + UTF-8 bytes |
| `Guid` | 16 bytes LE |
| `DateTime` | 8 bytes LE ticks + 1 byte `Kind` |
| `DateTimeOffset` | 8 bytes LE ticks + 2 bytes LE offset (minutes) |
| `TimeSpan` | 8 bytes LE ticks |
| `byte[]` | LEB128 varint length + raw bytes |
| `ReadOnlyMemory<byte>` | LEB128 varint length + raw bytes |

## DTO requirements

User DTOs are serialized by codecs that the XPacketRpc source generator emits at compile time. The generator scans for any `XPRpc.Touch<T>()`, `XPRpc.Write<T>()`, `XPRpc.Read<T>()`, `IRpcSerializer.Serialize<T>()` or `IRpcSerializer.Deserialize<T>()` call site and writes the codec into a module initializer.

To force codec emission for a DTO that is only fragmented at runtime (typed by `System.Type` rather than a generic argument), call `XPRpc.Touch<MyDto>()` once in the owning assembly — or use `XPacketRpcSerializer.Prewarm(typeof(IMyService))` at startup to walk the contract signature transitively.

In your shared contracts project, reference the generator as an analyzer:

```xml
<ProjectReference Include="..\.external\XProtokol\XPacketRpc\XPacketRpc.csproj" />
<ProjectReference Include="..\.external\XProtokol\XPacketRpc.Generators\XPacketRpc.Generators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

…and touch every DTO from a module initializer:

```csharp
using System.Runtime.CompilerServices;
using XPacketRpc;

internal static class GeneratorTouchSites
{
    [ModuleInitializer]
    internal static void TouchAll()
    {
        XPRpc.Touch<UserDto>();
        XPRpc.Touch<OrderDto>();
    }
}
```

Test projects that exercise XPacketRpc fragmentation must reference `XPacketRpc.Generators.csproj` with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` for in-test DTO Touch sites to take effect.

## Wire format & performance

- Compact binary; per-property inline read/write — no field tags, no length prefixes between properties.
- `SerializeFragment` / `DeserializeFragment` route through a per-`Type` compiled invoker (`FragmentInvokerCache`). The invoker is built exactly once per type, even under concurrent first-touch, via `ConcurrentDictionary<Type, Lazy<FragmentInvoker>>` with `LazyThreadSafetyMode.ExecutionAndPublication`.
- Fastest among the three adapters for known-shape DTOs (source-gen, no reflection on the hot path).

## Limitations

- `null` string fragments are coerced to `string.Empty` to match the generator's inline emit, which collapses `null`/empty to a single `0` length byte.
- Collection types (lists, dictionaries, arrays of non-`byte`) are not bootstrap-registered here — rely on the source generator's inline emission inside enclosing DTOs.
- DTO codecs are emitted only for types reachable from a `Touch<T>`/`Serialize<T>`/`Deserialize<T>` call site in the **same compilation** as the DTO. Add a module initializer in the contracts assembly.

## See Also

- [RabbitRpc.Client](https://www.nuget.org/packages/RabbitRpc.Client) — client-side library with typed proxies.
- [RabbitRpc.Server](https://www.nuget.org/packages/RabbitRpc.Server) — server-side library that hosts RPC implementations.
- [RabbitRpc.Serialization.MemoryPack](https://www.nuget.org/packages/RabbitRpc.Serialization.MemoryPack) — MemoryPack binary adapter with reflection-friendly DTO discovery.
- [RabbitRpc.Serialization.SystemTextJson](https://www.nuget.org/packages/RabbitRpc.Serialization.SystemTextJson) — JSON adapter for debugging and v3 wire compatibility.

## License

MIT
