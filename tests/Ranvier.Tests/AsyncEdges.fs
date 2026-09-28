module Ranvier.Tests.AsyncEdges

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>
/// A flight the test completes by hand, on its own thread, for the reason given
/// in <c>Async.fs</c>: handing the work to the pool would test the scheduler rather
/// than the semantics.
/// </summary>
type private Flight<'T>() =
    let source = TaskCompletionSource<'T>()
    member _.Task = source.Task

    member _.Settle(v: 'T) =
        source.SetResult v

    member _.Fail(e: exn) =
        source.SetException e

    member _.Cancel() =
        source.SetCanceled ()

/// <summary>Runs <c>work</c> to completion on a new thread, never on the caller's.</summary>
let private onAnotherThread (work: unit -> unit) =
    let thread = Thread work
    thread.Start ()
    thread.Join ()

/// <summary>
/// <c>Async.fs</c> covers the happy shapes of each <c>FlightPolicy</c>. This is what
/// happens at their edges: a flight that was already finished before anyone
/// read it, three supersessions deep, a queue whose middle entry fails, a
/// disposal landing between the start of a flight and its completion.
/// </summary>
/// <remarks>
/// These matter because a flight is the one thing in the graph whose lifetime
/// is not controlled by the graph. Everything else is torn down when its owner
/// says so; a <c>Task</c> finishes when it finishes, and the only question is what
/// state it finds the graph in when it does.
/// </remarks>
[<Tests>]
let tests =
    testList
        "AsyncEdges"
        [
            test "a flight that is already complete never makes the reader pending" {
                let g = new Graph ()

                let a = new AsyncMemo<int> (g, (fun _ _ -> Task.FromResult 42))

                // The continuation runs inline on an already-completed task, so
                // the value is there before the first read returns. A reader
                // that suspended here would be suspending on nothing.
                Expect.equal a.TryValue (Ready 42) "settled on the way in"
                Expect.equal a.Value 42 "and the throwing read does not throw"
            }

            test "an effect over an already-complete flight runs once per write" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let a = new AsyncMemo<int> (g, (fun _ _ -> Task.FromResult (s.Value * 10)))
                let seen = ResizeArray<int>()

                use _reader = new Effect (g, (fun () -> seen.Add a.Value))

                s.Value <- 1
                s.Value <- 2

                Expect.sequenceEqual seen [ 0; 10; 20 ] "one run per write"
            }

            test "a memo over an already-complete flight recomputes once per write" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let a = new AsyncMemo<int> (g, (fun _ _ -> Task.FromResult (s.Value * 10)))
                let m = Memo (g, (fun _ -> a.Value + 1))

                Expect.equal m.Value 1 "first read"
                let before = m.Runs
                s.Value <- 1
                Expect.equal m.Value 11 "recomputed"
                Expect.equal m.Value 11 "and read again"
                Expect.equal (m.Runs - before) 1 "once"
            }

            test "an already-faulted flight is Failed, not pending" {
                let g = new Graph ()

                let a =
                    new AsyncMemo<int> (g, (fun _ _ -> Task.FromException<int>(InvalidOperationException "nope")))

                match a.TryValue with
                | Failed ex -> Expect.stringContains ex.Message "nope" "the reason survived"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "three supersessions deep, only the last one publishes" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            trigger.Value |> ignore
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore

                for i in 1..3 do
                    trigger.Value <- i
                    a.TryValue |> ignore

                Expect.equal flights.Count 4 "one flight per run"

                // Settled out of order and oldest last, which is the case that
                // separates "ignores superseded flights" from "takes whatever
                // arrives most recently".
                flights[1].Settle 1
                flights[2].Settle 2
                flights[3].Settle 3
                flights[0].Settle 0

                Expect.equal a.TryValue (Ready 3) "the value is the newest flight's, not the newest arrival's"
            }

            test "a flight that settles from its own cancellation is discarded as superseded" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let flights = ResizeArray<TaskCompletionSource<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            let t = trigger.Value
                            let flight = TaskCompletionSource<int>()

                            // The common shape: a fallback on cancellation, completed
                            // inline by `Cancel`.
                            token.Register (fun () -> flight.TrySetResult (-1 - t) |> ignore)
                            |> ignore

                            flights.Add flight
                            flight.Task
                    )

                let runs = ref 0

                use _reader =
                    new Effect (
                        g,
                        fun () ->
                            incr runs
                            a.TryValue |> ignore
                    )

                flights[0].SetResult 7
                trigger.Value <- 1
                let before = runs.Value
                trigger.Value <- 2

                Expect.equal a.Peek 7 "the superseded flight's fallback is not published"
                Expect.equal a.Status Status.Pending "still waiting on the current flight"
                Expect.equal (runs.Value - before) 1 "one run for the write, none for the fallback"

                flights[2].SetResult 20
                Expect.equal a.TryValue (Ready 20) "the current flight publishes"
            }

            test "a flight that faults from its own cancellation does not mark the node failed" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let flights = ResizeArray<TaskCompletionSource<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            trigger.Value |> ignore
                            let flight = TaskCompletionSource<int>()

                            token.Register (fun () ->
                                flight.TrySetException (TimeoutException "superseded")
                                |> ignore)
                            |> ignore

                            flights.Add flight
                            flight.Task
                    )

                a.TryValue |> ignore
                flights[0].SetResult 7
                trigger.Value <- 1
                a.TryValue |> ignore
                trigger.Value <- 2
                a.TryValue |> ignore

                Expect.equal a.Status Status.Pending "no Error flag from the superseded flight"
                Expect.equal a.Peek 7 "the last settled value survives"
            }

            test "a current flight that cancels itself settles as Failed" {
                let g = new Graph ()
                let flight = Flight<int>()

                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))

                Expect.equal a.TryValue Pending "in flight"

                // The flight's own IO gave up, as an HttpClient timeout does. The
                // graph did not cancel it, and nothing will re-run it.
                flight.Cancel ()

                match a.TryValue with
                | Failed ex -> Expect.isTrue (ex :? OperationCanceledException) "the cancellation is the reason"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "a flight cancelled after its first await keeps the cancellation's exception" {
                let g = new Graph ()
                let gate = TaskCompletionSource<unit>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            task {
                                do! gate.Task
                                return raise (OperationCanceledException "http timeout")
                            }
                    )

                Expect.equal a.TryValue Pending "in flight"
                gate.SetResult ()

                match a.TryValue with
                | Failed ex -> Expect.equal ex.Message "http timeout" "as a synchronous throw of it would"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "a superseded flight's cancellation publishes nothing" {
                let g = new Graph ()
                let trigger = Signal (g, 0)

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ token ->
                            if trigger.Value = 0 then
                                let flight = TaskCompletionSource<int>()

                                token.Register (fun () -> flight.TrySetCanceled token |> ignore)
                                |> ignore

                                flight.Task
                            else
                                TaskCompletionSource<int>().Task
                    )

                a.TryValue |> ignore
                trigger.Value <- 1
                a.TryValue |> ignore

                Expect.equal a.TryValue Pending "the cancelled flight was not current"
                Expect.isFalse (a.Status.HasFlag Status.Error) "and did not fail the node"
            }

            test "the Queue policy applies a failure in the middle without losing what follows" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = Queue
                        }
                    )

                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            trigger.Value |> ignore
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore
                trigger.Value <- 1
                a.TryValue |> ignore
                trigger.Value <- 2
                a.TryValue |> ignore

                Expect.equal flights.Count 3 "three flights queued"

                flights[0].Settle 10
                Expect.equal a.TryValue (Ready 10) "the first applied"

                flights[1].Fail(InvalidOperationException "middle")

                match a.TryValue with
                | Failed ex -> Expect.stringContains ex.Message "middle" "the failure applied in its turn"
                | other -> failtestf "expected Failed, got %A" other

                flights[2].Settle 30
                Expect.equal a.TryValue (Ready 30) "and the one behind it still arrived"
            }

            test "the Queue policy applies a synchronous failure after the flight started before it" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = Queue
                        }
                    )

                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            if trigger.Value = 1 then
                                failwith "sync-fail"

                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore
                trigger.Value <- 1
                let early = a.TryValue

                flights[0].Settle 10

                match early, a.TryValue with
                | Pending, Failed ex -> Expect.equal ex.Message "sync-fail" "the newer run's failure is the last result"
                | e, other -> failtestf "expected Pending then Failed, got %A then %A" e other
            }

            test "the Queue policy keeps a run suspended on a source pending when an older flight lands" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = Queue
                        }
                    )

                let upstream = AsyncSource<int> g
                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            if trigger.Value = 1 then
                                Task.FromResult upstream.Value
                            else
                                let f = Flight<int>()
                                flights.Add f
                                f.Task
                    )

                a.TryValue |> ignore
                trigger.Value <- 1
                a.TryValue |> ignore

                flights[0].Settle 10

                Expect.equal a.TryValue Pending "the newest run is still waiting on its source"
                Expect.equal (Seq.length a.PendingSources) 1 "on that source"

                upstream.Settle 4
                Expect.equal a.TryValue (Ready 4) "and settles with it"
            }

            test "the Queue policy applies a synchronous failure after a flight settled off the graph thread" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = Queue
                        }
                    )

                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            if trigger.Value = 1 then
                                failwith "sync-fail"

                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore
                onAnotherThread (fun () -> flights[0].Settle 10)
                Expect.equal g.PendingWork 1 "the older flight's result waits in the inbox"

                trigger.Value <- 1
                Expect.equal a.TryValue Pending "the failure waits behind it"

                g.Pump () |> ignore

                match a.TryValue with
                | Failed ex -> Expect.equal ex.Message "sync-fail" "the newer run's failure is the last result"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "the Queue policy applies flights in start order when the older settles off the graph thread" {
                let g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = Queue
                        }
                    )

                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            trigger.Value |> ignore
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore
                trigger.Value <- 1
                a.TryValue |> ignore

                onAnotherThread (fun () -> flights[0].Settle 10)
                flights[1].Settle 11
                g.Pump () |> ignore

                Expect.equal a.TryValue (Ready 11) "the newer flight's value is the last applied"
            }

            test "a re-run after a failure clears the Error flag" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            trigger.Value |> ignore
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore
                flights[0].Fail(InvalidOperationException "x")
                Expect.equal a.Status Status.Error "failed"

                trigger.Value <- 1
                Expect.equal a.TryValue Pending "the retry is in flight"
                Expect.equal a.Status Status.Pending "and the stale failure is gone"
            }

            test "a re-run that suspends on a pending source clears the Error flag" {
                let g = new Graph ()
                let upstream = AsyncSource<int> g
                let fail = Signal (g, true)

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            if fail.Value then
                                Task.FromException<int>(InvalidOperationException "x")
                            else
                                Task.FromResult upstream.Value
                    )

                a.TryValue |> ignore
                Expect.equal a.Status Status.Error "failed"

                fail.Value <- false
                Expect.equal a.TryValue Pending "suspended on the source"
                Expect.equal a.Status Status.Pending "without the stale failure"
            }

            test "a flight settling after its node is disposed changes nothing" {
                let g = new Graph ()
                let flight = Flight<int>()

                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))

                a.TryValue |> ignore
                a.Dispose ()

                flight.Settle 99
                Expect.equal a.Peek 0 "the disposed node did not take the value"
            }

            test "disposing an async memo mid-flight fails its readers" {
                let g = new Graph ()
                let flight = Flight<int>()
                let mutable a = Unchecked.defaultof<AsyncMemo<int>>

                let owner =
                    g.CreateRoot (fun o ->
                        a <- new AsyncMemo<int> (g, (fun _ _ -> flight.Task))
                        o)

                let m = Memo (g, (fun _ -> a.Value + 1))
                let seen = ResizeArray<string>()

                use _reader =
                    new Effect (
                        g,
                        fun () ->
                            match m.TryValue with
                            | Failed ex -> seen.Add (ex.GetType().Name)
                            | other -> seen.Add $"%A{other}"
                    )

                Expect.sequenceEqual seen [ "Pending" ] "waiting on the flight"

                owner.Dispose ()

                let isDisposedFailure reading =
                    match reading with
                    | Failed (:? ObjectDisposedException) -> true
                    | _ -> false

                Expect.isTrue (isDisposedFailure a.TryValue) "the memo reads as disposed"
                Expect.isTrue (isDisposedFailure m.TryValue) "and so does its reader"
                Expect.sequenceEqual seen [ "Pending"; "ObjectDisposedException" ] "the effect was woken"

                flight.Settle 1
                Expect.isTrue (isDisposedFailure a.TryValue) "a late settle changes nothing"
            }

            test "disposing a settled async memo keeps its value" {
                let g = new Graph ()
                let a = new AsyncMemo<int> (g, (fun _ _ -> Task.FromResult 5))

                Expect.equal a.TryValue (Ready 5) "settled"
                a.Dispose ()
                Expect.equal a.TryValue (Ready 5) "still readable after disposal"
            }

            test "a flight settling after the whole graph is disposed changes nothing" {
                let g = new Graph ()
                let flight = Flight<int>()

                let a = new AsyncMemo<int> (g, (fun _ _ -> flight.Task))

                a.TryValue |> ignore
                g.Dispose ()

                // Nothing to assert a value about; the assertion is that this
                // does not throw out of a continuation, where there is no
                // caller to catch it and the process goes with it.
                flight.Settle 99
                Expect.equal a.Peek 0 "the settle landed nowhere, quietly"
            }

            test "a boundary over a superseded flight shows the fallback throughout" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            trigger.Value |> ignore
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                let b = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> -1))

                Expect.equal b.TryValue (Ready -1) "waiting on the first flight"

                trigger.Value <- 1
                Expect.equal b.TryValue (Ready -1) "and on the second"

                flights[0].Settle 10
                Expect.equal b.TryValue (Ready -1) "the superseded flight does not release it"

                flights[1].Settle 20
                Expect.equal b.TryValue (Ready 20) "the current one does"
            }

            test "AsyncSource.Fail rejects a null reason and stays pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g

                Expect.throwsT<ArgumentNullException> (fun () -> a.Fail null) "null is not a reason"
                Expect.equal a.TryValue Pending "the source was not failed"
            }

            test "an AsyncSource settled before anything reads it is never pending" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                a.Settle 5

                let m = Memo (g, (fun _ -> a.Value))
                Expect.equal m.Value 5 "the reader arrived after the fact and saw a plain value"
                Expect.equal m.Runs 1 "with no suspended attempt before it"
            }

            test "Runs counts body runs, including runs that start no flight" {
                let g = new Graph ()
                let up = AsyncSource<int> g
                let failing = Signal (g, true)
                let mutable flights = 0

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            let v = up.Value

                            if failing.Value then
                                failwith "sync"

                            flights <- flights + 1
                            Task.FromResult (v * 2)
                    )

                Expect.equal a.TryValue Pending "the body suspended on the source"
                Expect.equal (a.Runs, flights) (1, 0) "a suspended run counts, with no flight"
                up.Settle 1

                match a.TryValue with
                | Failed _ -> ()
                | other -> failtestf "expected Failed, got %A" other

                Expect.equal (a.Runs, flights) (2, 0) "a run that throws before its flight counts"
                failing.Value <- false
                Expect.equal a.TryValue (Ready 2) "the third run starts a flight"
                Expect.equal (a.Runs, flights) (3, 1) "one flight in three runs"
            }

            test "Peek on a flight in progress is the previous value, not the default" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let flights = ResizeArray<Flight<int>>()

                let a =
                    new AsyncMemo<int> (
                        g,
                        fun _ _ ->
                            trigger.Value |> ignore
                            let f = Flight<int>()
                            flights.Add f
                            f.Task
                    )

                a.TryValue |> ignore
                flights[0].Settle 7
                Expect.equal a.TryValue (Ready 7) "settled"

                trigger.Value <- 1
                Expect.equal a.TryValue Pending "in flight again"

                // The one read that never suspends, and the reason it exists:
                // showing the last known value while the next one is fetched is
                // the common case, not an edge.
                Expect.equal a.Peek 7 "Peek keeps the last settled value across the re-run"
            }

            test "a flight started inside a root is cancelled when that root is torn down" {
                let g = new Graph ()
                let observed = ref CancellationToken.None
                let flight = Flight<int>()

                let owner =
                    g.CreateRoot (fun owner ->
                        let a =
                            new AsyncMemo<int> (
                                g,
                                fun _ token ->
                                    observed.Value <- token
                                    flight.Task
                            )

                        a.TryValue |> ignore
                        owner)

                Expect.isFalse observed.Value.IsCancellationRequested "in flight"

                owner.Dispose ()
                Expect.isTrue observed.Value.IsCancellationRequested "tearing down the owner cancelled the flight"
            }
        ]
