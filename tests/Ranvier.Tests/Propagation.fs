module Ranvier.Tests.Propagation

open Expecto
open Ranvier

/// <summary>
/// The shape of the graph, rather than any one node in it. A diamond is the
/// smallest graph that can be wrong rather than merely slow: if the two legs
/// are updated independently, something at the bottom observes a state that
/// never existed — leg A from after the write and leg B from before it. Solid
/// calls avoiding this glitch-freedom, and every test here is a way of asking
/// for it.
/// </summary>
/// <remarks>
/// Depth is the other axis. A chain of memos is where an off-by-one in the
/// invalidation walk turns into an update that arrives at node 40 and not 41,
/// and where a cutoff either does or does not stop the walk dead.
/// </remarks>
[<Tests>]
let tests =
    testList
        "Propagation"
        [
            test "an effect at the bottom of a diamond runs once per write" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let left = Memo (g, (fun () -> source.Value * 2))
                let right = Memo (g, (fun () -> source.Value * 3))
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        left.Value + right.Value |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                Expect.equal runs.Value 1 "first run"

                source.Value <- 2
                Expect.equal runs.Value 2 "one write, one run — not one run per leg"
            }

            test "the bottom of a diamond never sees one leg updated and the other not" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let left = Memo (g, (fun () -> source.Value * 2))
                let right = Memo (g, (fun () -> source.Value * 3))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add (left.Value, right.Value)))
                |> ignore

                for i in 2..6 do
                    source.Value <- i

                // Every pair has to be `(n*2, n*3)` for one n. A pair like
                // `(4, 3)` is the glitch: left from after the write, right from
                // before it.
                for (l, r) in seen do
                    Expect.equal (l * 3) (r * 2) "each observation is of a single state"

                Expect.sequenceEqual seen [ (2, 3); (4, 6); (6, 9); (8, 12); (10, 15); (12, 18) ] "and there is exactly one observation per write"
            }

            test "a memo shared by two effects is recomputed once, not once per reader" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let shared = Memo (g, (fun () -> source.Value * 2))

                new Effect (g, (fun () -> shared.Value |> ignore))
                |> ignore

                new Effect (g, (fun () -> shared.Value |> ignore))
                |> ignore

                Expect.equal shared.Runs 1 "two readers, one computation"

                source.Value <- 2
                Expect.equal shared.Runs 2 "and one more for the write"
                Expect.equal source.ObserverCount 1 "the source is read by the memo alone"
            }

            test "a write is carried the whole length of a deep chain" {
                let g = new Graph ()
                let source = Signal (g, 0)

                let mutable previous = Memo (g, (fun () -> source.Value + 1))
                let chain = ResizeArray [ previous ]

                for _ in 2..50 do
                    // Bound outside the closure: capturing the mutable itself
                    // would give every link the *last* memo.
                    let inner = previous
                    previous <- Memo (g, (fun () -> inner.Value + 1))
                    chain.Add previous

                let last = chain[chain.Count - 1]
                Expect.equal last.Value 50 "fifty links, fifty increments"

                source.Value <- 100
                Expect.equal last.Value 150 "and the write reaches the far end"

                for link in chain do
                    Expect.equal link.Runs 2 "every link ran exactly twice, not once per path"
            }

            // The cutoff below the first derivation, which is what the `Check`
            // state buys. A write propagates as "maybe" rather than "dirty";
            // `flattened` re-runs because it genuinely must — nothing else can
            // know whether its value moved — and, finding that it did not, says
            // nothing. Everything below it resolves its `Check` to `Clean`
            // without running a body.
            //
            // The waste this removes is unbounded in the depth of the chain,
            // which is exactly where a reactive graph is supposed to win.
            test "a cutoff partway down a chain stops everything below it" {
                let g = new Graph ()
                let source = Signal (g, 1)

                // Collapses everything to a constant, so its own value never
                // changes however much the source does.
                let flattened = Memo (g, (fun () -> source.Value * 0))
                let below = Memo (g, (fun () -> flattened.Value + 1))
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        below.Value |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                Expect.equal below.Runs 1 "precondition"
                Expect.equal runs.Value 1 "precondition"

                source.Value <- 2
                Expect.equal flattened.Value 0 "the value did not move"
                Expect.equal flattened.Runs 2 "though the memo re-ran to find that out"
                Expect.equal below.Runs 1 "and the one below it did not"
                Expect.equal runs.Value 1 "nor did the effect at the bottom"
            }

            // The other half of the same mechanism: a cutoff must not become a
            // *lost* update. The moment the collapsing memo's value does move,
            // the `Check` walk below it has to resolve to `Dirty` and every
            // body has to run.
            test "the write that finally moves a cut-off memo wakes everything below" {
                let g = new Graph ()
                let source = Signal (g, 1)

                // Constant until the source exceeds 10, then it tracks it.
                let clamped = Memo (g, (fun () -> max 0 (source.Value - 10)))
                let below = Memo (g, (fun () -> clamped.Value * 2))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add below.Value))
                |> ignore

                for i in 2..10 do
                    source.Value <- i

                Expect.sequenceEqual seen [ 0 ] "nine writes, none of which moved the clamp"
                Expect.equal clamped.Runs 10 "the clamp itself ran for each one"
                Expect.equal below.Runs 1 "and nothing below it did"

                source.Value <- 12
                Expect.sequenceEqual seen [ 0; 4 ] "the write that broke the clamp came through"
                Expect.equal below.Runs 2 "having run the body below it exactly once"
            }

            test "a batch writing both sources of a memo recomputes it once" {
                let g = new Graph ()
                let a = Signal (g, 1)
                let b = Signal (g, 10)
                let m = Memo (g, (fun () -> a.Value + b.Value))

                new Effect (g, (fun () -> m.Value |> ignore))
                |> ignore

                Expect.equal m.Runs 1 "precondition"

                g.Batch (fun () ->
                    a.Value <- 2
                    b.Value <- 20)

                Expect.equal m.Value 22 "both writes landed"
                Expect.equal m.Runs 2 "as one recomputation"
            }

            test "the same two writes outside a batch recompute it twice" {
                let g = new Graph ()
                let a = Signal (g, 1)
                let b = Signal (g, 10)
                let m = Memo (g, (fun () -> a.Value + b.Value))

                new Effect (g, (fun () -> m.Value |> ignore))
                |> ignore

                a.Value <- 2
                b.Value <- 20

                Expect.equal m.Value 22 "same answer"
                Expect.equal m.Runs 3 "for twice the work — which is what a batch buys"
            }

            test "a memo nothing reads is never recomputed, however much its source moves" {
                let g = new Graph ()
                let source = Signal (g, 0)
                let m = Memo (g, (fun () -> source.Value * 2))

                for i in 1..10 do
                    source.Value <- i

                Expect.equal m.Runs 0 "pull-based: no reader, no work"
                Expect.equal m.Value 20 "and the first read is up to date"
                Expect.equal m.Runs 1 "having cost exactly one computation"
            }

            test "one pending leg suspends the diamond, and one settle releases it" {
                let g = new Graph ()
                let source = Signal (g, 1)
                let flight = AsyncSource<int> g

                let ready = Memo (g, (fun () -> source.Value * 2))
                let waiting = Memo (g, (fun () -> flight.Value + source.Value))
                let runs = ref 0

                let b =
                    Boundary<int>
                        .Suspense(
                            g,
                            (fun () ->
                                runs.Value <- runs.Value + 1
                                ready.Value + waiting.Value),
                            fun _ -> -1
                        )

                Expect.equal b.TryValue (Ready -1) "one pending leg is enough to suspend the whole body"
                Expect.equal runs.Value 1 "which took one attempt"

                flight.Settle 10
                Expect.equal b.TryValue (Ready 13) "2 + 11"
                Expect.equal runs.Value 2 "and one more attempt, not one per leg"
            }

            test "a deep chain wakes its far end once per write, not once per link" {
                let g = new Graph ()
                let source = Signal (g, 0)
                let runs = ref 0

                let mutable previous = Memo (g, (fun () -> source.Value + 1))

                for _ in 2..20 do
                    let inner = previous
                    previous <- Memo (g, (fun () -> inner.Value + 1))

                let last = previous

                new Effect (
                    g,
                    fun () ->
                        last.Value |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                Expect.equal runs.Value 1 "first run"

                source.Value <- 1
                Expect.equal runs.Value 2 "a twenty-link chain is still one invalidation"
            }
        ]
