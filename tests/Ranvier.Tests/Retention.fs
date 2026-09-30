module Ranvier.Tests.Retention

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

#if !FABLE_COMPILER
// .NET only: JavaScript exposes no forced collection.
/// <summary>
/// An owner that only ever grows is a leak of everything the graph has created.
/// Disposing a child individually — which is the common case, since re-running
/// an effect disposes its own scope — has to release it from its owner, or the
/// node stays reachable, closure included, for the life of the graph.
/// </summary>
/// <remarks>
/// These are the only tests here that reason about reachability, so they are
/// also the only ones that collect. The thresholds are loose on purpose: the
/// failure being pinned is unbounded retention, not one straggler the JIT
/// happens to be holding in a register.
/// </remarks>
let private collect () =
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    GC.Collect ()

let private aliveOf (refs: ResizeArray<WeakReference>) =
    collect ()

    refs
    |> Seq.filter (fun r -> r.IsAlive)
    |> Seq.length

let private flightPolicies =
    [
        "CancelPrevious", CancelPrevious
        "KeepLatest", KeepLatest
        "Queue", FlightPolicy.Queue
    ]

let private graphWith policy =
    new Graph (
        { GraphOptions.Default with
            FlightPolicy = policy
        }
    )

/// <summary>
/// Starts <c>count</c> flights of an async memo under <c>policy</c>, one per write and read. <c>body</c> receives the
/// flight's number, its token and a fresh payload, tracked in the returned weak references.
/// </summary>
let private runFlights policy count (body: int -> CancellationToken -> obj -> Task<int>) =
    let g = graphWith policy
    let s = Signal (g, 0)
    let refs = ResizeArray<WeakReference>()

    let a =
        Make.AsyncMemo<int> (
            g,
            fun _ token ->
                let n = s.Value
                let payload = obj ()
                refs.Add (WeakReference payload)
                body n token payload
        )

    for i in 1..count do
        s.Value <- i
        a.TryValue |> ignore

    g, a, refs
#endif

[<Tests>]
let tests =
    testList
        "Retention"
        [
#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no forced collection.
            test "a disposed effect is not retained by its owner" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let e = new Effect (g, (fun () -> s.Value |> ignore))
                    e.Dispose ()
                    refs.Add (WeakReference e)

                Expect.isLessThan (aliveOf refs) 20 "the graph root must not accumulate dead effects"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no forced collection.
            test "a disposed memo is not retained by its owner" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let m = Make.Memo (g, (fun _ -> s.Value * 2))
                    m.TryValue |> ignore
                    m.Dispose ()
                    refs.Add (WeakReference m)

                Expect.isLessThan (aliveOf refs) 20 "nor dead memos"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no forced collection.
            test "a disposed root is not retained by its parent" {
                let g = new Graph ()
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let owner = g.CreateRoot id
                    owner.Dispose ()
                    refs.Add (WeakReference owner)

                Expect.isLessThan (aliveOf refs) 20 "a scope per request must not cost a scope per process"
            }
#endif

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no forced collection.
            test "a torn-down subtree is released even while its signal lives on" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let owner =
                        g.CreateRoot (fun owner ->
                            let m = Make.Memo (g, (fun _ -> s.Value * 2))
                            m.TryValue |> ignore
                            refs.Add (WeakReference m)
                            owner)

                    owner.Dispose ()

                // The signal is still live and still reachable, which is the
                // point: it must not be what pins the subtree.
                Expect.isLessThan (aliveOf refs) 20 "a live source must not pin dead readers"
                s.Value <- 2
            }
#endif

            test "live children are still torn down with their owner" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        new Effect (g, (fun () -> seen.Add s.Value))
                        |> ignore

                        owner)

                Expect.sequenceEqual seen [ 1 ] "first run"
                owner.Dispose ()

                s.Value <- 2
                Expect.sequenceEqual seen [ 1 ] "unlinking on individual disposal must not break scope disposal"
            }

            test "disposal order is still last-created-first" {
                let g = new Graph ()
                let log = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        for i in 1..3 do
                            g.OnCleanup (fun () -> log.Add i)

                        new Effect (g, (fun () -> g.OnCleanup (fun () -> log.Add 0)))
                        |> ignore

                        owner)

                owner.Dispose ()
                Expect.sequenceEqual log [ 3; 2; 1; 0 ] "cleanups first, in reverse, then children"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript exposes no forced collection.
            for name, policy in flightPolicies do
                test $"{name}: an undisposed token registration is released once its flight settles" {
                    let g, a, refs =
                        runFlights policy 2_000 (fun n token payload ->
                            token.Register (fun () -> GC.KeepAlive payload) |> ignore
                            Task.FromResult n)

                    Expect.isLessThan (aliveOf refs) 20 "a settled flight's registrations must not live as long as the memo"
                    GC.KeepAlive a
                    g.Dispose ()
                }

                test $"{name}: a body that throws before returning its task releases its registration" {
                    let g, a, refs =
                        runFlights policy 2_000 (fun _ token payload ->
                            token.Register (fun () -> GC.KeepAlive payload) |> ignore
                            failwith "thrown before the task")

                    Expect.isLessThan (aliveOf refs) 20 "a synchronous failure must not hold its registration until disposal"
                    GC.KeepAlive a
                    g.Dispose ()
                }

                test $"{name}: overlapping flights release their registrations once the last one settles" {
                    let pending = ResizeArray<TaskCompletionSource<int>>()
                    let tokens = ResizeArray<CancellationToken>()

                    let g, a, refs =
                        runFlights policy 2_000 (fun n token payload ->
                            token.Register (fun () -> GC.KeepAlive payload) |> ignore
                            tokens.Add token
                            let source = TaskCompletionSource<int>()

                            // Settles the previous flight while this body runs, so a flight is always in progress.
                            if pending.Count > 0 then
                                pending[pending.Count - 1].TrySetResult (n - 1)
                                |> ignore

                            pending.Add source
                            source.Task)

                    Expect.isFalse tokens[tokens.Count - 1].IsCancellationRequested "the flight in progress keeps a live token"
                    pending[pending.Count - 1].TrySetResult 0 |> ignore
                    Expect.isLessThan (aliveOf refs) 20 "every flight settled, so every registration is released"
                    GC.KeepAlive a
                    g.Dispose ()
                }

                test $"{name}: disposing the memo cancels the flight in progress after an earlier flight settled" {
                    let g = graphWith policy
                    let s = Signal (g, 0)
                    let tokens = ResizeArray<CancellationToken>()

                    let a =
                        Make.AsyncMemo<int> (
                            g,
                            fun _ token ->
                                let n = s.Value
                                tokens.Add token

                                if n = 0 then
                                    Task.FromResult n
                                else
                                    TaskCompletionSource<int>().Task
                        )

                    a.TryValue |> ignore
                    s.Value <- 1
                    a.TryValue |> ignore
                    a.Dispose ()
                    Expect.isTrue tokens[1].IsCancellationRequested "the pending flight's token is cancelled"
                    g.Dispose ()
                }

                test $"{name}: a flight that never settles is released when the memo is disposed" {
                    let g, a, refs =
                        runFlights policy 2_000 (fun _ token payload ->
                            let source = TaskCompletionSource<int>()

                            token.Register (fun () ->
                                GC.KeepAlive payload
                                source.TrySetCanceled () |> ignore)
                            |> ignore

                            source.Task)

                    a.Dispose ()
                    Expect.isLessThan (aliveOf refs) 20 "disposal cancels every flight in progress"
                    g.Dispose ()
                }
#endif
        ]
