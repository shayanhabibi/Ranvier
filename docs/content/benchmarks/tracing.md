---
title: Tracing cost
order: 11
---

> **Preview** — Ranvier is pre-release; its APIs and these figures may change.

Tracing is a compile-time switch. `RanvierTrace=true` compiles the per-graph event log and the
`Trace` queries into the library; it is the default in Debug and off in Release. This page states
what the traced build costs against the untraced build of the same source, and the budget that
later instrumentation changes are held to.

Every figure comes from the baseline at `f48a3a7`, recorded on 2026-10-24 on an AMD Ryzen 9
9900X under Windows 11 with .NET 10.0.13, Node.js 26.7.0 (fickl 0.5.3, fable-library-js 2.9.0)
and Chromium 156. The raw results, the methodology and the machine are in
[`docs/.ai/benchmarks/tracing/f48a3a7`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/tracing/f48a3a7/README.md).

## Reproducing

```shell
dotnet fsi build.fsx -- perf trace <fresh-directory>
```

The command builds the counter bench, its Fable harness and the Playground's keyed-row bundle
twice, untraced and traced. It runs the counter scenarios, the trace growth probe on .NET and
Node.js, the keyed-row harness in Chromium and the BenchmarkDotNet `Trace` category, and writes
`report.md` beside the raw results. A full run takes about 75 minutes. `--short` is a smoke run
and is not comparable with the baseline.

## Cost model

### Events per update

The log records a fixed number of events per write, set by the graph's shape.

| Workload | Events per update |
| --- | ---: |
| `memo`: a signal read by one memo and one effect | 14.0 |
| `fanout`: a signal read by 64 memos | 392.1 |

Both runtimes record the same events. The count is deterministic: two runs agree exactly.

### Retained memory

The log holds every event until a checkpoint drops it. Its retained cost is the traced build's heap
slope against update count minus the untraced slope, measured after a full collection.

| Runtime | Workload | Log bytes per update | Log bytes per event |
| --- | --- | ---: | ---: |
| .NET | `memo` | 879 | 62.8 |
| .NET | `fanout` | 18,186 | 46.4 |
| Node.js | `memo` | 1,393 | 99.5 |
| Node.js | `fanout` | 37,899 | 96.7 |

Without a checkpoint, history grows linearly: 100,000 `memo` updates hold 1.4 million events,
about 88 MB on .NET. Checkpointed 100 updates before each measurement, the same graph retains
1,400 events and 156 KB on .NET (890 KB on Node.js) at 100,000 updates, with a retained slope
under 1 B per update ([`b786373`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/tracing/b786373/README.md)). In Chromium, a mounted keyed list retains about 600 to 700 more bytes per row
traced: 1,111 against 1,816 bytes per row at 1,000 rows, 976 against 1,584 at 10,000.

### Time and allocation per write

BenchmarkDotNet, the same method under an untraced and a traced job. Steady-state methods write
to a prebuilt graph; the untraced writes allocate nothing.

| Steady state | Untraced | Traced | Ratio | Traced allocation |
| --- | ---: | ---: | ---: | ---: |
| `signal` | 18.4 ns | 1,635 ns | 89.0 | 1,672 B |
| `memo` | 35.7 ns | 1,333 ns | 37.3 | 3,472 B |
| `chain` (4 memos) | 87.5 ns | 6,151 ns | 70.3 | 10,791 B |
| `fanout` (64 memos) | 833 ns | 47,614 ns | 57.2 | 107,646 B |
| `cutoff` (an equal result) | 26.5 ns | 360 ns | 13.6 | 2,576 B |
| `batch` (10 writes) | 43.0 ns | 2,912 ns | 67.7 | 6,464 B |

| Construction | Untraced | Traced | Ratio | Allocation ratio |
| --- | ---: | ---: | ---: | ---: |
| `signal` | 145 ns | 18,318 ns | 126.5 | 23.4 |
| `memo` | 234 ns | 27,086 ns | 117.3 | 28.6 |
| `chain` | 478 ns | 54,663 ns | 115.9 | 34.5 |
| `fanout` | 4,884 ns | 589,194 ns | 121.1 | 38.8 |
| `dispose` (1,000 effect rows) | 49.6 µs | 9,386 µs | 189.2 | 53.5 |

The traced figures include the amortised growth of the log's backing array, so they depend on
how much history the graph already holds. The traced `memo` write is bimodal (median 771 ns,
mean 1,333 ns).

In Chromium, the keyed-list operations of the Playground's framework harness run 1.2 to 2.3 times
slower traced, apart from operations dominated by writes to existing rows: `update10th1k` runs
10 times slower and `armsPayload1k` 7 to 8 times. The arm operations run 2.5 to 3.1 times slower.

### Export and incremental reads

Every query except `Trace.eventsSince` copies or walks the whole retained log, so each costs time proportional to the events held.
Per 1,000 events held, from the BenchmarkDotNet run at 100,000 `memo` updates (1.4 million
events) on .NET, and from the growth probe at the same checkpoint on Node.js:

| Operation | .NET | Node.js |
| --- | ---: | ---: |
| `Trace.events` | 4.6 µs | 0.7 µs |
| `Trace.why` | 4.2 µs | — |
| `Trace.snapshot` | 53 µs | 83 µs |
| `Trace.origin` | 54 µs | — |
| `Trace.dumpText` | 126 µs | 548 µs |
| Tail read | 4.0 µs | 0.8 µs |

The tail read copies the log with `Trace.events` and slices the events after the previous read.
It reads 1,400 new events at every checkpoint, yet its cost follows the length of the whole log.
`Trace.eventsSince` reads the same events from a seq cursor and costs what it returns: 1.4 to
1.9 µs and 54.7 KB at 1,000 to 100,000 updates on .NET, 2 µs or less on Node.js
([`b786373`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/tracing/b786373/README.md)).
The signal maps read with it.

`Trace.history` is quadratic in the run count: 11.5 ms at 1,000 updates, 104 ms at 3,000 and
4.2 s at 10,000.

## Budget

An instrumentation change is measured with `perf trace` on the same machine as the baseline it
is compared with, and stays inside these limits. A change that exceeds one records the new figure,
the reason, and a new baseline directory under `docs/.ai/benchmarks/tracing/`.

| Measure | Limit | Headroom from |
| --- | --- | --- |
| Untraced build | Identical bytes/op and library counters to the untraced baseline in every counter scenario (`counters.ps1 -NoPmc`), and the existing perf gate passing. | The untraced library is the shipped Release build; it carries no trace cost. |
| Events per update | At most 14.0 (`memo`) and 392.1 (`fanout`). | Two runs agree exactly. |
| Log bytes per event | At most 66 B (`memo`) and 49 B (`fanout`) on .NET; 105 B and 102 B on Node.js. | Two runs agree exactly; 5 % covers runtime and allocator updates. |
| Traced .NET bytes/op in GC-free counter scenarios | At most 2 % above the traced baseline. | Two runs differ by at most 0.6 %. |
| Traced time ratio, BenchmarkDotNet | At most the measured ratio plus twice its `RatioSD`: `signal` 93, `chain` 73, `fanout` 70, `cutoff` 15.4, `batch` 77; construction `signal` 155, `memo` 152, `chain` 152, `fanout` 147, `dispose` 210. | BenchmarkDotNet's ratio spread. The `memo` write is bimodal and is reported, not gated. |
| Export and tail read per 1,000 events | At most 1.5 times the table above. | Export timings of two runs differ by up to 40 %. |
| Chromium retained heap per row | At most 5 % above 1,816 B (1,000 rows) and 1,584 B (10,000 rows). | Replicates differ by under 2 %. |
| Chromium traced-to-untraced ratio | At most 1.25 times the measured ratio for operations whose per-process medians differ by under 15 %. | Per-process medians of most operations differ by under 15 %; operations with more spread are reported, not gated. |

The input journal (`GraphOptions.Journal`) is compiled into the untraced build. With no journal, a
signal write, an async-source settle, a flush and an untracked section each add one field load and
a null check. Against its fork point, every untraced counter scenario keeps its bytes/op and library
counters, except `server-render`, which builds a graph per request and gains 8 B/op for the graph's
journal field. A paired, interleaved timing of a signal write and a four-memo chain shows no
difference beyond an A/A run of the same harness. The figures are in
`docs/.ai/benchmarks/journal/`.

A Node.js counter row that ran a collection inside a measured region (`GC-free = no` in the
report) understates its allocation and is not gated; most traced Node.js rows are in this state.
The growth probe's bytes per event is the Node.js figure.
