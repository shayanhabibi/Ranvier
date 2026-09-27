---
title: Provenance
order: 10
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

Ranvier began as a one-time copy of **Partas.Signals**, a fine-grained reactive computation library for .NET by the same author.

| | |
| --- | --- |
| Source | Partas.Signals |
| Commit | `915f139` |
| Copied | 2026-09-27 |
| Kind | One-time copy, not a fork that tracks upstream |

## What was copied

Documentation content, code samples, benchmark sources and benchmark results were copied once from Partas.Signals at commit `915f139` on 2026-09-27. Nothing is synchronised automatically after that date. Changes made in either project from then on are independent.

## What changed in the copy

- **Namespace.** Code samples open `Ranvier` in place of `Partas.Signals`. Every API identifier (types, functions and members) is kept exactly as it was, so existing Partas.Signals knowledge carries over directly.
- **Benchmarks.** Results for Ranvier's own primitives are published with their original dates. Comparison runs against other libraries were left out pending a review of their methodology. See [Benchmarks](../benchmarks/index.md).
- **Fable.** The JavaScript target is described as planned. See [Fable (JavaScript) target](../fable/index.md).

Where a page still names Partas.Signals (for example a benchmark class name or a project path), it refers to the source as it was at `915f139`.

## License

Ranvier is released under the MIT License. Copyright (c) 2026 Shayan Habibi.
