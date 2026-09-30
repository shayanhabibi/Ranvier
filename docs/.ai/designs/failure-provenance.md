# Failure provenance: design

*Status: proposed, not implemented. Needs maintainer review (per-failure cost, node layout).* Line references are to
`c631f23`. Closes research §4 ("make the error carry the node it came from") and §8 ("where did this error come
from").

## 1. Goal

Given a failure seen anywhere downstream, a caller can name the node whose body, comparer, flight or `Fail` produced
it. Exceptions reach callers unchanged: same instance, same type.

## 2. Current behaviour

- A failed node stores the exception in `error` and reports `Status.Error` (`Memo`: `Core.fs:2224, 2382-2384`; effect:
  `Core.fs:3029-3031`; async memo: `Core.fs:3375-3378, 3505`; boundary: `Core.fs:3950, 3955`; `AsyncSource.Fail`:
  `Core.fs:2137-2146`; disposal of a pending async value: `Core.fs:3694`; lookup cell: `Projections.fs:1522`).
- A reader of a failed node gets the same instance: `.Value` captures it into a per-node `thrown`
  `ExceptionDispatchInfo` and rethrows (`Core.fs:2598-2601`, `Platform.fs:337-361`); the reader's `| ex ->` stores it.
  `TryValue` returns `Failed error` (`Core.fs:2613`). The cutoff treats a different instance as a move
  (`Core.fs:2391-2393`).
- What callers catch today: the original exception type from `.Value`; `Reading.Failed ex`
  (`public-api-baseline.txt:211-213`); `ex` in a boundary's `recover` and in `Caught` (`Core.fs:3938-3944, 4176`);
  `Effect.Error`, `Projection.Error` (`baseline:69, 192`); `Owner.Errors` for cleanups and inbox work
  (`Core.fs:767, 1507`).
- Nothing links an exception to a node. The stack trace names the throwing lambda, not the node, and holds no user
  frame for comparer failures, faulted tasks or `ObjectDisposedException`.
- `contracts.md:134-140` says `.Value` rethrows with `raise` and loses the throw site; the code has used
  `ExceptionDispatchInfo` since `captureFailure` was added. That paragraph is stale regardless of this design.
- `NotReadyException` already carries its source node (`Types.fs:90`), and the graph tracks the raised pending read
  in `raisedPending` (`Core.fs:1108, 1876-1880`).

## 3. Proposed API

```fsharp
/// A failure and the node it originated in.
[<Sealed>]
type Failure =
    member Error: exn
    member Origin: INode

// On Memo, AsyncMemo, Boundary, Effect, Projection, AsyncSource:
member ErrorOrigin: INode          // null unless Status has Error
// On Boundary:
member CaughtFrom: INode           // null unless Caught is non-null
```

`Reading<'T>`, `recover`'s signature and every thrown type stay as they are.

## 4. How it works

Each node replaces `error: exn` and `thrown: CapturedFailure` with `failure: Failure` (null when not failed). The
record holds the exception, the origin node, and one `CapturedFailure` taken at the origin.

- **Reading a failed node** (`.Value`, `Read`) writes `graph.lastRaised <- failure` and rethrows
  `failure.Captured`. This is the only new write, and it is on the failing branch.
- **A run that catches `ex`** (the `| ex ->` arms listed in §2): if `ex` is reference-equal to
  `graph.lastRaised.Error`, adopt `graph.lastRaised`; otherwise allocate `Failure (ex, this, capture ex)`. A boundary's
  `recover` that rethrows `ex` therefore keeps the upstream origin; one that throws a new exception becomes the origin.
  A wrapping exception originates at the wrapper, with the upstream failure in `InnerException`.
- `AsyncSource.Fail`, a faulted or cancelled flight, a throwing comparer and disposal-while-pending originate at their
  node.
- `lastRaised` is not saved and restored around `RunHosted` (`Core.fs:1779-1798`), unlike `raisedPending`: the match
  is by exception instance, so a stale slot never misattributes. Limit: one slot. A body that catches a failed read
  of A, then reads failed B, then rethrows A's exception attributes it to itself.
- An async body that reads a failed source after an `await` runs outside tracking; its faulted task originates at
  the async memo.
- Every node on the path shares one record, so all rethrows carry the origin's frames plus the current reader's
  frames, where today each node captures separately.

## 5. Cost model

Measured on this VM (4 cores, .NET 10, BenchmarkDotNet 0.15.8 full job, probe benchmarks in a throwaway worktree):

| Case | Mean | Allocated |
| --- | --- | --- |
| Signal write, memo recompute succeeds, `TryValue` | 37.8 ns | 0 B |
| Same, memo body throws a fresh exception | 2,888 ns | 216 B |
| Same, one more memo reads the failing memo (short job) | 7,228 ns ± 448 | 640 B |
| `ConditionalWeakTable` `TryAdd` + `TryGetValue`, incl. a fresh exception | 407 ns | 140 B |

Object sizes today: `Memo<int>` 120 B, `AsyncMemo<int>` 160 B, `Boundary<int>` 160 B, `Effect` 96 B (measured with
`GetUninitializedObject`).

- **Non-failing write, recompute, flush.** No added instructions: `failure <- null` replaces `error <- null`, and the
  cutoff compares `failure` references as it compares `error` today.
- **Per node.** Two reference fields become one on nodes with `thrown`: `Memo<int>` expected 120 → 112 B, computed from
  its field layout (102 B payload in 104). `Effect` has no `thrown` and keeps its size. The graph gains one field.
- **Per originating failure.** One `Failure` (three references, ~40 B) and an eager `ExceptionDispatchInfo.Capture`,
  which today happens lazily on the first `.Value`. Small against the 2.9 µs throw.
- **Per propagating node.** One reference compare. No per-node capture, so the one-hop case should get cheaper.
- **To settle:** add `FailingRecompute` and `FailingRecomputeOneHop` (with a `Depth` param, as `ChainBenchmarks` has)
  to `bench/Ranvier.Benchmarks/Suspension.fs`, and a `SizeOf` probe for the node types; run before and after, with
  `Memos.fs` `Recompute` as the non-failing guard.

## 6. Fable, AOT, trimming

- `Failure` is a plain class; `CapturedFailure` is already `exn` under Fable (`Platform.fs:323-327`). Fable gets
  provenance with no stack-trace change.
- No reflection or weak tables; AOT- and trim-safe.
- A failed node's record retains its origin node until the failure clears or the reader drops it, as the stored
  exception retains its own object graph today.

## 7. Breaking-ness

Additive: `Failure`, `ErrorOrigin` on six types, `CaughtFrom`. No thrown type, `Reading` shape or delegate signature
changes. Stack trace text of rethrows changes (shorter). `contracts.md` "Finding where a failure came from" is
rewritten.

## 8. Alternatives

- **Wrap in `NodeFailedException(node, inner)`.** Breaks `with :? FormatException`, type tests in `recover`, and the
  same-instance contract (`contracts.md:94-97`). One allocation per origin. Rejected.
- **Side table exception → node** (`ConditionalWeakTable`). Zero per-node memory and correct for post-`await` reads,
  but ~0.4 µs and GC-tracked ephemerons per failure (the Gen2 column in the probe), and Fable needs a `WeakMap` binding.
- **`Reading.Failed of error * origin`.** Grows every `Reading<'T>` returned on the hot `TryValue` path by 8 bytes and
  breaks `NewFailed`. Rejected.
- **Trace only.** `Tracer.Moved` already logs the error instance for memos, async memos, boundaries and lookup cells
  (`Core.fs:2405, 3291, 4027`, `Projections.fs:1593`); a `Trace.failureOrigin` query would scan for the first `Moved`
  carrying the instance. Zero cost untraced, but effects log only status (`Core.fs:3047`), projection rows log
  `null` (`Projections.fs:436, 610`), and release builds have no log. Worth adding alongside, not instead.

## 9. Recommendation

**Do** the shared `Failure` record, with the trace query as a follow-up. Fix `contracts.md:134-140` now either way.

## 10. Questions for the maintainer

1. Origin exposed as a property on each node type (`ErrorOrigin`) rather than one `Failure.originOf node` function?
   property / function
2. Share one captured stack across the path (rethrows show origin + reader frames only)? yes / no
3. Add `Trace.failureOrigin` in the same change? yes / no


## Reviewer corrections (not yet applied)

Verdict: needs fixes

- Severity: moderate. Retention is missed. §4 introduces `graph.lastRaised`, which is written on every failing read and never cleared ('not saved and restored around RunHosted'). It pins the last read `Failure` (exception, captured stack and origin node, possibly disposed) for the graph's lifetime or until the next failing read. §6 'A failed node's record retains its origin node until the failure clears or the reader drops it' leaves this out. Clear the slot at the end of the catching run or at flush end, or state the retention.
- `Origin: INode` can be an internal node the caller cannot identify or reach: a lookup cell (Projections.fs:1516, which the note lists as an origin), a projection row memo, a FoldRow or a projection beacon. Say what `ErrorOrigin` reports for these (for example, map to the owning public Projection/Lookup), or document that the Id may name an internal node.
- §2 'effect: `Core.fs:3029-3031`' is `EffectOn` (internal type at 2953). The public `Effect` stores its failure at Core.fs:2876. Likewise §8 'effects log only status (`Core.fs:3047`)' cites EffectOn; Effect's RunEnd is at 2879.
- §8 'the same-instance contract (`contracts.md:94-97`)': lines 94-97 are the read table. The same-instance sentence is at contracts.md:99-100.
