---
title: Testing async state
order: 11
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

A test of async state decides when each flight lands. It completes each flight by hand, on the
test's own thread, and reads `Pending`, `Ready` and `Failed` as values. The test needs no timers,
sleeps or polling. For the behaviour these tests check, see [Async and pending](async-and-pending.md).

Every sample on this page is an [Expecto](https://github.com/haf/expecto) test, and each one runs in
the Ranvier suite as `tests/Ranvier.Tests/TestingGuide.fs`.

```fsharp
open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier
```

## The setup

Build the graph with an explicit `ManualDispatcher`. The test thread owns the graph, and a flight
completed on that thread applies at once. A result that arrives from another thread waits in the
graph's inbox until the test calls `graph.Pump ()`, so no background thread changes the graph
while an assertion runs.

```fsharp
/// A graph under `policy` whose off-thread work waits for Pump.
let newGraph (policy: FlightPolicy) =
    new Graph (
        { GraphOptions.Default with
            Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
            FlightPolicy = policy
        }
    )
```

Replace the remote service with one that records each request and returns a
`TaskCompletionSource` task. Each call is one flight. The test lands it by completing `Reply`, and
reads the input and the cancellation token the flight received.

```fsharp
/// A request to the fake service. The test lands it by completing `Reply`.
type Call<'T> =
    {
        Query: string
        Token: CancellationToken
        Reply: TaskCompletionSource<'T>
    }

/// A search over a fake service. `calls` holds one call per flight, in start order.
let search (graph: Graph) =
    graph.Run (fun () ->
        let query = createSignal "a"
        let calls = ResizeArray<Call<string list>>()

        let hits =
            createAsync (fun _ token ->
                let call =
                    {
                        Query = query.Value
                        Token = token
                        Reply = TaskCompletionSource<string list>()
                    }

                calls.Add call
                call.Reply.Task)

        query, calls, hits)
```

Create the `TaskCompletionSource` without `TaskCreationOptions.RunContinuationsAsynchronously`.
With that option the continuation runs on the thread pool, and the result waits for a pump.

## Landing each flight

`TryValue` returns a `Reading<'T>` with structural equality, so each state is one `Expect.equal`. A
`Failed` reading holds the original exception and compares it by reference.

```fsharp
testCase "the test decides when each flight lands"
<| fun () ->
    use graph = newGraph FlightPolicy.CancelPrevious
    let query, calls, hits = search graph

    Expect.equal hits.TryValue Pending "the first read starts a flight"
    calls[0].Reply.SetResult [ "ab"; "ac" ]
    Expect.equal hits.TryValue (Ready [ "ab"; "ac" ]) "the flight landed"

    query.Value <- "x"
    Expect.equal hits.TryValue Pending "a new query starts a new flight"
    Expect.equal calls[1].Query "x" "the flight read the new query"
    calls[1].Reply.SetException(TimeoutException "timed out")

    match hits.TryValue with
    | Failed e -> Expect.equal e.Message "timed out" "the fault is the original exception"
    | other -> failtestf "expected Failed, got %A" other
```

An `AsyncSource` needs no fake: `Settle` and `Fail` land it directly.

```fsharp
testCase "an async source settles and fails by hand"
<| fun () ->
    use graph = newGraph FlightPolicy.CancelPrevious

    let user, greeting =
        graph.Run (fun () ->
            let user = createAsyncSource<string>()
            user, createMemo (fun _ -> "Hello, " + user.Value))

    Expect.equal greeting.TryValue Pending "nothing settled yet"
    let offline = exn "offline"
    user.Fail offline
    Expect.equal greeting.TryValue (Failed offline) "the failure reaches the memo"
    user.Settle "Ada"
    Expect.equal greeting.TryValue (Ready "Hello, Ada") "a settle clears the failure"
```

## Superseded flights

Start a second flight before the first lands, then land them in the order under test. Under
`CancelPrevious`, the first flight's token is cancelled and its late result is discarded:

```fsharp
testCase "CancelPrevious discards the superseded flight"
<| fun () ->
    use graph = newGraph FlightPolicy.CancelPrevious
    let query, calls, hits = search graph

    hits.TryValue |> ignore
    query.Value <- "b"
    hits.TryValue |> ignore
    Expect.isTrue calls[0].Token.IsCancellationRequested "the superseded token is cancelled"

    calls[0].Reply.SetResult [ "a1" ]
    Expect.equal hits.TryValue Pending "the superseded result is discarded"
    calls[1].Reply.SetResult [ "b1" ]
    Expect.equal hits.TryValue (Ready [ "b1" ]) "the newest flight lands"
```

Under `Queue`, every result is applied in start order. An effect records each value it sees. Here
the effect's re-run starts the second flight, and the second result lands first:

```fsharp
testCase "Queue applies every flight in start order"
<| fun () ->
    use graph = newGraph FlightPolicy.Queue
    let query, calls, hits = search graph
    let seen = ResizeArray<string list>()
    graph.Run (fun () -> createEffect (fun () -> seen.Add hits.Value))

    query.Value <- "b"
    calls[1].Reply.SetResult [ "b1" ]
    Expect.isEmpty seen "the second result waits for the first"
    calls[0].Reply.SetResult [ "a1" ]
    Expect.equal (List.ofSeq seen) [ [ "a1" ]; [ "b1" ] ] "both results, in start order"
```

## Boundaries

A boundary's `Value` and `IsWaiting` are ordinary reads. This boundary keeps its last value while a
refresh is in progress:

```fsharp
testCase "a boundary shows its fallback while a flight is in progress"
<| fun () ->
    use graph = newGraph FlightPolicy.CancelPrevious
    let query, calls, hits = search graph

    let results =
        graph.Run (fun () -> createSuspense (fun last -> ValueOption.defaultValue [] last) (fun () -> hits.Value))

    Expect.equal (results.Value, results.IsWaiting) ([], true) "the fallback before the first result"
    calls[0].Reply.SetResult [ "a1" ]
    Expect.equal (results.Value, results.IsWaiting) ([ "a1" ], false) "the body's value"
    query.Value <- "b"
    Expect.equal (results.Value, results.IsWaiting) ([ "a1" ], true) "the last value while refreshing"
```

## Work on other threads

Code under test that awaits real IO completes its flight on the thread pool. Under
`ManualDispatcher` the result waits in the inbox: wait until `graph.PendingWork` is above zero, then
call `graph.Pump ()` on the test thread. [Dispatcher selection](async-and-pending.md#dispatcher-selection)
shows the sequence. Prefer a fake that the test completes, as above: the test then decides the
order of results, and the thread pool plays no part.

Write each test as a synchronous `testCase`. In a `task` test, the code after an `await` can
resume on another thread, and the graph's thread guard raises on the next write.

## Time

Ranvier has no time-based policies yet: debounce and throttle are not in the library, as a flight
policy or as a combinator; the [roadmap](../concepts/roadmap.md) lists them under consideration. A
Ranvier graph reads no clock, so a test has no timer to fake.
