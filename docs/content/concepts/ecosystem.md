---
title: Ecosystem
order: 4
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

This page places Ranvier among related libraries in .NET and Fable. Where another library is the better fit,
the page says so. Statements about other projects describe their design, not their quality or their user
base. See each project's own documentation for current details.

## What Ranvier is

Ranvier is a fine-grained reactive core for F# on .NET. Its distinguishing feature is the
[pending channel](suspension.md): a propagating, recoverable "not ready" status. Straight-line code needs no
knowledge of it, and a boundary decides what to show in the meantime. The rest of the model (signals, memos,
effects, owners, height-ordered updates and equality cutoff) is well established, and other libraries
provide it too.

It is early. The project is pre-release and has one maintainer. It targets `net10.0`, `net8.0` and `netstandard2.1`, and its public
API includes an F# core and a delegate-based C# facade. Evaluate it with that in mind.

## Related libraries

| Library | Tracking | Update model | Async state |
| --- | --- | --- | --- |
| [FSharp.Data.Adaptive](https://github.com/fsprojects/FSharp.Data.Adaptive) | Dynamic, through adaptive APIs and token-bearing reads | Level-ordered, push invalidation / pull evaluation | Application-defined state; no documented built-in propagating pending channel |
| [SignalsDotnet](https://github.com/fedeAlterio/SignalsDotnet) | Automatic, on read | R3-based; unobserved computed signals do no work | `IsComputing`, exception handling, cancellation strategies |
| [R3](https://github.com/Cysharp/R3) / [System.Reactive](https://github.com/dotnet/reactive) | Explicit stream composition | Push streams | Rx `OnError` is terminal; R3 `OnErrorResume` is recoverable |
| [Fable.Ripple](https://github.com/fable-hub/Fable.Ripple) | Automatic, dynamic | Pull, synchronous settle | Application-managed async-state values |
| [Solid 2.0 RC](https://github.com/solidjs/solid/discussions/2995) | Automatic, dynamic | Batched reactive graph | Pending propagation and loading boundaries |
| Ranvier | Automatic, dynamic | Height-ordered, pull | Pending channel and boundaries |

### FSharp.Data.Adaptive

For a desktop-first walkthrough with code comparisons, read
[Ranvier or Adaptive for a desktop app?](../blog/desktop-adaptive.md).

A mature F# incremental computation library, used by the Aardvark platform. Its adaptive collections
(`aset`, `alist`, `amap`) pass deltas from one stage to the next and provide an established family of
incremental operators. Its adaptive APIs and `let!` expose the inputs in code; the engine registers and
maintains dynamic dependencies during token-bearing evaluation. See the
[Adaptive model](https://fsprojects.github.io/FSharp.Data.Adaptive/) and
[AVal API](https://fsprojects.github.io/FSharp.Data.Adaptive/reference/fsharp-data-adaptive-avalmodule.html).

Ranvier also implements delta-based incremental collection paths. Its
[editable keyed sources](../guide/collection-updates.fsx#direct-edits) update an existing row without
rescanning a source list or replacing the key array. Map views consume upstream membership deltas and
retain unchanged rows. Key readers report membership and order; value readers observe changed rows and
report unequal settled values. The C# `AsObservableCollection` adapter uses those value deltas for
value-only replacements rather than polling every row.

That closes the gap for these paths, not for every collection operator. Ranvier's filter, sort and grouping
membership paths still scan keys; sorting changed ranks uses a full sort. Some aggregate membership paths
also scan keys, and source membership changes copy the ordered key array. Map views reuse the upstream
array. Whole-list projection sources still reconcile their input when the list changes. See
[view costs](../guide/collection-views.fsx) and
[reader and adapter costs](../guide/projections.fsx#reading-changes).

Adaptive offers sets, maps and indexed lists with a broader established incremental API; Ranvier combines
keyed rows with ownership and propagating pending/failed states. Compare the operators, reset paths and
update patterns your workload needs. Neither library's use of deltas proves every operation costs only
the number of changed rows, and this page makes no comparative performance claim.

### SignalsDotnet

A published signals library for .NET MVVM, with integrations listed for WPF, Avalonia, MAUI, Uno, Blazor,
Unity and Godot. It tracks automatically and exposes async computations through an `IsComputing` flag.
It is built on R3 and exposes `Values` and `FutureValues` observable streams. Signals implement
`INotifyPropertyChanged`. `CollectionSignal` wraps an `ObservableCollection` and reacts both to the
collection being replaced and to its contents changing. The `SignalsDotnet.Blazor` package provides a
`TrackedScope` component that re-renders only the region of markup whose signals changed.
`SignalsDotnet.Query` and `SignalsDotnet.AspNetCore`, which streams projections as server-sent events, are
in alpha. Its documented `CancelCurrent` and `ScheduleNext` strategies offer cancellation/restart and
one trailing run. These are scheduling parallels to some Ranvier
[flight policies](async-graph.md#superseded-flights), not identical async-state contracts.

One distinction is how async state travels: SignalsDotnet exposes `IsComputing` and configurable exception
handling; Ranvier propagates pending and failed states through dependent reads to a boundary. SignalsDotnet
also documents linked signals, reactive dictionaries and source generators. In SignalsDotnet, writes wrapped in an atomic operation run each effect
once, at the end. Its documentation does not say whether derived values update in height order.

### R3 and System.Reactive

Push-based streams, familiar to most .NET developers, with a large operator vocabulary. They model events
over time. A derived value that must stay consistent across several inputs is a different shape of
problem, and Ranvier addresses that shape. For event pipelines, throttling, windowing and time-based
composition, a stream library provides operators Ranvier does not implement. Streams can also represent
state; the distinction here is graph-wide settling versus independent emissions. R3 supports recoverable
`OnErrorResume` notifications and ends via `OnCompleted(Result)`; System.Reactive's `OnError` terminates
the subscription, with recovery available through operators such as `Catch` and `Retry`.

Both are active. System.Reactive 7.0.0 was released in July 2026. R3 is a redesign by the author of
ReactiveProperty, whose README now says "If you're developing a new application, consider using R3 instead
of ReactiveProperty." ReactiveUI 25 runs on its own `ReactiveUI.Primitives` package
([reactiveui/ReactiveUI#4382](https://github.com/reactiveui/ReactiveUI/pull/4382)) and no longer depends on
System.Reactive by default. The `ReactiveUI.Reactive` package keeps System.Reactive interop.

### Fable.Ripple

A fine-grained reactive library written from scratch in F# for Fable. It ships a DOM layer, forms, a router
and component tests, currently as beta packages. Its core is written with careful attention to the code Fable generates. It promises
that a write settles synchronously, with no scheduler tick. For browser UI in F# without Solid, Ripple
provides those UI packages, while Ranvier provides a core. Ripple documents
[application-managed async state](https://github.com/fable-hub/Fable.Ripple/blob/main/docs/content/ripple-dom/async-data.md)
rather than built-in pending propagation.

### Solid (through Partas.Solid)

Ranvier's suspension model is inspired by
[Solid 2's async design](https://github.com/solidjs/solid/blob/next/documentation/solid-2.0/05-async-data.md):
pending propagation, boundaries and recovery through re-reads. Solid 2 remains a release candidate;
scheduling, transitions, rendering and APIs are not interchangeable with Ranvier. Partas.Solid binds
Solid's browser APIs. For a Partas.Solid application, use the Solid bindings supported by your installed
version so the renderer observes its own graph. This does not imply every Solid 2 API is already bound,
or that arbitrary Fable application code needs no Fable runtime.

### Prior art

Height-ordered, cutoff-aware recomputation descends from Adapton, Jane Street's Incremental and
FSharp.Data.Adaptive. These share incremental-computation ideas, rather than identical APIs or async contracts.

## Diamonds without glitches

A diamond is two derived values that read one source, and a third value that reads both. In Ranvier, one
write to `a` runs `d` once, and `d` reads `b` and `c` from the same write.

```fsharp
let a = createSignal 1
let b = createMemo (fun _ -> a.Value + 1)
let c = createMemo (fun _ -> a.Value * 10)
let d = createMemo (fun _ -> b.Value + c.Value)
createEffect (fun () -> printfn "d = %d" d.Value)

a.Value <- 2
a.Value <- 3
```

```text
d = 12
d = 23
d = 34
```

In the following synchronous System.Reactive subscription topology, `CombineLatest` emits once per leg. The first emission after each
write combines the new `b` with the old `c`, a state the source never had.

```fsharp
let a = new BehaviorSubject<int> (1)
let b = a.Select (fun x -> x + 1)
let c = a.Select (fun x -> x * 10)
Observable.CombineLatest(b, c, fun b c -> b + c).Subscribe (printfn "d = %d")

a.OnNext 2
a.OnNext 3
```

```text
d = 12
d = 13
d = 23
d = 24
d = 34
```

These intermediate emissions follow `CombineLatest`'s stream contract. This example does not imply every
Rx architecture produces inconsistent state: an application can derive both branches from one combined
state emission. Buffering or scheduling also changes delivery, but is not by itself a graph-consistency
guarantee. Ranvier updates this derived graph in height order, and `d` runs after both inputs. Two writes that
belong together go in one [`batch`](../guide/batch.md).

## Owners instead of hooks

Ranvier node identity belongs to the node object and does not depend on a render-time hook call position.
An owning body, such as `createMemoWith` or an effect, may create owned nodes inside branches and loops;
each run may create a different set. Pure memo bodies have stricter creation rules. Owned nodes belong to
the [owner](contracts.md#ownership) current at creation, and its next run disposes them. Signals and async
sources are unowned. State that must survive a body's re-run lives outside that body.

## Where Ranvier may fit

The C# binding adapters and Elmish bridge ship. Other entries below describe application patterns or
recipes, not framework-specific renderer packages.

- **XAML view models (WPF, Avalonia, MAUI, Uno, WinUI).** `ReactiveBindings` in
  [Ranvier.CSharp](../guide/csharp.md#binding-to-xaml) is the `INotifyPropertyChanged` bridge, usable inside an
  existing view model. A pending memo maps to a loading property, a failure maps to `INotifyDataErrorInfo`, and
  the bindings' `IsLoading` and `HasErrors` give one loading and error state covering several sources. The
  dispatcher follows the UI thread's `SynchronizationContext`, and each handler runs on the context it subscribed
  from. No framework-specific package ships yet.
- **Models shared between server and browser.** The same F# model code could run on .NET (server rendering,
  tests, desktop) and through Fable, with Ranvier's pending and failure channels. The Fable target is implemented
  and not yet published; see [Fable (JavaScript) target](../fable/index.md).
- **Deterministic async in tests.** With `ManualDispatcher`, a test chooses when each flight settles and
  reads Pending, Ready and Failed states as values, with no UI thread involved. See
  [Testing async state](../guide/testing.md).
- **Avalonia.FuncUI.** Its component state already has the shape of a signal.
- **Fluxor stores.** For Fluxor users, memos are memoised selectors: a memo over a signal holding the
  store's state recomputes when that state changes, and an equal result stops at the memo
  ([equality cutoff](../guide/memos.md#equality-cutoff)).
- **Blazor.** A boundary maps onto a component. Blazor has no built-in signals, and
  [dotnet/aspnetcore#67329](https://github.com/dotnet/aspnetcore/issues/67329), an open proposal, asks for
  them. For Blazor Server, `ThreadAffinity.Serialised` admits the circuit's work one thread at a time; see
  [Blazor Server](../guide/blazor-server.md).

## Current gaps

- No framework-specific UI packages yet. The .NET UI bindings are `ReactiveBindings` (`INotifyPropertyChanged` and
  `INotifyDataErrorInfo`), `ReactiveCommand` (`ICommand` with a derived `CanExecute`) and
  `Projection.AsObservableCollection`.
- No debounce or throttle, and no flight policy that drops a new run while one is in progress.
- Further collection incrementality: the scanning and sorting paths described above remain. Readers
  coalesce changes between reads and use bounded logs with reset recovery; they describe current state,
  not an event history. Coalescing is a state-delta contract, not evidence of absent incrementality.
- The Fable target is implemented and not yet published; see [Fable (JavaScript) target](../fable/index.md).

The [roadmap](roadmap.md) lists which of these are under consideration.

## Choosing

- Consider **FSharp.Data.Adaptive** for its established adaptive sets, maps, indexed lists and incremental operators.
- Consider **SignalsDotnet** for its R3 integration, MVVM/UI packages and documented signal features.
- Use **R3 or System.Reactive** for event streams and time-based composition.
- Use **Solid through Partas.Solid**, or **Fable.Ripple**, for browser UI in F#.
- Consider **Ranvier** when async loading and error states should propagate through derived values to a
  boundary, with owned computations and incremental keyed rows, in F# or C#. Its packages are pre-release.
