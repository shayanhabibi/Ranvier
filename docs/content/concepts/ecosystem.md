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
API is designed for F#. Evaluate it with that in mind.

## Related libraries

| Library | Tracking | Update model | Async state |
| --- | --- | --- | --- |
| [FSharp.Data.Adaptive](https://github.com/fsprojects/FSharp.Data.Adaptive) | Explicit, through `adaptive { }` and `let!` | Level-ordered, pull | Outside the adaptive model |
| [SignalsDotnet](https://www.nuget.org/packages/SignalsDotnet) | Automatic, on read | Push, built on R3 | `IsComputing` flag, cancellation strategies |
| [R3](https://github.com/Cysharp/R3) / System.Reactive | Explicit subscriptions | Push streams | Stream events; errors and completion end the stream |
| [Fable.Ripple](https://github.com/fable-hub/Fable.Ripple) | Automatic, dynamic | Pull, synchronous settle | Not part of the node model |
| [Solid 2.0](https://docs.solidjs.com/) (via [Partas.Solid](https://github.com/shayanhabibi/Partas.Solid)) | Automatic, dynamic | Height-ordered | Pending channel and boundaries |
| Ranvier | Automatic, dynamic | Height-ordered, pull | Pending channel and boundaries |

### FSharp.Data.Adaptive

A mature F# incremental computation library, used by the Aardvark platform. Its adaptive collections
(`aset`, `alist`, `amap`) pass deltas from one stage to the next. Ranvier's
[projections](../guide/collections.fsx) are keyed collections with `filter`, `map`, `sortBy`, `groupBy` and
fold views. Each view re-reads its upstream keys on a membership or order change, and delta readers are still
in design. Dependencies are threaded explicitly through the `adaptive { }` computation expression, which makes them
visible in the code. If you need delta-based incremental collections or a proven F# library today, use Adaptive.

### SignalsDotnet

A published signals library for .NET MVVM, with integrations listed for WPF, Avalonia, MAUI, Uno, Blazor,
Unity and Godot. It tracks automatically and exposes async computations through an `IsComputing` flag.
It is built on R3, so every signal is also an `Observable<T>`, and every signal implements
`INotifyPropertyChanged`. `CollectionSignal` wraps an `ObservableCollection` and reacts both to the
collection being replaced and to its contents changing. The `SignalsDotnet.Blazor` package provides a
`TrackedScope` component that re-renders only the region of markup whose signals changed.
`SignalsDotnet.Query` and `SignalsDotnet.AspNetCore`, which streams projections as server-sent events, are
in alpha. Its cancellation strategies for concurrent async runs are a direct counterpart of Ranvier's
[flight policies](async-graph.md#superseded-flights). If you want signals in a C# view model now, it is
available and documented.

The two libraries differ mainly in how async state travels: as a flag that each consumer checks, or as a
status that propagates to a boundary. In SignalsDotnet, writes wrapped in an atomic operation run each effect
once, at the end. Its documentation does not say whether derived values update in height order.

### R3 and System.Reactive

Push-based streams, familiar to most .NET developers, with a large operator vocabulary. They model events
over time. A derived value that must stay consistent across several inputs is a different shape of
problem, and Ranvier addresses that shape. For event pipelines, throttling, windowing and time-based
composition, a stream library is the right tool.

Both are active. System.Reactive 7.0.0 was released in July 2026. R3 is a redesign by the author of
ReactiveProperty, whose README now says "If you're developing a new application, consider using R3 instead
of ReactiveProperty." ReactiveUI 25 runs on its own `ReactiveUI.Primitives` package
([reactiveui/ReactiveUI#4382](https://github.com/reactiveui/ReactiveUI/pull/4382)) and no longer depends on
System.Reactive by default. The `ReactiveUI.Reactive` package keeps System.Reactive interop.

### Fable.Ripple

A fine-grained reactive library written from scratch in F# for Fable. It ships a DOM layer, forms, a router
and component tests. Its core is written with careful attention to the code Fable generates. It promises
that a write settles synchronously, with no scheduler tick. For browser UI in F# without Solid, Ripple
provides a complete stack, and Ranvier provides only a core.

### Solid (through Partas.Solid)

Ranvier's suspension model follows Solid 2.0's signals: the pending channel, status-parameterised
boundaries, re-running bodies from the top, and recovery driven by re-reads. In the browser, Partas.Solid
binds Solid's own signals directly. They are what Solid's renderer reads, and they add no F# runtime to the
bundle. For a Partas.Solid application, use Solid's signals.

### Prior art

Height-ordered, cutoff-aware recomputation descends from Adapton, Jane Street's Incremental and
FSharp.Data.Adaptive. The [Clef](https://clef-lang.com) language specification describes a Solid-style
reactive surface over incremental nodes, and it leaves asynchronous suspension unspecified.

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

The same diamond in System.Reactive, with `CombineLatest`, emits once per leg. The first emission after each
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

ReactiveUI's multi-property `WhenAnyValue` behaves the same way: setting `A` and then `B` first emits the new
`A` with the old `B`. The usual workarounds are `DelayChangeNotifications ()` or `Throttle (TimeSpan.Zero)`.
Ranvier updates derived values in height order, and `d` runs after both of its inputs. Two writes that
belong together go in one [`batch`](../guide/getting-started.md#batch).

## Owners instead of hooks

Ranvier has no rules of hooks. A node is an object held by reference, and its identity is independent of
call order or line number, unlike React hooks or FuncUI's hook identity (FuncUI#212). A body may create
nodes inside a branch or a loop, and each run may create a different set. Each node belongs to the
[owner](contracts.md#ownership) that was current at its creation, and the next run of that owner disposes
it. The cost is that state created in a body starts again on each run: state that has to survive a re-run
lives outside the body.

## Where Ranvier may fit

These are directions the design is aimed at. The XAML bridge ships in Ranvier.CSharp; the others have no integration yet.

- **XAML view models (WPF, Avalonia, MAUI, Uno, WinUI).** `ReactiveBindings` in
  [Ranvier.CSharp](../guide/csharp.md#binding-to-xaml) is the `INotifyPropertyChanged` bridge, usable inside an
  existing view model. A pending memo maps to a loading property, a failure maps to `INotifyDataErrorInfo`, and
  the bindings' `IsLoading` and `HasErrors` give one loading and error state covering several sources. The
  dispatcher follows the UI thread's `SynchronizationContext`, and each handler runs on the context it subscribed
  from. No framework-specific package ships yet.
- **Models shared between server and browser.** The same F# model code could run on .NET (server rendering,
  tests, desktop) and through Fable, with suspension that matches Solid 2.0. The Fable target is implemented
  and not yet published; see [Fable (JavaScript) target](../fable/index.md).
- **Deterministic async in tests.** With `ManualDispatcher`, a test chooses when each flight settles and
  reads Pending, Ready and Failed states as values, with no UI thread involved. See
  [Testing async state](../guide/testing.md).
- **Avalonia.FuncUI.** Its component state already has the shape of a signal.
- **Fluxor stores.** For Fluxor users, memos are memoised selectors: a memo over a signal holding the
  store's state recomputes when that state changes, and an equal result stops at the memo
  ([equality cutoff](../guide/getting-started.md#equality-cutoff)).
- **Blazor.** A boundary maps onto a component. Blazor has no built-in signals, and
  [dotnet/aspnetcore#67329](https://github.com/dotnet/aspnetcore/issues/67329), an open proposal, asks for
  them. Blazor Server needs the serialised affinity mode listed under current gaps.

## Current gaps

- No framework-specific UI packages yet. The .NET UI bindings are `ReactiveBindings` (`INotifyPropertyChanged` and
  `INotifyDataErrorInfo`), `ReactiveCommand` (`ICommand` with a derived `CanExecute`) and
  `Projection.AsObservableCollection`.
- The C# package, [Ranvier.CSharp](../guide/csharp.md), still exposes `ValueOption` in a few places, such as
  `Previous<T>.Settled`.
- No serialised-but-multi-threaded affinity mode, which Blazor Server needs.
- No debounce or throttle, and no flight policy that drops a new run while one is in progress.
- Projections publish their current state only. Delta readers, which report the keys added, removed and
  changed since a reader last looked, are in design.
- The Fable target is implemented and not yet published; see [Fable (JavaScript) target](../fable/index.md).

The [roadmap](roadmap.md) lists which of these are under consideration.

## Choosing

- Use **FSharp.Data.Adaptive** for incremental collections, or for a mature F# incremental library.
- Use **SignalsDotnet** for signals in C# MVVM today.
- Use **R3 or System.Reactive** for event streams and time-based composition.
- Use **Solid through Partas.Solid**, or **Fable.Ripple**, for browser UI in F#.
- Consider **Ranvier** when async loading and error states should propagate through derived values to a
  boundary, and you can work with a pre-release F# library.
