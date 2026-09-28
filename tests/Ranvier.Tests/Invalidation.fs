module Ranvier.Tests.Invalidation

open Expecto
open Ranvier

/// <summary>
/// A computation is marked clean before its body runs, so an invalidation
/// raised during the run survives it. Clearing the flag afterwards instead
/// discards that invalidation silently: the node goes on serving a value the
/// graph has already moved past, and nothing ever re-runs it.
/// </summary>
[<Tests>]
let tests =
    testList
        "Invalidation"
        [
            test "a memo sees a write its own body made" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let c =
                    Memo (
                        g,
                        fun _ ->
                            let v = s.Value

                            if v < 3 then
                                s.Value <- v + 1

                            v
                    )

                // A memo that writes is outside the contract, but it is the
                // cheapest way to put a write in the middle of a run. The same
                // shape arises legitimately from a source that settles inline.
                c.TryValue |> ignore
                Expect.equal s.Peek 2 "precondition: the body wrote"
                Expect.equal c.TryValue (Ready 2) "the next read must re-run against the new value"
                Expect.equal c.TryValue (Ready 3) "and converge once the body stops writing"
                Expect.equal c.Runs 3 "one run per invalidation, no more"
            }

            test "a boundary sees a write its own body made" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let b =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () ->
                                let v = s.Value

                                if v < 3 then
                                    s.Value <- v + 1

                                v),
                            fun _ -> 0
                        )

                b.TryValue |> ignore
                Expect.equal b.TryValue (Ready 2) "catching a channel does not change invalidation"
                Expect.equal b.TryValue (Ready 3) "and it converges the same way"
            }

            test "a memo that reads itself yields a default rather than overflowing" {
                let g = new Graph ()
                let mutable self: Memo<int> option = None

                let c =
                    Memo (
                        g,
                        fun _ ->
                            match self with
                            | Some (m: Memo<int>) -> m.Peek + 1
                            | None -> 0
                    )

                self <- Some c

                // Not a supported thing to write, but a cycle must fail as a
                // value, not as a stack overflow the caller cannot catch.
                Expect.equal c.TryValue (Ready 1) "one level, then the cached default"
            }

            test "an unrelated write still invalidates normally" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let c = Memo (g, (fun _ -> s.Value * 2))

                Expect.equal c.TryValue (Ready 2) "first"
                Expect.equal c.Runs 1 "cached"
                s.Value <- 5
                Expect.equal c.TryValue (Ready 10) "invalidated"
                Expect.equal c.Runs 2 "exactly one re-run"
            }
        ]
