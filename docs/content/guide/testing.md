---
title: Testing async state
order: 11
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Control when each async request completes, then assert its `Pending`, `Ready` or `Failed` state.
Complete requests on the test thread to keep the tests deterministic.

For the behaviour being tested, see [Async and pending](async-and-pending.md).

:::details Runnable examples
Every sample on this page is an [Expecto](https://github.com/haf/expecto) test, and each one runs in
the Ranvier suite as `tests/Ranvier.Tests/TestingGuide.fs`.
:::

```fsharp
open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier
```

## The setup

Use a `ManualDispatcher`. A request completed on the test thread applies immediately; work from
another thread waits until the test calls `graph.Pump ()`.

:::details Graph helper

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

:::

Replace the remote service with a fake returning a `TaskCompletionSource` task. Record each
request's input and cancellation token, then complete its `Reply` when the test chooses.

:::details Fake search service

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

:::

:::tip Complete continuations on the test thread
Create `TaskCompletionSource` without `TaskCreationOptions.RunContinuationsAsynchronously`.
That option sends the continuation to the thread pool, so the result waits for a pump.
:::

## Landing each flight

Assert `TryValue` directly with `Expect.equal`. Its `Reading<'T>` supports structural equality;
a `Failed` reading compares the original exception by reference.

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

:::details Test an AsyncSource directly

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

:::

## Superseded flights

Start a second flight before the first lands, then land them in the order under test. Under
`CancelPrevious`, the first flight's token is cancelled and its late result is discarded:

:::details Check cancellation and a late result

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

:::

Under `Queue`, results apply in start order even when they complete in reverse order.

:::details Check queued results

The effect records applied values and starts the second flight on its re-run. Complete the second
request first, assert that it waits, then complete the first:

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

:::

## Boundaries

A boundary's `Value` and `IsWaiting` are ordinary reads. This boundary keeps its last value while a
refresh is in progress:

:::details Assert the value and loading state together

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

:::

## Work on other threads

Prefer a fake completed by the test. It controls result order without involving the thread pool.

:::details When the test must use real IO
Wait until `graph.PendingWork` is above zero, then call `graph.Pump ()` on the test thread to apply
the queued result. See [Dispatcher selection](async-and-pending.md#dispatcher-selection).
:::

:::warning Keep the test on the graph's thread
Write each test as a synchronous `testCase`. In a `task` test, the code after an `await` can
resume on another thread, and the graph's thread guard raises on the next write.
:::

## Time

A Ranvier graph reads no clock, so these tests need no timer to fake. Debounce and throttle are
under consideration in the [roadmap](../concepts/roadmap.md); neither is currently a flight policy
or combinator.
