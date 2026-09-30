# Serialised thread affinity for Blazor Server: design

*Status: implemented on worktree-wf_c46b5816-3af-7, pending benchmark gate.* Line references in §2-§8 are to
`c631f23`; §11 records where the implementation departs from this note.
Closes research §9 (Blazor) and the known limitation in `concepts/contracts.md:77` and `concepts/async-graph.md:143`.

## 1. Goal

A Blazor Server circuit runs its work one item at a time, but each item may run on a different pool thread. A graph
used from a circuit must:

- accept direct writes from event handlers and from `OnInitializedAsync` code after an `await`, whichever pool thread
  runs them;
- raise when two threads are inside the graph at once, which is the failure aspnetcore#69323 makes possible;
- run settles and dispatched work on the circuit's context, serialised with rendering.

## 2. Current behaviour

- `ThreadAffinity` has two cases, `Guarded` and `Unchecked` (`Types.fs:19-31`); the default is `Guarded`
  (`Types.fs:274-280`).
- The graph records its constructing thread once (`Core.fs:1147`) and resolves the owner's ambient cell once when
  guarded (`Core.fs:1154`). `AssertOnGraphThread` compares thread ids (`Core.fs:1526-1531`, via
  `Platform.isOffThread`, `Platform.fs:59-65`). Its callers: root creation, cleanup registration, graph dispose, `Pump`, flush,
  stale read, batch, node creation, untracked read, signal write, node dispose (`Core.fs:1360-1905, 2026, 2044, 2479`...).
- `Post` runs work inline when `IsOnGraphThread` (`Core.fs:1427, 1443-1452`), otherwise enqueues it and posts a drain
  to the dispatcher. `Settle`/`Fail` go through `Post` (`Core.fs:2119-2130, 2137-2146`).
- With no dispatcher named, a graph constructed under a `SynchronizationContext` gets `SynchronizationContextDispatcher`
  (`Platform.fs:425-433`), which `Post`s the drain to that context (`PlatformDispatcher.fs:22-27`).
- On a circuit the next work item lands on another pool thread, so `Guarded` rejects it; `Unchecked` accepts it and
  also accepts genuine concurrency. `Unchecked` makes activation flow across threads (`Core.fs:1274`) and makes the
  trace log lock its bookkeeping (`Trace.fs:591-594`).

## 3. Proposed API

```fsharp
type ThreadAffinity =
    | Guarded
    | Unchecked
    /// Work may arrive on any thread, one at a time, on the context captured at construction. A write from a
    /// thread without that context raises; two threads inside the graph at once raise.
    | Serialised

let graph = new Graph (GraphOptions.Default.WithThreadAffinity Serialised)   // constructed on the circuit's context
```

No other public surface changes. `IsOnGraphThread` (`public-api-baseline.txt:103`) means "the calling thread holds the
graph" under `Serialised`.

## 4. How it works

**Holder, not owner.** The graph keeps `holder: int` (0 when free). Every entry point that calls
`AssertOnGraphThread` today brackets its work under `Serialised`. Where the check and the end of the work sit in
different members, the bracket spans them: `EnterPull` acquires, and the outermost `ExitPull` (`pullDepth` back
to 0) releases. Node creation is the exception (§11).

1. `SynchronizationContext.Current` is reference-equal to the context captured at construction, or the call raises
   `InvalidOperationException` ("ran outside the circuit's context"). Failing this check proves misuse; passing it
   proves nothing (#69323).
2. `Interlocked.CompareExchange(&holder, me, 0)`. Result `me`: nested entry, proceed without release. Result `0`:
   acquired, release in `finally`. Any other result: raise `InvalidOperationException` naming both thread ids. This
   is the check that turns the #69323 double-render into an exception rather than a corrupted observer set.
3. On release, if the inbox is non-empty, the releasing thread drains it before clearing `holder`, then re-checks the
   inbox after the `Volatile.Write`, and re-acquires if work arrived in between.

The CAS acquire and `Volatile.Write` release give a happens-before edge between consecutive holders, which
`Unchecked` does not guarantee.

**Off-holder work is always queued.** `Post`, `Dispatch`, `DispatchApplied`, `Settle` and `Fail` run inline only when
the caller already holds the graph. Otherwise they enqueue and post a drain to the captured context, even when the
context check would pass. This is the research §9 rule: the renderer queues rather than trusting `CheckAccess`, and a
settle applied inline on a pool thread would run effects concurrently with a render. `PumpFromDispatcher`
(`Core.fs:1515`) acquires; if the graph is held, it leaves the work in the inbox for the holder's release to drain.

**Ambient and continuations.** `ownerAmbient` stays null (per-thread cell, as `Unchecked` today, `Core.fs:1310-1320`).
Activation flows with the execution context (`Core.fs:1274`, third argument true). `EnterContinuation`/
`LeaveContinuation` (`Core.fs:1832, 1857`) use the holder test, so a flight continuation that resumes without holding
the graph stays untracked, as an off-thread one does today. The trace log uses its locked mode.

**Fable.** One thread: the context check is skipped and the CAS is a field write, behind a `Platform` helper, as
`isOffThread` is today (`Platform.fs:59-65`). `Serialised` behaves as `Unchecked` there.

## 5. Blazor mapping

One graph per circuit: a scoped service, constructed during the first component activation, on the renderer's
context, so the default dispatcher captures it. One owner scope per component. The full sample, which compiles
and was run under `HtmlRenderer`, is `docs/content/guide/blazor-server.md`; its shape:

```fsharp
type CircuitGraph() =
    let graph = new Graph (GraphOptions.Default.WithThreadAffinity Serialised)
    member _.Graph = graph
    interface IDisposable with
        member _.Dispose() = graph.Dispatch (fun () -> graph.Dispose ())

type CartSummary(circuit: CircuitGraph, cart: CartStore) =     // constructor injection, .NET 9+
    inherit ComponentBase()
    let graph = circuit.Graph
    let mutable scope: Owner option = None
    let mutable view: Memo<CartView> option = None

    member private this.Rerender() = this.StateHasChanged ()
    member private this.RequestRender() = this.InvokeAsync (Action this.Rerender) |> ignore

    override this.OnInitialized() =
        graph.Run (fun () ->                      // activates the graph around createRoot
            createRoot (fun owner ->
                let summary = createMemo (fun _ -> ...)
                createEffectOn (fun () -> summary.TryValue) (fun _ -> this.RequestRender ())
                scope <- Some owner
                view <- Some summary))

    interface IDisposable with
        member _.Dispose() = scope |> Option.iter _.Dispose()
```

Event handlers write signals directly (`@onclick` runs on the circuit's context, acquires, flushes, releases). An
`HttpClient` call inside an async memo settles from a pool thread and is queued to the context; the effect then
requests the render. The effect runs inside a flush on the renderer's context, where `InvokeAsync` may run
`StateHasChanged` inline; that only queues a render. Rendering reads memos by pull, which is glitch-free mid-flush.

## 6. Cost model

Measured on this VM (4 cores, .NET 10, BenchmarkDotNet 0.15.8, probe benchmarks in a throwaway worktree):

| Case | Mean |
| --- | --- |
| Thread-id compare (today's `Guarded` check) | 4.0 ns (short job) |
| CAS acquire + `Volatile.Write` release, uncontended | 10.2 ns (short job) |
| Context reference check + CAS pair | 10.2 ns (short job) |
| Signal write then memo read, `Guarded` | 39.1 ns ± 2.5 (full job) |
| Signal write then memo read, `Unchecked` | 35.8 ns ± 2.4 (full job) |

- **Write (top level).** About 6 ns over `Guarded` for the bracket, plus a `try/finally`. Nested writes (inside a
  flush, effect or batch) pay one `Volatile.Read` compare. Per-call ambient resolution costs no more than `Guarded`
  (the `Unchecked` row).
- **Recompute, flush.** Unchanged: bodies run inside the holder bracket of the entry point that started them.
- **Cross-thread settle.** One context `Post` per drain, as `Guarded` pays on WPF. The existing research measured a
  hop at 2.35-3.32 µs (`Core.fs:1432-1437`). A settle arriving on the context itself is also queued: one extra hop
  and one extra work item of latency versus an inline apply.
- **Memory.** Per node: none. Per graph: one `int` and one context reference. Allocations: none beyond today's drain
  delegate (`Core.fs:1452`, `PlatformDispatcher.fs:27`).
- **Code that does not use it.** `Guarded` and `Unchecked` pay one branch on a readonly field at each entry point.
  Unmeasured; settle it by parameterising `Signals.fs` write benchmarks and `Memos.fs` `Recompute` over
  `[Guarded; Unchecked; Serialised]` and comparing `Guarded` before and after on the same machine.

## 7. AOT, trimming, breaking

- `Interlocked`, `Volatile` and `SynchronizationContext` are AOT- and trim-safe; no reflection.
- A new union case adds `Tags Serialised`, `IsSerialised` and `get_Serialised` to the baseline (additive). F# code that
  matches `ThreadAffinity` exhaustively gets FS0025; Ranvier is unreleased.
- Behavioural change confined to the new case. The C# `BoundSignal<'T>.Value` setter (`Bindings.fs:195-201`) goes
  through `Dispatch`, so under `Serialised` a setter called outside the holder applies at the next drain: a read right
  after the set sees the old value. `ReactiveBindings.Dispose` (`Bindings.fs:496`) also goes through `Dispatch`, so a
  disposal outside the holder is deferred to the next drain in the same way.

## 8. Alternatives

- **Rebind the owner at each drain.** Fails for event handlers, which the renderer runs, not our dispatcher.
- **`Unchecked` plus a documented rule.** Works until #69323 fires, then corrupts silently.
- **A lock around every entry.** Blocks a renderer thread behind another and runs user code under a lock, contrary to
  `contracts.md:67-75`.
- **Context check alone.** #69323 shows it passes on the wrong thread.
- **`Dispatch` acquires and runs inline when the context check passes.** Keeps C# setters synchronous; risks running
  effects concurrently with a render in the #69323 case. Direct writes already take that risk.

## 9. Recommendation

**Do**, gated on the `Guarded` write benchmark staying within noise. Update `contracts.md` Threading (new row per
operation) and replace both known-limitation sections. Ship the component pattern as a docs page before any
`Ranvier.Blazor` package.

## 10. Questions for the maintainer

Answered in `docs/.ai/wave-b/decisions.md`:

1. Case name `Serialised`: yes.
2. `Dispatch` from the captured context outside the holder: queue.
3. Blazor component helper: docs only (`docs/content/guide/blazor-server.md`).


## Reviewer corrections (applied)

Verdict: needs fixes

- Severity: moderate. The §5 Blazor component does not compile or run as written. (1) `let mutable scope: Owner = null`, `member val Graph: Graph = null` and `member val View: Memo<CartView> = null`: Owner, Graph and Memo carry no `[<AllowNullLiteral>]` (the library itself uses `Unchecked.defaultof<Owner>`, Core.fs:3241), so these are FS0043 errors. (2) `this.Graph.CreateRoot (fun owner -> ... createMemo ...)`: `RunRoot` (Core.fs:1359-1370) sets only `currentOwner` and does not make the graph ambient, while `createMemo` uses `Graph.Current` (Api.fs:93-94). The call raises 'No ambient graph on this thread'. Add `use _ = this.Graph.Activate ()` (flowing under Serialised) or construct against the graph explicitly.
- §7 'C# `ReactiveProperty` setters (`Bindings.fs:193-201`)': Ranvier.CSharp has no `ReactiveProperty` type. The setter is `BoundSignal<'T>.Value` (Bindings.fs:195-201). `ReactiveBindings.Dispose` (Bindings.fs:496) also goes through `Dispatch`, so disposal outside the holder is deferred to the next drain in the same way.
- §2 list of `AssertOnGraphThread` callers omits `Pump` (Core.fs:1492).
- §4 'Every entry point that calls `AssertOnGraphThread` today brackets its work ... release in `finally`': the stale-read check sits in `EnterPull` (Core.fs:1624-1625), and its end is the separate `ExitPull` (1638). A per-method try/finally cannot bracket it: acquire has to happen in EnterPull and release in ExitPull, with pullDepth as the nesting count. The same applies wherever the assert and the end of the work are in different members.
- Minor line references: '`EnterContinuation`/`LeaveContinuation` (`Core.fs:1833, 1859`)' → 1832, 1857.

## 11. Implementation notes and deviations

- **Entry bracket.** `Graph.Entered (operation, body)` is an inline helper over `Enter`/`Release`. Under `Guarded`
  it is the old check plus one branch on the returned `false`; the body is inlined on both paths, so the `Guarded`
  path carries no `try/finally`. Under Fable it is `AssertOnGraphThread` then the body, and `serialised` is the
  constant `false`, so `Serialised` behaves as `Unchecked` there.
- **Release posts, it does not drain** (FOR-REVIEW at `Graph.Release`). The note has the releasing thread drain
  the inbox before clearing `holder`. The implementation clears `holder`, then posts a drain to the dispatcher when
  the inbox is non-empty. This keeps user work out of the `finally` of a failing entry, at the cost of one context
  hop for a settle that arrived while the graph was held. Under `ImmediateDispatcher` the post drains at once.
- **Node creation checks, it does not hold** (FOR-REVIEW at `Graph.NextId`). The constructor's work ends outside
  `NextId`, so a top-level node creation verifies the context and that no other thread is inside, then proceeds
  unheld. A node created inside `createRoot`, a body or a batch is held by that entry. The public
  `AssertOnGraphThread` has the same check-only meaning under `Serialised`.
- **Root scope disposal holds.** `Owner.Dispose` on a `RootScope` acquires through `ILateRunner.HoldTeardown`
  before it marks the scope disposed, so a rejected `Dispose` leaves the root live and a retry tears it down.
  `ILateRunner.RunDetached` (late children of a disposed root) acquires as well. Both acquire under `Serialised`
  only; `Guarded` still checks per disposed node, as before.
- **Release fences.** `Graph.Release` frees the graph with `Interlocked.Exchange`, a full fence, before it reads
  the inbox. A volatile store followed by the inbox's volatile loads could reorder and lose the wakeup against a
  drain whose CAS found the graph held.
- **Stale reads from a second thread.** `Graph.Deferring` is false under `Serialised` on every thread other than
  the holder (FOR-REVIEW at `Graph.Deferring`). Without it, a stale read during the holder's flush saw `current`
  set, skipped `EnterPull` and refreshed the memo unheld. The cost is one branch on stale reads inside a body for
  every affinity.
- **Null context.** A graph constructed with no `SynchronizationContext` captures null; the context check then
  passes on every thread without a context, and the concurrent-entry check still applies. The .NET tests use this.
- **Trace queries.** `Trace.*`'s thread gate calls `AssertOnGraphThread` under `Serialised` (a free graph on the
  context may be queried), and keeps `IsOnGraphThread` for the other affinities.
- **Error message.** The `Guarded` message is unchanged. `Serialised` adds two:
  `<op> ran on thread N outside the synchronisation context this Serialised graph was constructed on. ...` and
  `<op> ran on thread N while thread M was inside this Serialised graph. Two threads entered it at once.`
  Both are in `guide/troubleshooting.md`.
- **Blazor sample.** Constructor injection (.NET 9+) and `option` fields replace the nullable `[<Inject>]`
  properties; `graph.Run` activates the graph around `createRoot`. The container may dispose scoped services off
  the renderer's context, so the sample wraps the graph in `CircuitGraph`, whose `Dispose` goes through
  `Dispatch` (FOR-REVIEW: whether ASP.NET Core disposes the circuit scope on the renderer's context was not
  verified).
- **Benchmarks.** `SignalBenchmarks` and `MemoBenchmarks` take an `Affinity` parameter over
  `[Guarded; Unchecked; Serialised]` for the whole class, so `Read`, `Peek` and the cached reads are also
  multiplied, and every row's id gains `Affinity=...`. The A/B gate compares the `Guarded` rows against master's
  unparameterised rows. A short-job sanity run of `Recompute` on this VM: Guarded 47 ns, Unchecked 44 ns,
  Serialised 76 ns (short job, error bars of 10-60 ns). `Serialised` pays two acquire/release pairs per
  `Recompute` (the write and the stale read), more than §6's single-bracket estimate.
- **Tests.** `tests/Ranvier.Tests/Threading.fs` (`Serialised: ...`): concurrent entry from two threads raises for
  each entry point (stale reads included), a root `Dispose` rejected while another thread holds the graph leaves
  the root live and a retry disposes it, an entry off the construction context raises, a settle from outside the graph is queued even
  on the context, work posted from inside runs inline, and a drain that finds the graph held is re-posted by the
  holder. One smoke test runs under Fable. `tests/Ranvier.CSharp.Tests` covers the deferred `BoundSignal` set.
