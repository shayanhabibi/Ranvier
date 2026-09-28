module Ranvier.Tests.Async

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks
open Ranvier

/// <summary>
/// A flight the test completes by hand.
/// </summary>
/// <remarks>
/// Every task here is settled on the test's own thread, so the whole graph runs
/// in one consistency domain and the assertions are deterministic. Handing the
/// work to the thread pool would test the scheduler, not the semantics.
/// </remarks>
type private Flight<'T>() =
    // Deliberately *not* RunContinuationsAsynchronously: the continuation has
    // to run inline on the settling thread for the test to observe the result
    // without waiting on anything.
    let source = TaskCompletionSource<'T>()
    member _.Task = source.Task

    member _.Settle(v: 'T) =
        source.SetResult v

    member _.Fail(e: exn) =
        source.SetException e

[<Tests>]
let tests =
    testList
        "Async"
        [
            test "reading an unsettled flight suspends" {
                let g = new Graph ()
                let flight = Flight<int>()
                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))

                Expect.equal a.TryValue Pending "a flight in progress is pending"
                Expect.equal a.Runs 1 "the read started the flight"

                flight.Settle 10
                Expect.equal a.TryValue (Ready 10) "settling publishes"
                Expect.equal a.Runs 1 "and does not re-run the body"
            }

            test "a flight starts only when something reads it" {
                let g = new Graph ()
                let started = ref 0

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            incr started
                            Task.FromResult 1
                    )

                // Pull, not push: a memo nobody reads costs nothing, which is what
                // makes it safe to describe a request declaratively.
                Expect.equal started.Value 0 "construction must not fetch"
                a.TryValue |> ignore
                Expect.equal started.Value 1 "the read fetched"
            }

            test "dependencies read before the await are tracked" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let requested = ResizeArray ()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            let v = s.Value
                            requested.Add v
                            Task.FromResult (v * 10)
                    )

                Expect.equal a.TryValue (Ready 10) "first flight"
                s.Value <- 2
                Expect.equal a.TryValue (Ready 20) "the dependency change refetched"
                Expect.sequenceEqual requested [ 1; 2 ] "once per dependency value"
            }

            test "a continuation run inside another computation's body leaves that computation's edges alone" {
                let g = new Graph ()
                let before = Signal (g, 1)
                let after = Signal (g, "a")
                let gate = TaskCompletionSource<unit>()

                let a =
                    new AsyncMemo<string> (
                        g,
                        fun _ _ ->
                            let b = before.Value

                            task {
                                do! gate.Task
                                return $"%d{b}%s{after.Value}"
                            }
                    )

                a.TryValue |> ignore
                let effectRuns = ref 0

                // Completing the gate runs the flight's continuation inline,
                // inside the effect's body.
                use _settler =
                    new Effect (
                        g,
                        fun () ->
                            incr effectRuns

                            if effectRuns.Value = 1 then
                                gate.SetResult ()
                    )

                Expect.equal a.TryValue (Ready "1a") "the flight landed"
                Expect.equal after.ObserverCount 0 "the continuation's read linked no edge"

                after.Value <- "b"
                Expect.equal effectRuns.Value 1 "the effect did not read after"
                Expect.equal a.Runs 1 "nor did the memo"
            }

            test "a cleanup registered in a continuation run inside an effect belongs to the root" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let gate = TaskCompletionSource<unit>()
                let cleaned = ref false

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gate.Task
                                g.OnCleanup (fun () -> cleaned.Value <- true)
                                return 0
                            }
                    )

                a.TryValue |> ignore

                use _settler =
                    new Effect (
                        g,
                        fun () ->
                            trigger.Value |> ignore

                            if not gate.Task.IsCompleted then
                                gate.SetResult ()
                    )

                trigger.Value <- 1
                Expect.isFalse cleaned.Value "the effect's re-run did not run it"

                g.Dispose ()
                Expect.isTrue cleaned.Value "the graph's teardown did"
            }

            test "an effect flushed by a write in a continuation keeps its edges" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let gate = TaskCompletionSource<unit>()
                let seen = ResizeArray<int>()

                use _reader = new Effect (g, (fun () -> seen.Add s.Value))

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gate.Task
                                s.Value <- 1
                                return 0
                            }
                    )

                a.TryValue |> ignore
                gate.SetResult ()
                s.Value <- 2

                Expect.sequenceEqual seen [ 0; 1; 2 ] "the run inside the continuation tracked s"
            }

            test "a pending read in a continuation run inside an effect does not suspend the effect" {
                let g = new Graph ()
                let upstream = AsyncSource<int> g
                let gate = TaskCompletionSource<unit>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gate.Task

                                return
                                    try
                                        upstream.Value
                                    with NotReadyException _ ->
                                        -1
                            }
                    )

                a.TryValue |> ignore

                use settler =
                    new Effect (
                        g,
                        fun () ->
                            if not gate.Task.IsCompleted then
                                gate.SetResult ()
                    )

                Expect.equal a.TryValue (Ready -1) "the continuation saw the source pending"
                Expect.equal settler.Status Status.None "the effect ran to completion"
            }

            test "an effect run inside a continuation keeps the edges it reads after settling another flight" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let x = Signal (g, 0)
                let gateA = TaskCompletionSource<unit>()
                let gateB = TaskCompletionSource<unit>()
                let seen = ResizeArray<string>()

                let b =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gateB.Task
                                return 0
                            }
                    )

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gateA.Task
                                s.Value <- 1
                                return 0
                            }
                    )

                use _reader =
                    new Effect (
                        g,
                        fun () ->
                            let sv = s.Value

                            if sv = 1 && not gateB.Task.IsCompleted then
                                gateB.SetResult ()

                            seen.Add $"s=%d{sv} x=%d{x.Value}"
                    )

                b.TryValue |> ignore
                a.TryValue |> ignore
                gateA.SetResult ()
                Expect.equal x.ObserverCount 1 "the effect's read of x after the inline settle linked"

                x.Value <- 7
                Expect.sequenceEqual seen [ "s=0 x=0"; "s=1 x=0"; "s=1 x=7" ] "the effect re-ran for x"
            }

            test "a memo run inside a continuation keeps the edges it reads after settling another flight" {
                let g = new Graph ()
                let x = Signal (g, 0)
                let gateA = TaskCompletionSource<unit>()
                let gateB = TaskCompletionSource<unit>()

                let b =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gateB.Task
                                return 0
                            }
                    )

                let m =
                    Memo (
                        g,
                        fun _ ->
                            if not gateB.Task.IsCompleted then
                                gateB.SetResult ()

                            x.Value
                    )

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gateA.Task
                                return m.Value
                            }
                    )

                b.TryValue |> ignore
                a.TryValue |> ignore
                gateA.SetResult ()
                Expect.equal x.ObserverCount 1 "the memo's read of x after the inline settle linked"

                let runs = m.Runs
                x.Value <- 5
                Expect.equal m.Value 5 "the memo re-ran for x"
                Expect.equal m.Runs (runs + 1) "once"
            }

            test "a read after an await on a completed task is tracked" {
                let g = new Graph ()
                let after = Signal (g, "a")

                let a =
                    new AsyncMemo<string> (
                        g,
                        fun _ _ ->
                            task {
                                do! Task.CompletedTask
                                return after.Value
                            }
                    )

                Expect.equal a.TryValue (Ready "a") "the flight completed synchronously"
                Expect.equal after.ObserverCount 1 "the read linked an edge"

                after.Value <- "b"
                Expect.equal a.TryValue (Ready "b") "the memo re-ran for the read"
                Expect.equal a.Runs 2 "once"
            }

            test "a re-run is pending again, and Peek keeps the last value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let first = Flight<int>()
                let second = Flight<int>()

                let a =
                    new AsyncMemo<int> (g, (fun _ _ -> if s.Value = 1 then first.Task else second.Task))

                first.Settle 10
                Expect.equal a.TryValue (Ready 10) "settled"

                s.Value <- 2
                Expect.equal a.TryValue Pending "a refetch suspends readers again"
                Expect.equal a.Peek 10 "but the last settled value is still readable"

                second.Settle 20
                Expect.equal a.TryValue (Ready 20) "the second flight lands"
            }

            test "a superseded flight does not publish" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let first = Flight<int>()
                let second = Flight<int>()

                let a =
                    new AsyncMemo<int> (g, (fun _ _ -> if s.Value = 1 then first.Task else second.Task))

                Expect.equal a.TryValue Pending "first flight in progress"
                s.Value <- 2
                a.TryValue |> ignore // pull, which starts the second flight

                // The first flight lands late, carrying the value that belonged to
                // s = 1. Publishing it would pair a result with a dependency state
                // the graph has left — the same stale pairing the diamond pins.
                first.Settle 10
                Expect.equal a.TryValue Pending "the late first flight must be discarded"

                second.Settle 20
                Expect.equal a.TryValue (Ready 20) "only the current flight publishes"
            }

            test "CancelPrevious cancels the superseded token" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let tokens = ResizeArray<CancellationToken>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            // The read is what links the edge; without it the
                            // second pull is a cache hit and no flight starts.
                            s.Value |> ignore
                            tokens.Add token
                            Flight<int>().Task
                    )

                a.TryValue |> ignore
                s.Value <- 2
                a.TryValue |> ignore

                Expect.equal tokens.Count 2 "two flights"
                Expect.isTrue tokens[0].IsCancellationRequested "the superseded flight was cancelled"
                Expect.isFalse tokens[1].IsCancellationRequested "the current one was not"
            }

            test "a cancellableTask body stops a superseded flight at its next bind" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let first = Flight<int>()
                let second = Flight<int>()
                let tokens = ResizeArray<CancellationToken>()
                let flights = ResizeArray<Task<int>>()
                let finished = ResizeArray<int>()

                // One builder value per flight: under IcedTasks' dynamic fallback (Debug builds among them),
                // invocations of a single `cancellableTask` value share resumption state, and an overlapping
                // second flight resumes at the first flight's await.
                let body () =
                    cancellableTask {
                        let id = s.Value

                        let! v =
                            fun (token: CancellationToken) ->
                                tokens.Add token
                                if id = 1 then first.Task else second.Task

                        do! Task.CompletedTask
                        finished.Add id
                        return v
                    }

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            let flight = body () token
                            flights.Add flight
                            flight
                    )

                a.TryValue |> ignore
                s.Value <- 2
                a.TryValue |> ignore

                Expect.equal tokens.Count 2 "the read before the first bind is tracked"
                Expect.isTrue tokens[0].IsCancellationRequested "the bind received the superseded flight's token"

                first.Settle 10

                Expect.throws (fun () -> flights[0].Wait(TimeSpan.FromSeconds 5.) |> ignore) "the superseded flight ends"
                Expect.isTrue flights[0].IsCanceled "at its next bind, as cancelled"
                Expect.equal a.TryValue Pending "the superseded flight publishes nothing"

                second.Settle 20
                Expect.isTrue (flights[1].Wait(TimeSpan.FromSeconds 5.)) "the current flight completes"
                Expect.equal (List.ofSeq finished) [ 2 ] "only the current flight runs to its end"
                Expect.equal a.TryValue (Ready 20) "the current flight publishes"
            }

            test "KeepLatest leaves the superseded flight running" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = KeepLatest
                        }
                    )

                let s = Signal (g, 1)
                let tokens = ResizeArray<CancellationToken>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            // The read is what links the edge; without it the
                            // second pull is a cache hit and no flight starts.
                            s.Value |> ignore
                            tokens.Add token
                            Flight<int>().Task
                    )

                a.TryValue |> ignore
                s.Value <- 2
                a.TryValue |> ignore

                Expect.equal tokens.Count 2 "two flights"
                Expect.isFalse tokens[0].IsCancellationRequested "KeepLatest does not cancel; it discards the result"
            }

            test "Queue applies every result in the order the flights started" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = FlightPolicy.Queue
                        }
                    )

                let s = Signal (g, 1)
                let first = Flight<int>()
                let second = Flight<int>()

                let a =
                    new AsyncMemo<int> (g, (fun _ _ -> if s.Value = 1 then first.Task else second.Task))

                a.TryValue |> ignore
                s.Value <- 2
                a.TryValue |> ignore

                // The second flight lands first. Under Queue its result must wait
                // for the one that started before it, so the graph never shows a
                // later value being overtaken by an earlier one.
                second.Settle 20
                Expect.equal a.TryValue Pending "nothing may be applied before the first flight lands"

                first.Settle 10
                Expect.equal a.TryValue (Ready 20) "both applied, in start order, ending on the latest"
            }

            test "Queue shows an applied outcome while a later flight is in progress" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = FlightPolicy.Queue
                        }
                    )

                let s = Signal (g, 1)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            s.Value |> ignore
                            let flight = Flight<int>()
                            flights.Add flight
                            flight.Task
                    )

                a.TryValue |> ignore
                s.Value <- 2
                Expect.equal a.TryValue Pending "no outcome applied yet"
                flights[0].Settle 10
                Expect.equal a.TryValue (Ready 10) "the first outcome is readable while the second flight runs"
                s.Value <- 3
                Expect.equal a.TryValue Pending "a new run is pending until the next outcome"
                flights[1].Settle 20
                Expect.equal a.TryValue (Ready 20) "the second outcome, with the third flight in progress"
            }

            test "a failed flight settles as Failed" {
                let g = new Graph ()
                let flight = Flight<int>()
                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))

                Expect.equal a.TryValue Pending "precondition"
                flight.Fail (exn "boom")

                match a.TryValue with
                | Failed e -> Expect.equal e.Message "boom" "the AggregateException must be unwrapped"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "a body that throws before awaiting settles as Failed" {
                let g = new Graph ()
                let a = new AsyncMemo<int> (g, (fun _ _ -> failwith "no flight"))

                match a.TryValue with
                | Failed e -> Expect.equal e.Message "no flight" "a synchronous throw is a failure, not a flight"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "a body suspended on a pending source waits on the node, not a task" {
                let g = new Graph ()
                let upstream = AsyncSource<int>(g)

                let a = new AsyncMemo<int> (g, (fun _ _ -> Task.FromResult (upstream.Value * 2)))

                Expect.equal a.TryValue Pending "suspended before it could start a flight"

                Expect.sequenceEqual
                    (a.PendingSources |> Seq.map (fun n -> n.Id))
                    [ (upstream :> INode).Id ]
                    "and it records which source it is waiting on"

                upstream.Settle 5
                Expect.equal a.TryValue (Ready 10) "the settle lets the flight start"
            }

            test "pending propagates into a memo and an effect" {
                let g = new Graph ()
                let flight = Flight<int>()
                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))
                let doubled = Memo (g, (fun _ -> a.Value * 2))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add doubled.Value))
                |> ignore

                Expect.isEmpty seen "a suspended effect must not run its side effect"
                Expect.equal doubled.TryValue Pending "pending crossed the memo"

                flight.Settle 21
                Expect.sequenceEqual seen [ 42 ] "the settle woke the whole chain exactly once"
            }

            test "disposing cancels the flight in progress" {
                let g = new Graph ()
                let tokens = ResizeArray<CancellationToken>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            tokens.Add token
                            Flight<int>().Task
                    )

                a.TryValue |> ignore
                a.Dispose ()

                Expect.isTrue tokens[0].IsCancellationRequested "disposal must not leave a request in flight"
            }

            test "a settle after disposal does not publish" {
                let g = new Graph ()
                let flight = Flight<int>()
                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))

                a.TryValue |> ignore
                a.Dispose ()
                flight.Settle 10

                Expect.equal a.Peek 0 "a disposed node must not be written to"
            }
        ]
