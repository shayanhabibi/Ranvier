---
title: Fable (JavaScript) target
order: 1
---

> **Preview** — Ranvier is pre-release; its APIs may change.

**Status: Implemented, with Fable sources included in package builds.** Ranvier compiles with Fable and runs its shared test suite against the compiled engine under Node.js. CI also tests consumers of the packed sources. This page lists the platform differences, including cases where evaluation timing or equality differs.

## Intent

Ranvier is native .NET first and Fable second. The engine is written so that it can be compiled to JavaScript with [Fable](https://fable.io), with the .NET build remaining the reference. Where the two targets must differ, the difference is kept in a small number of platform-specific places rather than spread through the engine.

## Current state

The shared test suite runs the compiled engine under Node.js with traced and untraced builds, using
both inline and promise delivery. It exercises these APIs and records the target differences below:

- signals, memos and effects;
- fixed-dependency signal and memo bindings;
- `batch`, `untrack` and `onCleanup`;
- scope disposal and error boundaries;
- the equality cutoff;
- keyed projections, selectors and index projections;
- `AsyncSource`, and `AsyncMemo` through `createAsync`, `createAsyncWith`, `createAsyncWithPolicy` and `createOwningAsyncWithPolicy`, under all six flight policies, set for the graph or for one async memo;
- debounce and throttle through timed values;
- the Elmish bridge (`Mvu`) and the query layer;
- stream memos through `createStream` and `createStreamFold`, with `StreamSource.create`, `ofObservable` and `ofAsyncIterable`. `ofAsyncEnumerable` and `ofChannel` are .NET only; `ofAsyncIterable` is Fable only. See [Streams](../guide/streams.md).

Ranvier's engine compiles under Fable in this repository, and the .NET test suite runs against it under Node.js (`dotnet fsi build.fsx test-fable`), once untraced and once with the trace log compiled in. Tests that exercise a .NET-only facility, such as threads, garbage collection or `ObservableCollection`, are compiled out. The run writes `docs/.ai/fable-compat.md`: the tests that pass, fail and are excluded, in each file. Every failure listed there is one of the differences below.

## Package sources and CI

Package builds of `Ranvier`, `Ranvier.Elmish`, `Ranvier.CSharp` and `Ranvier.Query`, including their
`.Traced` variants, include Fable sources and a standalone `fable/<package>.fsproj`. The source
project lists the packaged files in compile order and does not depend on the repository's build
imports. Use a normal `PackageReference` from an F# project compiled with Fable; there is no separate
JavaScript engine package to install.

CI compiles and runs both the repository test project and consumers restored from locally packed
NuGet packages, traced and untraced. The package consumer check is
`tests/check-package-compatibility.ps1 -CheckFable`. These checks validate the package build;
availability of a particular version on NuGet is a separate release step.

**Packages ship the Fable sources.** `Ranvier` and `Ranvier.CSharp` from `0.1.0-preview.6`,
`Ranvier.Elmish` from `0.1.0-preview.5` and `Ranvier.Query` from `0.1.0-preview.2` contain the
standalone Fable project files, as do the view packages (`Ranvier.View`, `Ranvier.View.Dom`,
`Ranvier.View.Anime` and `Fable.Ranvier.Anime`) from their first version, `0.1.0-alpha.1`. See [Installation](../guide/installation.md#from-nuget) for the package
links. Earlier previews do not contain them; use a current version, a local package feed, or a repository
project reference. The CI consumer script packs temporary packages and demonstrates the local-feed path.

## Known differences from .NET

These are the differences that exist today. Some may narrow; others follow from the JavaScript runtime and will remain.

**Graph dispatch is single-threaded.** Under Fable, `Graph.Dispatch` runs work inline,
`IsOnGraphThread` is always true, and `PendingWork` and `Pump ()` return zero. Thread-affinity checks,
cross-thread inbox machinery and internal locks are omitted. Promise completions still arrive on
later microtasks, as described below; pumping cannot make a promise settle synchronously. The .NET
threading and dispatcher contracts are unchanged; see [Threading and dispatch](../guide/threading.md).

The generated JavaScript also erases the engine's internal source and observer slot wrappers to
their references. This changes the representation without changing the reactive API or adding a
new capability.

**Async results arrive on a later microtask.** Under Fable an async flight is a promise. A flight that has already completed when its body returns still reads as Pending on the first read and becomes ready once the microtask queue drains. On .NET the continuation runs inline and the first read is ready. Synchronous rendering, such as server-side rendering, sees the Pending state.

**A stream's end applies on a later microtask.** A stream memo observes the completion or failure of its source's `Run` task one microtask after the task settles. Until then the memo keeps its published state, and an item the source delivers in that window is still folded and published. On .NET a task completed on the graph thread, with continuations run synchronously, ends the stream before `SetResult` or `SetException` returns.

**A `Queue` flight applies on a later microtask.** Under `FlightPolicy.Queue` each result waits on a promise chain, so a flight applies on a later microtask even when it and every earlier flight have settled. On .NET a settled queue applies inline.

**Every `await` suspends.** A Fable `task` continues on a later microtask, outside every computation, so a read after an `await` is untracked even when the awaited promise had already resolved. On .NET a read after awaiting a completed task is tracked.
A signal write or an `AsyncSource` settle after an `await` in an async memo's body is recorded in the graph's
[input journal](../guide/journal.md) as an external input. On .NET the continuation is part of the body, and the write
records no entry. Passed through `Journal.produce`, with the work captured before the `await`, it records a `Produced`
entry, and both replay modes apply it once. For the same reason an effect's continuation is not attributed to the
effect run, as it is on .NET: pass its writes, settles and messages through `Journal.produce`.

**Cancellation is recognised by type.** A rejection counts as cancelled when its reason is Fable's `OperationCanceledException`, as produced by a cancelled `TaskCompletionSource`. Other rejections read as Failed.

**Equality of value types differs.** The default cutoff compares primitives by value and everything else by reference on both targets, but value types compile differently under Fable:

- structs, struct tuples, `DateTime`, `DateTimeOffset`, `decimal` and `KeyValuePair` become objects, so a write of an equal value that is cut off on .NET propagates under Fable;
- `Some x` is erased to `x`, so writing `Some 1` over `Some 1` propagates on .NET and is cut off under Fable;
- `StructuralPolicy` cuts off `nan` over `nan` on .NET and propagates it under Fable.

**Exception types.** The engine raises `InvalidOperationException`, `ArgumentException` and `ArgumentNullException` as those types on both targets, so a type test on them works under Fable. `ObjectDisposedException` and `KeyNotFoundException` compile to a plain `Exception`: a type test on either is always false under Fable.

**A node read before its first value holds `null`.** `Peek` on a memo that has never run, or on an async memo that has never settled, returns `Unchecked.defaultof<'T>`. In generic code under Fable that is `null`, not `0` or `false`.

**Text formatting.** `string true` is `"True"` on .NET and `"true"` under Fable, and `%A` renders lists and tuples differently. Format with `%b`, `%d` and `%s`, or build the text by hand, where the output must match.

**Collections compare by structure.** A `ResizeArray` is a JavaScript array, compared element by element. A projection or lookup keyed by one treats two equal lists as the same key under Fable and as distinct keys on .NET. Key by a class for identity on both targets.

**A null function argument is never null.** Fable wraps a function passed to a multi-argument parameter in a closure. Passing `Unchecked.defaultof<_>` as `subtract` to `Projection.foldGroup` raises `ArgumentNullException` on .NET and is accepted under Fable, failing later when it is called.

**Trace records carry less.** A traced build runs under Fable, with three differences in what the log records:

- A node's creation site is not captured: `Trace.origin` gives a null `Site`, and a path falls back to the node's kind.
- A failure is recorded by its message alone, without the exception's type name.
- A value is rendered with Fable's `%A` in place of the .NET build's `ToString`, so an anonymous record reads `{ Qty = 2 }` rather than `{| Qty = 2 |}`.

**Loop bounds.** Fable re-evaluates the upper bound of `for i in a .. b` on every iteration. A bound that reads a signal, or anything else that can change inside the loop, iterates a different number of times under Fable. Bind the bound first.

## Not available under Fable

- The public constructors of `Memo<'T>` and `AsyncMemo<'T>`. Under Fable, create a memo with `createMemo` or `createMemoWith`, and an async value with `createAsync` or `createAsyncWith`.

```fsharp
open Ranvier

use graph = new Graph ()

graph.Run (fun () ->
    let count = createSignal 1
    let doubled = createMemo (fun _ -> count.Value * 2)
    createEffect (fun () -> printfn "%d" doubled.Value)
    count.Value <- 5)
```

The snippet uses only functions that exist on both targets, which is the style to follow in code meant to compile under Fable.

- `Projection.AsObservableCollection`, since `ObservableCollection` is a .NET collection type.
- `Trace.dump`, which writes a file, and `TraceModel.parseDump`. `Trace.dumpText` gives the same text.
- `TimedClock.ofTimeProvider`, which adapts the .NET `TimeProvider`.
- `SynchronizationContextDispatcher`. There is no synchronisation context to capture and no other thread to marshal to.

## UI integration

The Fable target supplies the reactive engine, Elmish bridge and query layer. It does not include a
browser DOM renderer or a framework-specific view adapter. Build those integrations on the public
primitives, including [fixed-dependency bindings](../guide/bindings.md).
