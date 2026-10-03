# Debounce and throttle design

Date: 2026-10-03
Branch: `feat/debounce-throttle`
Baseline: `506c8d29c57e29d92cdce7164d89886a6d0b5618`
Status: proposed implementation design; no feature code implemented.

This design supersedes the deferred proposal in [the earlier design](../../.ai/designs/debounce-throttle.md). Its evidence is collected in [the prior-art research](../../.ai/debounce-throttle-prior-art.md). The implementation sequence is in [the plan](../plans/2026-10-03-debounce-throttle.md).

## Purpose and boundaries

Add portable timed combinators whose output changes according to a monotonic clock. They control admission of values to downstream computations. `CancelPrevious` and `KeepLatest` remain flight policies: they govern overlapping asynchronous work after it has started. Debounce delays admission until quiet; throttle limits admission frequency. Compose admission before an async memo when the goal is fewer requests.

Capture is eager at a normal graph flush boundary. Expensive work belongs downstream of the timed node. A timed combinator does not defer arbitrary upstream evaluation, sleep the graph thread, or turn a timer into an async flight.

The first implementation includes fixed-duration debounce, leading throttle, trailing throttle, and leading-plus-trailing throttle. It excludes dynamic durations, `maxWait`, frame scheduling, a shared timing wheel, and a new flight policy. No `GraphOptions` member or existing graph hot-path field is added.

## Public interface

Add `Timed<'T>` as a read-only tracked value with `Value`, graph identity, owner lifetime, and disposal behavior consistent with existing owned sources. Keep its constructor internal; factories are the public entry points.

Add these graph-taking F# factories in `Combinators.fs`:

```fsharp
debounce delay read graph
debounceWith options delay read graph
throttle interval read graph
throttleWith options interval read graph
throttleFirst interval read graph
throttleFirstWith options interval read graph
throttleLast interval read graph
throttleLastWith options interval read graph
```

Here `delay` and `interval` are `TimeSpan`, `read` is `unit -> 'T`, and the result is `Timed<'T>`. `throttle` means leading plus trailing. `throttleFirst` means leading only; `throttleLast` means trailing only. Keep these meanings identical in C# `Reactive` factories, using `Func<T>` and the facade's existing graph argument convention. Do not introduce extension methods in this feature.

`TimedOptions<'T>` is an immutable record with `Clock: TimedClock` and `Comparer: IEqualityComparer<'T> option`. `TimedOptions.defaults<'T>` supplies the system clock and the graph's existing default equality behavior when `Comparer = None`. Default factories obtain these options only when constructing a timed node. C# factories take an optional clock and comparer rather than requiring callers to construct an F# record.

`TimedClock` is an abstract portable clock with `NowMilliseconds: float` and `CreateTimer: Action -> TimedTimer`. `TimedTimer` is an abstract disposable timer with `Arm: TimeSpan -> unit` and `Disarm: unit -> unit`. Creation is disarmed; `Arm` replaces the outstanding one-shot due time. The clock is monotonic, thread-safe where callbacks run on another thread, and has an arbitrary origin. A callback may race with disarm or disposal; the node must reject stale callbacks. Callbacks cannot run synchronously inside `Arm`. Clock adapters document callback exceptions through the existing graph dispatcher behavior.

Supply `TimedClock.system`. On .NET 8/10 also supply `TimedClock.ofTimeProvider`, conditional on the target framework and excluded from Fable. Keep the base abstraction available on netstandard2.1 without a new runtime dependency. A manual clock is a test utility in `tests/Ranvier.Tests/Support.fs`, not a new public API. Its equal-deadline ordering follows arm sequence, and backward or nested advancement is rejected.

All nonnegative finite `TimeSpan` values are accepted. Negative durations throw at construction. Zero duration captures and publishes through normal graph scheduling without a timer. Native adapter limits are handled by chunking long waits and checking the clock again; positive sub-millisecond waits round up so that the adapter does not publish early or spin.

Before writing public signatures, use `fcs_refactor_impact` on affected existing factories and `fcs_public_api` to record the baseline. Treat all new public names as explicit additions. Do not replace existing signatures or silently regenerate a compatibility baseline.

## Observable semantics

The initial ready value is published immediately for every mode. If initial capture is pending, the timed node is pending until its first ready capture, which also publishes immediately. Timing applies to later changes. Reads return the admitted state and never pull a pending candidate through the timing gate.

Compare a new ready capture to the previous ready capture before changing timing state. An equal capture does not extend debounce or open a throttle window. Compare an admitted candidate to the published value before notifying downstream. Thus a burst returning to the already-published value can complete a window without a downstream run. Both comparisons use the selected comparer, and comparer exceptions follow the failure contract below.

For debounce, every changed ready capture replaces the single candidate and moves the deadline to `now + delay`. Publish only after the latest deadline. For inputs at 0, 50, and 90 ms with a 100 ms delay, the final candidate is admitted at or after 190 ms, never at 100 ms. There is no maximum wait during a continuous burst.

For leading throttle, admit the first changed capture outside the cooldown and discard captures within it. Use only a timestamp; allocate no timer. A discarded value is not replayed merely because time passes.

For trailing throttle, the first changed capture opens a fixed window. Further captures replace the candidate without extending its deadline. Admit the latest candidate when that window ends. An idle node has no armed timer.

For leading-plus-trailing throttle, admit the first eligible changed capture immediately and open a cooldown. Changed captures during cooldown replace a single trailing candidate. At the end, admit that candidate if present; a trailing admission opens the next cooldown. With no candidate, the window ends silently. Cooldowns are anchored to actual admission time on the graph owner, so delayed dispatch cannot produce catch-up bursts.

Source pending cancels the candidate and timer, retaining the previous published state if one exists. With no published state, the node remains pending. Source failure or a comparer exception cancels pending timing and publishes failure immediately, preserving `ErrorOrigin` through `Graph.FailureOf`. Recovery is admitted under the selected timing mode; the first ready value is immediate only if no ready value has ever been published. This keeps old ready values available during ordinary pending work while making actual failures visible.

Disposal is idempotent: detach source edges, dispose the timer, release the pending candidate, and reject queued callbacks. A disposed read follows existing memo behavior for the last published state. Conditional dependencies are captured and reconciled on each source evaluation, even when publication is delayed.

## Node and scheduling architecture

Implement one specialized owned source/scheduled computation in new `Timed.fs`, compiled after `Core.fs`. Reuse `SourceList`, ownership, pure `RunHosted` capture, graph scheduling, and failure translation. Do not build each operator from an effect plus a signal: that adds another node, edge, and scheduling turn.

Maintain two independent states: captured input and published output. Propagation marks the timed node dirty/check and schedules capture. At execution, walk sources as existing computations do, evaluate only when needed, reconcile dependencies, and restore freshness. This is essential: leaving an upstream memo dirty until the timer expires loses subsequent invalidations and converts sliding debounce into a first-change timer.

Do not run user code in `MarkDirty`, `MarkCheck`, a timer callback, or under a timer-backend lock. Capture uses a pure scope, so node creation/cleanup behavior matches existing pure computations. Keep the node queued through its source-check walk to avoid duplicate queue entries, then reset scheduling state before reentrant publication can occur.

Timer callbacks only request a wake through `Graph.Post`. The wake schedules the timed node through the existing graph queue and requests a flush; it does not publish directly. Execution always processes source invalidation before considering timer admission. Consequently a source write inside a batch can extend a debounce deadline before a queued expiry publishes. Graph batching and owner-thread rules remain authoritative.

Use a cached callback and wake delegate. Bound posted wake requests with a per-node wake flag using backend-safe atomic operations on .NET and ordinary serialized state on Fable. The graph-owner wake reads current state, clock, and disposal status; a callback never carries a captured candidate. A stale or early wake either rearms for the remaining delay or exits. Handle the flag-clear/enqueue race with a test that delivers another timer callback during a wake; do not lose a needed publication or enqueue an unbounded number of wakes.

Clear or snapshot candidate/window state before notifying observers, so reentrant writes cannot be overwritten after publication. Calls to a node from arbitrary threads retain existing graph access constraints; timer adapters provide no new permission to mutate sources off-owner.

## Timer strategy and prior art

Start with one reusable timer per timed node, created lazily when a window first needs it. Debounce updates the candidate and deadline on each changed capture but does not call the backend when an already-armed deadline moves later. The callback checks the current deadline and rearms once if necessary. Trailing throttle arms once per fixed window. Leading throttle has no timer.

This follows the structure of pinned RxJS 7.8.2 debounce and Tokio's lazy deadline extension. R3's reusable timer with `Change` on each input is the alternative to measure. Current .NET timer queues already multiplex timers, so a shared wheel is not justified merely by node count. See the research note for versions and primary sources. These sources motivate candidates; they do not establish Ranvier's fastest implementation.

Fable uses `performance.now()` and one-shot `setTimeout`/`clearTimeout`; .NET uses monotonic `Stopwatch` time and a reusable `System.Threading.Timer`. The `TimeProvider` adapter supports deterministic external clocks on modern .NET. Never use wall-clock time. Do not call user code while holding adapter synchronization.

Known costs include the new node, its source list and candidate storage, a lazy timer, and existing `Graph.Post` inbox/dispatcher work on a cross-thread expiry. Do not claim that expiry is allocation-free. The changed-input path after warm-up should allocate zero additional bytes for primitive values and preallocated delegates; verify rather than assume it.

## Tracing contract

Timed nodes are visible as a distinct node kind. They do not create `FlightStart`/`FlightEnd`, and `Trace.waitingOn` retains its async-flight meaning. Add `Trace.timing graph node`, returning timed mode, captured/published status, whether a window is open, relative remaining time, and the winning capture's causal event. It returns `None` for other node kinds. Keep existing tracing record shapes and `whyNot` cases unchanged; document using `Trace.timing` alongside `whyNot` to explain a held publication.

Append event kinds for captured input, window armed/extended, suppressed admission, cancelled window, and published output without renumbering existing kinds. Normal `RunStart`/`RunEnd` describe real capture executions. Publication emits a timed publication event, not a fabricated computation run. Store an explicit reference to the winning capture's run/event and use it when constructing the notification cause. An input superseded during a window must not be reported as the cause of the final downstream run.

Capture pending/failure and published pending/failure are separate in both live tracing and event folding. Run counts count captures; moved counts count actual changed published states. Reconciliation must agree with library counters, and folded live edges must match the graph after conditional dependency changes and disposal. Equal publication and dropped throttle inputs receive distinct suppression reasons.

Use JSONL schema 2 for dumps that include timed events/metadata. Preserve schema 1 for graphs without timed nodes and accept both schemas in the parser. Fold timer times as relative offsets from the graph's first traced timed event; do not serialize OS handles or absolute monotonic origins. Manual-clock scripts with identical input and callback order must produce byte-identical dumps. Real OS dispatch order is not promised to be deterministic.

All trace state, event creation, labels, and causal bookkeeping must be absent from untraced builds. Guard new state with `RANVIER_TRACE` and follow existing conditional hook conventions. The trace gate must prove no residue and compare untraced methods with a build of the same feature with hooks removed. Existing method bodies must still match the merge-base gate; do not waive the gate to accommodate this feature. Explicitly list new public symbols in pack/API compatibility checks.

## Acceptance and performance evidence

Behavior tests use manual time and a controllable dispatcher, never sleeps. Cover direct and computed sources, unchanged check walks, batches, equal captures/admissions, pending/failure/recovery, comparer failures, conditional dependency detachment, reentrancy, disposal, stale callbacks, owner dispatch, and every mode's boundaries. Run shared semantic tests on .NET 8/10 and Fable with tracing both enabled and disabled. Include C# construction/read tests and AOT/trimming smoke coverage.

Benchmark input processing separately from wake/dispatch and construction. Compare lazy extension with per-input timer rescheduling using the same semantics, payload, dispatcher, and trace configuration. Include sparse input, dense bursts, continuous input, sustained throttle, and 1/64/4096 active timed nodes. Report allocations, timer arm calls, callbacks, posts, admissions, latency distribution, and throughput on .NET and Fable. Instrument timer operations in benchmark/test adapters, not unconditional production counters.

Existing non-timed workloads must retain their untraced IL and allocations. Investigate a reproducible retired-instruction regression around 5% or larger using the repo's counters procedure; the 5% marker is a noise-investigation threshold, not permission to add overhead. Establish the timed fast-path allocation target with primitive inputs and warm-up. Choose the timer policy from measurements, recording any runtime-specific difference rather than claiming a universal winner.

Required final gates include release builds for all library target frameworks, complete traced/untraced suites, C# tests, Fable suites, formatting, explicit additive API compatibility, the full trace verifier, counters with retired instructions, and AOT smoke. A run without hardware counters is partial evidence and must be reported that way.

## Planning baseline and prerequisites

The existing untraced Release F# suite was run during planning: 938 tests passed on net8.0 and 938 on net10.0. An explicit library `fslangmcp check` was clean. The whole-workspace semantic check reported six diagnostics in the Fable test projection; it is not evidence that the actual Fable CLI suite fails. Establish that suite's actual baseline before implementation and distinguish pre-existing diagnostics from regressions.

Before any F# changes, read `.claude/skills/sagefs/SKILL.md`. Before new XML documentation, read `.claude/skills/fsharp-xml-docs/SKILL.md` and run its audit. Set up `comment-hygiene@roboz0r` only after obtaining the repository-required user permission if it is unavailable. Do not restart or stop a user SageFs daemon. Use semantic tools for F# symbol questions and CLI builds/tests as the final gate.
