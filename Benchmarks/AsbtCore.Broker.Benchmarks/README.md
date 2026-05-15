# AsbtCore.Broker Benchmarks

BenchmarkDotNet suite covering the v4.0 serialization layer, transport hot paths, and RPC round-trip throughput.

## Run all

```
dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter '*'

dotnet run -c Release -- --filter *PayloadSizeBench*
```

## Targeted runs

| Filter | What it measures |
|---|---|
| `*SerializerComparison*` | Envelope `Serialize<RpcRequest>` / `Deserialize<RpcRequest>` and fragment `SerializeFragment` allocation + ns across both adapters (`Json` vs `XPacket`) on representative DTOs. |
| `*FragmentCreation*` | `SerializeFragment` allocation + ns by serializer and DTO shape (`Small`, `Nested`, `List<Small>`). |
| `*RpcRoundTrip*` | Full proxy → in-memory transport → dispatcher → response round-trip latency (no live broker). |
| `*RpcClientInvoker*` | Client-side invoker cache hot path. |
| `*RpcServerInvoker*` | Server-side method-invoker hot path. |
| `*PublishConcurrency*` | Concurrent publish throughput under varying parallelism. |
| `*TypeResolution*` | `StableTypeName.From` / `Resolve` throughput. |

Per-optimization benches expose a `[Params(BenchMode.Legacy, BenchMode.New)]` switch so before/after numbers come from the same harness; `SerializerComparisonBench` and `FragmentCreationBench` parameterise over `Json` and `XPacket` axes.

## Baseline

v4.0 baseline numbers (XPacket vs Json on the smoke configuration) live at `Benchmarks/results/v4.0-baseline.md`. The XPacket adapter wins by roughly **2.5×** on envelope serialize, **3.7×** on deserialize, and **4×** on fragment-level serialize, with ~2.5× lower allocation.

## Acceptance targets

Performance acceptance targets and the design behind the new layer are documented in:

- `docs/superpowers/specs/2026-05-13-binary-serialization-design.md` — v4.0 design
- `docs/superpowers/specs/2026-05-07-rpc-perf-optimization-design.md` — v3 perf baseline
