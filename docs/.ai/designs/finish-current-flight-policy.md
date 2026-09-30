# FinishCurrent flight policy: design

**Status:** implemented on `worktree-wf_c46b5816-3af-2`, pending benchmark gate. Maintainer decisions (§9, from
`docs/.ai/wave-b/decisions.md`): trailing run only, no pure drop; named `FinishCurrent`; per-memo override later, not
now. Line references are to `src/Ranvier/Core.fs` and `src/Ranvier/Types.fs` at `c631f23`, with the reviewer's
corrections applied; the code has moved since (the shared-token retirement fix added `flying`, `running` and
`retireIfQuiet`), so re-locate by name. Costs are read from the code; no numbers were measured. §5 names the
benchmark that checks them, and §10 records how the implementation differs from this note.

## 1. Goal

Research §1 lists "drop / skip-while-running" as a policy users already ask for (CommunityToolkit #1125, R3
`AwaitOperation.Drop` and `ThrottleFirstLast`). Ranvier has three policies (`Types.fs:34-51`), and all three start
a new flight on every dependency change:

- `CancelPrevious` cancels the flight in progress (`Core.fs:3471-3476`).
- `KeepLatest` lets it run and discards its result (`Core.fs:3349`).
- `Queue` applies every result in start order (`Core.fs:3345-3347`).

The new case lets the flight in progress finish and collapses every change that arrives during it into **one trailing
run** after it settles. Work in progress is never abandoned, and the settled value always matches the current inputs.

## 2. Proposed API

```fsharp
type FlightPolicy =
    | CancelPrevious
    | KeepLatest
    | Queue
    /// Lets the flight in progress finish. Changes that arrive during it start no flight; once it settles, the memo
    /// runs once more against the current inputs. The settled result becomes Peek and Previous, and the memo stays
    /// Pending until the trailing flight settles.
    | FinishCurrent
```

`GraphOptions.Default.WithFlightPolicy FinishCurrent` selects it. No other surface changes; C# reaches it through
`FlightPolicy.FinishCurrent`.

## 3. Semantics

| Event | `FinishCurrent` |
| --- | --- |
| Change, no flight in progress | Runs the body and starts a flight, as `KeepLatest` does. |
| Change while a flight is in progress | Body does not run. Freshness returns to Clean, sources stay as tracked, a trailing run is owed. |
| Flight settles, nothing owed | Applied as under `KeepLatest`: Ready or Error, readers woken. |
| Flight settles Completed, run owed | `value` written (becomes `Peek` and the next `Previous`), status stays Pending, freshness Dirty, readers woken. The next pull runs the trailing flight. |
| Flight settles Faulted or Canceled, run owed | Outcome discarded (`FlightDrop`), status stays Pending, trailing run as above. |
| Body suspends on a pending source (`NotReadyException`, `Core.fs:3540-3547`) | No flight started, so nothing is in progress; the next change runs normally. |
| Dispose during a flight | Unchanged: token cancelled, `ObjectDisposedException` (`Core.fs:3669-3700`). The owed run is dropped. |

`Previous.Settled` is complete whenever the body runs, as under `CancelPrevious` (`Core.fs:3187`): one flight is in
progress at a time, so the waiter path (`Core.fs:3712-3726`) is never taken.

The trailing run is lazy. It starts on the next pull, like every async memo run (`Core.fs:3599`); an unobserved memo
runs it on its next read.

A pure drop, with no trailing run, is **not** proposed. Its settled value would pair with inputs the graph has left,
and the memo would publish that value as Ready until an unrelated change arrived. That is the stale pairing the
generation check exists to prevent (comment `Core.fs:3249-3254`, field `3255`).

## 4. How it works

State: whether a flight is in progress, and whether a run is owed. Two encodings:

- **Reuse `queued`** (`Core.fs:3265`), today read only under `Queue`: 0 idle, 1 in flight, 2 in flight with a run
  owed. No new field.
- **One `byte` field.** Clearer; object size may or may not grow depending on how the runtime packs it beside the four
  existing `bool`s (`disposed`, `violated`, `published`, `launching`).

Changes:

1. **`Start`** (`Core.fs:3440`), before the scope is discharged (a `createAsyncWith` flight keeps its scope while it
   runs):
   ```fsharp
   if queued <> 0 && graph.Options.FlightPolicy.IsFinishCurrent then
       freshness <- Freshness.Clean
       queued <- 2
       Tracer.RunDeferred (graph, id)       // new event, [<Conditional("RANVIER_TRACE")>]
   else ...existing body
   ```
   With the `queued` encoding, the first test reads a field that is 0 whenever no `Queue` chain is pending, so the
   policy load runs only under `Queue` with results outstanding or under `FinishCurrent` with a flight in progress.
2. **`Launch`** (`Core.fs:3470-3480`): `FinishCurrent` joins the `KeepLatest | Queue` arm (one CTS, created once).
   At the settle attachment (`Core.fs:3557-3564`) it joins the `KeepLatest` arm and sets `queued <- 1` first.
3. **`applyResult`** (`Core.fs:3340-3389`): `FinishCurrent` joins the `CancelPrevious | KeepLatest` arm of the first
   match (`gen = generation` holds, since a deferred change bumps nothing) and the no-op arm of the second. An owed run
   reuses the existing `suspended` arms (`Core.fs:3357-3367`), which already write a Completed value without waking
   and drop a failure:
   ```fsharp
   let owed = queued = 2                    // only FinishCurrent sets 2
   queued <- 0
   let suspended = owed || (not (isNull pendingSources) && pendingSources.Count > 0)
   ...existing match...
   if owed && not disposed then
       freshness <- Freshness.Dirty
       observers.NotifyDirty ()
       graph.RequestFlush ()
   ```
   Under `Queue` this code would clobber `queued`, so under the `queued` encoding the owed bookkeeping needs its own
   `FinishCurrent` arm in both matches on the policy: it cannot share the `CancelPrevious | KeepLatest` arm of the
   first match. With a dedicated field it runs unconditionally and costs one field test per applied result.
5. **Trace.** Reusing the `suspended` arms also reuses `Tracer.FlightSettled (..., held = true, ...)`, which sets
   `Settle.Flag = 1` and would present an owed-run settle the same as a settle held by a pending source. The owed
   settle takes its own flag value, and a failure discarded for an owed run its own `TraceDropReason` (§10).
4. **`fail`** (`Core.fs:3497-3506`): unchanged; a synchronous failure starts no flight.

`MarkDirty` (`Core.fs:3642-3653`) is unchanged. It still notifies dependents on every change during a flight; they
re-run and read Pending, as they do under the other policies today.

## 5. Cost model

**Code that uses other policies.**

| Path | Added work |
| --- | --- |
| Signal write, memo recompute, flush, effect run | None. No code on these paths changes. |
| `AsyncMemo.Start` (once per flight launch) | One `int` field test (`queued <> 0`), false for `CancelPrevious` and `KeepLatest`. Under `Queue` with results outstanding, one policy tag compare. |
| `Launch`, `applyResult` | None with the `queued` encoding: the new case joins existing arms of matches on `FlightPolicy`, which compile to a switch on `Tag`, and the owed bookkeeping sits in its own `FinishCurrent` arms. With a dedicated field, one field test per applied result. |
| Memory per `AsyncMemo` | None with the `queued` encoding; at most one byte (possibly one 8-byte slot) with a new field. |
| Allocations | None. |

**Code that uses `FinishCurrent`.** A deferred change allocates nothing and runs no body: no `CancellationTokenSource`
(`Core.fs:3476`), no flight `Task`, no `Previous`, no settle closure. For N changes during one flight it replaces N
flight starts (N body runs, N flights, and under `CancelPrevious` N CTS allocations) with N field writes and one
trailing flight.

**Benchmark to settle it.** No existing case launches flights repeatedly; `SettleBenchmarks`
(`bench/Ranvier.Benchmarks/Suspension.fs:114-135`) settles an `AsyncSource`. Add `FlightBenchmarks` to
`Suspension.fs` with `[<Params(CancelPrevious, KeepLatest, Queue, FinishCurrent)>]`:

- `RelaunchSettled`: write a signal an async memo reads, whose body returns a completed task; read the memo. Run on
  `master` and on the branch: the three existing policies must show no time or allocation change.
- `WritesDuringFlight` (`[<Params(1, 10)>]` writes): the body returns a task from a `TaskCompletionSource` held open;
  write N times, then complete it. Expected under `FinishCurrent`: two body runs and allocation independent of N.

## 6. Fable and AOT

- **Fable.** A nullary union case and an `int` field. Outcomes already arrive on a microtask
  (`Platform.fs:159-165`); the deferred path runs on the graph thread in `Start`. No `#if`.
- **AOT and trim.** No reflection, no new generic instantiation. Safe.

## 7. Compatibility

Additive to `docs/.ai/public-api-baseline.txt`: `FlightPolicy+Tags FinishCurrent`, `get_IsFinishCurrent`,
`static get_FinishCurrent`. Binary compatible. Source-breaking for an F# caller who matches `FlightPolicy`
exhaustively: FS0025 warning, and a `MatchFailureException` at runtime for an unhandled case. Ranvier is unreleased.

## 8. Tests and alternatives

Tests: two writes during a flight give `Runs = 2` and a trailing run against the final inputs; the trailing body's
`Previous.Settled` is the first flight's value; a fault with a run owed is discarded; dispose with a run owed runs
nothing; a flight with no change during it behaves as `KeepLatest`; a body suspended on a pending source does not
block the next run. Each runs under `ManualDispatcher` and on the Fable suite.

Alternatives:

- **Pure drop** (§3): rejected; publishes a value unrelated to the current inputs.
- **Publish the in-flight result as Ready, then run the trailing flight.** Gives stale-while-refreshing, but wakes
  readers with a value from inputs the graph has left, one flush before marking them dirty again. The proposal keeps
  the `Queue` precedent (`Core.fs:3357-3360`): the value lands in `Peek`, the status stays Pending.
- **Per-memo policy.** `FlightPolicy` is graph-wide (`GraphOptions.FlightPolicy`, `Types.fs:260`, read at
  `Core.fs:3344`). A submit-style memo wants `FinishCurrent` while a search memo in the same graph wants
  `CancelPrevious`. A per-memo override (`createAsyncWithPolicy`) costs one field per `AsyncMemo` and a read of it
  instead of `graph.Options`; it is a separate design.

## 9. Recommendation

**Do.** About 30 lines in `AsyncMemo`, no cost to other policies beyond one `int` test per flight launch, and it
closes research §1's missing "skip-while-running" policy without the stale value a pure drop publishes.

Questions for the maintainer:

1. Trailing run only, with no pure-drop case? (yes / no)
2. Name: `FinishCurrent`, or R3's `ThrottleFirstLast`? (FinishCurrent / ThrottleFirstLast)
3. Design a per-memo policy override next? (yes / no)


## Reviewer corrections (applied)

Verdict: needs fixes

- Severity: minor. The design logic checks out against Core.fs: `queued` is written only under Queue (3346, 3499-3500, 3559); `Previous` position = `chained`, which moves only under Queue, so Settled is always complete; the settle attachment sets queued before an inline settle can run. The fixes below are to line references and one trace detail.
- Line references drift by 1-2 lines: 'generation check ... (`Core.fs:3252-3257`)' → comment 3249-3254, field 3255; 'NotReadyException, `Core.fs:3538-3545`' → 3540-3547; 'suspended arms (`Core.fs:3357-3365`)' → 3357-3367; '`fail` (`Core.fs:3497-3505`)' → 3497-3506; '`Launch` (`Core.fs:3470-3479`)' → 3470-3480; '`SettleBenchmarks` (`bench/Ranvier.Benchmarks/Suspension.fs:114-140`)' → 114-135 (the file has 135 lines at c631f23).
- Reusing the `suspended` arm for an owed run also reuses its trace call `Tracer.FlightSettled (graph, id, gen, 0, true, box v)`, whose `held` flag sets Flag=1 (Trace.fs:1038). Trace queries will present an owed-run settle the same as a settle held by a pending source. Say that either a separate flag value is needed or the traced semantics change. This fits alongside the new RunDeferred event.
- §5: 'Under `Queue` with results outstanding, one policy tag compare' is correct. Add that under the `queued` encoding the FinishCurrent owed bookkeeping in `applyResult` needs its own match arm (it cannot share the `CancelPrevious | KeepLatest` arm as the §4 text says and still sit 'inside the FinishCurrent arm'). The cost stays zero for other policies.

## 10. Implementation and deviations

Implemented as §4 with the `queued` encoding (0 idle, 1 in flight, 2 in flight with a run owed). Differences:

- **Source retirement.** The shared-token fix that landed after this note retires the shared
  `CancellationTokenSource` once no flight holds it (`retireIfQuiet`). `FinishCurrent` joins it with
  `queued = 0`, so under `FinishCurrent` every flight settles into a quiet memo and the next flight allocates a new
  source: one source per flight, as `CancelPrevious` pays, never cancelled. §5's "a deferred change allocates
  nothing" still holds.
- **`suspended`.** `owed` is folded into the existing `suspended` test (`owed || pendingSources...`): one local
  `bool` test per applied result for every policy, in place of duplicating the four outcome arms. Tagged
  `FOR-REVIEW` for the benchmark gate.
- **Wake after an owed settle.** In its own `FinishCurrent when owed && not disposed` arm of the final policy match,
  per the reviewer: marks the memo `Dirty`, notifies observers dirty and requests a flush. The inline-settle path
  (`launching`) cannot owe a run, since `queued` is set to 1 immediately before the settle attaches.
- **Trace.** `Tracer.FlightSettled` takes `held: int`. `Settle.Flag` is 1 for a settle held by a run suspended on a
  pending source and 2 for one held by an owed trailing run; `TraceFlightState.Settled`'s `held` is `Flag <> 0`. A
  failure discarded with a run owed records `FlightDrop` with the new `TraceDropReason.Trailing = 4`, not
  `Suspended`. The new `TraceEventKind.RunDeferred = 24` records the deferred change (`Other`: the puller, `Arg`:
  the flight in progress, `Cause`: the first dirty mark). The owed notification's cause is the held settle
  (`Tracer.TrailingRun`).
- **Benchmarks.** `FlightBenchmarks` in `Suspension.fs` takes the policy as a string `Params` (union cases are not
  attribute constants) and uses a separate trigger per memo, so `RelaunchSettled` marks nothing else. `Writes`
  (`1`, `10`) also multiplies the `RelaunchSettled` cases. The master side of the A/B run must drop
  `"FinishCurrent"` from `Params`.
- **Tests.** `Async.fs` (seven `FinishCurrent` cases, covering §8's list plus an effect-driven trailing run; the `Previous` case is .NET-only because it reads `Settled`'s
  completion synchronously),
  `Tracing.fs` (`RunDeferred`, `Settle.Flag = 2`, `TraceDropReason.Trailing`), and `FinishCurrent` joins the policy
  loops of `Retention.fs` and `AsyncEdges.fs`. `Ranvier.CSharp.Tests` reaches it as `FlightPolicy.FinishCurrent`.
- **Signal maps.** `policy=finish-current` added to the map fence flags (`docs/maps`).
- **Docs.** `guide/async-and-pending.md` (policy table, `FinishCurrent` paragraph, name mapping, cost and previous
  value), `concepts/async-graph.md`, `concepts/contracts.md`, `concepts/roadmap.md`, `guide/tracing.md`,
  `guide/signal-maps.md` and `benchmarks/suspension.md`.
