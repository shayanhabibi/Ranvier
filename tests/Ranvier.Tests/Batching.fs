module Ranvier.Tests.Batching

open Expecto
open Ranvier

/// <summary>
/// <c>Batch</c> is the only place a caller can put the graph into a state that is
/// temporarily inconsistent on purpose: writes have landed, nothing has been
/// recomputed, and anything that reads in between sees the new values through
/// the old derivations. That window is useful and it is also where a mistake
/// hides, because everything inside it still works — it just works on a
/// mixture.
/// </summary>
/// <remarks>
/// The questions here are what a read inside the window sees, what happens when
/// the window is entered from somewhere unusual (an effect body, a cleanup, a
/// flush), and whether the graph is left in a sane state when the window closes
/// abnormally.
/// </remarks>
[<Tests>]
let tests =
    testList
        "Batching"
        [
            test "a batch returns what its body returned" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let result =
                    g.Batch (fun () ->
                        s.Value <- 2
                        s.Value * 10)

                Expect.equal result 20 "the write is visible to the body that made it"
                Expect.equal s.Value 2 "and it stuck"
            }

            test "a memo read inside a batch is recomputed there and then" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Memo (g, (fun () -> s.Value * 10))

                Expect.equal m.Value 10 "precondition"

                g.Batch (fun () ->
                    s.Value <- 2

                    // Memos are pull-based, so batching does not defer them —
                    // it defers *effects*. Reading one inside the window gets
                    // the current answer, which is the whole reason a batch is
                    // safe to read from.
                    Expect.equal m.Value 20 "the memo is current inside the batch"
                    Expect.equal m.Runs 2 "having recomputed on demand")

                Expect.equal m.Runs 2 "and the batch ending did not run it again"
            }

            test "an effect is deferred to the end of the batch even when its memo was read inside" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Memo (g, (fun () -> s.Value * 10))
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add m.Value))
                |> ignore

                Expect.sequenceEqual seen [ 10 ] "first run"

                g.Batch (fun () ->
                    s.Value <- 2
                    m.Value |> ignore
                    Expect.sequenceEqual seen [ 10 ] "the effect has not run yet")

                Expect.sequenceEqual seen [ 10; 20 ] "and runs once, at the close"
            }

            test "writing the same signal repeatedly in a batch is one run at the last value" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add s.Value))
                |> ignore

                seen.Clear ()

                g.Batch (fun () ->
                    for i in 1..100 do
                        s.Value <- i)

                Expect.sequenceEqual seen [ 100 ] "a hundred writes, one run, the last value"
            }

            test "a write that ends up back where it started still runs the effect" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        s.Value |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                g.Batch (fun () ->
                    s.Value <- 1
                    s.Value <- 0)

                // The cutoff is per write, and each of these two writes changed
                // the value. Nothing compares the value at the start of the
                // batch with the value at the end, so the effect runs on a
                // signal that is, net, unchanged.
                Expect.equal runs.Value 2 "the batch does not collapse a round trip"
                Expect.equal s.Value 0 "though the value is where it began"
            }

            test "a batch inside an effect body does not defer past the flush it is in" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let other = Signal (g, 0)
                let seen = ResizeArray ()

                new Effect (g, (fun () -> seen.Add other.Value))
                |> ignore

                new Effect (
                    g,
                    fun () ->
                        if trigger.Value > 0 then
                            g.Batch (fun () -> other.Value <- trigger.Value)
                )
                |> ignore

                Expect.sequenceEqual seen [ 0 ] "first run"

                trigger.Value <- 1
                Expect.sequenceEqual seen [ 0; 1 ] "the batched write still reached the other effect"
            }

            test "a settle inside a batch is deferred with everything else" {
                let g = new Graph ()
                let a = AsyncSource<int> g
                let s = Signal (g, 0)
                let seen = ResizeArray ()

                new Effect (
                    g,
                    fun () ->
                        match a.TryValue with
                        | Ready v -> seen.Add (v + s.Value)
                        | _ -> seen.Add -1
                )
                |> ignore

                Expect.sequenceEqual seen [ -1 ] "pending at construction"

                g.Batch (fun () ->
                    a.Settle 10
                    s.Value <- 1)

                Expect.sequenceEqual seen [ -1; 11 ] "one run seeing both the settle and the write"
            }

            test "disposing inside a batch stops the effect running at the close" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                let e =
                    new Effect (
                        g,
                        fun () ->
                            s.Value |> ignore
                            runs.Value <- runs.Value + 1
                    )

                g.Batch (fun () ->
                    s.Value <- 1
                    e.Dispose ())

                Expect.equal runs.Value 1 "the disposal won, as it would outside a batch"
            }

            test "an Untrack inside a batch is still untracked" {
                let g = new Graph ()
                let tracked = Signal (g, 1)
                let hidden = Signal (g, 10)

                let m = Memo (g, (fun () -> tracked.Value + g.Untrack (fun () -> hidden.Value)))

                Expect.equal m.Value 11 "precondition"

                g.Batch (fun () ->
                    hidden.Value <- 20
                    tracked.Value <- 2)

                Expect.equal m.Value 22 "the tracked write woke it, and it re-read the hidden one"
                Expect.equal hidden.ObserverCount 0 "which still recorded no edge"
            }

            test "a batch that throws is not left open" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        s.Value |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                for _ in 1..3 do
                    try
                        g.Batch (fun () ->
                            s.Value <- s.Value + 1
                            failwith "boom")
                        |> ignore
                    with _ ->
                        ()

                // Three throwing batches in a row. If the depth counter leaked
                // on the way out, the graph would now be permanently batching
                // and this last write would never flush.
                let before = runs.Value
                s.Value <- 100
                Expect.isGreaterThan runs.Value before "the graph is still flushing after three failed batches"
            }

            // A sharp edge, pinned rather than filed down. `RequestFlush`
            // respects `batchDepth`; the public `Flush` does not, so an
            // explicit drain from inside the window opens it. Defensible — a
            // caller who calls `Flush` by name has asked for a drain, and
            // making it silently do nothing would be its own trap — but it
            // means the batch's guarantee holds only against code that does not
            // call `Flush`, and any transitive call counts.
            test "an explicit flush inside a batch drains it, defeating the window" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        s.Value |> ignore
                        runs.Value <- runs.Value + 1
                )
                |> ignore

                g.Batch (fun () ->
                    s.Value <- 1
                    g.Flush ()
                    Expect.equal runs.Value 2 "the effect ran inside the window"

                    s.Value <- 2)

                Expect.equal runs.Value 3 "and the close flushed what was written after it"
            }
        ]
