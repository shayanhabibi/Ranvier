---
title: Query reconciliation
order: 9
---

> **Preview** — Ranvier is pre-release; its APIs may change.

**Cost of three-query reconciliation versus a hand-written Elmish update**, using
the same index, section previews, word detail, and `List.map` patch. Both paths
accept alternating edits to the first word; setup checks equivalent results and
unchanged original data.

| Previews | Hand-written mean | Query mean | Hand-written allocation | Query allocation |
| --- | ---: | ---: | ---: | ---: |
| 10 | 0.114 µs | 0.591 µs | 728 B | 2,704 B |
| 1,000 | 5.136 µs | 5.822 µs | 32,408 B | 34,384 B |
| 10,000 | 56.777 µs | 57.362 µs | 320,408 B | 322,384 B |

**Tradeoff:** +0.476/+0.687 µs at 10/1,000 previews (+416%/+13%), and **1,976 extra
bytes** at every size, for staged atomic publication and superseded-request retirement.
The [reconciliation map](../guide/queries.md#save-once-reconcile-loaded-queries)
shows those operations. At 10,000 previews, the 99.9% intervals overlap
(55.674–57.879 / 56.230–58.493 µs); list rebuilding dominates.

## Scope

- Loaded entries, three-edit `Commit`, then reads of accepted fields. No subscribers.
- Excludes IO, acquisition/disposal, `Mutate` tasks/FIFO, rendering, and adding words.
- Untraced .NET 10; default identity equality, thread affinity, immediate dispatcher.
  Structural comparison, more edits, and expensive updaters add work.
- The minimal hand-written baseline supplies no request sharing, cancellation,
  retirement, or failure recovery. This measures local overhead, not an application speedup.

## Reproduce

Recorded 2026-10-05: Ryzen 9 9900X, Windows 11, SDK 10.0.401/runtime 10.0.12,
workstation GC. BenchmarkDotNet 0.15.8, full configuration, `UnrollFactor=16`,
`MemoryDiagnoser`; all six cases completed. Its host `DEBUG` label does not describe
the separately compiled Release measurement process.

```shell
rtk proxy dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*QueryReconciliationBenchmarks*" --join
```

Harness: `bench/Ranvier.Benchmarks/Queries.fs`. Raw Markdown and full JSON:
`docs/.ai/benchmarks/results/Ranvier.Benchmarks.Queries.QueryReconciliationBenchmarks-report-*`.
Use the same machine, payload, equality policy, and configuration when comparing revisions.
