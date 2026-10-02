---
title: Threading and dispatch
---

Examples using tasks import the .NET types and construct a graph with a manual dispatcher:

```fsharp
open System
open System.Threading
open System.Threading.Tasks
open Ranvier

let newGraph () =
    new Graph ({ GraphOptions.Default with Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher) })
```

When a completion arrives from another thread, pump the graph on its owning thread. See
[Threading and dispatch](threading.md) and the `pumpUntil` helper in [Testing async state](testing.md).

Under the default `Guarded` affinity, the constructing thread owns the graph. Use
`graph.Dispatch` for work from another thread. Async completions already use it.

`Dispatch` runs inline on the graph thread. Otherwise, it queues the work and asks the dispatcher
to wake that thread. With a manual dispatcher, call `graph.Pump ()` to apply queued work.

:::details Operation-by-operation threading contracts

| Operation | Contract |
|-----------|----------|
| `Signal.Value <- v` | Guarded. Under `ThreadAffinity = Guarded` (the default) a write from another thread raises `InvalidOperationException` and the value is unchanged. An equal write raises too. |
| `AsyncSource.Settle` / `Fail`, a completing `AsyncMemo` flight | Marshalled through `Graph.Dispatch`. Under `ImmediateDispatcher` and `Guarded` affinity, an off-thread call raises `Pump ran on thread` instead (see [Dispatcher selection](#dispatcher-selection)). |
| `Graph.Dispatch work` | Runs `work` inline on the graph thread. From another thread, queues it in the graph's inbox and asks the dispatcher to wake the graph thread. |
| `Graph.Pump ()` | Guarded. Runs the inbox in arrival order, then flushes effects, and returns the number of items run. An item that throws is recorded in `graph.Root.Errors`, and the rest still run. |
| `Graph.PendingWork` | The number of inbox items waiting for a pump. |
| Creating a node, `Batch`, `Untrack`, `Flush`, `CreateRoot`, `OnCleanup`, `Dispose` | Guarded. From another thread each raises `InvalidOperationException` before it changes the graph. |
| `.Value`, `TryValue` | Guarded when the read recomputes a stale memo or boundary. A read of a current value is not guarded. |
| `Peek` | Not guarded. |
| `Graph.Current` | Flows with the async context of `graph.Activate ()`. A guarded graph is current on the activating thread only; an `Unchecked` or `Serialised` graph is current on every thread the activating context reaches. The `create*` functions elsewhere raise `No ambient graph on this thread`. |

:::

`ThreadAffinity = Unchecked` removes the guard. Use it only when every write is known to arrive on
one thread. `ThreadAffinity = Serialised` suits a host that runs its work one item at a time on a
synchronisation context but on varying threads, such as a Blazor Server circuit; see
[Blazor Server](blazor-server.md).

:::details Recognise an off-thread write

The write guard's message names both threads:

```text
A signal write ran on thread 12, but this graph is owned by thread 1. Marshal through Graph.Dispatch,
or set GraphOptions.ThreadAffinity to Unchecked if affinity is guaranteed some other way.
```

:::

### Dispatcher selection

`GraphOptions.Dispatcher` controls how queued work reaches the graph thread. By default:

- A captured `SynchronizationContext` receives the drain automatically, as on a UI thread.
- Without a context, the graph uses `ManualDispatcher`; call `graph.Pump ()` yourself.

:::details Dispatcher options

| `Dispatcher` | Behaviour |
|--------------|-----------|
| `None` (default), with a `SynchronizationContext` current at construction | A `SynchronizationContextDispatcher` posts the drain to that context. WPF, WinForms and Avalonia UI threads work this way with no further setup. |
| `None` (default), with no `SynchronizationContext` | `ManualDispatcher`. |
| `Some (ManualDispatcher ())` | The inbox is drained only when the graph thread calls `graph.Pump ()`. |
| `Some (ImmediateDispatcher ())` | Calls `Pump` on the thread that posted. Under `ThreadAffinity = Guarded` (the default), an off-thread post raises `Pump ran on thread ...` on that thread and the work stays queued. Under `Unchecked`, the drain runs on the posting thread. |

:::

A console app, server or test without a `SynchronizationContext` gets `ManualDispatcher`.
Under `ManualDispatcher`, an off-thread settle becomes visible only after `graph.Pump ()`. That
includes an `AsyncMemo` flight whose task completes on the thread pool:

::::details Test your understanding

The worker finishes before Pump runs. Is answer ready yet? How many inbox items does Pump apply?

```fsharp
let poolGraph = newGraph ()
let release = new ManualResetEventSlim (false)

let answer =
    poolGraph.Run (fun () ->
        createAsync (fun _ _ ->
            Task.Run (fun () ->
                release.Wait ()
                6 * 7)))

let inFlight = answer.TryValue
release.Set ()
SpinWait.SpinUntil ((fun () -> poolGraph.PendingWork > 0), TimeSpan.FromSeconds 5.) |> ignore
let beforePump = answer.TryValue, poolGraph.PendingWork
let ran = poolGraph.Pump ()
inFlight, beforePump, ran, answer.TryValue
```

:::details Answer

```text
(Pending, (Pending, 1), 1, Ready 42)
```

:::
::::

:::warning `ImmediateDispatcher` with off-thread settles

Under the default
`ThreadAffinity = Guarded`, an off-thread settle with `ImmediateDispatcher` raises
`Pump ran on thread ...` on the settling thread and leaves the work queued until the graph thread
calls `graph.Pump ()`. Under `Unchecked`, the drain runs on the settling thread and mutates the
graph there, which is safe only when every write already arrives on one thread.
When the settle is an `AsyncMemo` flight completing on the thread pool, the exception is raised in
the flight's continuation and reaches `TaskScheduler.UnobservedTaskException`.
:::

:::warning A console app that never calls `Pump`

The graph gets `ManualDispatcher`, the flight completes
on the thread pool, and the node stays `Pending`. Call `graph.Pump ()` from the graph thread (in a
loop, or after awaiting the work), or construct the graph on a thread with a
`SynchronizationContext`.
:::
