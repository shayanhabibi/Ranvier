module Ranvier.Tests.FanOut

open Expecto
open Ranvier

/// <summary>
/// <c>ObserverSet</c> and <c>SourceList</c> are hand-rolled, and they change shape as
/// they grow: a linear membership scan below eight observers, a position index
/// above it, swap-remove in both regimes, and a doubling array underneath.
/// Every one of those transitions is a place where an off-by-one keeps a stale
/// edge or drops a live one, and neither failure is visible in a value — a
/// stale edge costs a recomputation nobody asked for, a dropped edge costs an
/// update nobody gets.
/// </summary>
/// <remarks>
/// The assertions here are therefore on <c>ObserverCount</c>, <c>SourceCount</c> and
/// <c>Runs</c> rather than on values. Counting is the only thing that can see this.
/// </remarks>
[<Tests>]
let tests =
    testList
        "FanOut"
        [
            test "the observer count is exact either side of the index threshold" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let memos = [ for _ in 1..20 -> Make.Memo (g, (fun _ -> s.Value + 1)) ]

                // Nothing has read them, so nothing has linked yet.
                Expect.equal s.ObserverCount 0 "a memo links on its first read, not at construction"

                for m in memos do
                    m.Value |> ignore

                Expect.equal s.ObserverCount 20 "every reader is linked exactly once"

                for m in memos do
                    m.Dispose ()

                Expect.equal s.ObserverCount 0 "and every one of them unlinks"
            }

            test "re-reading above the threshold does not duplicate the edge" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let memos = [ for _ in 1..12 -> Make.Memo (g, (fun _ -> s.Value + 1)) ]

                for m in memos do
                    m.Value |> ignore

                Expect.equal s.ObserverCount 12 "precondition: the index is in play"

                // Every memo re-runs and re-links. The index path has to
                // recognise each of them as already present.
                s.Value <- 1

                for m in memos do
                    m.Value |> ignore

                Expect.equal s.ObserverCount 12 "a re-link is not a second edge"
            }

            test "removing the last observer keeps the index honest" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let memos = [| for _ in 1..10 -> Make.Memo (g, (fun _ -> s.Value + 1)) |]

                for m in memos do
                    m.Value |> ignore

                // Swap-remove moves the last element into the hole. Removing
                // the last element is the case where there is no hole to fill,
                // and the index must not be left pointing at a position that no
                // longer exists.
                memos[9].Dispose()
                Expect.equal s.ObserverCount 9 "removing the tail"

                memos[0].Dispose()
                Expect.equal s.ObserverCount 8 "removing the head, which swaps the tail down"

                s.Value <- 1

                for i in 1..8 do
                    Expect.equal memos[i].Value 2 "every surviving observer still updates"
            }

            test "removing from the middle in a loop leaves every survivor linked" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let memos = [| for _ in 1..16 -> Make.Memo (g, (fun _ -> s.Value + 1)) |]

                for m in memos do
                    m.Value |> ignore

                // Drop every other one, which walks the swap-remove through
                // both regimes: above the threshold at the start, below it by
                // the end.
                for i in 0..2..15 do
                    memos[i].Dispose()

                Expect.equal s.ObserverCount 8 "half of them are gone"

                s.Value <- 1

                for i in 1..2..15 do
                    Expect.equal memos[i].Value 2 "and the other half still hear about a write"
            }

            test "a body that reads the same source twice records one edge" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> s.Value + s.Value + s.Value))

                Expect.equal m.Value 3 "precondition"
                Expect.equal m.SourceCount 1 "three reads of one source is one dependency"
                Expect.equal s.ObserverCount 1 "and one observer"
            }

            test "a body that reads fewer sources on its second run sheds the rest" {
                let g = new Graph ()
                let count = Signal (g, 8)

                let sources = [| for i in 1..8 -> Signal (g, i) |]

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            let mutable total = 0
                            // Bound first: Fable re-evaluates a range bound on every iteration.
                            let n = count.Value

                            for i in 0 .. n - 1 do
                                total <- total + sources[i].Value

                            total
                    )

                Expect.equal m.Value 36 "precondition"
                Expect.equal m.SourceCount 9 "eight sources plus the count"

                count.Value <- 2
                Expect.equal m.Value 3 "reading a prefix"
                Expect.equal m.SourceCount 3 "the shed sources are gone from the list"

                for i in 2..7 do
                    Expect.equal sources[i].ObserverCount 0 "and gone from each source's observers"

                count.Value <- 8
                Expect.equal m.Value 36 "and the list grows back"
                Expect.equal m.SourceCount 9 "to exactly what it was"
            }

            test "a body that reverses its read order keeps every edge exactly once" {
                let g = new Graph ()
                let forwards = Signal (g, true)

                let sources = [| for i in 1..12 -> Signal (g, i) |]

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            let order = if forwards.Value then [ 0..11 ] else [ 11..-1..0 ]

                            order |> List.sumBy (fun i -> sources[i].Value)
                    )

                Expect.equal m.Value 78 "precondition"
                Expect.equal m.SourceCount 13 "twelve sources plus the switch"

                forwards.Value <- false
                Expect.equal m.Value 78 "the same sum, read backwards"
                Expect.equal m.SourceCount 13 "and the same edges, not twenty-four"

                for s in sources do
                    Expect.equal s.ObserverCount 1 "each source has one observer, not two"
            }

            test "a wide effect unlinks from all of its sources when disposed" {
                let g = new Graph ()

                let sources = [| for i in 1..32 -> Signal (g, i) |]

                let e =
                    new Effect (
                        g,
                        (fun () ->
                            sources
                            |> Array.sumBy (fun s -> s.Value)
                            |> ignore)
                    )

                for s in sources do
                    Expect.equal s.ObserverCount 1 "linked"

                e.Dispose ()

                for s in sources do
                    Expect.equal s.ObserverCount 0 "and unlinked"
            }
        ]
