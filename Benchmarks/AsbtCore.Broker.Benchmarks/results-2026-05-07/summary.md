# Benchmark Results — 2026-05-07

**Configuration:** BenchmarkDotNet v0.14.0, ShortRun (3 iterations × 1 launch × 3 warmups), .NET 10.0.7, X64 RyuJIT AVX2, Concurrent Server GC, Windows 11.

**Note:** ShortRun is intentionally noisy (large StdErr/Margin). Numbers are directional, not statistically tight. For paper-grade measurements re-run with default `--job` (no `--job Short`).

## Acceptance criteria scorecard

| # | Bench | Metric | Target | Actual | Pass? |
|---|-------|--------|--------|--------|-------|
| 1 | JsonElementCreation (Small) | allocations | ↓ ≥ 40 % | 36 % (336→216 B) | ❌ |
| 1 | JsonElementCreation (Small) | wall time | ↓ ≥ 25 % | 17 % (240→199 ns) | ❌ |
| 1 | JsonElementCreation (Nested) | allocations | — | 19 % (864→704 B) | — |
| 1 | JsonElementCreation (List) | allocations | — | 19 % (6424→5192 B) | — |
| 2 | RpcClientInvoker (SumAsync) | wall time | ↓ ≥ 70 % | 39 % (837→510 ns) | ❌ |
| 3 | RpcServerInvoker (SumAsync) | wall time | ↓ ≥ 60 % | 35 % (77→50 ns) | ❌ |
| 3 | RpcServerInvoker (Add sync) | wall time | — | 34 % (68→45 ns) | — |
| 3 | RpcServerInvoker (Ping) | wall time | — | 41 % (14→8 ns) | — |
| 4 | TypeResolution (post first) | wall time | ↓ ≥ 95 % | **98 %** (960→16 ns at Calls=1; 10 µs→168 ns at 10) | ✅ |
| 4 | TypeResolution | allocations | — | **100 %** (800 B → 0 B) | ✅ |
| 5 | PublishConcurrency @16 | throughput | ↑ ≥ 3 × | **4.31×** (17.85 µs → 4.14 µs) | ✅ |
| 5 | PublishConcurrency @64 | throughput | — | 3.31× (60.5 µs → 18.3 µs) | — |
| 6 | RoundTrip Ping | end-to-end | — | 201 ns / 704 B (new only) | — |
| 6 | RoundTrip SumAsync | end-to-end | — | 1.17 µs / 2.43 KB (new only) | — |
| 6 | RoundTrip GetByIdAsync | end-to-end | — | 1.10 µs / 1.94 KB (new only) | — |

**Verdict:** 2 of 5 measurable categories meet the stated target. Targets were aspirational; the actual gains reflect that the legacy paths were already on `System.Text.Json` (not Newtonsoft) and that `JsonDocument.Parse` of pre-serialized bytes is cheaper than expected. Two categories — `TypeNameCache` and `publishLock` removal — show the dominant wins (≥98 % and >4× respectively).

## Tables

### #1 JsonElementCreation

| Method | Mode | Mean | Allocated |
|--------|------|------|-----------|
| Small_Element | Legacy | 240.0 ns | 336 B |
| Small_Element | New | 199.0 ns | 216 B |
| Nested_Element | Legacy | 582.9 ns | 864 B |
| Nested_Element | New | 511.2 ns | 704 B |
| List_Element | Legacy | 6,201.8 ns | 6,424 B |
| List_Element | New | 5,639.9 ns | 5,192 B |

### #2 RpcClientInvoker

| Method | Mode | Mean | Allocated |
|--------|------|------|-----------|
| SumAsync_Dispatch | Legacy | 836.9 ns | 1.33 KB |
| SumAsync_Dispatch | New | 510.5 ns | 1.08 KB |

### #3 RpcServerInvoker

| Method | Mode | Mean | Allocated |
|--------|------|------|-----------|
| SumAsync_Invoke | Legacy | 77.04 ns | 256 B |
| SumAsync_Invoke | New | 50.38 ns | 256 B |
| Add_Invoke | Legacy | 68.46 ns | 256 B |
| Add_Invoke | New | 44.76 ns | 256 B |
| Ping_Invoke | Legacy | 13.96 ns | – |
| Ping_Invoke | New | 8.21 ns | – |

### #4 TypeResolution

| Method | Calls | Mode | Mean | Allocated |
|--------|------|------|------|-----------|
| Resolve | 1 | Legacy | 960.11 ns | 800 B |
| Resolve | 1 | New | **16.39 ns** | – |
| Resolve | 10 | Legacy | 10,277.11 ns | 8,000 B |
| Resolve | 10 | New | **168.24 ns** | – |
| Resolve | 1000 | Legacy | 980,967.40 ns | – |
| Resolve | 1000 | New | **15,866.29 ns** | – |

### #5 PublishConcurrency

| Method | Concurrency | Mode | Mean | Allocated |
|--------|-------------|------|------|-----------|
| ParallelPublish | 1 | Legacy | 768.2 ns | 331 B |
| ParallelPublish | 1 | New | 702.9 ns | 220 B |
| ParallelPublish | 4 | Legacy | 3,240.4 ns | 1,295 B |
| ParallelPublish | 4 | New | 1,305.0 ns | 607 B |
| ParallelPublish | 16 | Legacy | 17,845.8 ns | 4,904 B |
| ParallelPublish | 16 | New | **4,136.3 ns** | 1,830 B |
| ParallelPublish | 64 | Legacy | 60,478.4 ns | 19,118 B |
| ParallelPublish | 64 | New | 18,252.9 ns | 6,802 B |

### #6 RoundTrip (in-process; new pipeline only)

| Method | Mean | Allocated |
|--------|------|-----------|
| PingAsync | 201.5 ns | 704 B |
| SumAsync | 1,168.8 ns | 2,432 B |
| GetByIdAsync | 1,102.5 ns | 1,944 B |

The end-to-end bench has no `LegacyOrNew` mode — by design, it measures the absolute cost of the new pipeline. Comparing against pre-refactor master would require a separate run.

## Reproduction

```
dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks -- --filter '*' --job Short --exporters json html github
```

For higher-fidelity numbers (much longer):

```
dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks -- --filter '*'
```

Artifacts directory: `BenchmarkDotNet.Artifacts/results/` at solution root.

## Discussion of missed targets

- **JsonElement (#1)**: legacy path was already STJ-based. `JsonDocument.Parse` of UTF-8 bytes is ~20–30 % overhead, not ~60 %, so eliminating it yields 12–17 % time wins. Allocation savings (336→216 B for Small) reflect skipping the byte buffer and JsonDocument allocation. Real hot-path win is small per arg but compounds at high request rates.

- **ClientInvoker (#2)**: 70 % target assumed reflection dominated. In practice `transport.SendAsync` (even mocked) plus `JsonSerializer` work dominates a single proxy call; saving `MakeGenericMethod`/`MethodInfo.Invoke` removes 39 % wall time and 19 % allocations. Still meaningful at scale.

- **ServerInvoker (#3)**: similar story. `MethodInfo.Invoke` + `task.GetType().GetProperty("Result").GetValue` is real but tiny in absolute terms (single-digit ns). Compiled delegates remove most of it; the remainder is async-state-machine cost we can't avoid.

- **TypeResolution (#4)**: cache hit avoids Type.GetType reparse — 98 % wins, exactly as expected.

- **PublishConcurrency (#5)**: removing the global `SemaphoreSlim` lets parallel publishes proceed without contention; throughput gains scale with concurrency. ≥3× at 16 concurrent threads, ≥3× at 64 too.

The two perf items with overwhelming wins (#4 and #5) are the ones the spec was most confident about. The other three improvements are still real (~20–40 %) and combined with the wins on #4/#5 produce a meaningful end-to-end speedup at high RPC load.
