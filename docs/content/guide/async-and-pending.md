---
title: Async and pending
order: 4
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

How values that are not ready yet flow through the graph, how to wait for them with boundaries, and
which thread may write. For the synchronous core, see [Getting started](getting-started.md); for
pending rows in collections, see [Collections](collections.fsx).

Every example on this page builds its graph with an explicit `ManualDispatcher`, so the output does not
depend on the host the page runs in. [Threading and dispatch](#threading-and-dispatch) describes what
the default does instead.

```fsharp
let newGraph () =
    new Graph ({ GraphOptions.Default with Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher) })
```

## The pending channel

Every node reports a `Status`, which is a `[<Flags>]` enum:

| Flag | Meaning |
|------|---------|
| `Status.None` | The node holds a settled value. |
| `Status.Pending` | The node is waiting on a value that has not arrived. |
| `Status.Error` | The node's last run failed. Reads report the exception. |
| `Status.Uninitialized` | The node has not produced a value yet. |

The flags combine: an `AsyncSource` that has not settled reports `Pending ||| Uninitialized`.

Pending is independent of dirtiness. A dirty node is out of date and recomputes on its next read. A
pending node has run and is waiting on a source. Pending propagates: a memo, effect or boundary that
reads a pending node becomes pending itself, until a [boundary](#boundaries) stops it.

There are two ways to read a node that may be pending:

- `TryValue` returns a `Reading<'T>`: `Ready value`, `Pending`, or `Failed error`.
- `.Value` returns the value. On a pending node it raises `NotReadyException`, which carries the
  source that is not ready. On a failed node it raises the stored exception.

Both reads link the same dependency edge, so a reader that saw `Pending` is woken when the source
settles.

A body that catches `NotReadyException` in its own `try/with` is still pending, whatever it returns.
A read inside `untrack` is the exception: the body's result stands. A pending read inside `untrack`
that the body does not catch leaves the body pending with no edge to the source, so settling the
source does not re-run it. Read the source tracked, or catch the exception.

```fsharp
/// Renders a reading with the exception message only.
let show (reading: Reading<'T>) =
    match reading with
    | Ready v -> sprintf "Ready %A" v
    | Pending -> "Pending"
    | Failed e -> sprintf "Failed %s: %s" (e.GetType().Name) e.Message

let graph = newGraph ()

let user, greeting =
    graph.Run (fun () ->
        let user = createAsyncSource<string> ()
        let greeting = createMemo (fun _ -> "Hello, " + user.Value)
        user, greeting)

greeting.TryValue
```

```text
Pending
```

Settling the source wakes the memo:

```fsharp
user.Settle "Ada"
greeting.TryValue
```

```text
Ready "Hello, Ada"
```

A pending effect does not run its side effect. The body is aborted at the pending read and runs again
once the source settles:

```fsharp
let log = ResizeArray<string> ()

let name =
    graph.Run (fun () ->
        let name = createAsyncSource<string> ()
        createEffect (fun () -> log.Add ("saw " + name.Value))
        name)

let beforeSettle = log.Count
name.Settle "Grace"
beforeSettle, List.ofSeq log
```

```text
(0, ["saw Grace"])
```

A reader that suspended re-runs from the start of its body when the source settles, not from the
read that suspended. See
[Re-running versus resuming](../concepts/suspension.md#re-running-versus-resuming) for the reasoning.

## AsyncSource

`createAsyncSource<'T> ()` returns an `AsyncSource<'T>`: a source that starts pending and is completed
by hand. `Settle value` publishes a value, and `Fail exn` publishes a failure.

- `Settle` applies no equality cutoff. Settling twice publishes the second value, and settling with the
  value already held still wakes every reader.
- A failed source can be settled later. The settle clears the `Error` flag.
- A source settled before anything reads it is a plain value to its first reader, which is not
  pending at any point.

```fsharp
let price = graph.Run (fun () -> createAsyncSource<int> ())
let shown = graph.Run (fun () -> createMemo (fun _ -> price.Value))

price.Settle 10
shown.Value |> ignore
price.Settle 10
shown.Value |> ignore
shown.Runs
```

```text
2
```

```fsharp
price.Fail (exn "offline")
let whileFailed = shown.TryValue
price.Settle 12
show whileFailed, show shown.TryValue, price.Status
```

```text
("Failed Exception: offline", "Ready 12", None)
```

## Async memos

`createAsync (compute: Previous<'T> -> CancellationToken -> Task<'T>)` returns an `AsyncMemo<'T>`. The memo is lazy:
the first read starts a flight, and the memo is pending until the task completes. A change to a
source the body read starts a new flight on the next read, and the memo is pending again. `Peek`
returns the last settled value without starting a flight.

The body runs synchronously up to its first `await` that suspends. Only the reactive reads made in
that part are tracked. An `await` on a task that has already completed does not suspend, so a read
after it is still tracked.

```fsharp
let flightGraph = newGraph ()
let userId = flightGraph.Run (fun () -> createSignal 1)
let response = TaskCompletionSource<string> ()

let profile =
    flightGraph.Run (fun () ->
        createAsync (fun _ _ ->
            let id = userId.Value // tracked: read before the first await

            task {
                let! body = response.Task
                return $"user %d{id}: %s{body}"
            }))

profile.TryValue
```

```text
Pending
```

Completing the task settles the memo:

```fsharp
response.SetResult "Ada"
pumpUntil flightGraph (fun () -> not (profile.Status.HasFlag Status.Pending)) (TimeSpan.FromSeconds 5.)
profile.TryValue
```

```text
Ready "user 1: Ada"
```

`pumpUntil` is this page's helper: it calls `graph.Pump ()` until the condition holds.

```fsharp
let pumpUntil (g: Graph) (cond: unit -> bool) (timeout: TimeSpan) =
    let sw = Diagnostics.Stopwatch.StartNew ()

    while not (cond ()) do
        if sw.Elapsed > timeout then
            failwithf "condition not met within %O" timeout

        g.Pump () |> ignore
        Threading.Thread.Sleep 1
```

[Threading and dispatch](#threading-and-dispatch) explains when a pump is required.

> **Caution: reads after an `await` that suspends are untracked.** A read in the continuation runs
> outside the memo's tracking context, so a later change to that source does not start a new flight.
> Read every reactive value the flight depends on before the first `await`, and capture it in a
> local. On .NET a continuation can run inline inside another computation's body, when that body
> completes the awaited task. Its reads are untracked there too: they link no edge to that
> computation.
>
> The same boundary applies to creation. `createAsync`'s purity check and `createAsyncWith`'s scope
> cover the body up to its first `await` that suspends. A node created in a continuation, `onCleanup` included,
> attaches to the owner current when the continuation runs, usually the graph's root, and lives
> until that owner is disposed.

A body that throws before its first `await`, and a flight whose task faults, both settle the memo as
`Failed` with the original exception. A flight whose task is cancelled by its own IO, such as an
`HttpClient` timeout, settles as `Failed` with the `OperationCanceledException`. A cancellation of
a superseded flight publishes nothing.

```fsharp
let failGraph = newGraph ()

let refused =
    failGraph.Run (fun () -> createAsync (fun _ _ -> failwith "no connection" : Task<int>))

let faulting = TaskCompletionSource<int> ()
let faulted = failGraph.Run (fun () -> createAsync (fun _ _ -> faulting.Task))
faulted.TryValue |> ignore
faulting.SetException (TimeoutException "timed out")
pumpUntil failGraph (fun () -> not (faulted.Status.HasFlag Status.Pending)) (TimeSpan.FromSeconds 5.)

show refused.TryValue, show faulted.TryValue
```

```text
("Failed Exception: no connection", "Failed TimeoutException: timed out")
```

### Flight policy

A new flight supersedes the one in progress. `GraphOptions.FlightPolicy` sets what happens to the
superseded flight:

| Policy | Old token cancelled | Superseded result discarded | Every result applied |
|--------|---------------------|-----------------------------|----------------------|
| `CancelPrevious` (default) | Yes | Yes | No |
| `KeepLatest` | No | Yes | No |
| `Queue` | No | No | Yes, in the order the flights started |

`CancelPrevious` and `KeepLatest` publish the same values. They differ only in whether the
superseded flight's `CancellationToken` is cancelled. Pass the token to the IO the flight performs,
so that `CancelPrevious` stops the superseded IO.

Every policy starts a new flight as soon as a changed memo is read; the policy decides only the fate of
the flights already in progress. The memo observes every flight's exception, including a superseded
flight's and one raised after the memo or its graph was disposed, so `TaskScheduler.UnobservedTaskException`
receives none of them.

The same policies under the names other .NET libraries use:

| Ranvier | R3 `AwaitOperation` | SignalsDotnet `ConcurrentChangeStrategy` | CommunityToolkit `AsyncRelayCommand` |
|---------|---------------------|------------------------------------------|--------------------------------------|
| `CancelPrevious` | `Switch` | `CancelCurrent` | — |
| `KeepLatest` | — (`Switch` without the cancellation) | — | — |
| `Queue` | `SequentialParallel` | — | — |
| Not implemented | `Sequential` | `ScheduleNext` (runs once more after the current run, at most one queued) | — |
| Not implemented | `Drop` | — | `AllowConcurrentExecutions = false` (the default: the command cannot execute while running) |
| Not implemented | `Parallel` (every result, in completion order) | — | `AllowConcurrentExecutions = true` |
| Not implemented | `ThrottleFirstLast` | — | — |

Debounce and throttle are not implemented either, as a policy or as a combinator.

Cancellation costs one `CancellationTokenSource` per flight under `CancelPrevious`: each launch cancels
and disposes the superseded flight's source and allocates the next. `KeepLatest` and `Queue` allocate
one source per memo, at its first flight, and cancel it only when the memo is disposed. A body that
ignores its token pays that allocation and a `Cancel` with no registered callbacks. The token belongs
to the async memo alone: signals, memos and effects carry none, and their reads take no token.

In the map, `Desk` stands in for a remote service: its requests stay pending until a button answers them. **Next user** starts a flight; pressed twice, the second flight supersedes the first, which drops. **Answer** settles the newest flight and **Fail** fails it. The timeline steps through each event.

```fsharp map timeline
let desk = Desk<string>()
let userId = createSignal 1
let profile = createAsync (fun _ _ -> desk.Quote userId.Value)
createEffect (fun () -> printfn $"profile {profile.Value}")

controls [
    button "Next user" (fun () -> userId.Value <- userId.Value + 1)
    button "Answer" (fun () -> desk.Settle $"user {userId.Value}")
    button "Fail" (fun () -> desk.Fail "no connection")
]
```

Under `Queue` each outcome is readable as soon as it is applied, while later flights are still in
progress: a new run makes the memo pending until the next outcome is applied. A body that throws
before its first `await` fails in its turn, after the flights started before it. While the newest
run waits on a pending source, an earlier flight's value becomes the `Peek` value and the memo stays
pending.

### The previous value

The body's first argument is a `Previous<'T>`: a handle on the value the memo last published. Its
one member, `Settled`, is a `Task<'T voption>` that completes with `ValueNone` before the first value. As
on a [memo](getting-started.md#the-previous-value), a run that suspends on a pending source or fails
leaves the previous value unchanged.

Read every input, then await `previous.Settled`. Under `CancelPrevious` and `KeepLatest`, `Settled`
is complete when the body runs, and only the newest flight's result becomes a previous value.
Under `Queue`, `Settled` completes when the flight started before this one is applied, so the
flights fold in start order. An await on it suspends, and reads after it are untracked. If the
earlier flight fails or is dropped, `Settled` returns the value published before it. Disposing the
memo completes a pending `Settled` with the value last published.

`Settled` creates its task when first read: one completed task, shared by every read until the memo
next publishes, or under `Queue`, one pending task for a flight that waits on an earlier one. The
async scenarios in the [counter bench](../benchmarks/counters.md) run within 0.1 % of their earlier
instruction counts, with the same allocation.

Each page below appends to the list the previous flight produced. The second page answers first,
and the second flight still waits for the first:

```fsharp
let queueGraph =
    new Graph (
        { GraphOptions.Default with
            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
            FlightPolicy = FlightPolicy.Queue }
    )

let page = queueGraph.Run (fun () -> createSignal 1)
let replies = Array.init 2 (fun _ -> TaskCompletionSource<string list> ())

let feed =
    queueGraph.Run (fun () ->
        createAsync (fun previous _ ->
            let n = page.Value // tracked: read before the first await

            task {
                let! items = replies[n - 1].Task
                let! earlier = previous.Settled
                return ValueOption.defaultValue [] earlier @ items
            }))

let pages = ResizeArray<string list> ()
queueGraph.Run (fun () -> createEffect (fun () -> pages.Add feed.Value))

page.Value <- 2
replies[1].SetResult [ "c"; "d" ]
replies[0].SetResult [ "a"; "b" ]
pumpUntil queueGraph (fun () -> pages.Count = 2) (TimeSpan.FromSeconds 5.)
List.ofSeq pages
```

```text
[["a"; "b"]; ["a"; "b"; "c"; "d"]]
```

### Bodies written with cancellableTask

On .NET, the `cancellableTask` builder from [IcedTasks](https://github.com/TheAngryByrd/IcedTasks)
builds a `CancellationToken -> Task<'T>`: write the body as `fun previous -> cancellableTask { ... }`, or
apply it to the token as below. Its `let!` and `do!` pass
the flight's token to any `CancellationToken -> Task` they bind, and each bind throws once the
token is cancelled. Under `CancelPrevious`, a superseded flight stops at its next bind and settles
as cancelled, so it publishes nothing.

```fsharp
open IcedTasks

let http = new Net.Http.HttpClient (BaseAddress = Uri "https://example.com")

let fetchProfile (id: int) (token: CancellationToken) : Task<string> =
    http.GetStringAsync ($"/users/%d{id}", token)

let profile =
    createAsync (fun _ token ->
        (cancellableTask {
            let id = userId.Value // tracked: read before the first bind that suspends
            let! body = fetchProfile id
            return $"user %d{id}: %s{body}"
        }) token)
```

Build the `cancellableTask` inside the function, once per flight. When the compiler cannot turn
the builder into a static state machine, as in Debug builds, the invocations of a single
`cancellableTask` value share their resumption state: a flight started while an earlier one is
suspended resumes at the earlier flight's `await` and blocks on it. A body bound once, as in
`let body = cancellableTask { ... }` then `createAsync (fun _ -> body)`, can pass every test in Release
and hang in Debug.

The tracking and purity rules of the body are unchanged: the builder runs synchronously up to its
first bind that suspends. IcedTasks targets .NET only; a body shared with Fable stays a `task`.

## Failures

A failure is a settled outcome, not a slow success. A memo whose source fails settles as `Failed`,
and its readers see the error, not `Pending`. The exception survives unchanged through every memo
between the source and the reader.

A throwing effect does not stop the flush. Every effect queued behind it still runs. `createEffect`
returns `unit`, so an effect's error is readable only on an `Effect` constructed directly with
`new Effect (graph, body)`, through its `Status` and `Error`:

```fsharp
let effectGraph = newGraph ()
let count = effectGraph.Run (fun () -> createSignal 1)
let seen = ResizeArray<int> ()
let failing = new Effect (effectGraph, (fun () -> failwithf "boom %d" count.Value))
effectGraph.Run (fun () -> createEffect (fun () -> seen.Add count.Value))

count.Value <- 2
failing.Status, failing.Error.Message, List.ofSeq seen
```

```text
(Error, "boom 2", [1; 2])
```

## Boundaries

A boundary is a computation that stops a channel. It runs a body, and when the body suspends or
throws, it substitutes a value of the same type. Its readers see an ordinary value. A boundary is
control flow over the graph; what the value represents is the caller's choice.

The body always re-runs from the start. The suspended read linked its edge before throwing, so the
source settling wakes the boundary.

| Constructor | Pending body | Failed body |
|-------------|--------------|-------------|
| `createSuspense fallback body` | shows `fallback ()` | propagates as `Failed` |
| `createErrorBoundary recover body` | propagates as `Pending` | shows `recover ex` |
| `createBoundary fallback recover body` | shows `fallback ()` | shows `recover ex` |

A boundary catches what its body reads, directly or through memos. A node the body creates and does
not read is outside its reach: an effect created in the body that suspends or fails leaves the
boundary showing the body's value.

A boundary owns the nodes its body creates and replaces them on every re-run. An async value created
and read in the body restarts its flight on every settle, and the boundary shows the fallback
forever. Create the async value outside the boundary and read it in the body; see
[Troubleshooting](troubleshooting.md#a-boundary-shows-its-fallback-forever-and-starts-a-flight-on-every-settle).

All three return a `Boundary<'T>`. `IsWaiting` is `true` while a fallback stands in for the body, and
`Caught` holds the exception a `recover` handled on the current run, or `null`. Both are tracked reads that bring the
boundary current, so an effect reading only `IsWaiting` wakes when the body settles.

### Stale while refreshing, and empty versus not yet known

A fallback receives the boundary's last value, `ValueNone` before the first. A fallback that returns it
keeps the last result on screen while a new flight is in progress, and `IsWaiting` reports the refresh.
Choose a value type that separates a result not known yet from a result that is empty:

```fsharp
type Results =
    | NotYetKnown
    | Loaded of string list

let searchGraph = newGraph ()
let query = searchGraph.Run (fun () -> createSignal "a")
let replies = ResizeArray<TaskCompletionSource<string list>> ()

let hits =
    searchGraph.Run (fun () ->
        createAsync (fun _ _ ->
            query.Value |> ignore // tracked: read before the first await
            let reply = TaskCompletionSource<string list> ()
            replies.Add reply
            reply.Task))

let results =
    searchGraph.Run (fun () ->
        createSuspense (fun last -> ValueOption.defaultValue NotYetKnown last) (fun () -> Loaded hits.Value))

let snapshot () = results.Value, results.IsWaiting

let unknown = snapshot ()
replies[0].SetResult []
let empty = snapshot ()
query.Value <- "ab"
let refreshing = snapshot ()
replies[1].SetResult [ "abc" ]
unknown, empty, refreshing, snapshot ()
```

```text
((NotYetKnown, true), (Loaded [], false), (Loaded [], true), (Loaded ["abc"], false))
```

The three states of Uno MVUX's `Option<T>` map to `NotYetKnown`, `Loaded []` and `Loaded items`, and its
progress axis maps to `IsWaiting`. `createBoundary`'s `recover` receives the last value too, so a failed
refresh can keep the stale list beside the error in `Caught`. Outside a boundary, `AsyncMemo.Peek` reads
the last settled value, or the default before the first, untracked and without starting a flight.

### createSuspense

The boundary shows the fallback while the body is pending, and shows the body's value once every
source the body waited on has settled. A failure passes through as `Failed`.

```fsharp
let viewGraph = newGraph ()
let data = viewGraph.Run (fun () -> createAsyncSource<string> ())

let view =
    viewGraph.Run (fun () -> createSuspense (fun () -> "loading") (fun () -> "loaded " + data.Value))

let waiting = view.TryValue, view.IsWaiting, view.Status
data.Settle "report"
waiting, (view.TryValue, view.IsWaiting)
```

```text
((Ready "loading", true, None), (Ready "loaded report", false))
```

In the map, the effect reads `view` and runs with the fallback while `data` is pending. **Settle** runs the body again with the value; **Fail** passes the failure through the boundary to the effect.

```fsharp map
let data = createAsyncSource<string> ()
let view = createSuspense (fun _ -> "loading") (fun () -> "loaded " + data.Value)
createEffect (fun () -> printfn "%s" view.Value)
let offline = exn "offline"

controls [
    button "Settle" (fun () -> data.Settle "report")
    button "Fail" (fun () -> data.Fail offline)
]
```

A fallback may read reactive values itself. A fallback that suspends leaves the boundary `Pending`,
and a fallback that throws leaves it `Failed`.

### createErrorBoundary

The boundary shows `recover ex` when the body throws, and shows the body's value again when a source
the body read changes and the re-run succeeds. A pending body passes through as `Pending`.

```fsharp
let parseGraph = newGraph ()
let input = parseGraph.Run (fun () -> createSignal "42")

let parsed =
    parseGraph.Run (fun () -> createErrorBoundary (fun _ -> -1) (fun () -> int input.Value))

input.Value <- "forty-two"
let recovered = parsed.TryValue, parsed.Caught.GetType().Name
input.Value <- "7"
recovered, (parsed.TryValue, isNull parsed.Caught)
```

```text
((Ready -1, "FormatException"), (Ready 7, true))
```

A `recover` that throws leaves the boundary `Failed` with the exception `recover` threw, so
re-raising narrows the boundary to the errors it handles.

### createBoundary

The boundary catches both channels: `fallback ()` while the body is pending, `recover ex` when it
throws, and the body's value once it succeeds.

```fsharp
let bothGraph = newGraph ()
let feed = bothGraph.Run (fun () -> createAsyncSource<int> ())

let panel =
    bothGraph.Run (fun () -> createBoundary (fun () -> 0) (fun _ -> -1) (fun () -> feed.Value))

let loading = panel.TryValue
feed.Fail (exn "offline")
let broken = panel.TryValue
feed.Settle 9
loading, broken, panel.TryValue
```

```text
(Ready 0, Ready -1, Ready 9)
```

## Threading and dispatch

A graph belongs to the thread that constructed it. The rules:

| Operation | Contract |
|-----------|----------|
| `Signal.Value <- v` | Guarded. Under `ThreadAffinity = Guarded` (the default) a write from another thread raises `InvalidOperationException` and the value is unchanged. An equal write raises too. |
| `AsyncSource.Settle` / `Fail`, a completing `AsyncMemo` flight | Marshalled through `Graph.Dispatch`. Under `ImmediateDispatcher` and `Guarded` affinity, an off-thread call raises `Pump ran on thread` instead (see [Dispatcher selection](#dispatcher-selection)). |
| `Graph.Dispatch work` | Runs `work` inline on the graph thread. From another thread, queues it in the graph's inbox and asks the dispatcher to wake the graph thread. |
| `Graph.Pump ()` | Guarded. Runs the inbox in arrival order, then flushes effects, and returns the number of items run. An item that throws is recorded in `graph.Root.Errors`, and the rest still run. |
| `Graph.PendingWork` | The number of inbox items waiting for a pump. |
| `.Value`, `TryValue`, `Peek` | Not guarded. |
| `Graph.Current` | Thread-static. The `create*` functions on another thread raise `No ambient graph on this thread` unless that thread activated a graph. |

`ThreadAffinity = Unchecked` removes the guard. Use it only when every write is known to arrive on
one thread.

The write guard's message names both threads:

```text
A signal write ran on thread 12, but this graph is owned by thread 1. Marshal through Graph.Dispatch,
or set GraphOptions.ThreadAffinity to Unchecked if affinity is guaranteed some other way.
```

### Dispatcher selection

`GraphOptions.Dispatcher` sets how the graph thread is woken when work arrives from another thread:

| `Dispatcher` | Behaviour |
|--------------|-----------|
| `None` (default), with a `SynchronizationContext` current at construction | A `SynchronizationContextDispatcher` posts the drain to that context. WPF, WinForms and Avalonia UI threads work this way with no further setup. |
| `None` (default), with no `SynchronizationContext` | `ManualDispatcher`. |
| `Some (ManualDispatcher ())` | The inbox is drained only when the graph thread calls `graph.Pump ()`. |
| `Some (ImmediateDispatcher ())` | Calls `Pump` on the thread that posted. Under `ThreadAffinity = Guarded` (the default), an off-thread post raises `Pump ran on thread ...` on that thread and the work stays queued. Under `Unchecked`, the drain runs on the posting thread. |

A console app, a server or a test has no `SynchronizationContext`, so it gets `ManualDispatcher`.
Under `ManualDispatcher`, an off-thread settle becomes visible only after `graph.Pump ()`. That
includes an `AsyncMemo` flight whose task completes on the thread pool:

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

```text
(Pending, (Pending, 1), 1, Ready 42)
```

> **Caution: `ImmediateDispatcher` with off-thread settles.** Under the default
> `ThreadAffinity = Guarded`, an off-thread settle with `ImmediateDispatcher` raises
> `Pump ran on thread ...` on the settling thread and leaves the work queued until the graph thread
> calls `graph.Pump ()`. Under `Unchecked`, the drain runs on the settling thread and mutates the
> graph there, which is safe only when every write already arrives on one thread.
> When the settle is an `AsyncMemo` flight completing on the thread pool, the exception is raised in
> the flight's continuation and reaches `TaskScheduler.UnobservedTaskException`.

## Common mistakes

> **A console app that never calls `Pump`.** The graph gets `ManualDispatcher`, the flight completes
> on the thread pool, and the node stays `Pending`. Call `graph.Pump ()` from the graph thread (in a
> loop, or after awaiting the work), or construct the graph on a thread with a
> `SynchronizationContext`.

> **Expecting a read after an `await` to be tracked.** A signal read after an `await` that suspends
> does not start a new flight when it changes. Read it before the first `await`.

For the exception messages these produce, see [Troubleshooting](troubleshooting.md).

## Key types

- `Status`: the pending, error and uninitialized flags.
- `Reading<'T>`: the result of `TryValue`.
- `NotReadyException`: raised by a
  `.Value` read of a pending node.
- `AsyncSource<'T>`: a source settled by hand.
- `AsyncMemo<'T>`: a memo computed by a task.
- `Boundary<'T>`: suspense, error and catching
  boundaries.
- `FlightPolicy`: what happens to a superseded
  flight.
- `IGraphDispatcher`,
  `ManualDispatcher` and
  `ImmediateDispatcher`: how off-thread
  work reaches the graph thread.
