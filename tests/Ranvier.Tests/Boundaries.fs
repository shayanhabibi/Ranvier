module Ranvier.Tests.Boundaries

open System
open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.Tests.Readings

/// <summary>
/// Boundaries are control flow over the graph, so every case here is about
/// which channel stops where. Nothing renders.
/// </summary>
[<Tests>]
let tests =
    testList
        "Boundaries"
        [
            test "a suspense boundary substitutes the fallback" {
                let g = new Graph ()
                let a = AsyncSource<string>(g)

                let b = Boundary<string>.Suspense(g, (fun () -> a.Value), (fun _ -> "waiting"))

                Expect.equal b.TryValue (Ready "waiting") "the pending read was caught"
                Expect.isTrue b.IsWaiting "and the boundary says the value is a stand-in"
                Expect.equal b.Status Status.None "nothing downstream may learn it is pending"
            }

            test "the caught wait still wakes on the settle" {
                let g = new Graph ()
                let a = AsyncSource<string>(g)

                let b = Boundary<string>.Suspense(g, (fun () -> a.Value), (fun _ -> "waiting"))

                Expect.equal b.TryValue (Ready "waiting") "precondition"

                // Absorbing the channel must not drop the edge. The read that
                // suspended linked it before throwing, so this settle has to
                // bring the body back.
                a.Settle "done"
                Expect.equal b.TryValue (Ready "done") "the boundary re-ran"
                Expect.isFalse b.IsWaiting "and is no longer standing in"
                Expect.equal b.Runs 2 "the body ran again from the top"
            }

            test "a dependent never sees the pending channel" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let b = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> 0))

                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add b.Value))
                |> ignore

                // Without the boundary this effect would not have run at all:
                // that is the whole difference between a suspended subtree and a
                // suspended application.
                Expect.sequenceEqual seen [ 0 ] "the effect ran against the fallback"

                a.Settle 42
                Expect.sequenceEqual seen [ 0; 42 ] "and again on the settle"
            }

            test "a suspense boundary does not catch errors" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let b = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> 0))

                a.Fail (exn "boom")

                match b.TryValue with
                | Failed e -> Expect.equal e.Message "boom" "a failure is not a slow success"
                | other -> failtestf "expected Failed, got %A" other
            }

            test "an error boundary recovers and reports what it caught" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b =
                    Boundary<int>.Errors(g, (fun () -> if s.Value = 1 then failwith "boom" else s.Value), (fun _ _ -> -1))

                Expect.equal b.TryValue (Ready -1) "the error was caught"
                Expect.equal b.Caught.Message "boom" "and is readable"
                Expect.equal b.Status Status.None "nothing downstream sees an error"

                s.Value <- 2
                Expect.equal b.TryValue (Ready 2) "a re-run can recover"
                Expect.isTrue (isNull b.Caught) "and clears what it caught"
            }

            test "a fallback receives the last value shown" {
                let g = new Graph ()
                let first = AsyncSource<int>(g)
                let second = AsyncSource<int>(g)
                let useSecond = Signal (g, false)
                let seen = ResizeArray ()

                let b =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () -> if useSecond.Value then second.Value else first.Value),
                            (fun last ->
                                seen.Add last
                                last |> ValueOption.defaultValue -1)
                        )

                Expect.equal b.TryValue (Ready -1) "the first fallback has no previous value"
                first.Settle 5
                Expect.equal b.TryValue (Ready 5) "the body settled"
                useSecond.Value <- true
                Expect.equal b.TryValue (Ready 5) "the reload keeps the last value"
                Expect.sequenceEqual seen [ ValueNone; ValueSome 5 ] "each fallback saw the value before it"
            }

            test "a recover receives nothing until the boundary has shown a value" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let seen = ResizeArray ()

                let b =
                    Boundary<int>
                        .Errors(
                            g,
                            (fun () -> a.Value),
                            (fun _ last ->
                                seen.Add last
                                -1)
                        )

                Expect.equal b.TryValue Pending "the uncaught wait shows nothing"
                a.Fail (exn "boom")
                Expect.equal b.TryValue (Ready -1) "the failure was recovered"
                Expect.sequenceEqual seen [ ValueNone ] "a pending run is not a value shown"
            }

            test "an error boundary does not catch the pending channel" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let b = Boundary<int>.Errors(g, (fun () -> a.Value), (fun _ _ -> -1))

                Expect.equal b.TryValue Pending "a boundary that reports failures must not hide flights"
                Expect.equal b.Status Status.Pending "the channel still propagates"
            }

            test "a catching boundary takes both channels" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let b =
                    Boundary<int>.Catching(g, (fun () -> a.Value), (fun _ -> 0), (fun _ _ -> -1))

                Expect.equal b.TryValue (Ready 0) "pending caught"
                a.Fail (exn "boom")
                Expect.equal b.TryValue (Ready -1) "error caught"
                Expect.equal b.Caught.Message "boom" "and reported"
            }

            test "the innermost boundary catches" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let b = AsyncSource<int>(g)

                let inner = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> 0))

                let outer =
                    Boundary<int>.Suspense(g, (fun () -> inner.Value + b.Value), (fun _ -> -1))

                // `inner` absorbs its own wait, so the only thing `outer` is
                // waiting on is `b`.
                Expect.equal outer.TryValue (Ready -1) "outer is waiting on b"
                Expect.isTrue inner.IsWaiting "inner is waiting on a"

                b.Settle 5
                Expect.equal outer.TryValue (Ready 5) "outer settles on the fallback of inner"
                Expect.isFalse outer.IsWaiting "outer is not waiting"

                a.Settle 10
                Expect.equal outer.TryValue (Ready 15) "and updates when inner settles"
            }

            test "a boundary over a memo chain catches the propagated channel" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let doubled = Memo (g, (fun () -> a.Value * 2))
                let plus = Memo (g, (fun () -> doubled.Value + 1))

                let b = Boundary<int>.Suspense(g, (fun () -> plus.Value), (fun _ -> 0))

                Expect.equal b.TryValue (Ready 0) "the channel crossed two memos and stopped here"

                a.Settle 5
                Expect.equal b.TryValue (Ready 11) "and the whole chain settles"
            }

            test "a boundary records which source it caught a wait on" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let b = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> 0))

                b.TryValue |> ignore

                Expect.sequenceEqual
                    (b.PendingSources |> Seq.map (fun n -> n.Id))
                    [ (a :> INode).Id ]
                    "catching a channel must not lose what was waited on"
            }

            test "a boundary over an AsyncMemo catches the flight" {
                let g = new Graph ()
                let source = TaskCompletionSource<int>()
                let query = new AsyncMemo<int> (g, (fun _ -> source.Task))

                let b = Boundary<int>.Suspense(g, (fun () -> query.Value), (fun _ -> 0))

                Expect.equal b.TryValue (Ready 0) "the flight is caught"
                Expect.equal query.Runs 1 "and the read started it"

                source.SetResult 7
                Expect.equal b.TryValue (Ready 7) "the landing wakes the boundary"
            }

            test "a clean boundary is not re-run" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b = Boundary<int>.Suspense(g, (fun () -> s.Value), (fun _ -> 0))

                Expect.equal b.TryValue (Ready 1) "first read"
                Expect.equal b.TryValue (Ready 1) "cached"
                Expect.equal b.Runs 1 "a boundary is a computation like any other"
            }

            test "a failure that passes through a suspense boundary reaches its readers with each new exception" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b = Boundary<int>.Suspense(g, (fun () -> failwithf "e%d" s.Value), (fun _ -> 0))

                let m = Memo (g, (fun () -> b.Value))

                Expect.equal (reason m.TryValue) "e1" "first failure"
                s.Value <- 2
                Expect.equal (reason b.TryValue) "e2" "the boundary moved"
                Expect.equal (reason m.TryValue) "e2" "and its reader sees the new exception"
            }

            test "an error boundary whose recover rethrows reaches its readers with each new exception" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b =
                    Boundary<int>.Errors(g, (fun () -> failwithf "e%d" s.Value), (fun ex _ -> failwithf "r:%s" ex.Message))

                let m = Memo (g, (fun () -> b.Value))

                Expect.equal (reason m.TryValue) "r:e1" "first failure"
                s.Value <- 2
                Expect.equal (reason m.TryValue) "r:e2" "the reader sees the new exception"
            }

            test "a reader of Caught sees a new exception recovered to the same value" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b =
                    Boundary<int>.Errors(g, (fun () -> failwithf "e%d" s.Value), (fun _ _ -> -1))

                let m = Memo (g, (fun () -> sprintf "%d %s" b.Value b.Caught.Message))

                Expect.equal m.TryValue (Ready "-1 e1") "first recovery"
                s.Value <- 2
                Expect.equal m.TryValue (Ready "-1 e2") "the reader sees the new Caught"
            }

            test "a reader of Caught sees it clear when the body succeeds with the recovered value" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b =
                    Boundary<int>.Errors(g, (fun () -> if s.Value = 1 then failwith "e1" else -1), (fun _ _ -> -1))

                let m = Memo (g, (fun () -> b.Value, isNull b.Caught))

                Expect.equal m.TryValue (Ready (-1, false)) "recovered"
                s.Value <- 2
                Expect.equal m.TryValue (Ready (-1, true)) "the reader sees Caught clear"
            }

            test "IsWaiting before any read reflects the body" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let b = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> -1))

                Expect.isTrue b.IsWaiting "the body would suspend"
                a.Settle 1
                Expect.isFalse b.IsWaiting "the body is ready after the settle"
            }

            test "an effect reading only IsWaiting wakes when the body settles" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let b = Boundary<int>.Suspense(g, (fun () -> a.Value), (fun _ -> -1))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add b.IsWaiting))
                |> ignore

                Expect.sequenceEqual seen [ true ] "the first run sees the fallback"
                a.Settle 1
                Expect.sequenceEqual seen [ true; false ] "the settle wakes the effect"
            }

            test "Caught before any read reflects the body, and a reader of Caught alone wakes" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b =
                    Boundary<int>.Errors(g, (fun () -> if s.Value = 1 then failwith "boom" else 0), (fun _ _ -> -1))

                Expect.equal (b.Caught |> Option.ofObj |> Option.map _.Message) (Some "boom") "the body would fail"

                let m = Memo (g, (fun () -> isNull b.Caught))
                Expect.equal m.TryValue (Ready false) "the reader sees the recovery"
                s.Value <- 2
                Expect.equal m.TryValue (Ready true) "the reader wakes when the body succeeds"
            }

            test "an async value created and read in a boundary body restarts its flight on every settle" {
                let g = new Graph ()
                use _ = g.Activate ()
                let flights = ResizeArray<TaskCompletionSource<int>>()

                let fetch () =
                    createAsync (fun _ ->
                        let flight = TaskCompletionSource<int>()
                        flights.Add flight
                        flight.Task)

                let inside = createSuspense (fun _ -> -1) (fun () -> (fetch ()).Value)
                Expect.equal inside.TryValue (Ready -1) "the first flight is pending"

                for i in 1..3 do
                    flights[flights.Count - 1].SetResult i
                    Expect.equal inside.TryValue (Ready -1) "the settle re-ran the body, which started a new flight"

                Expect.equal flights.Count 4 "one flight per settle, and none lands"

                flights.Clear ()
                let outside = fetch ()
                let settles = createSuspense (fun _ -> -1) (fun () -> outside.Value)
                Expect.equal settles.TryValue (Ready -1) "the flight is pending"
                flights[0].SetResult 7
                Expect.equal settles.TryValue (Ready 7) "created outside the body, the value settles the boundary"
                Expect.equal flights.Count 1 "with one flight"
            }

            test "an async value created and read in an effect body restarts its flight on every settle" {
                let g = new Graph ()
                use _ = g.Activate ()
                let flights = ResizeArray<TaskCompletionSource<int>>()
                let seen = ResizeArray<int>()

                createEffect (fun () ->
                    let a =
                        createAsync (fun _ ->
                            let flight = TaskCompletionSource<int>()
                            flights.Add flight
                            flight.Task)

                    seen.Add a.Value)

                flights[0].SetResult 1
                flights[1].SetResult 2
                Expect.isEmpty seen "the effect never runs past the read"
                Expect.equal flights.Count 3 "one flight per settle"
            }

            test "an async value created and read in an owning memo body restarts its flight on every settle" {
                let g = new Graph ()
                use _ = g.Activate ()
                let flights = ResizeArray<TaskCompletionSource<int>>()

                let m =
                    createMemoWith (fun () ->
                        let a =
                            createAsync (fun _ ->
                                let flight = TaskCompletionSource<int>()
                                flights.Add flight
                                flight.Task)

                        a.Value)

                Expect.equal m.TryValue Pending "the first flight is pending"
                flights[0].SetResult 1
                Expect.equal m.TryValue Pending "the settle re-ran the body, which started a new flight"
                Expect.equal flights.Count 2 "a second flight"
            }
        ]
