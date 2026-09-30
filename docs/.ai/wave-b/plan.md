# Ecosystem pain points: plan

Source: `docs/.ai/RESEARCH-ecosystem-pain-points.md` at master `c631f23`.

## What master already covers

The research doc says "no code changed", but master already ships much of what it asks for:

| Doc item | Where it already lives |
| --- | --- |
| §2 INPC adapter | `ReactiveBindings` / `ReactiveObject` in `src/Ranvier.CSharp/Bindings.fs` |
| §6 threading contract | `docs/content/concepts/contracts.md` → Threading |
| §4 error recovery, where exceptions land | `contracts.md` → Error recovery |
| §5 ownership, nodes created inside a body | `contracts.md` → Ownership |
| §1 stale-while-refreshing, empty vs unknown | `guide/async-and-pending.md` |
| §3 stage 1 (Add/Remove/Move instead of Reset) | `Positional.diff` + `AsObservableCollection` |
| §15 R3 / Rx comparison bench | `bench/Ranvier.Benchmarks/Comparisons.fs` (single chain only) |

## Workflow shape

One Workflow per wave, under 10 agents each. Every agent works in its own git worktree; a final agent per wave
runs the full .NET suite (net10.0 + net8.0) and the Fable suite (traced + untraced) before anything is proposed
for a push.

## Wave A: no runtime cost (proposed to run straight away)

1. **Checks** the doc leaves open, each answered by a test, with a fix only if the test fails:
   - a superseded or disposed flight's exception never reaches `TaskScheduler.UnobservedTaskException` (§1)
   - a node can take a custom comparer, and the default comparer works for `DateTime`, records, NaN etc. on .NET and Fable (§7)
2. **Docs**
   - flight policies in the vocabulary users already know: `CancelPrevious` = switch / cancel-current,
     `KeepLatest`, `Queue` = schedule-next; say plainly that drop-while-running is not offered (§1)
   - cost of cancellation (§1), measured with the existing benchmark harness rather than asserted
   - glitch-free diamonds side by side with `CombineLatest` / `WhenAnyValue` (§7)
   - testing recipe with `ManualDispatcher` and `Settle` (§11)
   - "owners mean no rules of hooks" (§13), memos as memoised selectors vs Fluxor (§9)
   - roadmap page (§16)
3. **Samples**: headline C# view model with a derived value over an async source, loading and error with no
   hand-written `IsBusy` (§1, §2)
4. **Benchmarks**: add fan-out and diamond cases to `Comparisons.fs` for R3 `ReactiveProperty` and Rx
   `BehaviorSubject` (§15). Bench-only, not in the library.
5. **Ecosystem page**: fold in the landscape changes (Rx.NET 7, ReactiveUI 25, ReactiveProperty → R3,
   SignalsDotnet, aspnetcore#67329), each re-verified against its source first.

## Wave B: needs review with you (CPU, API or design tradeoffs)

| # | Item | Why it needs review |
| --- | --- | --- |
| B1 | §10 AOT/trim analysis: `IsTrimmable`/`IsAotCompatible` + a publish-AOT smoke project in CI | Adds CI time. Will surface `%A` in exception messages (`Projections.fs`, `Combinators.fs`) and `WeakReference` in `Trace.fs`. Replacing `%A` changes message text for records/tuples. |
| B2 | §1 new flight policy `DropWhileRunning` (skip new runs while one is in flight) | New public DU case; one more branch on the async-memo run path. |
| B3 | §1 debounce / throttle as a combinator over `TimeProvider` | New API; `TimeProvider` has no Fable equivalent, so needs a portable clock abstraction. Timers per node. |
| B4 | §3 stages 2–4 of `projection-delta-reader.md` (op-log, key/value readers, `ApplyDelta`) | Core change to the projection pass: per-projection log memory and a hook per retire/add/publish. |
| B5 | §14 command with memo-derived `CanExecute` + "any pending" memo (Ranvier.CSharp) | New public C# type; no core cost, but it is API design. |
| B6 | §9 serialised-affinity mode for Blazor Server | Changes the threading core; queue-not-inline per aspnetcore#69323. |
| B7 | §12 replace `ValueOption` in `Previous<T>.Settled` for C# | Breaking change to the C# surface. |
| B8 | §13 MVU bridge, per-field store, writable derived value | Three new features; design docs first, no code. |
| B9 | §4 make a failure carry the node it came from | Either wraps exceptions (changes what callers catch) or adds a side table (memory per failure). |
| B10 | C# tracing (named gap on the ecosystem page) | Surface design only. |

For Wave B the workflow would produce a short design note per item (cost measured where it matters), not code,
unless you pick items to implement.
