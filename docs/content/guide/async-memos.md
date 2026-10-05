---
title: Async memos
---

`createAsync` returns an `AsyncMemo<'T>`. Its first read starts an async request, called a **flight**.
The memo stays pending until the task completes. A tracked input change starts another flight on
the next read.

`Peek` returns the last settled value without starting a flight. The compute function takes
`Previous<'T>` and a `CancellationToken`, and returns `Task<'T>`.

`createAsync` is pure; `createAsyncWith` owns the nodes and cleanups created before its first
suspending await. Their scopes follow the same rules as [pure and owning memos](memos.md#pure-and-owning-memos).

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

[Threading and dispatch](threading.md) explains when a pump is required.

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

let show (reading: Reading<'T>) =
    match reading with
    | Ready value -> sprintf "Ready %A" value
    | Pending -> "Pending"
    | Failed error -> sprintf "Failed %s: %s" (error.GetType().Name) error.Message

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
so that `CancelPrevious` requests cancellation of superseded IO. The operation must cooperate with
the token; cancellation does not guarantee that work or external side effects stop.

Compare this queued replay with the default-policy map below. Three flights start; the
newest answer waits until both older answers can be applied in order.

```fsharp map replay code=collapsed policy=queue
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

The following names describe related scheduling choices in
[R3](https://github.com/Cysharp/R3#asyncawait-support) and
[SignalsDotnet](https://github.com/fedeAlterio/SignalsDotnet). They are scheduling parallels, not
interchangeable APIs or identical state semantics: stream operators and computed signals do not share
Ranvier's demand-driven flights, `Previous.Settled` or propagating pending channel.

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
`CommandPolicy.Disable` provides analogous command gating to
[`AllowConcurrentExecutions = false`](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand#handling-concurrent-executions);
this is not an async-memo drop policy. See [C#](csharp.md#commands).
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

```fsharp map replay code=collapsed
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
on a [memo](memos.md#the-previous-value), a run that suspends on a pending source or fails
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

:::details Disposal while pending
A disposed async memo reads as `Failed` with an `ObjectDisposedException`. Its readers are woken once to see the failure.
:::


:::warning Expecting a read after an `await` to be tracked

A signal read after an `await` that suspends
does not start a new flight when it changes. Read it before the first `await`.
:::
