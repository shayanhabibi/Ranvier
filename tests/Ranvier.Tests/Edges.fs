module Ranvier.Tests.Edges

open Expecto
open Ranvier

/// <summary>
/// Dependencies are re-collected on every run, so a body that stops reading a
/// source must stop hearing from it. The failure is quiet in both directions:
/// a stale edge costs a wasted recomputation, and keeps the computation alive
/// for as long as the source it no longer reads.
/// </summary>
[<Tests>]
let tests =
    testList
        "Edges"
        [
            test "a memo stops being woken by a source it no longer reads" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let right = Signal (g, 100)

                let c = Memo (g, (fun _ -> if useLeft.Value then left.Value else right.Value))

                Expect.equal c.TryValue (Ready 1) "reading left"
                useLeft.Value <- false
                Expect.equal c.TryValue (Ready 100) "reading right"
                Expect.equal c.Runs 2 "precondition"

                // `left` is no longer read by anything. Writing to it must not
                // reach this memo at all.
                left.Value <- 2
                Expect.equal c.TryValue (Ready 100) "the dropped source cannot change the value"
                Expect.equal c.Runs 2 "and must not have caused a recomputation"
            }

            test "the dropped edge is re-linked when the branch is taken again" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let right = Signal (g, 100)

                let c = Memo (g, (fun _ -> if useLeft.Value then left.Value else right.Value))

                Expect.equal c.TryValue (Ready 1) "reading left"
                useLeft.Value <- false
                Expect.equal c.TryValue (Ready 100) "reading right"

                left.Value <- 2
                useLeft.Value <- true

                // Detaching must not be a one-way door: the write to `left`
                // happened while the edge was gone, and the re-run has to see it.
                Expect.equal c.TryValue (Ready 2) "the branch is live again"
            }

            test "a dropped source does not wake the effect behind the memo" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let right = Signal (g, 100)

                let c = Memo (g, (fun _ -> if useLeft.Value then left.Value else right.Value))

                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add c.Value))
                |> ignore

                Expect.sequenceEqual seen [ 1 ] "first run"
                useLeft.Value <- false
                Expect.sequenceEqual seen [ 1; 100 ] "switched branch"

                left.Value <- 2
                Expect.sequenceEqual seen [ 1; 100 ] "a source nothing reads must not schedule anything"
            }

            test "an effect stops being woken by a source it no longer reads" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let right = Signal (g, 100)

                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add (if useLeft.Value then left.Value else right.Value)))
                |> ignore

                Expect.sequenceEqual seen [ 1 ] "first run"
                useLeft.Value <- false
                Expect.sequenceEqual seen [ 1; 100 ] "switched branch"

                left.Value <- 2
                Expect.sequenceEqual seen [ 1; 100 ] "the push side has the same obligation as the pull side"
            }

            test "a boundary stops being woken by a source it no longer reads" {
                let g = new Graph ()
                let useLeft = Signal (g, true)
                let left = Signal (g, 1)
                let right = Signal (g, 100)

                let b =
                    Boundary<int>.Suspense(g, (fun () -> if useLeft.Value then left.Value else right.Value), (fun _ -> 0))

                Expect.equal b.TryValue (Ready 1) "reading left"
                useLeft.Value <- false
                Expect.equal b.TryValue (Ready 100) "reading right"
                Expect.equal b.Runs 2 "precondition"

                left.Value <- 2
                Expect.equal b.Runs 2 "catching a channel does not change who wakes you"
            }
        ]
