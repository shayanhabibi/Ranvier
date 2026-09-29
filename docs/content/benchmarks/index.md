---
title: Benchmarks
order: 1
---

> **Preview** — Ranvier is pre-release; its APIs may change.

Ranvier ships a BenchmarkDotNet suite with one benchmark class per primitive: signals, memos, effects, lifetimes, projections and suspension. Its purpose is **tracking**: a number here is meaningful against another number from the same machine and the same configuration, typically the previous commit. It is not a statement of how fast Ranvier will be on your hardware, and these pages make no comparison with other libraries.

| Area | What it covers |
| --- | --- |
| [Signals](signals.md) | Reads, writes with 0 to 64 observers, the equality cutoff, and `StructuralPolicy` against identity comparison. |
| [Memos](memos.md) | Cache hits, one recomputation, propagation through chains of 1 to 64 memos, and a diamond. |
| [Effects](effects.md) | Write-and-flush with 1 to 64 effects, and a batch of ten writes. |
| [Lifetimes](lifetimes.md) | Construction and disposal of nodes, fan-out on one source, and scopes with children. |
| [Projections](projections.md) | Keyed projection reads, single-item edits, reorders, and selector (`Lookup`) reads. |
| [Suspension](suspension.md) | The cost of the pending channel: throwing through a chain, boundaries, settling a source, and cancelling a flight. |
| [Instruction counts](counters.md) | Whole scenarios by instructions per operation, against FSharp.Data.Adaptive, R3 and Fable.Ripple, on .NET and Node.js. |

## Results

The benchmark suite lives in this repository as `bench/Ranvier.Benchmarks`. The results on these pages come from a full run of the suite at commit `d87920f`, recorded on **2026-09-28**; each table states its own date and the benchmark class it came from.

The BenchmarkDotNet comparison runs against other .NET reactive libraries are not published here; [Instruction counts](counters.md) compares engines by instructions instead. The engine-internal diagnostic probes in the suite are not published either; they compare candidate implementations of internal data structures rather than measure a public operation.

## Configuration

Every run uses the same BenchmarkDotNet configuration, so that runs are comparable with each other:

- **Job**: BenchmarkDotNet's default job (`Job.Default`) with **`UnrollFactor = 16`**. The primitives are small enough that loop overhead would otherwise be a visible part of the measurement, so each measured call is repeated sixteen times in an unrolled loop.
- **Diagnoser**: `MemoryDiagnoser`, which adds the `Gen0`/`Gen1` and `Allocated` columns (bytes allocated per operation; `-` means none).
- **Columns**: the default column set plus `Categories` and `Op/s` (operations per second).
- **Ratio style**: `RatioStyle.Trend`, so ratios read as "_N_x slower/faster" relative to the class's baseline method.
- **Exporters**: GitHub-flavoured Markdown (the tables on these pages) and compressed full JSON (for diffing runs).
- A **short** configuration (`Job.ShortRun`, same unroll factor) exists for smoke runs. It catches order-of-magnitude regressions only and is never used for published numbers. All results on these pages come from the full configuration.

## Environment

All results were recorded on a single machine, as reported by BenchmarkDotNet:

```text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host]          : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4 DEBUG
  UnrollFactor=16 : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
```

The `DEBUG` marker on the `[Host]` line refers to BenchmarkDotNet's host process. Measurements run in the separate `UnrollFactor=16` job process, which BenchmarkDotNet builds and launches on its own.

## Reproducing

Run these commands from the repository root, in Release configuration. A full run takes minutes.

```shell
# Everything
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*"

# One area
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*Memo*"

# By category: Signal, Memo, Effect, Lifetime, Projection, Suspension
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Memo

# Smoke run: short job, not for publishing
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --short --filter "*"
```

The instruction-count bench runs through `counters.ps1` at the repository root, from an elevated
shell. BenchmarkDotNet writes one Markdown and one JSON report per benchmark class and overwrites them on the next run.

## Reading the results

- **Absolute times are not portable.** Many of these operations take a few nanoseconds. The machine, its power profile and whatever else is running can move them by more than most code changes do. Compare numbers only against numbers from the same machine and configuration.
- **Allocation is the steadier signal.** The `Allocated` column is deterministic where timing is not. If an operation that allocated nothing starts allocating, the column says exactly how much.
- **Ratios within a class travel better than means.** Several classes have a baseline method (for example `CachedRead` for memos, `RecomputeSettledChain` for suspension, `IdentityCutoff` for equality). The ratio against the baseline survives a change of machine better than the mean does.
- **Sub-nanosecond rows are at the resolution limit.** A result well under a nanosecond means the operation compiles to little more than a field read; treat it as negligible rather than as a precise figure, and expect a large `RatioSD` on ratios computed from it.
- **Microbenchmarks isolate one primitive.** They do not predict the cost of a real application, where graph shape, allocation pressure and the work inside computations dominate.
- **Regression sentinels.** `MemoBenchmarks.Recompute` and `ChainBenchmarks.WriteThenReadTail` are tagged `Sentinel` and are run against the parent commit on every engine change. Their cost depends on JIT tiering as well as on the algorithm, so a runtime update can move them with no code change.
