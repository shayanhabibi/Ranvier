# Ecosystem capability audit

Checked 2026-10-03. Scope: external claims in `content/concepts/ecosystem.md`, `roadmap.md` and the policy comparison in `guide/async-memos.md`. Primary sources below; no benchmark files changed. Proposed wording is deliberately narrower than a performance or feature-equivalence claim.

## Ranvier implementation checks and applied corrections

The public documentation also updates the home page, collection guides, contracts, suspension, async
graph, tracing, Fable status and installation. No engine behavior changes or comparative benchmarks are
part of this audit.

- `src/Ranvier/Collections.fs`: existing-key edits write a row signal without a collection reconciliation;
  source membership passes copy the ordered key array. Direct edits do not make removal or insertion-order
  maintenance constant-cost.
- `src/Ranvier/Combinators.fs` and `Projections.fs`: map membership visits upstream deltas, retaining rows
  and reusing upstream key arrays; reset/error recovery falls back to enumeration. Filter/sort/grouping
  membership and some fold membership paths still scan; changed sort ranks use a full sort.
- `src/Ranvier/ValueReaders.fs`: shared observation, accepted settled values, independent bounded reader
  cursors and reset recovery. Coalescing is a state-change contract, not an intermediate-write history.
- `AsObservableCollection`: value-only changes use accepted-value deltas; membership/order changes use
  positional reconciliation. Initialization/reset/notification-failure recovery can emit Reset. Never-settled
  rows are omitted; pending/failed rows retain accepted values.
- Trace implementation: payloads can retain values/exceptions by reference; no immutable historical
  snapshots or eviction. Fable has `dumpText`, but no file-writing `dump`/`parseDump`. Event-log locking
  does not make unchecked graph access thread-safe. The verification script exists; do not claim CI runs
  it for every change.
- `.github/workflows/ci.yml` and `tests/Ranvier.AotSmoke/Ranvier.AotSmoke.fsproj`: Native AOT smoke coverage
  roots the three untraced packages on .NET 10, linux-x64; this is not proof for every target or traced build.
- NuGet's primary flat-container indexes for
  [Ranvier](https://api.nuget.org/v3-flatcontainer/ranvier/index.json),
  [Ranvier.CSharp](https://api.nuget.org/v3-flatcontainer/ranvier.csharp/index.json) and
  [Ranvier.Elmish](https://api.nuget.org/v3-flatcontainer/ranvier.elmish/index.json) all include
  `0.1.0-preview.4` on the audit date. Corrected the Fable page's claim that neither target was published
  and updated package-reference examples. Those examples are ordinary PackageReference, not CPM.
- Cancellation requests are cooperative; ownership covers owned nodes and registered cleanup, not
  automatic cleanup of every external subscription. Platform-specific implementations and timing/equality
  differences remain despite sharing the F# graph model.

Core-project FsLangMCP check was clean. The whole-workspace check reported six Fable-test compile errors
in unrelated linked test helpers; therefore semantic lookups were scoped to the clean core project.

## FSharp.Data.Adaptive

- **Correct the tracking classification.** Public combinators and the `adaptive` CE explicitly select adaptive inputs, but the engine records dependencies during token-bearing evaluation. `EvaluateAlways` passes `token.WithCaller x`, adds the caller to `Outputs`, and raises its level. Calling `AVal.force` inside an adaptive computation does not track dependencies. Suggested table cell: **“Dynamic dependencies through adaptive combinators / CE and token-bearing reads.”** Suggested prose: **“Adaptive exposes dependencies through adaptive values, combinators and `let!`; the engine registers and maintains the dependency graph during evaluation.”** Sources: [AdaptiveObject implementation](https://raw.githubusercontent.com/fsprojects/FSharp.Data.Adaptive/master/src/FSharp.Data.Adaptive/Core/AdaptiveObject.fs), [AVal API](https://fsprojects.github.io/FSharp.Data.Adaptive/reference/fsharp-data-adaptive-avalmodule.html).
- **Retain collection strength without treating it as exclusive.** Adaptive has adaptive sets, indexed lists and maps with incremental combinators; dynamic `bind` selects dependencies at runtime. Its longstanding Aardvark history is supported. “If you need delta-based incremental collections, use Adaptive” undersells Ranvier's implemented delta paths. Prefer **“Adaptive provides a broader, established family of incremental collection operators; compare the operators and update patterns your workload needs.”** Avoid extrapolating the README's constant-cost append/map/fold example to all operations. Source: [project README](https://github.com/fsprojects/FSharp.Data.Adaptive).
- “Outside the adaptive model” is too categorical for async: an adaptive value can hold an application-defined async-state union. Prefer **“No built-in propagating pending channel in the documented adaptive-value model.”** This is an inference from the documented model, not an exhaustive absence proof. [Core model and push-pull evaluation](https://fsprojects.github.io/FSharp.Data.Adaptive/).

## R3 and System.Reactive

- **Split their error semantics.** System.Reactive's `OnError` terminates a subscription. R3's `OnErrorResume` is nonterminal; R3 ends via `OnCompleted(Result)`, which can carry success or failure. Suggested cell: **“Stream events; Rx OnError is terminal, R3 OnErrorResume is recoverable.”**
- **Policy parallels concern scheduling only.** R3 `Switch` cancels the prior async operation; `SequentialParallel` launches work concurrently and delivers results in input order; `Drop` ignores incoming values while work runs. These resemble Ranvier flight scheduling but are stream operators, with no Ranvier `Previous.Settled`, pending propagation or demand-driven launch contract. Replace “same policies ... matches exactly” with **“These are scheduling parallels, not interchangeable APIs or identical state semantics.”** `FinishCurrent` remains an approximate comparison with `ThrottleFirstLast`, not an equivalence. Source for these claims: [R3 README, core interface and async/await interoperability](https://github.com/Cysharp/R3#core-interface).
- Rx.NET 7.0.0 release and July 2026 date are supported, but release trivia is unnecessary unless useful to a reader's choice. [Official releases](https://github.com/dotnet/reactive/releases/tag/rxnet-v7.0.0).
- Do not suggest streams cannot represent state: R3 includes `BehaviorSubject`, `ReactiveProperty`, binding/validation integrations and async operators. A better distinction is **graph-wide settling of derived values versus independent stream emissions**. The shown synchronous System.Reactive diamond illustrates that particular subscription topology; it is not a proof that every Rx architecture glitches. [R3 reactive-property and XAML sections](https://github.com/Cysharp/R3#subjectsreactiveproperty).

## SignalsDotnet

The README supports dynamic automatic tracking, `INotifyPropertyChanged`, collection replacement/content tracking, Blazor `TrackedScope`, and alpha query/server integration. It also describes linked signals, per-key reactive dictionaries, source generators, atomic effect batching and error-handler configuration, so “differ mainly in async state” is too narrow. Suggested comparison: **“A key distinction is async-state propagation: SignalsDotnet exposes IsComputing and configurable exception handling; Ranvier carries pending and failed states through dependent nodes to boundaries.”**

The documented concurrent strategies are `CancelCurrent` (cancel/restart) and `ScheduleNext` (finish, then at most one trailing run). Compare those to Ranvier's scheduling choices without claiming exact memo-state equivalence. The README both calls signals observables in its introduction and describes `Values`/`FutureValues` streams in its detailed API; avoid adjudicating that inconsistency without type inspection. Safe wording: **“Built on R3, with Values and FutureValues observable streams.”** Its published model says unobserved computed signals are ref-counted and do no work, so an unqualified “push” label loses that qualification. Source: [maintainer README](https://github.com/fedeAlterio/SignalsDotnet).

## CommunityToolkit command comparison

Roadmap's **“drop ... as ... AsyncRelayCommand do”** conflates an async-memo policy with a command's enabled state. `AllowConcurrentExecutions=false` makes `CanExecute` false while executing; it is configurable. Token-accepting commands also cancel a prior token when concurrent execution is requested. Prefer citing only **R3 AwaitOperation.Drop** for a drop-while-running flight-policy analogue. The existing async-memos explanation of CanExecute is substantially better, but “matches” should remain a command-gating comparison. Source: [official concurrent-execution documentation](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand#handling-concurrent-executions).

## Solid and Partas.Solid

- **Identify Solid 2 as prerelease.** Official releases list `2.0.0-rc.13` packages as prerelease. Link the 2.0 documentation or announcement for 2.0-specific async claims, not the general stable documentation homepage. Suggested label: **“Solid 2.0 RC reactive core.”** [Releases](https://github.com/solidjs/solid/releases), [maintainer RC announcement](https://github.com/solidjs/solid/discussions/2995).
- Solid 2's documented async computations propagate a not-ready path to loading boundaries and provide `isPending`/`latest`; the current design also keeps previous content during revalidation. Say Ranvier is **“inspired by Solid 2's pending propagation”**, not “matches Solid 2.0” generally. Scheduling, transitions, rendering and API details differ, and the design is still changing. [Official 2.0 async design](https://github.com/solidjs/solid/blob/next/documentation/solid-2.0/05-async-data.md).
- The reactive core batches writes and exposes `flush`; do not imply Ranvier's immediate setter contract matches it. [Signals core README](https://github.com/solidjs/solid/blob/next/packages/signals/README.md).
- Partas.Solid clearly wraps Solid and transforms F# into JSX. The inspected README does not establish blanket **“no F# runtime”** for arbitrary applications, or prove that the published package exposes every Solid 2 API. Prefer **“Partas.Solid binds Solid's browser APIs; use the bindings supported by your installed version.”** [Partas.Solid repository](https://github.com/shayanhabibi/Partas.Solid).

## Fable.Ripple

The package family does supply DOM rendering, forms, URL parsing and browser component tests, but is itself beta. “Complete stack” can imply broader production completeness; enumerate its supplied packages instead. [Official README](https://github.com/fable-hub/Fable.Ripple).

Synchronous flushing is supported: source writes flush immediately and batches flush on exit. [Core introduction](https://github.com/fable-hub/Fable.Ripple/blob/main/docs/content/ripple/introduction.md), [batching documentation](https://github.com/fable-hub/Fable.Ripple/blob/main/docs/content/ripple/batching.md). Hash and path routers are documented. [Routing](https://github.com/fable-hub/Fable.Ripple/blob/main/docs/content/ripple-dom/routing.md).

“Not part of the node model” for async is reasonable if made precise: **“Application-managed async-state values; no built-in pending propagation.”** The official async page explicitly describes a manually assembled state union and generation counter rather than a library API. [Async data pattern](https://github.com/fable-hub/Fable.Ripple/blob/main/docs/content/ripple-dom/async-data.md).

## React and FuncUI

React's hooks require stable call ordering and prohibit calling ordinary hooks in conditions/loops; React's `use` is an explicit exception. Do not state “all hooks” without that qualification. [React rules](https://react.dev/reference/rules/rules-of-hooks).

FuncUI #212 is a historical discussion of caller-line identities, conditional hooks and a proposed move to call-order identity with explicit identity as an escape hatch. It does not justify a timeless assertion that FuncUI identity is line-number based. Suggested text: **“Unlike render-time hook state, Ranvier node identity belongs to the node object; its lifetime follows its owner.”** Drop the FuncUI detail unless verified against the current implementation. [Historical PR](https://github.com/fsprojects/Avalonia.FuncUI/pull/212).

## ReactiveUI

The merged migration PR supports the default Primitives engine and separate `.Reactive` distribution for System.Reactive public interop. Keep this if retained. [Merged migration](https://github.com/reactiveui/ReactiveUI/pull/4382).

The ecosystem page's assertion that multi-property WhenAnyValue behaves identically across versions, and that DelayChangeNotifications or zero-time Throttle are “usual workarounds,” was not established by this source. Delay/coalescing is not automatically a graph-consistency guarantee. Prefer limiting the demonstrated glitch claim to the explicit System.Reactive example and saying applications can compose state as a single upstream value or batch coordinated state changes.

## Clef

The linked specification defines signals/memos/effects/batching as a surface over incremental and observable intrinsics, with compiler-analyzed read dependencies and scoped cleanup. This is a language specification, not evidence for a shipped reactive runtime. “Solid-style” is an analogy rather than its stated design attribution. No async suspension section was found in the reactive-signals chapter; that supports **“this chapter does not specify a pending channel”**, not “the language leaves asynchronous suspension unspecified.” Consider omitting the comparison if it adds little. [Reactive signals specification](https://clef-lang.com/spec/draft/reactive-signals/), [incremental computation specification](https://clef-lang.com/spec/draft/incremental-computation/).
