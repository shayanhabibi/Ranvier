---
title: Fable (JavaScript) target
order: 1
---

> **Preview** — Ranvier is pre-release; its APIs may change.

**Status: Planned.** Ranvier intends to support a Fable target, so the same reactive graph can run in JavaScript as well as on .NET. This page describes where that work stands. It is not yet a supported or published target.

## Intent

Ranvier is native .NET first and Fable second. The engine is written so that it can be compiled to JavaScript with [Fable](https://fable.io), with the .NET build remaining the reference. Where the two targets must differ, the difference is kept in a small number of platform-specific places rather than spread through the engine.

## Current state

The engine already compiles under Fable, and a smoke check runs the compiled engine under Node.js and asserts the same behaviour as the .NET test suite. Verified by running it, not only by compiling it:

- signals, memos and effects;
- `batch`, `untrack` and `onCleanup`;
- scope disposal and error boundaries;
- the equality cutoff;
- keyed projections, selectors and index projections;
- `AsyncSource`, and `AsyncMemo` through `createAsync` and `createAsyncWith`, under all three flight policies.

Ranvier's engine compiles under Fable in this repository, and the .NET test suite runs against it under Node.js (`dotnet fsi build.fsx test-fable`), once untraced and once with the trace log compiled in. Tests that exercise a .NET-only facility, such as threads, garbage collection or `ObservableCollection`, are compiled out. The run writes `docs/.ai/fable-compat.md`: the tests that pass, fail and are excluded, in each file. Every failure listed there is one of the differences below. No Fable package is published, and the Fable build is not part of Ranvier's release process yet.

## Known differences from .NET

These are the differences that exist today. Some may narrow; others follow from the JavaScript runtime and will remain.

**Async results arrive on a later microtask.** Under Fable an async flight is a promise. A flight that has already completed when its body returns still reads as Pending on the first read and becomes ready once the microtask queue drains. On .NET the continuation runs inline and the first read is ready. Synchronous rendering, such as server-side rendering, sees the Pending state.

**A `Queue` flight applies on a later microtask.** Under `FlightPolicy.Queue` each result waits on a promise chain, so a flight applies on a later microtask even when it and every earlier flight have settled. On .NET a settled queue applies inline.

**Every `await` suspends.** A Fable `task` continues on a later microtask, outside every computation, so a read after an `await` is untracked even when the awaited promise had already resolved. On .NET a read after awaiting a completed task is tracked.

**Cancellation is recognised by type.** A rejection counts as cancelled when its reason is Fable's `OperationCanceledException`, as produced by a cancelled `TaskCompletionSource`. Other rejections read as Failed.

**Equality of value types differs.** The default cutoff compares primitives by value and everything else by reference on both targets, but value types compile differently under Fable:

- structs, struct tuples, `DateTime`, `decimal` and `KeyValuePair` become objects, so a write of an equal value that is cut off on .NET propagates under Fable;
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
- A value is rendered with Fable's `%A`, so an anonymous record reads `{ Qty = 2 }` rather than `{| Qty = 2 |}`.

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
- `SynchronizationContextDispatcher`. There is no synchronisation context to capture and no other thread to marshal to.

## What comes next

Before the Fable target can be called supported, Ranvier needs a published package, the Fable test run in its release process, and documentation of each difference above next to the API it affects. Until then, treat anything on this page as subject to change.
