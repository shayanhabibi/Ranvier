---
title: Fable (JavaScript) target
order: 1
---

> **Preview** — Ranvier is pre-release; APIs follow Partas.Signals and may change.

**Status: Planned.** Ranvier intends to support a Fable target, so the same reactive graph can run in JavaScript as well as on .NET. This page describes where that work stands. It is not yet a supported or published target.

## Intent

Ranvier is native .NET first and Fable second. The engine is written so that it can be compiled to JavaScript with [Fable](https://fable.io), with the .NET build remaining the reference. Where the two targets must differ, the difference is kept in a small number of platform-specific places rather than spread through the engine.

## Current state

The engine Ranvier is derived from (Partas.Signals, at commit `915f139`) already compiles under Fable, and a smoke check runs the compiled engine under Node.js and asserts the same behaviour as the .NET test suite. Verified by running it, not only by compiling it:

- signals, memos and effects;
- `batch`, `untrack` and `onCleanup`;
- scope disposal and error boundaries;
- the equality cutoff;
- keyed projections, selectors and index projections;
- `AsyncSource`, and `AsyncMemo` through `createAsync` and `createAsyncWith`, under all three flight policies.

That check has not yet been carried over to the Ranvier repository, no Fable package is published, and the Fable build is not part of Ranvier's release process yet.

## Known differences from .NET

These are the differences that exist today. Some may narrow; others follow from the JavaScript runtime and will remain.

**Async results arrive on a later microtask.** Under Fable an async flight is a promise. A flight that has already completed when its body returns still reads as Pending on the first read and becomes ready once the microtask queue drains. On .NET the continuation runs inline and the first read is ready. Synchronous rendering, such as server-side rendering, sees the Pending state.

**Every `await` suspends.** A Fable `task` continues on a later microtask, outside every computation, so a read after an `await` is untracked even when the awaited promise had already resolved. On .NET a read after awaiting a completed task is tracked.

**Cancellation is recognised by type.** A rejection counts as cancelled when its reason is Fable's `OperationCanceledException`, as produced by a cancelled `TaskCompletionSource`. Other rejections read as Failed.

**Equality of value types differs.** The default cutoff compares primitives by value and everything else by reference on both targets, but value types compile differently under Fable:

- structs, struct tuples, `DateTime`, `decimal` and `KeyValuePair` become objects, so a write of an equal value that is cut off on .NET propagates under Fable;
- `Some x` is erased to `x`, so writing `Some 1` over `Some 1` propagates on .NET and is cut off under Fable;
- `StructuralPolicy` cuts off `nan` over `nan` on .NET and propagates it under Fable.

## Not available under Fable

- The public constructors of `Memo<'T>` and `AsyncMemo<'T>`. Under Fable, create a memo with `createMemo` or `createMemoWith`, and an async value with `createAsync` or `createAsyncWith`.

```fsharp
open Ranvier

use graph = new Graph ()

graph.Run (fun () ->
    let count = createSignal 1
    let doubled = createMemo (fun () -> count.Value * 2)
    createEffect (fun () -> printfn "%d" doubled.Value)
    count.Value <- 5)
```

The snippet uses only functions that exist on both targets, which is the style to follow in code meant to compile under Fable.

- `Projection.AsObservableCollection`, since `ObservableCollection` is a .NET collection type.
- `SynchronizationContextDispatcher`. There is no synchronisation context to capture and no other thread to marshal to.

## What comes next

Before the Fable target can be called supported, Ranvier needs the smoke check running in its own build, a published package, and documentation of each difference above next to the API it affects. Until then, treat anything on this page as subject to change.
