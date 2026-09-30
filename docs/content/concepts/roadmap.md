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
- **Pending channel and boundaries.** A propagating "not ready" status, with `createSuspense`,
  `createErrorBoundary` and `createBoundary` to decide what to show in the meantime. See
  [Suspension](suspension.md).
- **Async memos.** `createAsync` with the `CancelPrevious`, `KeepLatest` and `Queue` flight policies, and the
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
  `Move` and `Replace` changes in place of `Reset`. See [C#](../guide/csharp.md).

## In progress

- **Fable target.** The engine compiles with Fable and the test suite runs against it under Node.js. It still
  needs a published package, a place in the release process, and documentation of each difference next to
  the API it affects. See [Fable (JavaScript) target](../fable/index.md).

## Under consideration

None of these is available. Each one is an open question about whether and how it fits.

- **AOT and trimming analysis in CI.** Checks that the libraries stay compatible with Native AOT and
  trimming as the C# surface grows.
- **A drop-while-running flight policy.** A policy that ignores a new run while one is in progress, as R3's
  `Drop` and CommunityToolkit's `AsyncRelayCommand` do.
- **Debounce and throttle.** As a flight policy or as a combinator.
- **Projection delta readers.** Readers that report the keys added, removed and changed since they last
  looked, then value changes, then views that apply deltas instead of re-reading their upstream keys.
- **A serialised affinity mode for Blazor Server.** A graph whose work may run on several threads, one at a
  time, queued as the renderer queues its own work.
- **C# surface cleanups.** Replacing the remaining `ValueOption` in the C# surface, such as
  `Previous<T>.Settled`, with types that read naturally in C#.
- **F# application patterns.** A bridge from an MVU model to per-selector memos, a per-field store without
  code generation, and a writable derived value seeded from upstream and editable locally.
- **Failure provenance.** Reporting which node raised the failure that a boundary or a failed memo holds.

The [Ecosystem](ecosystem.md) page lists the current gaps these items address.
