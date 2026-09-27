# Benchmarks

One benchmark per primitive, so that two implementations of the same primitive
can be compared directly. The point is tracking: a number here is only ever
meaningful against another number from the same machine.

```powershell
# Everything. Minutes.
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*"

# One primitive.
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --filter "*Memo*"

# Smoke run: same benchmarks, short job. Catches an order-of-magnitude
# regression, not a 5% one.
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --short --filter "*"

# By category: Signal, Memo, Effect, Suspension, Lifetime.
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Memo

# Regression sentinels. Run on every engine change.
dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Sentinel
```

Results land in this directory: GitHub-flavoured Markdown to read, compressed
JSON to diff. BenchmarkDotNet writes one file per benchmark class per run and
overwrites on the next, so commit a run you want to keep.

`results/historical/` and `counters/historical/` hold the Partas.Signals runs
this suite was ported with. They predate the Ranvier series and are not
comparable with it.

## What each group answers

| Group | Question |
| --- | --- |
| `SignalBenchmarks` | What does a write cost, with 0 to 64 observers, and what does the cutoff cost when it stops one? |
| `EqualityBenchmarks` | What does `StructuralPolicy` cost against the default `===` identity? |
| `MemoBenchmarks` | Cache hit, tracked cache hit, and one recomputation. |
| `ChainBenchmarks` | Does propagation scale with depth, at 1 / 4 / 16 / 64? |
| `DiamondBenchmarks` | Is a reconverging node recomputed once per write, or twice? |
| `EffectBenchmarks` | Schedule and flush, and what batching saves over ten separate writes. |
| `SuspensionBenchmarks` | What does a throw through a chain cost, against re-running it over a settled source? |
| `BoundaryBenchmarks` | Re-running a body and catching its throw, against a clean boundary. |
| `SettleBenchmarks` | Settling on the graph's own thread, taking the inline path. |
| `ConstructionBenchmarks`, `ScopeBenchmarks` | Mount and unmount: construction, disposal, and a scope with children. |
| `DerivedValueComparison`, `ChainComparison`, `CutoffComparison` | The same work in FSharp.Data.Adaptive, R3, System.Reactive and by hand. |

## Regression sentinels

`MemoBenchmarks.Recompute` and `ChainBenchmarks.WriteThenReadTail` carry the
`Sentinel` category. Run them, interleaved against the parent commit, on every
change to `Core.fs` or to the engine's build settings; `Depth=64` is the row to
read first.

Their speed rests on JIT tiering as much as on the algorithm. A method holding a
`tail.` call, or an F# self-recursive member compiled to a jump back to its own
entry, is compiled straight to full opts and never reaches Tier1 and its
PGO-guided inlining. Three settings keep the recompute path tiering:

- `<Tailcalls>false</Tailcalls>` in `Ranvier.fsproj`;
- `Memo.Recompute` and `Memo.RecomputeScoped` as two members, mutually rather
  than self-recursive;
- `Memo.Run` without `AggressiveInlining`, so its exception handlers stay out of
  the recursive frame.

Losing any of them, a JIT update, or code growth that pushes a method past an
inlining budget can bring the cost back with the algorithm unchanged. The
single-run-per-discharge rulings (a76673a), measured before these settings,
cost 1.29x on `Recompute` and 1.67x on `WriteThenReadTail` at `Depth=64`.

## Comparing against other libraries

`--anyCategories Comparison` runs the same shapes through the competition.
The libraries are not the same shape, so the caveats are part of the result:

- **FSharp.Data.Adaptive** is the true peer — pull-based, cutoff, recompute on
  read. Its writes go through `transact`, which is part of its model and so is
  measured as part of its write.
- **R3** and **System.Reactive** are push-based streams. Their "derived value"
  is a `Select` plus a subscription holding the latest, which recomputes
  eagerly whether or not anyone reads it and does not deduplicate a diamond.
  They are here because they are what a .NET developer reaches for today.
- **Manual** is the floor: a field and a function. A reactive library that
  loses to it on a shape is not paying for itself on that shape.

No competitor has a pending channel, so suspension has no column. That is the
honest summary: on the overlap we should be competitive, and off the overlap
there is nothing to compare against.

### On the JavaScript side: Fable.Ripple

`dotnet fsi build.fsx -- fable-bench` runs the same kind of shapes under node
against [Fable.Ripple](https://github.com/fable-hub/Fable.Ripple), the only
other F# fine-grained reactive library. It is a fair pairing for the reason the
.NET peers above need caveats and this one does not: `Var` is a source,
`Signal.computed` a cached derived value, and `Scheduler.notifyChange` flushes
effects synchronously on write, exactly as we do. Only the graph is compared —
Ripple ships a DOM layer and we do not.

Ripple 1.0.0-beta.5 under Fable 5.18. Median of three runs, each the median
ns/op over 7 sweeps; magnitudes move by up to 20% between runs, and one run in
three can double a memo row:

| Shape | Ranvier | Ripple | Ranvier is | Work/op |
| --- | --- | --- | --- | --- |
| write, 0 observers | 3.6 ns | 23.5 ns | **6.5x faster** | 0 |
| write, 1 observer | 27.8 ns | 63.4 ns | **2.3x faster** | 1 |
| write, 8 observers | 209.7 ns | 205.7 ns | parity | 8 |
| write, 64 observers | 1261.0 ns | 1458.9 ns | 1.15x faster | 64 |
| cutoff, 8 observers | 4.5 ns | 2.5 ns | 1.8x slower | 0 |
| memo chain, depth 10 | 319.8 ns | 397.7 ns | **1.25x faster** | 10 |
| diamond | 105.9 ns | 185.5 ns | **1.75x faster** | 3 |
| create 1000 effects, per node | 300.2 ns | 97.5 ns | **3.1x slower** | — |

**Work/op** is what makes the rest of the table mean anything: effect runs plus
memo recomputations provoked by one op, counted by running it once with the
counters zeroed, and identical for both engines in every row. A ratio here is a
speed difference, not an engine quietly doing less — and the diamond row shows
both recomputing the join once rather than twice.

Propagation through memos was always ours. Effect dispatch was not: the first
run of this table had us 1.5x behind at both 8 and 64 observers with work
identical, which made it per-effect-run overhead rather than extra work. Three
fixes closed it and then took the lead, every one of them found by profiling
the JavaScript, and every one a no-op or a gain on .NET:

1. A cleanup list cleared unconditionally on every effect run. `Clear` is a
   `splice(0)` allocation under Fable, paid whether or not anything was
   registered, and most effect bodies register nothing. 2349 → 1820 ns at 64.
2. The scheduler queue. `Queue` and `ResizeArray.Clear` both route through
   fable-library; a cursor over a buffer that is never cleared is one indexed
   read and an increment. 1820 → 1544 ns.
3. Bounds-checked reads. Fable compiles an array read to `item(i, xs)` and an
   array write to a raw `xs[i] = v`; three loops that had just tested their
   index were paying the difference, 8.7% of the profile. 1544 → 1339 ns.

The fan-out rows still move by 10% between runs; at 8 observers the direction
flips between runs, so read it as parity. What is left in them is not waste: our effects carry a scope, cleanup
registration and boundary context that Ripple's do not, and that machinery is
what suspension and boundaries are made of.

The cutoff row is Ripple's. Ours sits at 4.4-4.8 ns; Ripple's, bimodal between
2.4 and 5.9 ns on beta.2, holds at 2.5-3.0 ns on beta.5.

Creation is Ripple's by 3x, and the cost is V8's collector. An effect carries
more objects than a Ripple node, and half its CPU profile was garbage
collection when last taken, before a native JS `Map` for the observer index
took the row from 1042 to about 200 ns per node.

Async is absent from the table because our async half does not run under Fable
at all yet; see [the Fable target](../NOTES-fable-target.md).

## Reading them honestly

- **Absolutes are worthless here.** These are nanosecond operations. The
  machine, the power profile and whatever else is running move them by more
  than most changes worth making.
- **Allocation is the reliable signal.** The `Allocated` column is
  deterministic where timing is not. A primitive that starts allocating has
  regressed, and the number says by exactly how much.
- **The baselines are the comparisons that matter.** `CachedRead` for memos,
  `ThrowThroughChain` for suspension, `IdentityCutoff` for equality. A ratio
  against those survives a change of machine; a mean does not.

## Counter bench: instructions, allocations and operation counts

`bench/Ranvier.Counters` reports three figures per operation and no
wall-clock time: instructions retired, bytes allocated, and the library's own
operation counts. The first two are stable where a nanosecond is not; the third
says how much work the engine did, so a change in the other two can be read as
cheaper work or less work.

Five scenarios, each in Ranvier, FSharp.Data.Adaptive 1.2.27 and R3
1.3.1:

| Scenario | One operation |
| --- | --- |
| `create` | Create a root of 1000 rows. A root holds a shared source; a row is a source and one effect reading the row and the shared source. |
| `update` | Write every 10th row of a 1000-row root. |
| `chain` | Write the source of a chain of 4 memos and read the tail. |
| `cutoff` | Write an equal value to a source with one observer. |
| `dispose` | Dispose a 1000-row root. |

Six application-shaped scenarios measure Ranvier alone. They live in
`bench/Ranvier.Counters/Workloads.fs`, which the Fable harness compiles as
well, so both targets run the same graphs and report the same library counters:

| Scenario | One operation |
| --- | --- |
| `app-table` | Over 1000 rows through a `filter` on a query signal and a `sortBy` on a direction signal, alternately change the query and the direction. |
| `app-detail` | Move a `createSelector` selection over 1000 highlighted rows; the detail effect rebuilds 20 memo and effect pairs. |
| `shape-diamond` | Write a source read by 100 memos joined by one sum memo. |
| `shape-dynamic` | For 100 effects reading `a` or `b` by a shared condition, alternately flip the condition and write every active source. |
| `async-resolve` | Reload 10 suspense widgets of 10 async sources in one batch, then settle each source. |
| `async-recover` | Fail one source of one error-boundary widget, then settle a replacement. |

Every figure is `(m(2N) - m(N)) / N`: each case runs at N and at 2N, after one
unmeasured run at N, and the difference cancels the fixed cost of the
measurement itself. Roots in `create` and `dispose` share no source, so the
cost of a root is constant in the number of roots alive.

```powershell
# Allocations and library counters. No elevation. Five runs, compared.
dotnet run --project bench/Ranvier.Counters -c Release -p:RanvierCounters=true -- --no-pmc --repeat 5

# Instructions retired, cycles and branch mispredictions. Elevated shell.
# Built without RanvierCounters: the counters add their own increments.
dotnet run --project bench/Ranvier.Counters -c Release -- --repeat 5

# The profile sources this machine offers, and their ids.
dotnet run --project bench/Ranvier.Counters -c Release -- --list-sources
```

Without elevation and without `--no-pmc`, the tool prints what it needs and
exits with code 2. Output lands in `docs/.ai/benchmarks/counters/` as
`<commit>[-dirty][-nopmc].md` and `.json`; the JSON holds every raw
measurement of every run.

### Library counters

`-p:RanvierCounters=true` defines `RANVIER_COUNTERS` and compiles
`src/Ranvier/Counters.fs`: creations of signals, memos, effects and
owners, edges added and removed, observer inserts and removes, memo
recomputations, effect runs and flushes. `Counters.Reset` and
`Counters.Snapshot` are public in that build only. The default build does not
contain the type, and its IL is identical to the build before the counters were
added. The `objects/op` column is the sum of the four creation counters.

### Determinism

The driver runs every measurement in a child worker process with
`DOTNET_TieredCompilation=0`, `DOTNET_TieredPGO=0`, `DOTNET_ReadyToRun=0` and
`DOTNET_gcServer=0`: every method is compiled once, fully optimised, before the
first measured region. Allocations are
`GC.GetAllocatedBytesForCurrentThread` around the operations. Each region runs
inside `GC.TryStartNoGCRegion` after two full collections; a row marked
`(GC in region)` had a collection inside it. `--repeat k` runs the worker k
times and lists every allocation or library counter that differed between runs;
with processor counters, it also reports the spread of each per-operation
figure.

### Processor counters: per-context-switch, not sampling

TraceEvent exposes hardware counters as sampling profile sources: an interrupt
every k events, with the count reconstructed as samples times k. Sampling is
lossy and inflates the count it measures, since each interrupt retires
instructions of its own. At an interval fine enough for a region of a few
million instructions, the interrupt rate reaches the order of a million per
second.

The tool attaches the counters to the kernel's context-switch event instead
(`TraceSetInformation` with `TracePmcCounterListInfo` and
`TracePmcEventListInfo` on `CSwitch`). Each switch carries the processor's raw
counter values; a thread's count over one run interval is its switch-out value
minus its switch-in value on the same processor. The count is exact up to the
interrupts the processor services while the thread runs.

The worker brackets each region with `Begin` and `End` events from the
`Ranvier-Counters` EventSource, recorded by a user session, and sleeps
1 ms after each marker to force a switch. A region's count is the sum of the
worker thread's run intervals that start between its two markers. The sleeps
and markers are identical at N and 2N and cancel in the difference.

Collection starts the `NT Kernel Logger` session and stops any session already
running under that name, including one another tool holds. Under Hyper-V, the
guest reads processor counters only when the host exposes them to the VM.

### Under Fable: Node.js

`fable/Ranvier.Counters` runs the same five scenarios in JavaScript, for
Ranvier and Fable.Ripple 1.0.0-beta.5, with the same N per scenario.
It writes the .NET worker's JSON shape, and the .NET tool reads it with
`--fable <dir>`, so one run of the tool writes one report for both targets.

`counters.ps1` at the repository root is that one command. It compiles the
harness twice, runs the tool, and writes
`docs/.ai/benchmarks/counters/<commit>[-dirty][-nopmc].md` and `.json`:

- `output/plain`: the library as shipped. Allocations and instructions.
- `output/counters`: compiled with `RanvierCounters=true` in the environment,
  which MSBuild reads as a property when Fable cracks the project. Library
  counters only.

```powershell
# Allocations and library counters, both targets. No elevation.
./counters.ps1 -NoPmc

# As -NoPmc, and also drives the Fable region handshake without ETW sessions.
./counters.ps1 -PmcDryRun

# Instructions retired, cycles and branch mispredictions too. Elevated shell.
./counters.ps1
```

Without elevation and without `-NoPmc` or `-PmcDryRun`, the script prints what
it needs and exits with code 2, as the tool does.

**Allocations.** Every node process runs with `--expose-gc --single-threaded`,
ten unmeasured warm-up runs of each case at 2N, and two `gc()` calls before
each region. `bytes/op` is the growth of the new, old and large-object heap
spaces over the region. `heapUsed/op`, from `process.memoryUsage().heapUsed`,
adds the code and trusted spaces, which grow when V8 compiles or optimises a
function. `v8.GCProfiler` counts the collections inside each region; a row with
one is marked `(GC in region)`. Five processes give the median and the range.

`--single-threaded` is what makes the figures repeat: V8 then compiles and
optimises on the main thread, at the same point of every run. Without it, five
processes gave Ranvier `update` heapUsed figures from 3,730 to 10,605
bytes/op; with it, all five agree to the byte.

**Library counters.** Under Fable a count is a JavaScript number, not a
`bigint`: `int64` compiles to `bigint`, whose increments allocate.

**Instructions.** The harness is run with `--handshake`: before and after each
region it writes `BEGIN <region>` or `END <region>` to standard output and
blocks on standard input. The tool writes the matching `Begin` or `End` marker
event from its own process, then replies. After the `BEGIN` reply the harness
sleeps one millisecond, so its next run interval starts after the marker. The
counts are the node process's main thread (`main`), the first thread the process
starts inside the kernel session, and every thread of the process (`all`).
The sleep, the handshakes and the marker events are the same at N and 2N and
cancel in the difference.
