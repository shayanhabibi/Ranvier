---
title: The async graph on .NET
order: 3
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

This page covers how asynchronous work fits into a synchronous, re-runnable dependency graph on .NET. It
explains where tasks enter the graph, what happens to superseded requests, which thread owns the graph, and
what changes on JavaScript through Fable. [Suspension](suspension.md) covers the pending channel itself.

## Async as a property of every computation

Solid 2.0 removed `createResource`. Its release notes give the reason as "all computations".
Async is no longer a separate primitive. Every computation can be pending, and loading boundaries became
constructs of the graph, not of the renderer. Ranvier follows the same model:

- Any memo, effect or boundary becomes Pending when it reads a pending value. It needs no special
  constructor to do so.
- Values that genuinely arrive later come from two constructors, both *implemented*:
  - `createAsync` computes the value from a task.
  - `createAsyncSource` is settled by hand.

## Tasks enter at the edge of a body

*Implemented.*

A body that re-runs from the top cannot `await` in the middle of a run. If it did, a re-run would restart
the body while an earlier run was still waiting. Instead, the body *returns* a task, and the engine awaits
it:

```fsharp
let profile =
    createAsync (fun _ (ct: CancellationToken) ->
        let id = userId.Value            // tracked: read before the first await
        fetchUserAsync (id, ct))         // Task<User>; the engine awaits it and marks the memo Pending
```

- `createAsync (compute: Previous<'T> -> CancellationToken -> Task<'T>)` returns an `AsyncMemo<'T>`. It is lazy: the first
  read starts a flight, and the memo is Pending until the task completes. When a source that the body read
  changes, the next read starts a new flight.
- Dependencies are tracked only in the synchronous part of the body, before the first `await` that
  suspends. A signal read after that point is not a dependency, so changing it does not start a new flight.
  Read every input before the first `await`.
- The primitive is `Task<'T>`, not `ValueTask<'T>`, because the engine keeps a flight and may consult it
  again. A `ValueTask` cannot be awaited twice.
- `createAsyncSource<'T> ()` returns an `AsyncSource<'T>` that starts Pending. `Settle value` publishes a
  value, and `Fail exn` publishes a failure. `Settle` applies no equality cutoff: settling the same value
  twice still wakes every reader.

If the body reads a pending source before it returns a task, the async memo waits on that source. No
flight starts until the source settles.

### Superseded flights

*Implemented.*

A new flight supersedes one that is still in progress. `GraphOptions.FlightPolicy` controls what happens to
the older flight:

| Policy | Old token cancelled | Superseded result discarded | Every result applied |
| --- | --- | --- | --- |
| `CancelPrevious` (default) | Yes | Yes | No |
| `KeepLatest` | No | Yes | No |
| `Queue` | No | No | Yes, in the order the flights started |

Pass the `CancellationToken` to the I/O that the flight performs. Under `CancelPrevious`, the superseded I/O
then stops. Solid has no token to cancel, so this policy is a .NET addition.

### Not implemented

*Exploratory.* The following ideas from the design research have not been built:

- an `Async<'T>` adapter for cold, restartable F# async workflows
- a streaming memo over `IAsyncEnumerable<'T>`, matching Solid's async-iterable results

## Threads and dispatch

*Implemented.*

JavaScript has one thread, so Solid's graph never has to ask which thread a completion arrives on. On .NET,
a task usually completes on the thread pool. Ranvier gives each graph an owning thread and marshals
completions back to it.

- **Ownership.** A graph belongs to the thread that constructed it. Under `ThreadAffinity = Guarded` (the
  default), a signal write from another thread raises `InvalidOperationException`, and the error message
  names both threads. `Unchecked` removes the check. Use `Unchecked` for Fable, or when every write is known
  to arrive on one thread.
- **Inbox.** Completions from other threads, such as `AsyncSource.Settle` and finished `AsyncMemo` flights,
  go into the graph's inbox. `Graph.Pump ()` applies them in arrival order on the owning thread and then
  flushes effects.
- **Dispatcher.** `GraphOptions.Dispatcher` controls how the owning thread learns that work is waiting:

| `Dispatcher` | Behaviour |
| --- | --- |
| `None`, with a `SynchronizationContext` present at construction | Posts the drain to that context. UI threads in WPF, WinForms and Avalonia work this way with no further setup. |
| `None`, with no `SynchronizationContext` | Uses `ManualDispatcher`. |
| `Some (ManualDispatcher ())` | The inbox fills and is drained when the owning thread calls `graph.Pump ()`. |
| `Some (ImmediateDispatcher ())` | Drains on the thread that posted. Under `Guarded`, an off-thread drain raises and the work stays queued. |

A console app, server or test usually has no `SynchronizationContext`, so it gets `ManualDispatcher`. An
off-thread settle is then visible only after `graph.Pump ()`. A missing pump produces a stall, which is easy
to diagnose, where the alternative is a race, which is not. The same behaviour makes async code
deterministic in tests: the test decides when each flight settles.

A message queue belongs at this boundary, one message per drain. It does not belong between nodes. Inside the
graph, a write settles synchronously before the next line runs.

### Where tracking state lives

Tracking must not follow an `await` into its continuation. Code that resumes after an `await` is outside the
synchronous window in which reads are recorded. For this reason, the tracking context is held by the graph
and scoped to a single run. The ambient graph that the `create*` functions use is per thread, so code on
another thread must activate a graph before it creates nodes.

### Known limitation: hopping synchronisation contexts

*Exploratory.* A Blazor Server circuit serialises work but runs it on different pool threads. Under
`Guarded`, a graph owned by one of those threads rejects writes that arrive on the next one. Under
`Unchecked`, the writes land but the thread check is gone. A dedicated affinity mode for a "serialised, but
not on one thread" host has been identified but not built. Blazor WebAssembly is single-threaded and already
works.

## Ownership and disposal

*Implemented.*

A pending node has to outlive the loss of its last subscriber, or its flight would complete into a
disposed node. On .NET, an in-flight task also holds a strong reference to the node. Teardown is therefore
deterministic:

- Nodes belong to an owner scope. `createRoot` creates a scope, and disposing that scope disposes everything
  under it.
- Disposing an async memo cancels its flight's token.
- A node that is Pending when it is disposed becomes Failed with `ObjectDisposedException`, so nothing waits
  on it indefinitely.

## One source, two targets

The core is written in F# that compiles both on .NET and through Fable to JavaScript. Where the two targets
differ, the difference is a construction-time option on `GraphOptions`, not a conditional branch in the
graph:

- **Equality.** The default `JsIdentityPolicy` matches JavaScript's `===`. It compares primitives and
  strings by value and reference types by reference. On .NET it compares other value types by value, and
  `float` uses IEEE equality, so a `nan` write is not cut off. `StructuralPolicy` uses structural equality
  where a native build wants it.
- **Exceptions.** Suspension uses a throw on both targets, so user code looks the same on both. This is one
  more reason resumable code was rejected: Fable cannot compile it, so it would have produced code that
  builds on one target and not on the other.
- **Threads.** JavaScript has one thread and ignores the dispatcher machinery.

The Fable target is planned; see [Fable (JavaScript) target](../fable/index.md). On the browser, Solid's own signals (through Partas.Solid) are the natural choice for
rendering. A second graph is useful mainly for models that are shared with a .NET server. See
[Ecosystem](ecosystem.md).

## Outside the scope of the core

Ranvier is a reactive core, not a UI framework. It has no DOM layer and no template compiler. In Solid, a
compiler that extracts templates provides the fine-grained DOM performance, and that is a separate and much
larger body of work than the graph. On native .NET hosts such as Avalonia, WPF, MAUI and Blazor, the host
toolkit does the rendering, so that work does not arise.
