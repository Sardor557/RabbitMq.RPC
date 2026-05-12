# RabbitRpc.Serialization.XPacketRpc

Binary `IRpcSerializer` adapter for the RabbitRpc client/server, backed by the
[XPacketRpc](https://github.com/tdav/XProtokol) library. Ships a per-type compiled
fragment invoker cache and a primitive-codec bootstrap so that `int Add(int, int)`-style
contracts work out of the box.

## Why a bootstrap library?

The XPacketRpc source generator deliberately skips every type whose namespace starts with
`System` (see `IsBuiltinSkipType` in `XPacketRpcGenerator.cs`). DTOs work because the
generator emits *inline* reads/writes for primitive properties — but the top-level
`XPRpc.Write<T>` entrypoint for primitive `T` would throw `MissingSerializerException`.

`XPacketRpcSerializer` hand-registers byte-for-byte identical codecs for the following
types on first construction (idempotent and thread-safe):

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

## Usage

```csharp
services.AddRabbitRpcServer(broker => broker
    .Register<IUserService, UserService>())
    .UseXPacketRpcSerialization();

// client side
services.AddRabbitRpcClient(broker => broker
    .AddProxy<IUserService>())
    .UseXPacketRpcSerialization();
```

`ContentType` published on `BasicProperties` is `application/x-xpacket-rpc`.

## DTOs

User DTOs are serialized by codecs that the XPacketRpc source generator emits at compile
time. The generator scans for any `XPRpc.Touch<T>()`, `XPRpc.Write<T>()`, `XPRpc.Read<T>()`,
`IRpcSerializer.Serialize<T>()` or `IRpcSerializer.Deserialize<T>()` call site and writes
the codec into a module initializer. To force codec emission for a DTO that is only
fragmented at runtime (typed by `System.Type` rather than a generic argument), call
`XPRpc.Touch<MyDto>()` once in the owning assembly — or use
`XPacketRpcSerializer.Prewarm(typeof(IMyService))` at startup to walk the contract
signature transitively.

The test project must reference `XPacketRpc.Generators.csproj` with
`OutputItemType="Analyzer" ReferenceOutputAssembly="false"` for in-test DTO Touch sites to
take effect.

## Fragment cache

`SerializeFragment` / `DeserializeFragment` route through a per-`Type` compiled invoker
(`FragmentInvokerCache`). The invoker is built exactly once per type, even under
concurrent first-touch, via `ConcurrentDictionary<Type, Lazy<FragmentInvoker>>` with
`LazyThreadSafetyMode.ExecutionAndPublication`.

## Limitations

* `null` string fragments are coerced to `string.Empty` to match the generator's inline
  emit, which collapses `null`/empty to a single `0` length byte.
* Collection types (lists, dictionaries, arrays of non-`byte`) are not bootstrap-registered
  here — rely on the source generator's inline emission inside enclosing DTOs.
