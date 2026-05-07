# AsbtCore.Broker Benchmarks

Run all:

    dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter '*'

Run a specific class:

    dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter '*JsonElementCreationBench*'

Each per-optimization benchmark exposes a `[Params(LegacyOrNew.Legacy, LegacyOrNew.New)]` switch so before/after numbers come from the same harness.

Acceptance targets are documented in the design spec at
`docs/superpowers/specs/2026-05-07-rpc-perf-optimization-design.md` (Section 8.4).
