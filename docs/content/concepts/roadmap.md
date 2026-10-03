---
title: Roadmap
order: 6
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

This page lists what Ranvier provides today, what is being finished, and what is being considered. The last
list is a set of candidates, not a schedule. An item may change shape, move, or be dropped. The page carries
no dates.

## Shipped

These are implemented and covered by tests.

- **Reactive core.** Signals, memos, effects and owners, with automatic dependency tracking, height-ordered
  updates and an equality cutoff. See [Getting started](../guide/getting-started.md).
- **Per-node equality.** Typed comparers for signals, memos, split effects and boundaries, overriding
  the graph's default value cutoff. See [Equality](../guide/equality.md#per-node-comparers).
- **Pending channel and boundaries.** A propagating "not ready" status, with `createSuspense`,
  `createErrorBoundary` and `createBoundary` to decide what to show in the meantime. See
  [Suspension](suspension.md).
- **Async memos.** `createAsync` with the `CancelPrevious`, `KeepLatest`, `Queue` and `FinishCurrent` flight policies, and the
  previous value passed to each flight. See [Async and pending](../guide/async-and-pending.md).
- **Threading, failure and ownership contracts.** Thread affinity checks, dispatch from other threads, and
  `ManualDispatcher` for deterministic tests. See [Contracts](contracts.md).
- **Keyed collections.** Projections, index projections, lookups and selectors, with `filter`, `map`,
  `sortBy`, `groupBy`, slicing and fold views. See [Collections](../guide/collections.fsx) and
  [Aggregates](../guide/aggregates.fsx).
- **Trace log.** A traced build that records why each node ran. See [Tracing](../guide/tracing.md).
- **C# package.** `Ranvier.CSharp` with delegate-based factories, `Tracing`, `ReactiveBindings` for
  `INotifyPropertyChanged` and `INotifyDataErrorInfo`, and `ReactiveCommand`, an `ICommand` whose `CanExecute`
  and busy state come from graph nodes. `AsObservableCollection` raises `Add`, `Remove`,
  `Move` and `Replace` changes after population; initial population and reset recovery use `Reset`.
  Value-only updates consume settled-value deltas. Previous values reach C# through a seed or
  `SettledOr`/`TrySettled`, without `ValueOption`. See [C#](../guide/csharp.md).
- **Native AOT and trimming.** CI publishes untraced package smoke applications under .NET 10 for
  `linux-x64` with no trim or AOT warnings. This covers that configuration, not every traced build or
  deployment target. See [Installation](../guide/installation.md#native-aot-and-trimming).
- **F# application patterns.** `createEditable` and `createDraft` for values seeded from upstream and edited
  locally, and forms as records of signals. See [Editable values and forms](../guide/forms.md).
- **MVU bridge.** The `Ranvier.Elmish` package: `Mvu`, which reads an Elmish-style model through selector memos. See
  [Migrating from Elmish](../guide/elmish.md).
- **Editable keyed collections.** `createKeyedCollection` provides direct row edits in insertion order,
  with batched `Edit` callbacks and the existing projection operators. See
  [Direct edits](../guide/collection-updates.fsx#direct-edits).
- **Projection change readers.** `NewKeyReader` reports membership and order; `NewValueReader` also
  reports unequal settled row values. Readers have independent bounded cursors and return a reset
  on their first read or after overflow. See [Reading changes](../guide/projections.fsx#reading-changes).
- **Serialised thread affinity.** `ThreadAffinity.Serialised` admits one thread at a time on the construction
  context, for hosts such as Blazor Server. See [Serialised hosts](contracts.md#serialised-hosts) and
  [Blazor Server](../guide/blazor-server.md).
- **Failure provenance.** `ErrorOrigin` on a failed node and `CaughtFrom` on an error boundary report the node a
  failure originated in. See [Finding where a failure came from](contracts.md#finding-where-a-failure-came-from).

## In progress

- **Fable target.** The engine compiles with Fable and the test suite runs against it under Node.js. It still
  needs a published package, a place in the release process, and documentation of each difference next to
  the API it affects. See [Fable (JavaScript) target](../fable/index.md).

## Under consideration

None of these is available. Each one is an open question about whether and how it fits.

- **A drop-while-running flight policy.** A policy that ignores a new run while one is in progress,
  analogous to [R3's `AwaitOperation.Drop`](https://github.com/Cysharp/R3#asyncawait-support).
  Command gating through `CanExecute` is a separate feature already available in `ReactiveCommand`.
- **Debounce and throttle.** As a flight policy or as a combinator.
- **More incremental collection views.** Map membership consumes upstream key deltas. Filter, sort,
  grouping and some aggregate membership paths still scan keys; further delta processing remains
  under consideration.

The [Ecosystem](ecosystem.md) page lists the current gaps these items address.
