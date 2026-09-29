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

A mature F# incremental computation library, used by the Aardvark platform. It has adaptive collections
(`aset`, `alist`, `amap`) that Ranvier does not match: collection combinators here are still in design.
Dependencies are threaded explicitly through the `adaptive { }` computation expression, which makes them
visible in the code. If you need incremental collections or a proven F# library today, use Adaptive.

### SignalsDotnet

A published signals library for .NET MVVM, with integrations listed for WPF, Avalonia, MAUI, Uno, Blazor,
Unity and Godot. It tracks automatically and exposes async computations through an `IsComputing` flag.
Its cancellation strategies for concurrent async runs are a direct counterpart of Ranvier's
[flight policies](async-graph.md#superseded-flights). If you want signals in a C# view model now, it is
available and documented.

The two libraries differ mainly in how async state travels: as a flag that each consumer checks, or as a
status that propagates to a boundary.

### R3 and System.Reactive

Push-based streams, familiar to most .NET developers, with a large operator vocabulary. They model events
over time. A derived value that must stay consistent across several inputs is a different shape of
problem, and Ranvier addresses that shape. For event pipelines, throttling, windowing and time-based
composition, a stream library is the right tool.

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
  reads Pending, Ready and Failed states as values, with no UI thread involved.
- **Avalonia.FuncUI.** Its component state already has the shape of a signal.

## Current gaps

- No framework-specific UI packages yet. The .NET UI bindings are `ReactiveBindings` (`INotifyPropertyChanged` and
  `INotifyDataErrorInfo`) and `Projection.AsObservableCollection`; commands with a derived `CanExecute` are not covered.
- The C# package, [Ranvier.CSharp](../guide/csharp.md), has no tracing, and a few of its types, such as
  `Previous<T>.Settled`, still carry `ValueOption`.
- No serialised-but-multi-threaded affinity mode, which Blazor Server needs.
- The Fable target is implemented and not yet published; see [Fable (JavaScript) target](../fable/index.md).

## Choosing

- Use **FSharp.Data.Adaptive** for incremental collections, or for a mature F# incremental library.
- Use **SignalsDotnet** for signals in C# MVVM today.
- Use **R3 or System.Reactive** for event streams and time-based composition.
- Use **Solid through Partas.Solid**, or **Fable.Ripple**, for browser UI in F#.
- Consider **Ranvier** when async loading and error states should propagate through derived values to a
  boundary, and you can work with a pre-release F# library.
