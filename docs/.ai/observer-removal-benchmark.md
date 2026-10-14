# Observer removal benchmark

Can `ObserverSet.Remove` get cheaper by tombstoning slots instead of swap-removing them? The motivation was a Chromium
profile of disposing 128 view rows (Fable build), where `ObserverSet.Remove` took 49.6% of teardown, most of it in
`refIndexRemove` and `refIndexSet` on the `RefIndex` kept above 8 observers.

**Outcome: rejected.** On .NET the tombstone design made disposal 22-31% faster at 64 and 1024 observers. Under
Fable/V8 it made root disposal about 30% slower, and Fable is the build the profile came from. Master's swap-remove
stays. The benchmarks and tests from this work are committed. The engine change is not.

## Setup

- Machine: AMD Ryzen 9 9900X 4.40GHz, 12 cores / 24 threads; Windows 11 (10.0.26200.9457, 25H2); High performance plan
- .NET: SDK 10.0.402, runtime 10.0.13, X64 RyuJIT x86-64-v4; BenchmarkDotNet v0.15.8, job `UnrollFactor=16`
- JS: Node v26.7.0 running `dotnet fable fable/Ranvier.Tests.Fable -c Release` output; scripts are in the session
  scratchpad and are not committed
- Base: master `e2e0f95`. Benchmarks: commit `75c9296` (`bench/Ranvier.Benchmarks/Lifetimes.fs`,
  `ObserverRemovalBenchmarks` and `ObserverChurnBenchmarks`)
- Command: `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories ObserverRemoval`
- Raw base reports: `docs/.ai/benchmarks/results/Ranvier.Benchmarks.Lifetimes.Observer*-report-github.md`

Cases (`Nodes` = N effects on one source):

- `DisposeRoot`: create N effects under a root, then dispose the root. Removal runs newest first.
- `DisposeInCreationOrder`: create N effects, then dispose them oldest first. This is swap-remove's worst order.
- `RelinkAll`: every effect drops its edge to the wide source, then every effect links it again.
- `RelinkOne`: one effect drops its edge and links it again, cycling through the effects.
- `WriteWide`: one write that notifies all N effects. Setup has already relinked half of them.

## Design measured

- **Vacant slots.** `Remove` blanks the slot and moves no other observer. The live observers sit in a window
  `[head, length)`. Removing the first or last live observer shrinks the window and trims any vacant slots it exposes.
- **Compaction.** The live observers are moved to the front once vacant slots outnumber them 2:1. Each moved entry
  gets an index overwrite. A walk can trigger compaction, and that is safe: entries only move towards the front, so
  no unvisited observer is skipped.
- **Lazy end removal.** Removing an end observer leaves its index entry stale. A lookup checks the hit against the
  slot, so a stale entry reads as a miss. The index is cleared once stale entries exceed half the live count, and it
  is rebuilt when a lookup next needs it.
- **Eager middle removal.** Removing a middle observer deletes its index entry immediately.
- **Count zero.** When the live count reaches 0, the window resets and the index is cleared.

All existing tests passed with this design, including the traced and Fable suites, plus 21 new ones.

## .NET (BenchmarkDotNet, back-to-back runs, StdDev 1-3%)

| Case | N | master | tombstone | ratio |
| --- | ---: | ---: | ---: | ---: |
| DisposeRoot | 8 | 438.6 ns | 473.3 ns | 1.08 |
| DisposeRoot | 64 | 6,159.5 ns | 4,741.6 ns | 0.77 |
| DisposeRoot | 1024 | 101,567 ns | 79,234 ns | 0.78 |
| DisposeInCreationOrder | 8 | 649.8 ns | 453.4 ns | 0.70 |
| DisposeInCreationOrder | 64 | 6,976.0 ns | 4,795.3 ns | 0.69 |
| DisposeInCreationOrder | 1024 | 118,984 ns | 105,785 ns | 0.89 |
| RelinkAll | 8 | 378.0 ns | 410.1 ns | 1.08 |
| RelinkAll | 64 | 3,927.7 ns | 3,640.4 ns | 0.93 |
| RelinkAll | 1024 | 74,813 ns | 62,853 ns | 0.84 |
| RelinkOne | 8 | 60.95 ns | 60.15 ns | 0.99 |
| RelinkOne | 64 | 72.26 ns | 69.26 ns | 0.96 |
| RelinkOne | 1024 | 83.41 ns | 76.98 ns | 0.92 |
| WriteWide | 8 | 132.9 ns | 135.3 ns | 1.02 |
| WriteWide | 64 | 1,008.7 ns | 1,021.9 ns | 1.01 |
| WriteWide | 1024 | 17,975 ns | 16,954 ns | 0.94 |

The tombstone column clears stale entries above 16 rather than above half the live count. That threshold changes
only how many stale entries the index holds; the removal paths are the same. Allocation is identical in every case.
A first master run, made while the machine was busy, was about 30% slower across the board, including cases that
never touch removal. It is discarded.

## Fable / V8

These numbers come from the same cases written against the compiled graph, with master and the tombstone build
interleaved in one process, median of 11.

- **The isolated `ObserverSet` won where the profile pointed.** Adding N plain observers and removing them, per
  observer at N = 1024:

  | Removal order | master | tombstone |
  | --- | ---: | ---: |
  | Reverse | 41.3 ns | 33.3 ns |
  | Forward | 47.3 ns | 33.5 ns |
  | Scattered | 55.2 ns | 55.5 ns |

  At N = 64, reverse went from 34.2 ns to 28.9 ns and forward from 40.3 ns to 29.7 ns.
- **Through the graph, root disposal got slower.** `DisposeRoot` was measured in separate processes, 40,000
  iterations each:

  | N | master | tombstone |
  | ---: | ---: | ---: |
  | 64 | 11.9-12.0 µs | 15.7-16.3 µs |
  | 1024 | 192 µs | 244 µs |

  The `--trace-gc` counts over the same run explain it. Master did 162 scavenges and 30 mark-compacts. The tombstone
  build did 179 scavenges and 42 mark-compacts. The CPU profile agrees: `ObserverSet.Remove` drops from 7.4% of
  samples to under 1%, but the time moves into GC. Two causes are likely:
  - Removed effects stay reachable from stale `Map` entries until the clear, so more of them survive a scavenge.
  - `Map.clear()` followed by a rebuild reallocates the backing table, where master's progressive `delete` does not.
- **Other cases did not move clearly.** The interleaved runs gave these ratios (tombstone / master):

  | Case | Ratio range |
  | --- | --- |
  | `DisposeInCreationOrder` | 0.91-0.98 |
  | `RelinkAll` | 0.81-1.03 |
  | `RelinkOne` | 0.83-1.04 |
  | `WriteWide` | 0.97-1.35 |
  | `DisposeRoot` at 64 | 1.08-1.15 |
- **Other variants did no better.**
  - Deleting index entries eagerly on every removal keeps the window and compaction and has no retention. It was a
    wash: `DisposeRoot` 64 measured 12.1 µs against master's 12.0 µs, and 1024 measured 201 µs against 192 µs.
    Without the lazy deletion there is little left to save.
  - Clearing stale entries above the live count, not half of it, was slower still.

## What remains

- The cost the profile shows is hashing, and only removing the hash removes it. Store each edge's slot position on
  both sides: the `SourceList` entry and the `ObserverSet` slot. Removal then becomes an O(1) unlink with no index
  operation. This is the doubly-linked `Link` model the `SourceList` remarks already name as the end state. It
  changes `ISource.AddObserver` and `RemoveObserver` and their 12 pass-through implementers.
- Under Fable, `ObserverSlot` compiles to a heap object, so every `Add` allocates. The profile shows it as
  `ObserverSlot_$ctor`, at about 3% of a teardown-heavy run. A Fable-only representation that stores the observer
  directly would remove that allocation. The struct exists only for .NET's covariant-store check.
- The new tests (`Observers.fs` `ObserverSet`, and `Retention.fs`) pin the behaviour any future design must keep:
  - removal and compaction during a notification walk;
  - observers added mid-walk;
  - an exact `Count` under out-of-order and repeated removal;
  - re-adding after removal on a wide list;
  - wide-source retention.
