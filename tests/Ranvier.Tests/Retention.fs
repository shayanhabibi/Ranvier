module Ranvier.Tests.Retention

open System
open Expecto
open Ranvier

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

[<Tests>]
let tests =
    testList
        "Retention"
        [
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

            test "a disposed memo is not retained by its owner" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let m = Memo (g, (fun _ -> s.Value * 2))
                    m.TryValue |> ignore
                    m.Dispose ()
                    refs.Add (WeakReference m)

                Expect.isLessThan (aliveOf refs) 20 "nor dead memos"
            }

            test "a disposed root is not retained by its parent" {
                let g = new Graph ()
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let owner = g.CreateRoot id
                    owner.Dispose ()
                    refs.Add (WeakReference owner)

                Expect.isLessThan (aliveOf refs) 20 "a scope per request must not cost a scope per process"
            }

            test "a torn-down subtree is released even while its signal lives on" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let refs = ResizeArray<WeakReference>()

                for _ in 1..200 do
                    let owner =
                        g.CreateRoot (fun owner ->
                            let m = Memo (g, (fun _ -> s.Value * 2))
                            m.TryValue |> ignore
                            refs.Add (WeakReference m)
                            owner)

                    owner.Dispose ()

                // The signal is still live and still reachable, which is the
                // point: it must not be what pins the subtree.
                Expect.isLessThan (aliveOf refs) 20 "a live source must not pin dead readers"
                s.Value <- 2
            }

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
        ]
