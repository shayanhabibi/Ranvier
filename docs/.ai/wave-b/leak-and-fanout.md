# Leak and fan-out investigation

Branches are local proposals, not pushed.

## Shared flight token (KeepLatest / Queue)

Branch `worktree-wf_b9cea08f-842-1` at `46b4c6d`.

## Shared flight token under KeepLatest/Queue

**Finding.** The leak is real, but only for some bodies. Under `KeepLatest` and `Queue` every flight shares one `CancellationTokenSource`, and it lives until the memo is disposed (baseline `src/Ranvier/Core.fs:3477-3480` in Launch, `:3685` in Dispose). So a registration that is never disposed, and everything its callback captures, lives as long as the memo. Registrations that the API disposes itself are not affected.

**Evidence (measured).** 10,000 flights per case. I held each captured payload with a WeakReference and forced a GC.

| Body | CancelPrevious | KeepLatest | Queue |
|---|---|---|---|
| a) `Task.Delay(1, token)` (2,000 flights) | 0 | 0 | 0 |
| `use _ = token.Register` (disposed) | 0 | 0 | 0 |
| b) `token.Register` not disposed, flight completes | 1 | **10,000** | **10,000** |
| c) completes only when cancelled, flights overlap forever | 1 | **10,000** (8.9 MB heap) | **10,000** (12.5 MB) |
| c) after disposing the memo | 0 | 0 | 0 |

- **Case a** is bounded by the operations still in flight, as you expected.
- **Case b** is the leak.
- **Case c** is how KeepLatest and Queue are meant to work. A superseded flight is deliberately never cancelled, so a body that finishes only when cancelled stays in flight until the memo is disposed. Under Queue it also blocks the chain. Fixing that would turn them into `CancelPrevious`, so I documented it instead of changing it.

**Root cause.** The shared source's lifetime is tied to the memo, not to the flights using it. `CancellationTokenSource.Dispose` drops its registrations (measured: case b goes to 0 once the source is disposed), but nothing disposes the source before the memo goes.

**Proposed fix** (commit `46b4c6d`, not pushed):
- **Core.fs:** under KeepLatest and Queue, dispose the shared source without cancelling it once every flight holding it has settled and no body is running. The next flight allocates a new one. Counters used: `flying` for KeepLatest, the existing `queued` for Queue, and a new `running` guard around the body.
- **Why `running` is needed:** without it, a body that settles the previous flight inline disposed its own live token. My first attempt had that bug and the overlap test caught it.
- **Dispose change:** Dispose now detaches `cts` before cancelling it. Cancelling can settle flights inline, which disposes the source and caused a null-reference crash. A test caught that too.
- **Limit (measured):** while flights keep overlapping, registrations still build up. The overlap test keeps 10,000 while running and releases them all when the last flight settles.
- **Also committed:** new tests in `Retention.fs` and an updated flight-policy cost section in `async-and-pending.md`.

**Cost** (Stopwatch A/B, 500k flights with `Task.FromResult` bodies, 3 alternating processes × 2 rounds; noisy machine):
- **Allocation:** KeepLatest goes from 672 to 720 B/flight and Queue from 1320 to 1368 B/flight. That is one source (+48 B), the same as CancelPrevious already pays.
- **Time:** KeepLatest base 386–438 ns, fixed 407–472 ns. That is within noise, and I infer the real cost is about +10–25 ns. Queue shows no measurable change (base 716–848 ns, fixed 742–806 ns).
- **Primitive costs:** allocating and disposing a source is 11–23 ns and 48 B. Cancel adds about 20 ns.
- **Async bodies (inferred, not measured):** they pay one source per quiet period rather than per flight, which is negligible next to a task state machine.
- **Unchanged:** CancelPrevious, and every non-async node.

**Semantic change.** Leftover work from a flight that already settled (fire-and-forget that kept the token) is no longer cancelled when the memo is disposed once the source has been retired. Work still in flight is still cancelled; the test "disposing the memo cancels the flight in progress after an earlier flight settled" checks this.

**Tests.** 12 new cases in `tests/Ranvier.Tests/Retention.fs` (4 × 3 policies):
- "an undisposed token registration is released once its flight settles"
- "overlapping flights release their registrations once the last one settles"
- "disposing the memo cancels the flight in progress after an earlier flight settled"
- "a flight that never settles is released when the memo is disposed"

The first two fail on the baseline for KeepLatest and Queue, 4 failures (measured). The full .NET suite passes, 767/767 on net8.0 and net10.0. The Fable suite has the same 56 known inline-delivery failures before and after, with no new ones.

**Alternative (not implemented).** A per-flight linked source would bound retention by in-flight flights even while they keep overlapping. It costs 60–73 ns and 64 B per flight measured, plus bookkeeping, and applies on every flight, not once per quiet period.

**Recommendation.** Take the retire-at-quiescence fix: it makes the common raw-`Register` and `TaskCompletionSource` pattern safe for about 48 B. If you'd rather not pay any cost, keep the code as it is and use just the docs paragraph, which says bodies must dispose their registrations.

**Questions**
1. Is leftover work losing cancellation on memo dispose acceptable?
2. Should the docs steer bodies that only finish on cancellation away from KeepLatest and Queue more strongly, or should that raise a warning?
3. Is bounding retention by in-flight flights while they keep overlapping worth the linked-source cost?

### Independent review

**The leak is real, and fix `46b4c6d` is correct as far as it reaches, but it misses flights whose body fails before returning a task (details below).** I re-ran the tests on net10.0 Release in a scratch clone and have removed that clone.

**Confirmed**
- **Line references.** The baseline Launch shares one source for KeepLatest and Queue (Core.fs 3476-3480), and Dispose cancels it at about line 3686. The old docs said so too ("cancel it only when the memo is disposed").
- **The four baseline failures.** With the baseline Core.fs and the new tests, both new retention tests fail under KeepLatest and Queue, 4 failures in all. With the fix, all 18 Retention tests pass.
- **Threading.** `flying`, `running` and `queued` are plain mutable fields. That is safe: `applyResult` goes through `graph.Post`, which runs it inline on the graph thread or queues it for that thread.
- **The `running` guard and the change to Dispose are needed, and only because of the fix.** The baseline Dispose never retires the source, so it could not crash. Under the fix, cancelling can settle flights inline, which calls `retireSource`, so detaching the source before cancelling is correct.
- **Allocation cost** (my own A/B, 500k flights with `Task.FromResult` bodies, 3 rounds, separate processes):
  - KeepLatest goes from 864 to 912 B/flight and Queue from 1512 to 1560 B/flight. That is exactly +48 B each.
  - CancelPrevious is unchanged (912 to 912).
  - My totals are higher than the report's because the harness differs, but the difference matches.
  - Time is lost in noise (KeepLatest 616-758 ns base against 558-729 ns fixed), which is consistent with "within noise".

**Weakened or refuted**
- **"Registrations are released once the flights settle" is incomplete.** Nothing retires the source for flights that fail synchronously or suspend before returning a task. The cases are `fail` when `queued = 0`, the `NotReadyException` suspend, and the violation path; none of them calls `applyResult`, so the retire check never runs. My probe tests, 2,000 flights each:
  - Body calls `token.Register` and then throws before returning a task: 2,000 payloads retained under KeepLatest and Queue with the fix; CancelPrevious passes.
  - Body settles the previous flight inline, then every other flight throws: 2,000 retained under KeepLatest and Queue.
  - The baseline leaks in the same cases, so this is not a regression, only a gap. A body written as a `task { }` block captures its exceptions in the task and is not affected. A body that registers and then reads a pending source outside the task is.
  - **Cheapest close:** run the same check (`running = 0 && flying = 0`, or `queued = 0`, then `retireSource ()`) at the end of Launch's `with` handlers, or once after the try block. I did not measure that change.
- **"Unchanged: CancelPrevious" is not strictly true.** Every flight under every policy now pays for `running <- running ± 1` and a policy match in `applyResult`. It costs nothing measurable, but the path does change.
- **The semantic change is slightly wider than reported.**
  - Leftover work that still holds a retired token no longer gets cancelled when the memo is disposed. The report says this.
  - That work also sees a disposed source, which the report does not say. I checked on .NET 10: `token.WaitHandle` throws `ObjectDisposedException`. `Register`, `CreateLinkedTokenSource`, `Task.Delay(t)` and `SemaphoreSlim.WaitAsync(t)` still work.
  - CancelPrevious already behaves this way at the next launch, so it is consistent, but the docs paragraph should mention it.
- **Retirement waits for the graph thread.** A flight that settles on another thread is only counted once its result is applied, so with the ManualDispatcher the source is retired only at the next `Pump`. That fits the design, but "once its flight settles" really means "once its result is applied".

**Bugs in the proposed change**
- The only correctness gap I found is the synchronous-failure one above. Its effect is retention, not a crash.
- I found no double-dispose, no use-after-retire, and no counter imbalance on the paths that start a flight:
  - `flying` goes up only when `whenSettled` is attached, and down in `applyResult`.
  - A `Queue` synchronous failure with `queued > 0` goes up and down in pairs.
  - An `applyResult` that arrives after Dispose finds `cts` null and does nothing.

Worktree: /home/claude/ranvier/.claude/worktrees/wf_b9cea08f-842-1 (commit 46b4c6d; Core.fs lines 3349-3374, 3533-3595, 3716-3721; tests/Ranvier.Tests/Retention.fs).

## Fan-out and diamond cost

Branch `worktree-wf_b9cea08f-842-2` at `0372637`.

## Fan-out cost: where Ranvier's time goes (proposal only, not pushed)

**Short answer.** About 60% of the gap to R3/Rx comes from the pull-based design itself. The other ~40% (~15 ns per memo) is Ranvier's own per-read overhead. About half of that is the bracket around each read made from outside the graph. There is a little fat to cut. The bigger saving needs you to decide on semantics.

Note: the worktree started on `main`, so I reset it to `claude/ecosystem-pain-points-wave-a` (6869724) before doing anything.

### Measured
Setup: Stopwatch harness, one process per scenario, baseline and variant alternating, 4–5 rounds. The machine was shared, so noise was ±10%; an unchanged control scenario moved by up to 9%.
- **FanOut** (1 signal, 100 memos, write then read all): ~2.45 µs, about 25 ns per memo.
  - Write phase ~260 ns (~2.6 ns per mark). Read phase ~2.3 µs.
- **Floor:** a minimal hand-written pull memo (dirty flag, dynamic tracking, positional edge match, cutoff) runs the same shape in **~0.95 µs** (~9 ns/memo). That is about R3's number.
- **Pull bracket on reads from outside the graph** (`Memo.Pull`: `EnterPull` + thread guard + ambient + try/finally + `ExitPull`, Core.fs ~2440 and ~1618):
  - Removing it entirely (not shippable) gives FanOut **−27…−32%** and Recompute −18…−26%, in 3 separate runs.
  - The thread guard alone is ~2 ns of it (`Environment.CurrentManagedThreadId` is a real call).
  - The try/finally is ≤1 ns. The rest is call and spill overhead.
  - `AggressiveInlining` did not recover it.
- **`RunHosted` save/restore** (Core.fs ~1790/1813): 2 of its 4 reference stores are GC write barriers that are usually redundant. Dropping them gives Diamond −12…−15%.
- **Leaf-memo `NotifyCheck` call on write** (empty observer set): write phase −11%.
- **Tracing is off:** `[<Conditional("RANVIER_TRACE")>]` removes the calls. No `Tracer` code appears in the disassembly, and nothing allocates (BDN shows 0 B).
- **Already fine (from the .NET 10 disassembly):** Dynamic PGO inlines `RunHosted`, the closure chain, the user lambda, `Signal.Value`, `Track` and `SourceList.Add` all into `Memo.Run`.

### Inferred
- The rest of `Run` over the floor (~8 ns) is spread over features, each 0.5–1 ns: status/error/pending bookkeeping, `ValueSome` prev, violated/published flags, `EndRun`, the status-aware cutoff, and F# `as this` init checks (`FailInit` compares repeated in `get_Value` and `Run`).
- None of these is a single hotspot.
- The gap looks different in real use:
  - Memos read inside an effect or memo body skip the pull bracket, because `Deferring` is true there.
  - But `fanout.effect` (one effect reading all 100 memos) measured no cheaper, ~2.6 µs, because it pays the Check walk instead.
- FSharp.Data.Adaptive's 27 µs shows that pull plus cutoff has no inherent 10x cost.

### Proposal (commit 0372637; `//FOR-REVIEW` notes left in, for you to strip)
1. **Thread guard** (Core.fs:1526): `AssertOnGraphThread` now compares `AmbientSlot.Existing` with `ownerAmbient` (one thread-static read) instead of reading the thread id, and the throw moves to a private `FailOffThread`. `Platform.isOffThread` is removed.
2. **`RunHosted`** (both overloads): restores `currentOwner` and `raisedPending` only when they changed.
3. **`Memo` and `Boundary` `MarkDirty`/`MarkCheck`**: skip `NotifyCheck` when `observers.Count = 0`.

**Tests.** The full .NET suite passes: 781/781 on both net8.0 and net10.0. I also compiled the tests with Fable and ran them under Mocha: 646 pass, 56 fail, and the failing set is identical to the baseline's (diffed), so nothing new broke.

**BenchmarkDotNet A/B** (default job, baseline and proposal alternating, 2 runs each, mean of medians):

| Benchmark | Before | After | Change |
|---|---|---|---|
| FanOutComparison.Ranvier | 2930 ns | 2674 ns | −9% |
| DiamondComparison.Ranvier | 109 ns | 93 ns | −15% |
| DiamondBenchmarks | 109 ns | 92 ns | −16% |
| Memo.Recompute | 37.8 ns | 32.0 ns | −15% |
| Chain Depth 1 / 4 / 16 / 64 | | | −18 / −6 / −8 / −4% |
| Signal.Write Observers 0 / 1 | | | −11 / −25% |
| WriteCutoff (all params) | | | −12…−35% |
| Memo cached reads | | | unchanged |

**Cost on paths that don't benefit (measured, both runs):**
- `Signal.Write(64)`: 94.6 → 99.9 ns (+5.6%)
- `WriteAndPropagate(64)`: 119 → 128 ns (+7%)
- Write/WriteAndPropagate(8): +0–3%

These walk 64 observers that are already dirty, and none of the changed branches run on that walk. I think it is a JIT layout or inlining shift in `NotifyDirty`/`MarkDirty` (inferred, not proven), about 0.1 ns per observer.

### Recommendation
Land 1 and 2: small, no semantic change, clear wins. Land 3 only if the +6% on writes to 64 already-dirty observers is acceptable; it is the most likely source of that regression. The bigger remaining lever is the ~7 ns bracket on reads from outside the graph, which needs your call (questions below).

### Questions
1. Could the bracket for a read from outside the graph (ambient graph, pull depth, settling the owed flush) be merged into `RunHosted`'s single try/finally for Dirty pure memos? That would be one enter/exit per read, at the cost of more complexity in `Run`.
2. Should the per-read thread guard stay for reads from outside the graph? The alternative is to document that `ThreadAffinity = Unchecked` removes it.
3. Could `Memo`'s `new(...) as this` constructors be restructured to drop the F# init checks? Fable limits this.

### Independent review

## Review of the fan-out cost proposal (commit 0372637)

I didn't find a correctness bug in the diff. The three changes behave as the report says, and the tests pass. The overall claims mostly hold up. Two measured claims don't: the size of the Diamond and FanOut wins, and the explanation for the +6% on `Write(64)`. One benchmark, `WriteAndPropagate`, is broken: it doesn't do what its name says.

### Confirmed
- **Line references are right.** Checked against 6869724: `AssertOnGraphThread` is at Core.fs:1526, `RunHosted` at ~1790/1813, `EnterPull`/`ExitPull` at 1624/1638 and `Memo.Pull` at 2450. The tracing hooks carry `[<Conditional("RANVIER_TRACE")>]` (Trace.fs:590 and on).
- **Change 1 (thread guard) is equivalent.** The per-thread ambient cell (`AmbientSlot.cell`) is only ever written once, when it is first created, and never reset. So "the calling thread's cell is `ownerAmbient`" means the same as "the calling thread is the owner thread".
  - If anything it is stricter than before. .NET can reuse a thread id after the thread exits, and the old id comparison would then have wrongly passed.
  - The exception type and message are unchanged, and the tests in Threading.fs that check for "owned by thread" still pass.
  - Unguarded graphs and Fable are fine: the `guarded` short-circuit still applies, and under Fable the static is shared so the check is always false. `Platform.isOffThread` has no other callers anywhere in the repo.
  - Minor: `IsOnGraphThread` (Core.fs:1427) still compares thread ids. That's inconsistent with the new guard but harmless.
- **Change 2 (`RunHosted`) is correct.** It only skips a store when the value is already equal.
  - Nit: the comment says `currentOwner` usually still holds the saved value "in a nested run". In fact it is usually null on both sides in every run, so the skip applies generally.
- **Change 3 (skip `NotifyCheck` on no observers) is correct.** `ObserverSet.NotifyCheck` on an empty set is a loop that never runs and has no side effects. The `Boundary` copy is the same.
- **Tests pass.** I re-ran on a scratch copy of 0372637: Ranvier.Tests 781/781 on net8.0 and net10.0.
  - The report missed `tests/Ranvier.CSharp.Tests`. I ran it: 44/44 pass on both.
- **Some wins reproduce.** From my BenchmarkDotNet `--short` runs, baseline and proposal alternating, 2 rounds each:
  - `Signal.Write(0)`: 6.1–7.3 → 4.1–4.5 ns, about −35%. That is bigger than the −11% reported.
  - `Signal.Write(1)`: 6.5–7.5 → 5.6–6.0 ns, about −15 to −20%.
  - `Memo.Recompute`: 36.7/41.3 → 32.3/35.1 ns, about −13%.

### Weakened or refuted
- **Diamond −15% did not reproduce.** I got 105.3/97.5 ns before and 96.0/97.5 after, so about −5% at best, and within noise.
- **FanOut −9% did not reproduce.** I got 2.80/3.21 µs before and 2.90/2.69 µs after. That's inconclusive: the headline fan-out win isn't established on this machine.
- **The `Write(64)` regression is real, but blaming change 3 is not supported.**
  - My first round agrees with the report: 90/92 → 97/98 ns.
  - I then built a third variant, the proposal minus change 3, and ran all three twice:

    | Variant | Round 1 | Round 2 |
    |---|---|---|
    | Baseline | 100.7 ns | 110.1 ns |
    | Proposal | 109.5 ns | 94.2 ns |
    | Proposal without change 3 | 93.2 ns | 93.8 ns |

  - Differences of ±10% between rounds swamp the effect.
  - Change 3's code can't run on that path anyway: every observer is already Dirty, so it returns before the edited branch.
  - Removing change 3 also moved `Write(0)` from 4.1–4.4 to 5.2–5.3 ns, and that path calls no `MarkDirty` at all. This matches the report's own guess that code layout moves things by about 1 ns.
  - Conclusion: "land 3 only if +6% is acceptable" rests on an unproven cause. It should be judged on a longer, pinned run, not refused on this evidence.
- **`WriteAndPropagate` does not propagate (bug in the existing benchmark).**
  - `Signals.fs:72-79` reads the memos back with `memo.Peek`, and `Memo.Peek = value` (Core.fs:2619 in 6869724) returns the cached value without recomputing.
  - So it is `Write` plus an array loop. The memos stay Dirty and nothing recomputes, which contradicts its own doc comment.
  - That is why `WriteAndPropagate(64)` moved in step with `Write(64)`. It is not a second, independent regression.
  - The fix is to read with `.Value` or `.TryValue`.
- **"Walk 64 observers that are already dirty; none of the changed branches run on that walk."** This is true for the walk itself. But change 1 does run once per write on that path, and it is a saving. So what the regression leaves to explain is layout, not new work, which fits the table above.

### Bugs in the proposed change
- None found.
- Cosmetic: the `//FOR-REVIEW` comments must be stripped before landing, as the report says.
- The `RunHosted` comment's "in a nested run" wording is slightly misleading (see change 2 above).

I did not re-measure the "~60% of the gap is inherent" split or the ~0.95 µs hand-written floor. They depend on the agent's own prototype, which isn't in the commit. Treat them as the agent's estimate.

My scratch copies under `/tmp/claude-0/rv-review` are removed. The worktree `/home/claude/ranvier/.claude/worktrees/wf_b9cea08f-842-2` is unchanged, still at 0372637 with a clean status.

