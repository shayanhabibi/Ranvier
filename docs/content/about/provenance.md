---
title: Provenance
order: 10
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

Ranvier began as a one-time copy of **Partas.Signals**, a fine-grained reactive computation library for .NET by the same author. Ranvier is maintained independently of it.

| | |
| --- | --- |
| Source | Partas.Signals |
| Commit | `915f139` |
| Docs copied | 2026-09-27 |
| Engine, tests and benchmarks ported | 2026-09-28 |
| Kind | One-time copy, not a fork that tracks upstream |

## What was copied

Documentation content, code samples and benchmark results were copied once from Partas.Signals at commit `915f139` on 2026-09-27. The engine, its test suite and the BenchmarkDotNet suite followed on 2026-09-28, as `src/Ranvier`, `tests/Ranvier.Tests` and `bench/Ranvier.Benchmarks`. Nothing is synchronised automatically. Changes made in either project from then on are independent.

## What changed in the copy

- **Namespace.** Code samples open `Ranvier` in place of `Partas.Signals`. Every API identifier (types, functions and members) is kept exactly as it was, so existing Partas.Signals knowledge carries over directly.
- **Benchmarks.** The BenchmarkDotNet results are still those recorded on Partas.Signals and carry their original dates. Comparison runs against other libraries were left out pending a review of their methodology; the instruction-count bench, new in Ranvier, compares engines instead. See [Benchmarks](../benchmarks/index.md).
- **Fable.** The JavaScript target is described as planned. See [Fable (JavaScript) target](../fable/index.md).

Where a page still names Partas.Signals (for example a benchmark class name), it refers to the source as it was at `915f139`.

## License

Ranvier is released under the MIT License. Copyright (c) 2026 Shayan Habibi.
