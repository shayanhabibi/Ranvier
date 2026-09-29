module Ranvier.Tests.Reentrancy

open Expecto
open Ranvier

/// <summary>
/// Everything here happens during something else: a write inside a flush, a
/// disposal inside the body being disposed, a batch inside a batch, a flush
/// inside a flush. These are the shapes where a graph either has an answer or
/// corrupts itself quietly, and they are exactly what a UI does — an effect
/// writes a signal another effect reads, and a component tears itself down in
/// response to the state it just observed.
/// </summary>
/// <remarks>
/// Several of these have no obviously right answer. Where that is so, the test
/// pins the answer the implementation gives, and says which it is, so that a
/// change of behaviour is a conversation rather than a surprise.
/// </remarks>
[<Tests>]
let tests =
    testList
        "Reentrancy"
        [
            test "an effect that disposes itself mid-body does not run again" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0
                let self = ref Unchecked.defaultof<Effect>

                self.Value <-
                    new Effect (
                        g,
                        fun () ->
                            s.Value |> ignore
                            runs.Value <- runs.Value + 1

                            if runs.Value = 2 then
                                self.Value.Dispose ()
                    )

                Expect.equal runs.Value 1 "first run"
                s.Value <- 1
                Expect.equal runs.Value 2 "second run, which disposed it"

                s.Value <- 2
                Expect.equal runs.Value 2 "a disposed effect does not hear the third write"
                Expect.equal s.ObserverCount 0 "and is unlinked from what it read"
            }

            test "observers are notified in reverse subscription order" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let order = ResizeArray ()

                for i in 1..3 do
                    new Effect (
                        g,
                        fun () ->
                            s.Value |> ignore
                            order.Add i
                    )
                    |> ignore

                order.Clear ()
                s.Value <- 1

                // Backwards, because `NotifyDirty` walks the observer array
                // from the end: that is the iteration that survives an observer
                // removing itself mid-walk. Nothing depends on the order being
                // *this* one, but something depends on it being fixed, and a
                // test that queues one effect behind another has to know which
                // way round it is.
                Expect.sequenceEqual order [ 3; 2; 1 ] "last subscribed runs first"
            }

            test "an effect that disposes the one queued behind it stops it running" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let victimRuns = ref 0

                // Subscribed first, so notified last: this is the one still
                // sitting in the queue when the disposer runs.
                let victim =
                    new Effect (
                        g,
                        fun () ->
                            s.Value |> ignore
                            victimRuns.Value <- victimRuns.Value + 1
                    )

                new Effect (
                    g,
                    fun () ->
                        if s.Value > 0 then
                            victim.Dispose ()
                )
                |> ignore

                Expect.equal victimRuns.Value 1 "constructed and run once"

                // Both are dirtied by this write. The disposer runs first and
                // kills the other before the flush reaches it.
                s.Value <- 1
                Expect.equal victimRuns.Value 1 "a disposed effect must not be executed out of the queue"
            }

            test "an effect that writes the signal it reads settles rather than looping" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        runs.Value <- runs.Value + 1

                        if s.Value < 3 then
                            s.Value <- s.Value + 1
                )
                |> ignore

                // If this ever fails it will hang rather than assert, which is
                // itself the finding.
                Expect.equal s.Value 3 "it climbs to the fixpoint"
                Expect.isLessThan runs.Value 10 "and stops there"
            }

            test "a write inside a cleanup is seen by the next run" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let other = Signal (g, 0)
                let seen = ResizeArray ()

                new Effect (
                    g,
                    fun () ->
                        seen.Add (trigger.Value, other.Value)
                        g.OnCleanup (fun () -> other.Value <- other.Value + 1)
                )
                |> ignore

                Expect.sequenceEqual seen [ (0, 0) ] "first run"

                trigger.Value <- 1
                Expect.sequenceEqual seen [ (0, 0); (1, 1) ] "the cleanup's write is visible to the re-run it precedes"
            }

            test "a nested batch does not flush until the outer one ends" {
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

                Expect.equal runs.Value 1 "first run"

                g.Batch (fun () ->
                    s.Value <- 1

                    g.Batch (fun () ->
                        s.Value <- 2
                        Expect.equal runs.Value 1 "nothing has flushed inside the inner batch")

                    Expect.equal runs.Value 1 "and the inner batch ending did not flush either")

                Expect.equal runs.Value 2 "one run for the whole nest"
                Expect.equal s.Value 2 "with the last write winning"
            }

            test "a batch that throws still flushes the writes it made" {
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

                let threw =
                    try
                        g.Batch (fun () ->
                            s.Value <- 1
                            failwith "boom")
                        |> ignore

                        false
                    with _ ->
                        true

                Expect.isTrue threw "the exception is not swallowed"
                Expect.equal s.Value 1 "the write happened"

                // Whichever way this lands, it should not be a graph that is
                // stuck in batching mode for ever.
                s.Value <- 2
                Expect.equal s.Value 2 "and the graph still takes writes afterwards"
                Expect.isGreaterThan runs.Value 1 "and still runs effects afterwards"
            }

            test "flushing from inside an effect body is absorbed" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                new Effect (
                    g,
                    fun () ->
                        s.Value |> ignore
                        runs.Value <- runs.Value + 1
                        // Re-entering the drain must not run this effect again
                        // from inside itself.
                        g.Flush ()
                )
                |> ignore

                Expect.equal runs.Value 1 "construction ran it once"
                s.Value <- 1
                Expect.equal runs.Value 2 "and the write ran it once more"
            }

            test "an untracked body that throws does not leave tracking off" {
                let g = new Graph ()
                let s = Signal (g, 0)

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            try
                                g.Untrack (fun () -> failwith "boom")
                            with _ ->
                                ()

                            // If the failed `Untrack` left tracking suppressed,
                            // this read records no edge and the memo goes deaf.
                            s.Value
                    )

                Expect.equal m.Value 0 "precondition"
                s.Value <- 1
                Expect.equal m.Value 1 "the read after the throw was tracked"
            }

            test "disposing a root from inside an effect it owns does not strand the flush" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let after = ref 0

                let owner =
                    g.CreateRoot (fun owner ->
                        new Effect (
                            g,
                            fun () ->
                                if s.Value > 0 then
                                    owner.Dispose ()
                        )
                        |> ignore

                        owner)

                new Effect (
                    g,
                    fun () ->
                        s.Value |> ignore
                        after.Value <- after.Value + 1
                )
                |> ignore

                s.Value <- 1
                Expect.isTrue owner.IsDisposed "the root tore itself down"
                Expect.equal after.Value 2 "and the effect queued behind it still ran"
            }

            test "a cleanup registered during a root's teardown runs at once" {
                let g = new Graph ()
                let order = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        g.OnCleanup (fun () ->
                            order.Add "first"
                            g.OnCleanup (fun () -> order.Add "registered during teardown"))

                        owner)

                owner.Dispose ()

                // The teardown runs with the root as the current owner, as a
                // computation's discharge does. The root is already disposed,
                // so the nested registration runs immediately.
                Expect.sequenceEqual order [ "first"; "registered during teardown" ] "the nested cleanup ran inside the teardown"
            }

            test "effects woken by a write in a memo body run after the body, before the read returns" {
                let g = new Graph ()
                let log = ResizeArray ()
                let input = Signal (g, 0)
                let output = Signal (g, 0)

                new Effect (g, (fun () -> log.Add $"effect {output.Value}"))
                |> ignore

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            log.Add "body start"
                            output.Value <- input.Value + 1
                            log.Add "body end"
                            input.Value
                    )

                log.Add "read"
                m.Value |> ignore
                log.Add "returned"

                Expect.sequenceEqual
                    log
                    [ "effect 0"; "read"; "body start"; "body end"; "effect 1"; "returned" ]
                    "the effect ran once the body finished"
            }
        ]
