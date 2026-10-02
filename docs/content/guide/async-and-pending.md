---
title: Async and pending
order: 4
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Read async values, display loading and errors, and apply results on the graph's thread.
For the synchronous core, see [Getting started](getting-started.md); for pending rows, see
[Collections](collections.fsx).

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

Settle the source, fail it, then settle it again. The state travels through the memo;
the boundary supplies a display value for the effect.

```fsharp map replay show=output
let price = createAsyncSource<int> ()
let total = createMemo (fun _ -> price.Value * 3)
let view = createBoundary (fun _ -> "Loading") (fun ex _ -> "Error: " + ex.Message) (fun () -> sprintf "Total %d" total.Value)
createEffect (fun () -> printfn "%s" view.Value)

controls [
    button "Settle 4" (fun () -> price.Settle 4)
    |> describe "The source settles at 4; the boundary displays Total 12."
    |> expect "The source settles at 4; the boundary displays Total 12." (fun () -> view.Peek = "Total 12")
    button "Fail" (fun () -> price.Fail (exn "offline"))
    |> describe "The boundary catches the failed source and displays the error."
    |> expect "The boundary catches the failed source and displays the error." (fun () -> view.Peek = "Error: offline")
    button "Recover with 5" (fun () -> price.Settle 5)
    |> describe "A new settlement clears the error and displays Total 15."
    |> expect "A new settlement clears the error and displays Total 15." (fun () -> view.Peek = "Total 15")
]
```

:::details Catching pending reads and using untrack

A body that catches `NotReadyException` in its own `try/with` is still pending, whatever it returns.
A read inside `untrack` is the exception: the body's result stands. A pending read inside `untrack`
that the body does not catch leaves the body pending with no edge to the source, so settling the
source does not re-run it. Read the source tracked, or catch the exception.

:::

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

A pending read stops an effect's body at that read. The body runs again from the start when the
source settles:

::::details Test your understanding

Does this effect add a message before the name settles? What is in the log afterward?

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

:::details Answer

```text
(0, ["saw Grace"])
```

:::
::::

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

`createAsync` returns an `AsyncMemo<'T>`. Its first read starts an async request, called a **flight**.
The memo stays pending until the task completes. A tracked input change starts another flight on
the next read.

`Peek` returns the last settled value without starting a flight. The compute function takes
`Previous<'T>` and a `CancellationToken`, and returns `Task<'T>`.

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

:::details The pumpUntil helper

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

:::

[Threading and dispatch](#threading-and-dispatch) explains when a pump is required.

:::warning Read reactive inputs before awaiting

A read in the continuation runs
outside the memo's tracking context, so a later change to that source does not start a new flight.
Read every reactive value the flight depends on before the first `await`, and capture it in a
local. On .NET a continuation can run inline inside another computation's body, when that body
completes the awaited task. Its reads are untracked there too: they link no edge to that
computation.
:::

:::details Creating nodes in an async continuation

The same boundary applies to creation. `createAsync`'s purity check and `createAsyncWith`'s scope
cover the body up to its first `await` that suspends. A node created in a continuation, `onCleanup` included,
attaches to the owner current when the continuation runs, usually the graph's root, and lives
until that owner is disposed.
:::

:::details Failures and cancellation during a flight

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

:::

### Flight policy

`GraphOptions.FlightPolicy` sets what a change does while a flight is in progress:

| Policy | New flight at once | Old token cancelled | Superseded result discarded | Every result applied |
|--------|--------------------|---------------------|-----------------------------|----------------------|
| `CancelPrevious` (default) | Yes | Yes | Yes | No |
| `KeepLatest` | Yes | No | Yes | No |
| `Queue` | Yes | No | No | Yes, in the order the flights started |
| `FinishCurrent` | No, one trailing flight after it settles | No | No flight is superseded | No |

`CancelPrevious` and `KeepLatest` publish the same values. They differ only in whether the
superseded flight's `CancellationToken` is cancelled. Pass the token to the IO the flight performs,
so that `CancelPrevious` stops the superseded IO.

Compare this queued replay with the default-policy map below. Three flights start; the
newest answer waits until both older answers can be applied in order.

```fsharp map replay show=output policy=queue
let desk = Desk<int>(queued = true)
let page = createSignal 1
let result = createAsync (fun _ _ -> desk.Quote page.Value)
createEffect (fun () -> printfn "result = %d" result.Value)

controls [
    button "Page 2" (fun () -> page.Value <- 2)
    |> describe "Page 2 starts a second quote while the first remains pending."
    |> expect "Page 2 starts a second quote while the first remains pending." (fun () -> desk.Pending = 2)
    button "Page 3" (fun () -> page.Value <- 3)
    |> describe "Page 3 starts a third quote; all three await answers."
    |> expect "Page 3 starts a third quote; all three await answers." (fun () -> desk.Pending = 3)
    button "Answer newest: 30" (fun () -> desk.SettleNewest 30)
    |> describe "The newest answer waits for the two older flights."
    |> expect "The newest answer waits for the two older flights." (fun () -> desk.Pending = 2)
    button "Answer oldest: 10" (fun () -> desk.Settle 10)
    |> describe "The first answer applies; the newest still waits behind the second."
    |> expect "The first answer applies; the newest still waits behind the second." (fun () -> result.Peek = 10 && desk.Pending = 1)
    button "Answer next: 20" (fun () -> desk.Settle 20)
    |> describe "The second answer releases the queued third answer; the final result is 30."
    |> expect "The second answer releases the queued third answer; the final result is 30." (fun () -> result.Peek = 30 && desk.Pending = 0)
]
```

:::details Starting flights and observing exceptions

`CancelPrevious`, `KeepLatest` and `Queue` start a new flight as soon as a changed memo is read, and
decide only the fate of the flights already in progress. The memo observes every flight's exception, including a superseded
flight's and one raised after the memo or its graph was disposed, so `TaskScheduler.UnobservedTaskException`
receives none of them.

:::

:::details When to use FinishCurrent

`FinishCurrent` lets the flight in progress finish. A change during it runs no body and starts no
flight, and the memo stays pending. When the flight settles, the memo runs once more against the
current inputs, however many changes arrived: its value becomes the `Peek` value and the trailing
run's previous value, and a failure is discarded. The trailing run starts at the memo's next read,
so a memo read by an effect starts it as soon as the flight settles. A flight with no change during
it applies as under `KeepLatest`. Use it for work that must not be abandoned halfway, such as a
save, where the settled value must still match the latest inputs.

:::

:::details Compare policies with other .NET libraries

The same policies under the names other .NET libraries use. A name appears only where its behaviour
matches exactly:

| Ranvier | R3 `AwaitOperation` | SignalsDotnet `ConcurrentChangeStrategy` |
|---------|---------------------|------------------------------------------|
| `CancelPrevious` | `Switch` | `CancelCurrent` |
| `KeepLatest` | none (`Switch` without the cancellation) | none |
| `Queue` | `SequentialParallel` | none |
| `FinishCurrent` | none | none |

`FinishCurrent` is closest to SignalsDotnet's `ScheduleNext` and R3's `ThrottleFirstLast`. Both
publish the finished run's result; `FinishCurrent` keeps the memo pending until the trailing run
settles, so a published value always belongs to the current inputs.

Ranvier does not implement these policies:

| Policy | R3 `AwaitOperation` | SignalsDotnet `ConcurrentChangeStrategy` |
|--------|---------------------|------------------------------------------|
| Run one flight at a time, queue every change | `Sequential` | none |
| Run once more after the current run and publish both results | none | `ScheduleNext` |
| Ignore changes while a flight runs | `Drop` | none |
| Run every flight, apply results in completion order | `Parallel` | none |
| Run the first and the last change | `ThrottleFirstLast` | none |

CommunityToolkit's `AsyncRelayCommand` has no async-memo counterpart. `AllowConcurrentExecutions` is a
gate on `CanExecute` for a command with no result: `false` reports the command as not executable while
it runs, and `true` lets executions overlap. An async memo has no `CanExecute`: every change starts a
new flight, or under `FinishCurrent` a trailing one. For commands, C# has `ReactiveCommand`, whose
`CommandPolicy.Disable` matches `AllowConcurrentExecutions = false`; see [C#](csharp.md#commands).
Debounce and throttle are not implemented either, as a policy or as a combinator.

:::

:::details Cancellation costs

Cancellation costs one `CancellationTokenSource` per flight under `CancelPrevious`: each launch cancels
and disposes the superseded flight's source and allocates the next. Under `KeepLatest` and `Queue`,
overlapping flights share one source. Under `FinishCurrent` flights never overlap, and each flight
allocates one source, disposed without being cancelled when the flight settles. A change during
a flight allocates nothing. It is disposed, without being cancelled, once every flight that
holds it has settled, and the next flight allocates another; disposing the memo cancels it. A body
that ignores its token pays that allocation and a `Cancel` with no registered callbacks. A body that registers on the
token, as `HttpClient` does, also pays for the callbacks the `Cancel` runs. The token
belongs to the async memo alone: signals, memos and effects carry none, and their reads take no token.

:::

:::details Token registrations and lifetime

A registration on the token lives as long as its source. `use _ = token.Register ...`, and every API
that takes the token and completes (`Task.Delay`, `HttpClient`, `SemaphoreSlim.WaitAsync`,
`cancellableTask` binds), releases its registration when the operation ends. A registration left
undisposed is released with the source: under `CancelPrevious` at the next launch, under `FinishCurrent`
when its flight settles, under `KeepLatest` and `Queue` once the flights sharing the source have all settled, so a stream of overlapping flights
keeps each one's registration until the stream goes quiet. A flight that completes only on
cancellation stays in progress under `KeepLatest` and `Queue` until the memo is disposed, together
with everything its task and registrations hold; use `CancelPrevious` for such bodies. Work that
outlives its flight and keeps the token sees a disposed source once the source is released: it is
not cancelled when the memo is disposed, and `token.WaitHandle` throws `ObjectDisposedException`.
`FlightPolicyBenchmarks` in the [suspension bench](../benchmarks/suspension.md#flightpolicybenchmarks)
compares a launch under `CancelPrevious` with one under `KeepLatest`, and `FlightBenchmarks`
compares every policy, with and without changes during a flight.

:::

Try the default policy in the map. `Desk` keeps each request pending until you answer it:

- Press **Next user** twice to supersede a flight.
- Press **Answer** to settle the newest flight, or **Fail** to fail it.
- Use the timeline to step through the events.

```fsharp map replay show=output
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

:::details Reading queued outcomes while later flights run

Under `Queue` each outcome is readable as soon as it is applied, while later flights are still in
progress: a new run makes the memo pending until the next outcome is applied. A body that throws
before its first `await` fails in its turn, after the flights started before it. While the newest
run waits on a pending source, an earlier flight's value becomes the `Peek` value and the memo stays
pending.

:::

### The previous value

The body's first argument is a `Previous<'T>`: a handle on the value the memo last published. Its
one member, `Settled`, is a `Task<'T voption>` that completes with `ValueNone` before the first value. As
on a [memo](getting-started.md#the-previous-value), a run that suspends on a pending source or fails
leaves the previous value unchanged.

:::details Previous values under each flight policy

Read every input, then await `previous.Settled`. Under `CancelPrevious`, `KeepLatest` and
`FinishCurrent`, `Settled` is complete when the body runs. Under the first two, only the newest
flight's result becomes a previous value; under `FinishCurrent`, a trailing run receives the value of
the flight it waited for, or the value before it when that flight failed.
Under `Queue`, `Settled` completes when the flight started before this one is applied, so the
flights fold in start order. An await on it suspends, and reads after it are untracked. If the
earlier flight fails or is dropped, `Settled` returns the value published before it. Disposing the
memo completes a pending `Settled` with the value last published.

:::

:::details Cost of reading Settled

`Settled` creates its task when first read: one completed task, shared by every read until the memo
next publishes, or under `Queue`, one pending task for a flight that waits on an earlier one. The
async scenarios in the [counter bench](../benchmarks/counters.md) run within 0.1 % of their earlier
instruction counts, with the same allocation.

:::

Each page below appends to the list the previous flight produced. The second page answers first,
and the second flight still waits for the first:

::::details Test your understanding

The second page completes first. Which lists does the effect see, and in what order?

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

:::details Answer

```text
[["a"; "b"]; ["a"; "b"; "c"; "d"]]
```

:::
::::

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

:::warning Create a fresh cancellableTask for each flight

Build the `cancellableTask` inside the function, once per flight. When the compiler cannot turn
the builder into a static state machine, as in Debug builds, the invocations of a single
`cancellableTask` value share their resumption state: a flight started while an earlier one is
suspended resumes at the earlier flight's `await` and blocks on it. A body bound once, as in
`let body = cancellableTask { ... }` then `createAsync (fun _ -> body)`, can pass every test in Release
and hang in Debug.

:::

The tracking and purity rules of the body are unchanged: the builder runs synchronously up to its
first bind that suspends. IcedTasks targets .NET only; a body shared with Fable stays a `task`.

## Failures

A failure is a settled outcome, not a slow success. A memo whose source fails settles as `Failed`,
and its readers see the error, not `Pending`. The exception survives unchanged through every memo
between the source and the reader.

A throwing effect does not stop the flush. Every effect queued behind it still runs. `createEffect`
returns `unit`, so an effect's error is readable only on an `Effect` constructed directly with
`new Effect (graph, body)`, through its `Status` and `Error`:

::::details Test your understanding

If the first effect throws, does the second still run? Where is the exception stored?

```fsharp
let effectGraph = newGraph ()
let count = effectGraph.Run (fun () -> createSignal 1)
let seen = ResizeArray<int> ()
let failing = new Effect (effectGraph, (fun () -> failwithf "boom %d" count.Value))
effectGraph.Run (fun () -> createEffect (fun () -> seen.Add count.Value))

count.Value <- 2
failing.Status, failing.Error.Message, List.ofSeq seen
```

:::details Answer

```text
(Error, "boom 2", [1; 2])
```

:::
::::

## Boundaries

A boundary supplies a value when its body is pending or fails. Its readers see that fallback or
recovered value instead of the state it handles. Choose the value to suit your UI or computation.

The body always re-runs from the start. The suspended read linked its edge before throwing, so the
source settling wakes the boundary.

| Constructor | Pending body | Failed body |
|-------------|--------------|-------------|
| `createSuspense fallback body` | shows `fallback ()` | propagates as `Failed` |
| `createErrorBoundary recover body` | propagates as `Pending` | shows `recover ex` |
| `createBoundary fallback recover body` | shows `fallback ()` | shows `recover ex` |

:::details Which failures a boundary catches

A boundary catches what its body reads, directly or through memos. A node the body creates and does
not read is outside its reach: an effect created in the body that suspends or fails leaves the
boundary showing the body's value.

:::

:::warning Create async values outside the boundary

A boundary owns the nodes its body creates and replaces them on every re-run. An async value created
and read in the body restarts its flight on every settle, and the boundary shows the fallback
forever. Create the async value outside the boundary and read it in the body; see
[Troubleshooting](troubleshooting.md#a-boundary-shows-its-fallback-forever-and-starts-a-flight-on-every-settle).

:::

All three return a `Boundary<'T>`. `IsWaiting` is `true` while a fallback stands in for the body, and
`Caught` holds the exception a `recover` handled on the current run, or `null`. Both are tracked reads that bring the
boundary current, so an effect reading only `IsWaiting` wakes when the body settles.

### Stale while refreshing, and empty versus not yet known

A fallback receives the boundary's last value, `ValueNone` before the first. A fallback that returns it
keeps the last result on screen while a new flight is in progress, and `IsWaiting` reports the refresh.
Choose a value type that separates a result not known yet from a result that is empty:

::::details Test your understanding

What distinguishes an unknown result from an empty result? Which value remains visible during a refresh?

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

:::details Answer

```text
((NotYetKnown, true), (Loaded [], false), (Loaded [], true), (Loaded ["abc"], false))
```

:::
::::

:::details Other ways to read a previous result

The three states of Uno MVUX's `Option<T>` map to `NotYetKnown`, `Loaded []` and `Loaded items`, and its
progress axis maps to `IsWaiting`. `createBoundary`'s `recover` receives the last value too, so a failed
refresh can keep the stale list beside the error in `Caught`. Outside a boundary, `AsyncMemo.Peek` reads
the last settled value, or the default before the first, untracked and without starting a flight.

:::

### createSuspense

The boundary shows the fallback while the body is pending, and shows the body's value once every
source the body waited on has settled. A failure passes through as `Failed`.

::::details Test your understanding

While data is pending, is the boundary itself pending? What value does it publish?

```fsharp
let viewGraph = newGraph ()
let data = viewGraph.Run (fun () -> createAsyncSource<string> ())

let view =
    viewGraph.Run (fun () -> createSuspense (fun () -> "loading") (fun () -> "loaded " + data.Value))

let waiting = view.TryValue, view.IsWaiting, view.Status
data.Settle "report"
waiting, (view.TryValue, view.IsWaiting)
```

:::details Answer

```text
((Ready "loading", true, None), (Ready "loaded report", false))
```

:::
::::

In the map, the effect reads `view` and runs with the fallback while `data` is pending. **Settle** runs the body again with the value; **Fail** passes the failure through the boundary to the effect.

```fsharp map replay show=output
let data = createAsyncSource<string> ()
let view = createSuspense (fun _ -> "loading") (fun () -> "loaded " + data.Value)
createEffect (fun () -> printfn "%s" view.Value)
let offline = exn "offline"

controls [
    button "Settle" (fun () -> data.Settle "report")
    button "Fail" (fun () -> data.Fail offline)
]
```

:::details Pending or failed fallback computations

A fallback may read reactive values itself. A fallback that suspends leaves the boundary `Pending`,
and a fallback that throws leaves it `Failed`.

:::

### createErrorBoundary

The boundary shows `recover ex` when the body throws, and shows the body's value again when a source
the body read changes and the re-run succeeds. A pending body passes through as `Pending`.

Enter an invalid integer, then a valid one. The boundary displays its recovery value and
later returns to the parsed value without rebuilding the graph.

```fsharp map replay show=output
let input = createSignal "42"
let parsed = createMemo (fun _ -> int input.Value)
let view = createErrorBoundary (fun _ _ -> -1) (fun () -> parsed.Value)
createEffect (fun () -> printfn "parsed = %d" view.Value)

controls [
    textSignal "Input" input [ "forty-two"; "7" ]
    |> describe "Invalid input displays -1; entering 7 recovers without rebuilding the graph."
    |> expect "Invalid input displays -1; entering 7 recovers without rebuilding the graph." (fun () -> view.Peek = (if input.Peek = "7" then 7 else -1))
]
```

::::details Test your understanding

Which value replaces the invalid input? What happens to Caught when the input becomes valid?

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

:::details Answer

```text
((Ready -1, "FormatException"), (Ready 7, true))
```

:::
::::

:::details Handle selected error types

A `recover` that throws leaves the boundary `Failed` with the exception `recover` threw, so
re-raising narrows the boundary to the errors it handles.

:::

### createBoundary

The boundary catches both channels: `fallback ()` while the body is pending, `recover ex` when it
throws, and the body's value once it succeeds.

::::details Test your understanding

What does the panel show while loading, after a failure, and after the source settles?

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

:::details Answer

```text
(Ready 0, Ready -1, Ready 9)
```

:::
::::

## Threading and dispatch

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

## Common mistakes

:::warning A console app that never calls `Pump`

The graph gets `ManualDispatcher`, the flight completes
on the thread pool, and the node stays `Pending`. Call `graph.Pump ()` from the graph thread (in a
loop, or after awaiting the work), or construct the graph on a thread with a
`SynchronizationContext`.
:::

:::warning Expecting a read after an `await` to be tracked

A signal read after an `await` that suspends
does not start a new flight when it changes. Read it before the first `await`.
:::

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
