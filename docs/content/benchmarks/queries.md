---
title: Query reconciliation
order: 9
---

# Query reconciliation

This comparison measures the local cost of accepting an existing-word save in
Kerams's three-page dictionary example. Both implementations retain the same
ordinary index, section-preview, and word-detail records. The hand-written update
maps the navigation stack and patches the matching models. `Ranvier.Query` builds
three edits and commits them together, then reads back the same accepted fields.

Both paths use the same `List.map` to replace the first word's preview and alternate
between two definitions on every operation. The index receives an unchanged
authoritative total, as expected for an edit. Preview counts are 10, 1,000, and
10,000. Setup checks that four successive saves produce identical records and
leave the original shared list unchanged.

## Measured results

Recorded on 2026-10-05 using BenchmarkDotNet 0.15.8's full configuration,
`UnrollFactor=16`, and `MemoryDiagnoser`, on an AMD Ryzen 9 9900X under Windows 11,
.NET SDK 10.0.401 / runtime 10.0.12, workstation GC. All six benchmark cases
completed successfully. The host's `DEBUG` marker in the raw report does not
describe the separately compiled Release measurement process.

| Previews | Hand-written mean | Query mean | Hand-written allocation | Query allocation |
| --- | ---: | ---: | ---: | ---: |
| 10 | 0.114 µs | 0.591 µs | 728 B | 2,704 B |
| 1,000 | 5.136 µs | 5.822 µs | 32,408 B | 34,384 B |
| 10,000 | 56.777 µs | 57.362 µs | 320,408 B | 322,384 B |

For these three-query commits, Query allocates **1,976 extra bytes per save**
at every tested size. At 10 previews it costs **0.476 µs extra** (5.16 times the
minimal update's time); at 1,000 it costs **0.687 µs extra** (about 13%).
At 10,000 the 99.9% confidence intervals overlap: hand-written
55.674–57.879 µs, Query 56.230–58.493 µs. List reconstruction dominates both
paths at that size; the run does not establish a timing difference there.

The practical tradeoff is sub-microsecond additional local work in the two smaller
cases and about 1.9 KiB of temporary allocation, in exchange for staged atomic
publication into shared entries and retirement of superseded query requests.
Request sharing, lifecycle management, and selective notification are additional
services whose benefits are covered by behavior tests, rather than timed here.
These figures price this scenario; they are not a bound on every commit. More
edited entries, expensive updaters/equality, and subscribers add work.

The raw reports are retained in
`docs/.ai/benchmarks/results/Ranvier.Benchmarks.Queries.QueryReconciliationBenchmarks-report-github.md`
and the sibling `-report-full-compressed.json` file.

## Scope

The run measures `Commit` reconciliation with already-loaded entries. It excludes
network IO, initial acquisition, lease disposal, the `Mutate` task/FIFO wrapper,
and rendering. There are no reactive subscribers. It uses the untraced .NET 10
build, default graph equality and thread affinity, and an immediate dispatcher.
The default graph policy compares record references, so this run does not include
structural comparison of the preview list. A custom structural equality policy
would add comparison work, depending on the data and the location of a change.
Appending a new word is not measured here.

This is a minimal hand-written baseline, rather than an implementation of request
deduplication, cancellation, stale-response retirement, or reconciliation failure
recovery. The comparison prices the Query layer's extra services; it does not
establish equivalent functionality or an application-wide rendering speedup.

## Reproduce

The harness is `bench/Ranvier.Benchmarks/Queries.fs`. Run the full configuration:

```shell
rtk proxy dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*QueryReconciliationBenchmarks*" --join
```

BenchmarkDotNet writes raw Markdown and full JSON under `docs/.ai/benchmarks/results`.
Keep the payload, configuration, and machine the same when comparing revisions.
