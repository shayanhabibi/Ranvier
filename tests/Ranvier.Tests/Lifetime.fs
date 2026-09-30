module Ranvier.Tests.Lifetime

open Expecto
open Ranvier

/// <summary>
/// A memo is kept alive by the sources it reads, so nothing collects it and it
/// goes on being marked dirty for as long as the graph lives. Disposal makes
/// that structural: the enclosing scope owns it, and tearing the scope down
/// unlinks it.
/// </summary>
[<Tests>]
let tests =
    testList
        "Lifetime"
        [
            test "disposing a memo stops it being woken" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Make.Memo (g, (fun _ -> s.Value * 2))

                Expect.equal c.TryValue (Ready 2) "first"
                c.Dispose ()

                s.Value <- 5
                Expect.equal c.TryValue (Ready 2) "a disposed memo answers with its last value"
                Expect.equal c.Runs 1 "and must not recompute"
            }

            test "disposing a memo twice is a no-op" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Make.Memo (g, (fun _ -> s.Value))

                c.TryValue |> ignore
                c.Dispose ()
                c.Dispose ()
                Expect.equal c.Runs 1 "idempotent, like every other Dispose here"
            }

            test "a root disposes the memos created inside it" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let c, owner =
                    g.CreateRoot (fun owner -> Make.Memo (g, (fun _ -> s.Value * 2)), owner)

                Expect.equal c.TryValue (Ready 2) "live"
                owner.Dispose ()

                // This is the point of the change: the caller tears down one
                // scope, not a list of memos it had to remember to collect.
                s.Value <- 5
                Expect.equal c.TryValue (Ready 2) "the root took it down with it"
                Expect.equal c.Runs 1 "no recomputation after teardown"
            }

            test "a root disposes the boundaries created inside it" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b, owner =
                    g.CreateRoot (fun owner -> Boundary<int>.Suspense(g, (fun () -> s.Value), (fun _ -> 0)), owner)

                Expect.equal b.TryValue (Ready 1) "live"
                owner.Dispose ()

                s.Value <- 5
                Expect.equal b.TryValue (Ready 1) "and the boundary is down too"
                Expect.equal b.Runs 1 "no recomputation after teardown"
            }

            test "a disposed memo stops waking what reads it" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Make.Memo (g, (fun _ -> s.Value * 2))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add c.Value))
                |> ignore

                Expect.sequenceEqual seen [ 2 ] "first run"
                c.Dispose ()

                s.Value <- 5
                Expect.sequenceEqual seen [ 2 ] "the chain is cut at the memo"
            }

            test "disposing the graph takes every memo with it" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Make.Memo (g, (fun _ -> s.Value * 2))

                Expect.equal c.TryValue (Ready 2) "live"
                g.Root.Dispose ()

                s.Value <- 5
                Expect.equal c.Runs 1 "an ungrouped memo still belongs to the graph root"
            }
        ]
