# Debounce and throttle: design

**Status:** proposal, not implemented. Needs a maintainer decision (§9). Line references are to `src/Ranvier/*.fs`
at `c631f23`. Costs are read from the code; no numbers were measured. §5 names the benchmarks that check them.

## 1. Goal

Research §1 asks for debounce and throttle (FuncUI#360, Fabulous discussion #1061); §11 asks that any time-based
policy take its time from a replaceable source, so a test advances time instead of sleeping (R3 uses
`FakeTimeProvider`). Ranvier has no timers today: no source file references `TimeProvider`, `Timer`, `Task.Delay` or
`setTimeout`.

The canonical case: a search box signal, debounced, feeding an async memo under `CancelPrevious`.

## 2. Proposed API

```fsharp
// Api.fs
val debounce : delay: TimeSpan -> source: (unit -> 'T) -> Timed<'T>
val throttle : interval: TimeSpan -> source: (unit -> 'T) -> Timed<'T>

// Types.fs
[<AbstractClass>]
type Clock =
    abstract CreateTimer: callback: (unit -> unit) -> ClockTimer
    static member System: Clock                                    // Threading.Timer on .NET, setTimeout under Fable
#if NET8_0_OR_GREATER
    static member OfTimeProvider: TimeProvider -> Clock            // FakeTimeProvider in .NET tests
#endif
and [<AbstractClass>] ClockTimer =
    abstract Arm: due: TimeSpan -> unit                            // re-arming replaces the pending due time
    abstract Disarm: unit -> unit
    interface IDisposable

type ManualClock() =                                               // portable fake, the timer analogue of ManualDispatcher
    inherit Clock()
    member Advance: TimeSpan -> unit                               // fires every timer due, in due order, inline

type GraphOptions = { ...; Clock: Clock }                          // Default: Clock.System; WithClock
```

`Timed<'T>` is a read-only node: `Value` (tracked), `Peek` (untracked), `Dispose`. Owned by the current owner, like
a memo.

```fsharp
let query = createSignal ""
let settled = debounce (TimeSpan.FromMilliseconds 300.) query.Get
let results = createAsync (fun _ ct -> search settled.Value ct)
```

## 3. Semantics

- **`debounce d`.** Holds the source's value at construction. Each change to anything `source` reads re-arms the timer
  for `d`. When the timer fires, `source` runs, tracked, and its result is written through the graph's equality
  cutoff; readers wake only if it moved.
- **`throttle i`** (leading and trailing). A change with no window open runs `source` at once and opens a window of
  `i`. Changes inside the window are owed. At the window's end an owed change runs `source` once and opens the next
  window; otherwise the window closes.
- **Readers see a settled value, never Pending.** The node keeps its last value while the timer runs. A reader that
  wants a spinner from the first keystroke compares `settled.Value` against `query.Value`.
- **A failing or pending `source`** fails or suspends the node, as a memo body does: its readers see the error or
  Pending until the next fire.
- **Threads.** The timer callback runs on the clock's thread. It goes through `Graph.Post` (`Core.fs:1443-1452`) and
  lands in the inbox like a flight completion (`docs/content/concepts/contracts.md:30`). Under `ManualDispatcher` it
  applies on the next pump. A `ManualClock` advanced on the graph thread applies inline.
- **Dispose** disarms and disposes the timer. A callback already posted finds the node disposed and does nothing.

## 4. How it works

`Timed<'T>` is one node that is a computation towards `source` and a source towards its readers, like `Memo`
(`Core.fs:2202`), with one difference: `MarkDirty` and `MarkCheck` (on `Memo`, `Core.fs:2545-2570`; on `AsyncMemo`,
`Core.fs:3642-3659`) arm the timer instead of notifying readers. The node's own value is current between fires, so its
freshness towards readers stays Clean and a read never runs `source`. The node subscribes to its sources eagerly, as an
effect does; an unobserved `Timed` still fires.

A `Check` that later resolves to no change still re-arms the timer; the fire then runs `source`, the cutoff holds, and
nothing wakes. Evaluating `source` on each `Check` instead would put the source's work back on every write, which is
the work debounce exists to skip.

The fire callback and the posted work are allocated once per node and reused; `ClockTimer.Arm` on .NET maps to
`ITimer.Change` or `Threading.Timer.Change`, which re-arm without allocating.

Under Fable, `Clock.System` uses `Fable.Core.JS.setTimeout` and `clearTimeout`. `ManualClock` is plain F# (a sorted list
of due timers), so the same test runs on both targets.

## 5. Cost model

**Code that does not use debounce or throttle.**

| Path | Added work |
| --- | --- |
| Write, recompute, flush, flight | None. No existing node changes. |
| Per node | None. |
| Per graph | One reference (`GraphOptions.Clock`) if the clock goes on `GraphOptions`; none if it is a combinator argument. |
| Startup | `Clock.System` is created on first use. |

**Code that uses them.**

| Path | Cost |
| --- | --- |
| Construction | One node (same shape as a `Memo`: observer set, source list, value) plus one timer object and two cached closures. |
| Source write, `debounce` | One `MarkDirty` call on the node plus one timer re-arm. On .NET, `Change` takes the timer queue's lock. Under Fable, `clearTimeout` plus `setTimeout`. |
| Source write, `throttle`, window open | One field write. |
| Fire | One `Graph.Post` (inline on the graph thread; one inbox entry otherwise), one `source` run, one cutoff comparison, one flush if the value moved. |

The re-arm is the one cost without a precedent in the library: it runs inside write propagation, once per write per
debounced node. Benchmarks that settle it, added to `bench/Ranvier.Benchmarks` as `Timed.fs`:

- `WriteThroughDebounce` vs baseline `WriteThroughMemo`: write a signal read by one `debounce` node (resp. one memo with
  an effect). Measures the per-write re-arm on `Clock.System`; `MemoryDiagnoser` must show zero bytes per write.
- `FireDebounced`: `ManualClock.Advance` past the delay after one write. Measures the fire path.
- The same pair on `Clock.OfTimeProvider TimeProvider.System` (net8.0 and net10.0) to compare the two .NET timers.

## 6. Fable, AOT and target frameworks

- **Fable.** `TimeProvider` does not exist under Fable, so `Clock` is Ranvier's own type and `Clock.OfTimeProvider`
  is `#if !FABLE_COMPILER` as well as `NET8_0_OR_GREATER`. `TimeSpan` compiles to milliseconds. Timers fire on a
  macrotask, after any pending microtask outcome.
- **netstandard2.1.** `System.TimeProvider` is not in netstandard2.1 (`src/Ranvier/Ranvier.fsproj:4` targets
  `net10.0;net8.0;netstandard2.1`). `Clock.System` uses `System.Threading.Timer`, available on all three, so no
  `Microsoft.Bcl.TimeProvider` dependency is added. `OfTimeProvider` exists on net8.0 and net10.0 only.
- **AOT and trim.** Abstract classes and closures; no reflection. Safe.

## 7. Compatibility

- New public types `Clock`, `ClockTimer`, `ManualClock`, `Timed<'T>` and functions `debounce`, `throttle`: additive.
- A `Clock` field on `GraphOptions` changes its constructor
  (`docs/.ai/public-api-baseline.txt:110`) and breaks record-expression construction. Callers of
  `GraphOptions.Default.With...` (`Types.fs:271-298`) are unaffected. Ranvier is unreleased.
- Not a `FlightPolicy` case, so the `FlightPolicy` baseline is unchanged.

## 8. Alternatives

- **A flight policy `Debounce of TimeSpan`.** Rejected. `FlightPolicy` is graph-wide (`Types.fs:260`, read at
  `Core.fs:3344` and `Core.fs:3470`), so every async memo in the graph would share one delay. It also debounces the
  wrong node: the delay belongs upstream of the fetch, where a sync source is shielded too. A memo that wants it gets
  it by reading a `Timed` node.
- **Build from `createSignal` plus `createEffectOn` plus a timer.** Works with public API today and is the fallback
  recipe for docs, but costs three nodes per debounced value and a flush per write to run the effect.
- **Clock as a combinator argument** (`debounceWith clock delay source`) instead of a `GraphOptions` field. Non-breaking
  and zero per-graph cost, but every call site in a test has to thread the fake clock; a graph option swaps it once,
  as `Dispatcher` does (`Types.fs:262-267`).
- **`TimeProvider` everywhere.** Rejected: absent under Fable and on netstandard2.1.

Tests (all with `ManualClock`, repeated on the Fable suite; one .NET test with `FakeTimeProvider` off the graph
thread): writes within the window reset the delay; `Advance (d - 1ms)` leaves the old value; the fire wakes readers
once; an unchanged fire wakes nothing; throttle emits leading and trailing values; dispose disarms; a callback posted
off-thread applies on the next pump under `ManualDispatcher`.

## 9. Recommendation

**Do later, as combinators.** It is new surface with its own node type and a timer re-arm on the write path of every
debounced source, so the benchmarks above run first. Ship `FinishCurrent` (the flight-duration throttle) before it;
until then the docs carry the signal-plus-effect recipe.

Questions for the maintainer:

1. Clock on `GraphOptions` (breaking) rather than per call? (yes / no)
2. Debounced node Pending while its timer runs? (yes / no; proposal: no)
3. Ship `throttle` with `debounce`, or `debounce` alone first? (both / debounce)


## Reviewer corrections (not yet applied)

Verdict: needs fixes

- Severity: substantive (semantics). 'Each change to anything `source` reads re-arms the timer' does not hold when `source` reads through a computed node. `Memo.MarkDirty`/`MarkCheck` notify observers only on the Clean→Check/Dirty transition (Core.fs:2544-2564: 'Only on the Clean transition'); the same holds for `AsyncMemo.MarkDirty` (3642-3653). Because `Timed` deliberately does not pull on Check, an intermediate memo stays Check after the first write, and later writes in the window never reach `Timed`. The timer then fires `d` after the first change, not the last: throttle-trailing behaviour, not debounce. Only direct signal reads re-arm on every write. The design needs to pull the Check (the cost it rejects), accept and document the difference, or restrict `source` to signals.
- 'Readers see a settled value, never Pending' contradicts the next bullet, 'A failing or pending `source` ... readers see the error or Pending until the next fire'. Pick one: either a pending source keeps the last value (and the node never shows Pending), or state the exception to the first bullet.
- 'throttle i ... A change with no window open runs `source` at once': a change arrives through MarkDirty, inside write propagation, while an observer set is being walked. Running user code there re-enters the graph mid-propagation. The leading-edge run must be scheduled into the flush (as an effect is), and the cost table's 'Source write, throttle, window open: One field write' then misses the scheduled run and flush.
- Cost model: 'The fire callback and the posted work are allocated once per node and reused' holds only for the work closure. An off-thread fire goes through `Graph.Post`, which allocates `Action this.PumpFromDispatcher` on every call (Core.fs:1452); `SynchronizationContext.Post` and the ConcurrentQueue enqueue allocate as well. The Fire row should list these per-fire allocations off the graph thread.
- 'Startup: `Clock.System` is created on first use': if `GraphOptions.Default` sets `Clock = Clock.System`, then `static member Default` (Types.fs:274-280) is a property that builds a new record on each access, so every default graph touches Clock.System. Every app pays the static initialisation, not only apps that use debounce. It is small, but the row is wrong as written.
- Minor: 'costs three nodes per debounced value' for the signal-plus-effect recipe: `createSignal` + `createEffectOn` is two nodes plus a timer.
