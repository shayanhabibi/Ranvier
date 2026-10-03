# Ecosystem pain points: what .NET reactivity users ask for, and where Ranvier stands

**Status.** Research survey, 2026-09-29. No code changed.

**Question.** Which issues and requests keep recurring in popular .NET and F# reactivity libraries,
and which of them bear on Ranvier's design, positioning or roadmap?

**Scope.** Three groups, surveyed from primary sources: GitHub issues, discussions and PRs sorted by
reactions and comments, official docs, READMEs, ADRs and maintainer comments.

| Group | Libraries |
| --- | --- |
| Push streams | System.Reactive (Rx.NET), R3, ReactiveUI, DynamicData, ReactiveProperty |
| F# / Fable | FSharp.Data.Adaptive (FDA), Adaptify, Fabulous, Avalonia.FuncUI, Elmish / Elmish.WPF, Fun.Blazor, Sutil, Gjallarhorn, Fable.Ripple, Clef spec |
| C# state / UI | SignalsDotnet, CommunityToolkit.Mvvm, Uno MVUX, Blazor (aspnetcore), Fluxor, WinUI / Avalonia / MAUI, dotnet/runtime, csharplang |

**Conventions.** R = total reactions and C = comments, both from the GitHub API on 2026-09-29.
⚠️ marks a claim the survey could not verify against the source. **Ranvier:** lines say whether the
theme is *addressed*, a *gap*, or an *opportunity*. A line that says *check* names a behaviour of
Ranvier the survey did not confirm in code.

**Signal strength.** Reaction counts are low across this ecosystem. Most issues sit at 0–5 reactions,
so comment volume, recurrence across libraries and who is speaking (dsyme, krauthaufen, Sergio0694,
neuecc via the R3 README) carry more weight than reactions. The highest-reaction items found:
csharplang#6420 partial properties (R261), CommunityToolkit#555 (R68), runtime#61517 weak events
(R54), runtime#18645 (R49), microsoft-ui-xaml#2795 (R36), aspnetcore#18919 (R36).

---

## §0. Executive summary

| # | Theme | Recurs in | Ranvier |
| --- | --- | --- | --- |
| 1 | Async values: loading / error state, cancellation, superseded work | every group | **Addressed. Headline differentiator.** |
| 2 | Derived-property boilerplate (`NotifyPropertyChangedFor`, `WhenAnyValue`) | C#, push | Addressed in core; **INPC adapter is the gap** |
| 3 | Incremental keyed collections and minimal list diffs | F#, push | **Gap. Top opportunity.** |
| 4 | Errors that terminate streams | push, C#, Uno | Addressed if errors are recoverable state; document it |
| 5 | Subscription / event-handler leaks, GC-dependent correctness | all | Addressed by owners; differentiator |
| 6 | Threading: UI-thread marshalling, cross-thread races, deadlocks | all | Partly addressed; **write the threading contract** |
| 7 | Glitches in diamond dependencies | push | Addressed; demonstrate it |
| 8 | Debugging: what is alive, why did it recompute, where did the error come from | all | Addressed (tracing, signal maps) |
| 9 | Blazor: re-render granularity, `StateHasChanged`, serialised affinity | C# | **Gap** (known) |
| 10 | AOT, trimming, WASM, Fable portability | all | Opportunity; design constraint now |
| 11 | Testing async state deterministically | push, C# | Partly addressed (`ManualDispatcher`); weak evidence |
| 12 | MVU interop and codegen-free per-field reactivity | F# | Opportunity |
| 13 | Writable derived values; transaction rules | F# | Opportunity |
| 14 | Commands whose `CanExecute` / busy state is derived | C# | Opportunity |
| 15 | Scheduler cost and platform package bloat | push | Keep core platform-free |
| 16 | Docs, learning curve, maintainer continuity | all | Opportunity |

**Priority read.**

1. **Lead with the pending channel.** Every group rebuilds loading and error state by hand. Nothing
   surveyed propagates Pending through derived values to a boundary: SignalsDotnet exposes
   per-computation `IsComputing`, Uno MVUX has per-feed axes tied to its own controls, and FDA leaves
   async at the `custom` level.
2. **Close the XAML gap.** The C# audience's top pain (derived notifications, UI-thread dispatch,
   leaks) is answered by Ranvier's core but invisible to XAML without an INPC adapter. SignalsDotnet
   already ships one.
3. **Incremental collections** are the most-cited performance pain in the F# UI libraries and the
   gap the ecosystem page already names.
4. **Write down contracts** the other libraries' users learned from bug reports: threading, error
   recovery, ownership of nodes created inside computations.

---

## §1. Async values: loading state, cancellation, superseded work

The most consistent unmet need across all three groups. Users wrap `Task` by hand to get
loading/error state, and pick concurrency policies ad hoc.

- Stephen Cleary, MSDN Magazine (2014), "Patterns for Asynchronous MVVM Applications: Data Binding":
  "Task<T> is not data-binding friendly … it doesn't implement INotifyPropertyChanged and its Result
  property is blocking." Introduces `NotifyTaskCompletion<T>`.
  https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/march/async-programming-patterns-for-asynchronous-mvvm-applications-data-binding
- FDA#78 "Discussion on async/cancellation and exceptions" (C10). Putting a `CancellationToken` in
  `AdaptiveToken` "causes significant overhead", so async stayed at the `custom` level.
  https://github.com/fsprojects/FSharp.Data.Adaptive/issues/78
- Fabulous discussion #928 "Is there a recommended pattern for cancellation?" (C36, most-discussed
  thread there): "there does not currently exist a definitive recommended pattern for cancellation in
  the Elmish architecture." https://github.com/fabulous-dev/Fabulous/discussions/928
- Sutil#85 "Preferred way to represent 'not yet known' value": "no way to represent 'loading'
  state". https://github.com/davedawkins/Sutil/issues/85
- Avalonia.FuncUI#342: a stale response from a previous request overwrites state. This is the
  superseded-work problem. https://github.com/fsprojects/Avalonia.FuncUI/issues/342
- SignalsDotnet#5 compares `AsyncComputed` with Angular `resource`. The maintainer describes the
  state as "Running or not running", and Value as "the last successfully computed value".
  https://github.com/fedeAlterio/SignalsDotnet/issues/5
- ReactiveUI: the `IsExecuting` + `ThrownExceptions` + `ToProperty` pattern breaks. #3261 (C12), #969
  (C17), #2299 (C19). https://github.com/reactiveui/ReactiveUI/issues/3261
- Concurrency policy vocabulary already in use:
  - R3 `AwaitOperation { Sequential, Drop, Switch, Parallel, SequentialParallel, ThrottleFirstLast }`
    (https://github.com/Cysharp/R3#readme)
  - SignalsDotnet `ConcurrentChangeStrategy { CancelCurrent, ScheduleNext }`
  - CommunityToolkit #118 (cancel commands, R8), #1125 (drop while running), #333
    (`AllowConcurrentExecutions=false` fails)
- Rx.NET#1256 "Observable.FromAsync produces unobserved task exceptions" (R16): an exception that
  lands after unsubscribe goes to `UnobservedTaskException`. https://github.com/dotnet/reactive/issues/1256

**Uno MVUX, the closest prior art.** A feed has three independent axes: Data, Error, Progress
("transient or final"). Values are `Option<T>` with Some, None ("loaded but empty") and Undefined
("no info yet"). The docs justify it by the failure modes of Tasks (manual re-fetch) and Rx (an
exception breaks the stream). Friction: uno.extensions#3141 (Retry template is "a silently dead
button"), #2597 (`Feed.Create` cannot be refreshed).
https://platform.uno/docs/articles/external/uno.extensions/doc/Reference/Reactive/concept.html
⚠️ Uno wording came through a summarising fetch; check it against the page before quoting it.

**TC39 Signals #30 "Async"** (C36) discusses propagating pending through the graph "like React
Suspense". It does not cite .NET. https://github.com/tc39/proposal-signals/issues/30

**Ranvier: addressed. Headline differentiator.** Pending propagation to boundaries plus flight
policies answers every item above. Follow-ups:

- Document flight policies in the vocabulary users already have: cancel-current / switch,
  drop / skip-while-running, queue / schedule-next.
- *Check* that the Pending model expresses Uno's two extra states: stale-while-refreshing, and loaded
  but empty.
- *Check* that a superseded or disposed flight's exception is observed, not leaked to
  `UnobservedTaskException`.
- Consider debounce and throttle as a flight policy or combinator. They are asked for in
  FuncUI#360 (C13) and Fabulous discussion #1061.
- Document the cost of cancellation, since FDA measured it as significant.
- Write a headline sample: a view model with a derived value over an async source, showing loading
  and error with no hand-written `IsBusy`.

## §2. Derived-property boilerplate

In CommunityToolkit.Mvvm, a property declares which properties depend on it. The dependencies are
neither automatic nor transitive.

- CommunityToolkit#857 "Better support for notifying cascading calculated properties" (open, R5):
  15–20 inputs feed "a dozen outputs", and "outputs often influence each other".
  https://github.com/CommunityToolkit/dotnet/issues/857
- #377 `[DependsOn]` request (closed as duplicate of #367; a user forked the toolkit to get it).
- Open PR #1195 (2026-05) "Add MVVM dependency graph notifications": transitive `[DependsOn]`, not
  merged. https://github.com/CommunityToolkit/dotnet/pull/1195
- ReactiveUI: multi-property `WhenAnyValue` needs expression wiring. #1209 is its runtime failure
  ("Unsupported expression type"). ReactiveProperty#197 asks for nested property paths (R4, its top
  issue).

**Ranvier: addressed in core, gap at the surface.** Memos track their dependencies automatically and
are height-ordered, so dependency declarations disappear. XAML users only see this through an
INPC adapter. That adapter, `INotifyPropertyChanged` for .NET, is listed as planned in
`docs/content/concepts/ecosystem.md`, and it is the main pitch to C# users. SignalsDotnet already
implements INPC.

## §3. Incremental keyed collections and list diffing

- Fabulous#258 "Fabulous with Incremental/Adaptive Views" (R14, C22, the highest-reaction F# item):
  dsyme on 10K chart points with one removed, "a significant amount of work to spot the minimal
  diff." https://github.com/fabulous-dev/Fabulous/issues/258
- Elmish.WPF#143 "Lazy subModelSeq" (C56): performance "decrease significantly". Elmish.WPF#137
  "Optimizations using IDs" (C42): quadratic `ObservableCollection.Remove`/`Move`, and WPF has no bulk
  change events (dotnet/wpf#1887). https://github.com/elmish/Elmish.WPF/issues/137
- FuncUI#120 "Improve list diffing/patching performance" (open), FuncUI#191 and Sutil#28 (items mixed
  up or reordered on removal).
- DynamicData#887 "[Feature]: R3 Support" and R3#181 ask for change sets without Rx. DynamicData#749
  (unexpected Reset on first bind, C17), #1066 (sort moves, open, C20), #787 ("Collection was
  modified").
- FDA wants richer collection operators: #46, #89, #93 (nested `amap`), #80 (`ASet.toAVal` not
  incremental, C12).

**Ranvier: gap, top opportunity.** Ranvier already has `Projection.AsObservableCollection`, and keyed
collection boxes are in progress on signal maps. Users will judge it on three things: correct
Add/Remove/Move deltas rather than Reset, changes applied on the UI thread, and a C#-friendly
surface designed up front (see §12, FDA#66/#77).

## §4. Errors that terminate the pipeline

- R3 README, first design point: "Stopping the pipeline at OnError is a mistake." R3 adds
  `OnErrorResume`, `OnCompleted(Result)` and a global unhandled-exception handler.
  https://github.com/Cysharp/R3#readme
- ReactiveUI#3261 "ObservableAsPropertyHelper stops working after command throws exception" (C12).
  ReactiveUI#2120: `UnhandledErrorException` stack traces do not show which pipeline failed.
  https://github.com/reactiveui/ReactiveUI/issues/2120
- Rx.NET ADR 0004: stack traces grow on every re-await (#2187).
  https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/adr/0004-onerror-to-throw.md
- CommunityToolkit#1205 asks for an opt-in `ExecutionFailed` event with Handled semantics (open).
  #714 asks for an analyzer against `async void` commands.

**Ranvier: addressed, provided the error state is recoverable.** Per the concepts pages, errors and
recovery travel the same path as Pending. State it in the docs as the contrast with Rx. Make the error
carry the node it came from; tracing can answer "where did this come from".

## §5. Leaks, disposal ambiguity, GC-dependent correctness

- R3 README: "Subscription leaks are a common problem in applications with long lifecycles, such as
  GUIs or games." R3 ships `ObservableTracker`.
- R3#263 and #393 (2026): `ToReadOnlyReactiveProperty` sometimes returns the source itself, so
  disposing the result disposes the source. https://github.com/Cysharp/R3/issues/393
- ReactiveUI#2935 (`WhenActivated` leak), #3091 (WPF leak, C18), #2377 (Blazor WASM leak).
  DynamicData#305, #690.
- dotnet/runtime#61517 weak event listener helper (R54, open since 2021); runtime#18645 (R49, C63);
  dotnet/maui#12039 "Memory leaks EVERYWHERE" (R33, C44).
  https://github.com/dotnet/runtime/issues/61517
- FDA's weak output edges cause silent bugs that depend on GC timing:
  - FDA#122 (2026-09): "AVal.custom silently serves a stale value when its compute function creates
    the node it reads (GC-dependent)", fixed in PR #123
  - FDA#112 (a null reference when marking from two threads)
  - FDA#32 (`WeakReference` blocks Fable)

  https://github.com/fsprojects/FSharp.Data.Adaptive/issues/122
- Fun.Blazor#35: a cval created inside an adaptive expression resets when an upstream value changes.
  The maintainer: "This is expected. It is the way of FSharp.Data.Adaptive."

**Ranvier: addressed, differentiator.** Owners give each node a deterministic lifetime, with no weak
references and no GC timing. Follow-ups:

- Document what happens to a node created inside a memo or effect body. It is owned by that run and
  disposed on the next run. This is the exact confusion in FDA#122 and Fun.Blazor#35.
- For each API that returns a node, say who owns it (the R3#393 trap).
- Tie the INPC adapter's subscriptions to an owner, so an undisposed view does not pin the graph.

## §6. Threading

- ReactiveUI#3122 "WhenAnyValue might not send latest value" (C31): a cross-thread set that races
  the subscribe is lost. https://github.com/reactiveui/ReactiveUI/issues/3122
- DynamicData#1073 (open) cross-cache deadlock: a lock held during `OnNext`. The fix, PR #1074,
  reports a "production hang where 6 threads deadlocked".
  https://github.com/reactivemarbles/DynamicData/issues/1073
- FDA#120 "AddCallback causes deadlock" under concurrent transactions (2026-09, fixed in #121).
  FDA#16 (concurrent consistency) was closed without an implementation.
- microsoft-ui-xaml#2795 implicit DispatcherQueue for x:Bind (R36, open since 2020): "it should be
  responsibility of the event *handler* to dispatch to the synchronization context it needs".
  https://github.com/microsoft/microsoft-ui-xaml/issues/2795
- CommunityToolkit#536 `NotifyCanExecuteChanged` from a background thread (R12). The workaround
  captures the `SynchronizationContext` when the handler registers. Avalonia#20729 (2026, open)
  asks for automatic UI-thread dispatch of `CanExecuteChanged`.
- Rx.NET#2062: `Task.ToObservable` continues on `TaskScheduler.Current`, not the sync context.

**Ranvier: partly addressed.** The dispatcher follows `SynchronizationContext`. Follow-ups:

- Write the threading contract users of FDA and DynamicData learned from deadlock reports:
  - which threads may write
  - where effects run
  - what happens to a cross-thread write during a flush
  - whether any user callback runs while an internal lock is held
- The INPC adapter should raise events on the context captured when each handler subscribes (#536).

## §7. Glitches in diamond dependencies

- ReactiveUI documents that multi-property `WhenAnyValue` fires once for each change: setting A then
  B first emits (new A, old B). Workaround: `DelayChangeNotifications()` or `Throttle(TimeSpan.Zero)`.
- ReactiveUI PR #2447: `CombineLatest` ran an old view model through resolution again.
  https://github.com/reactiveui/ReactiveUI/pull/2447
- Outside .NET: RxJava#6338 "combineLatest glitch problem?". Staltz: glitch-free libraries need a
  DAG and a breadth-first scheduler. https://staltz.com/rx-glitches-arent-actually-a-problem.html
- Gjallarhorn#52: subscriptions fire when a value is set to the same value. FDA#117: a `DateTime`
  cval throws from `ShallowEqualityComparer`.
- ⚠️ No dedicated glitch issue exists in dotnet/reactive. Users report it as "fires twice" or as a
  stale value.

**Ranvier: addressed.** Height ordering plus equality cutoff is the DAG answer. Show it side by side
with `CombineLatest` / `WhenAnyValue`, because users feel this pain without having a name for it.
*Check*: whether a node can take a custom comparer, and whether the default comparer works for every
type on both .NET and Fable.

## §8. Debugging and diagnosis

- R3#109 "How to debug reactive streams in R3?" The maintainer declined to add a `.Debug()`
  operator. R3's tool is `ObservableTracker`, a list of live subscriptions with stack traces; its
  Godot integration has an open bug (#334). https://github.com/Cysharp/R3/issues/109
- ReactiveUI#2120 (useless stack traces), #1209 (opaque expression error). DynamicData#147
  (SourceLink).
- Elmish#18 time-travel debugger (C27), Elmish.WPF#105 logging (C38), FDA#73 graph serialisation.
- FDA#122 went unseen because nothing traced it.
- ⚠️ No high-signal "why did this recompute?" issue was found. The demand is inferred from these
  adjacent requests.

**Ranvier: addressed, differentiator.** Tracing and signal maps go beyond anything surveyed. Aim them
at the three questions users ask: what is alive and who owns it, why did this run, where did this
error come from. `RESEARCH-graph-transparency.md` covers the mechanism.

## §9. Blazor

- The aspnetcore rendering docs (.NET 10) say ComponentBase does not know about C# events raised by a
  custom data store: the component must call `StateHasChanged`, through `InvokeAsync` when the call
  comes from outside the renderer. https://learn.microsoft.com/en-us/aspnet/core/blazor/components/rendering?view=aspnetcore-10.0
- aspnetcore#67329 "Signals (Reactivity Components) for Blazor Interactive Render Modes" (2026-06,
  Backlog, R6, C10, by Steven Giesel). It proposes `Signal<T>`/`Computed<T>` and has no team
  response. https://github.com/dotnet/aspnetcore/issues/67329
- aspnetcore#18919 "pure event handlers" to avoid automatic re-render (R36, C45). The team declined
  and pointed to `IHandleEvent`.
- aspnetcore#69323 (2026-09, open): `RendererSynchronizationContext` can stay installed on an
  unrelated pool thread, so `CheckAccess()` passes and "two threads can therefore render the same
  component tree at the same time". https://github.com/dotnet/aspnetcore/issues/69323
- SignalsDotnet ships `SignalsDotnet.Blazor` with a `<TrackedScope>` that re-renders through
  `InvokeAsync(StateHasChanged)`.
- Fluxor#344 wants memoised selectors (ngrx-style). Fluxor#203 complains about boilerplate.

**Ranvier: gap (already listed as current).** A boundary maps naturally onto a Blazor component. For
the serialised-affinity mode, #69323 shows that `SynchronizationContext.Current == captured` does not
prove affinity. Queue the work, as the renderer does, instead of running it inline when the check
passes. For positioning against Fluxor, memos are memoised selectors.

## §10. AOT, trimming, WASM and Fable portability

- ReactiveUI#4243 "[Roadmap] NativeAOT Support via Splat Cleanup & ReactiveUI Source Generation"
  (R9). A community PR was closed because annotating alone left the code "linker-aware but not truly
  linker-safe". ReactiveUI#4147: a trimmed MAUI app crashes.
- Rx.NET ADR 0005: trimmed self-contained size went from 18.3 MB to 65.7 MB with Rx. Rx.NET#1745:
  WinUI3/MAUI package size doubles. Rx.NET#1847 asks to split out platform schedulers (R17).
  https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/adr/0005-package-split.md
- FDA#96 (C10): Blazor WASM fails with "Cannot start threads on this runtime", because a static
  initializer starts a thread. FDA#104 (C13): 1.7 MB and "Is this library trimmable".
- FDA#32 lists what blocks Fable: `WeakReference`, `Monitor`, `Interlocked`, byrefs, `typeof<'T>`,
  `OptimizedClosures`. FDA#82: byref workarounds "obfuscate the code massively".
- CommunityToolkit#1139 (R15): the generator breaks default .NET 10 projects.

**Ranvier: opportunity and design constraint.** Add an AOT/trim analysis to CI before the C# surface
grows. Keep the core free of:

- weak references
- threads started from static initializers
- reflection (including `%A` / `sprintf "%A"` on library paths)
- `Monitor` / `Interlocked` / byrefs outside `#if FABLE_COMPILER` fences

Runtime dependency tracking needs no source generator, which is itself an AOT advantage.

## §11. Testing async state

- R3 swaps Rx's `TestScheduler` for `FakeTimeProvider` plus `FakeFrameProvider`.
- The ReactiveUI testing docs warn that real timers and thread hops make tests slow and flaky. The
  docs route app-builder tests through a global lock.
- ReactiveUI#3022 (C13) on timing of parallel command execution. uno.extensions PR #3192 lets
  derived feeds observe mocked inputs.
- ⚠️ Evidence is thin. No highly reacted issue asks for this.

**Ranvier: partly addressed.** `ManualDispatcher` and `Settle` on async sources already let a test
decide when each flight lands. Opportunity: a documented testing recipe, and `TimeProvider` for any
time-based policy (debounce, throttle).

## §12. C# usability of F#-first libraries

- FDA#66: C# callers write `IOpReader<IndexList<T>, IndexListDelta<T>>`; krauthaufen: "can't think of
  a way here." FDA#77 (BCL collection interfaces) was blocked by `'K * 'V` vs `KeyValuePair`.
- Elmish#240 (C30): after wrapping for C#, "the final result is totally unreadable."

**Ranvier: addressed by the C# facade.** For collections, design the C# surface up front:
`KeyValuePair`, BCL interfaces, no long generic reader types. FDA could not add these later without
breaking changes. The ecosystem page already notes `ValueOption` leaking into `Previous<T>.Settled`.

## §13. MVU interop, codegen and writable derived values

- Fabulous#258: dsyme calls adaptive views "fairly invasive". End-to-end adaptive "begins to feel
  more like mutable programming". His fork stalled because "duplication/divergence in update logic
  and codegen is too large". FuncUI#20 (open, R5): "the hard thing is building a DSL that works well
  with the MVU architecture." FuncUI settled on hooks (`IReadable`/`IWritable`, #179, C51).
- Fabulous 10.0 added non-MVU state to components (#1074 Binding in Components). Sutil#78 points
  Elmish users with slow spreadsheets to a reactive "Cells" sample.
- Adaptify (codegen from immutable models to adaptive models): #35 getting started (C11), #40
  corrupts C# projects, #30 silently requires FSharp.Core 6.
- FDA#114 (open) "A Changeable Value that can depend on other adaptive values": a Fun.Blazor team
  hand-rolled a value that keeps local edits until commit.
- FDA#79 / #108: "cannot mark object without transaction". #108's real cause was a type mismatch
  the message hid.
- FuncUI#212 (C11) hook identity by line number versus call order. FuncUI#342 and Sutil#93: cleanup
  timing.
- The Clef draft spec defines `Store<'T>` for per-field reactivity without codegen, with effect
  lifetime tied to region ("No GC or finalizer is required").
  https://clef-lang.com/spec/draft/reactive-signals/ ⚠️ Summarised, not quoted from the page.

**Ranvier: opportunity.**

- **MVU bridge:** a model mapped to per-selector memos with cutoff, for incremental adoption.
- **Per-field store without codegen:** a record of signals, in the spirit of Clef's `Store<'T>`.
- **Writable derived value:** Solid's writable memo, a form field seeded from upstream and editable
  locally.

Owners mean there are no rules of hooks. Say so.

## §14. Commands with derived `CanExecute` and busy state

- CommunityToolkit#959: update `CanExecute` automatically from the property it depends on (R3).
  CommunityToolkit#826: disable a command while any other command runs (R10); the workaround is a
  hand-maintained `IsBusy`. CommunityToolkit#1168: notification when `AsyncRelayCommand.IsRunning`
  changes.

**Ranvier: opportunity.** A command whose `CanExecute` is a memo, and an "any pending" memo over a
set of sources, covers all three. Ranvier has no command abstraction yet.

## §15. Scheduler cost and platform coupling

- R3 README: "IScheduler is the root of poor performance." R3 moves to `TimeProvider` and
  `FrameProvider`.
- Rx.NET#1847 (R17), #1628 (Unity, R15), #1745 (package bloat). R3 benchmarks subscribe/dispose
  against Rx.NET. ReactiveUI#4195 reduces allocations.

**Ranvier: keep the core platform-free.** Offer a frame or tick hook if game engines become a target.
The repo's comparison benchmarks could add R3 `ReactiveProperty` and Rx `BehaviorSubject` fan-out and
diamond cases.

## §16. Docs, learning curve and continuity

- Rx.NET#1274 "Getting Started For Dummies" (R12), #836 "Up-to-date docs" (R13, open), #1097 (R22).
  Rx.NET#395: throttle vs debounce naming (R17, C29). R3#113 asks for a docs site. ReactiveUI#687
  "Good Examples" (C70).
- FuncUI#172 "Seeking new co-maintainers" (R8, top in the repo). Fabulous discussion #1107 "State of
  Fabulous?": dsyme asks for maintainers; fabulous.dev had expired. FDA#88 asks for papers.

**Ranvier: opportunity.** Signal, memo and effect vocabulary is smaller than an operator catalogue.
Lead with worked MVVM and async examples. The existing semantics docs (memo lifetime, flight policy,
empty-stream rules) directly answer the "concepts need theory" complaint. Because adopters here are
wary of single-maintainer projects stalling, a clear roadmap page helps.

---

## Landscape changes since `concepts/ecosystem.md` was written

These are reported by the survey. Verify each before editing the ecosystem page.

- **Rx.NET 7.0.0** shipped 2026-07-17.
- **ReactiveUI 25.x** moved to its own `ReactiveUI.Primitives` engine (PR #4382, merged 2026-06-20);
  System.Reactive is now optional.
- **ReactiveProperty's README** now says: "If you're developing new application, consider using R3
  instead."
- **SignalsDotnet** (about 56 stars, active) is built on R3. It ships INPC, `SignalsDotnet.Blazor`
  (`TrackedScope`), collection signals, and alpha query/SSE packages.
  ⚠️ The survey did not check whether its updates are height-ordered or glitch-free.
- **aspnetcore#67329** is the first upstream Blazor signals proposal. It has no team response yet.
- **FDA** is active: #120–#123 (deadlock, stale-value GC bug) were all fixed in 2026-09.
- **Fable.Ripple** is beta (pushed 2026-09-28). **Oxpecker#107** "Solid 2.0?" shows F# interest in
  the model Ranvier follows.

## Not covered

- GitHub Discussions for most push-stream repos, and StackOverflow. The GitHub search rate limit cut
  both off.
- Fabulous issue search was rate-limited; only its discussions and #258 were read.
- The reactiveui.net handbook returned 404 through the fetch tool; quotes come from the
  reactiveui/website source repo.

---

## Follow-up: sceptical assessment against CommunityToolkit.Mvvm (2026-10-02)

This addendum qualifies the earlier survey's positioning claims. Evidence comes from the current
Ranvier documentation, CommunityToolkit.Mvvm 8.4.0 documentation/source, and the local C# and WPF
replay benchmark. It is not a new survey of every library above. Earlier statements that an INPC
adapter is missing are stale: Ranvier now has a C# `ReactiveObject` binding surface.

### Coordination code, rather than an absence of MVVM features

Toolkit already supplies generated observable properties, dependent-property notifications,
commands, validation, cancellation and task-completion notifications. `ObservableObject` suppresses
equal assignments; `AsyncRelayCommand` exposes `IsRunning` and `ExecutionTask`. Its completion
monitor checks that the completing task is still the current execution. Ranvier should not claim
exclusive support for equality suppression, loading state, validation or latest-task monitoring.

Ranvier's more specific distinction is a runtime dependency graph: reads establish dependencies,
derived values cache results, and pending/error state can propagate through computations to a
boundary. This can reduce explicit notification and state-coordination code as relationships become
more interconnected. A larger app with many independent fields and commands may remain concise
with Toolkit's generators. No maintenance study or growing-app code-size comparison was performed;
the boilerplate advantage is a design inference, not a measured general result.

Concurrency and lifetime claims also need boundaries. Ranvier's async policies govern publication
of returned memo results, not arbitrary side effects or property assignments inside the task.
Owners dispose owned computations and registered cleanup when their scope is disposed; arbitrary
resources still need registration, and the application must dispose its scopes. Toolkit has weak
messaging and cancellation facilities, so it is unfair to describe it as having no lifetime tools.
Ranvier's guarded graph affinity and dispatcher help enforce its own threading contract; they do
not make all application code thread-safe. Batching defers effects and is not a rollback transaction.

Sources: [Toolkit overview](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/),
[ObservableObject](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/observableobject),
[AsyncRelayCommand](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/asyncrelaycommand),
[8.4.0 command source](https://github.com/CommunityToolkit/dotnet/blob/v8.4.0/src/CommunityToolkit.Mvvm/Input/AsyncRelayCommand.cs).

### Tracing: a useful distinction with a narrower scope

MVVM is a pattern, not a diagnostics product. The reviewed Toolkit surface has no built-in
equivalent to Ranvier's causal dependency-graph tracing. However, applications using Toolkit are
not without diagnostics: WPF has `PresentationTraceSources`, and Visual Studio's XAML Binding
Failures window identifies failed bindings and their relevant context. These tools diagnose the
binding layer; they do not automatically reconstruct a chain of application-level calculated
properties or explain an equality cutoff in a reactive computation graph. Custom logging can add
that information, at the cost of explicit instrumentation.

Ranvier documents queries for why a node ran or did not run, its creation site and owner chain,
graph snapshots, run history, and pending async memo flights including superseded outcomes. These
are a plausible advantage when debugging propagation inside a Ranvier graph. They do not replace
XAML binding diagnostics, application logging, or a performance profiler. A WPF app using Ranvier
can use both sets of tools. No comparative debugging/usability study was conducted, so "better
diagnostics overall" is not established.

The feature is explicitly preview/research. Traced builds allocate per event, run more slowly,
and retain an unbounded event log for the graph's lifetime. Untraced builds compile instrumentation
out. There is also a documentation inconsistency: examples show captured values, while the limits
section says value capture is planned and history contains no values. Do not advertise full value
history or state replay without resolving that contradiction and verifying the implementation.
Event/graph replay alone does not establish application time-travel debugging.

Sources: [Ranvier tracing](../content/guide/tracing.md),
[WPF trace sources](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.presentationtracesources?view=windowsdesktop-10.0),
[Visual Studio binding diagnostics](https://learn.microsoft.com/en-us/visualstudio/xaml-tools/xaml-data-binding-diagnostics).

### Performance: the small example favours Toolkit

The local benchmark compared Toolkit 8.4.0 with Ranvier revision
`ac96be31ee37d6e3e27a8fda366ad113bb2d9e94`, using Release builds with tracing and tiered compilation
off. It replayed loading, edits, refresh, failure/retry, independent rows and screen lifetime.
The basic runner passed 36 correctness checks; the real WPF binding runner passed 20.

Toolkit had lower median CPU time and UI-thread allocation in every CPU-focused flow tested.
With actual WPF controls and bindings, Ranvier's steady-state medians were approximately 6–15%
slower, and screen creation/disposal approximately 59% slower. Several steady-state trial ranges
overlapped. Ranvier emitted fewer notifications for refresh and error/retry, but that did not
translate into lower measured time or allocation. A changed row already targets one row in the
Toolkit baseline; comparing against a whole-screen refresh would misrepresent Toolkit.

These are synthetic replays of realistic flows on one machine, with controlled services and
headless WPF controls. They exclude layout, paint/GPU work and background-thread allocation.
The delayed-service flow was dominated by timers/scheduling and supports no speed claim. The
benchmark does not determine performance for expensive, shared derived computations or larger
dependency graphs. It supports a coordination/diagnostics pitch for this example, not a speed pitch.

Evidence: [benchmark report](../../experiments/csharp-viewmodel-bench/report.md),
[harness and commands](../../experiments/csharp-viewmodel-bench/README.md).

### Positioning that the evidence supports

Describe Ranvier as reducing explicit coordination for interconnected derived and async state,
with causal tracing of the graph it manages. Avoid claiming that Toolkit cannot build the same
correct application, that every growing app has less code with Ranvier, or that Ranvier is faster.
The original §8 statement "go beyond anything surveyed" is too broad: the survey itself found
subscription trackers and time-travel requests, and did not compare diagnostics comprehensively.
The strongest verified comparison here is the narrower absence of automatic causal computation
tracing in Toolkit's reviewed surface, alongside existing WPF binding diagnostics.
