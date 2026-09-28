module Ranvier.Tests.Effects

open Expecto
open Ranvier

[<Tests>]
let tests =
    testList
        "Effects"
        [
            test "an effect runs once on construction" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()
                let e = new Effect (g, (fun () -> seen.Add s.Value))

                Expect.sequenceEqual seen [ 1 ] "the body must have run"
                Expect.equal e.Runs 1 "exactly once"
            }

            test "a write re-runs the effect" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()
                let e = new Effect (g, (fun () -> seen.Add s.Value))

                s.Value <- 2
                s.Value <- 3

                Expect.sequenceEqual seen [ 1; 2; 3 ] "every write must be observed"
                Expect.equal e.Runs 3 "one run per write"
            }

            test "a batch collapses writes into one run" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let t = Signal (g, 10)
                let seen = ResizeArray ()

                let e = new Effect (g, (fun () -> seen.Add (s.Value + t.Value)))

                g.Batch (fun () ->
                    s.Value <- 2
                    t.Value <- 20)

                Expect.sequenceEqual seen [ 11; 22 ] "the intermediate 12 must never be observed"
                Expect.equal e.Runs 2 "construction plus one flush"
            }

            test "a suspended effect does not run its side effect" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let seen = ResizeArray ()
                let e = new Effect (g, (fun () -> seen.Add a.Value))

                Expect.isEmpty seen "the body aborted before reaching the side effect"
                Expect.equal e.Status Status.Pending "the effect is pending"

                Expect.sequenceEqual (e.PendingSources |> Seq.map (fun n -> n.Id)) [ (a :> INode).Id ] "it must record what it waits on"

                a.Settle 7
                Expect.sequenceEqual seen [ 7 ] "the settle must run the side effect exactly once"
            }

            test "a settle wakes an effect suspended behind a memo" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)

                let m = Make.Memo (g, (fun _ -> a.Value * 2))

                let seen = ResizeArray ()
                let e = new Effect (g, (fun () -> seen.Add m.Value))

                Expect.isEmpty seen "pending crosses the memo"
                a.Settle 5
                Expect.sequenceEqual seen [ 10 ] "and so does the settle"
                Expect.equal e.Status Status.None "the effect is settled"
            }

            test "a throwing effect does not strand the ones queued behind it" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()
                let bad = new Effect (g, (fun () -> failwithf "boom %d" s.Value))
                let good = new Effect (g, (fun () -> seen.Add s.Value))

                s.Value <- 2

                Expect.sequenceEqual seen [ 1; 2 ] "the second effect must still run"
                Expect.equal bad.Status Status.Error "the failure is recorded"
                Expect.equal bad.Error.Message "boom 2" "and the error is readable"
            }

            test "dependencies are re-collected, so a dropped source stops waking it" {
                let g = new Graph ()
                let switch = Signal (g, true)
                let tracked = Signal (g, 1)

                let e =
                    new Effect (
                        g,
                        (fun () ->
                            if switch.Value then
                                tracked.Value |> ignore)
                    )

                Expect.equal e.Runs 1 "construction"

                switch.Value <- false
                Expect.equal e.Runs 2 "the switch is still a dependency"

                // `tracked` was not read on the last run, so it must have been
                // dropped as a dependency.
                tracked.Value <- 99
                Expect.equal e.Runs 2 "a dropped source must not wake the effect"
            }

            test "a disposed effect stops running" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let e = new Effect (g, (fun () -> s.Value |> ignore))

                Expect.equal e.Runs 1 "construction"
                e.Dispose ()

                s.Value <- 2
                Expect.equal e.Runs 1 "disposal detaches it from the graph"
            }

            test "an effect writing a signal is absorbed by the running flush" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let t = Signal (g, 0)

                let source = new Effect (g, (fun () -> t.Value <- s.Value + 1))

                let seen = ResizeArray ()
                let sink = new Effect (g, (fun () -> seen.Add t.Value))

                Expect.sequenceEqual seen [ 1 ] "the write during construction is seen"

                s.Value <- 10
                Expect.sequenceEqual seen [ 1; 11 ] "the cascade resolves within one flush"
                Expect.equal sink.Runs 2 "and does not re-enter the flush loop"
            }

            test "effectOn runs no action before a pending read settles" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let seen = ResizeArray ()

                g.Run (fun () -> createEffectOn (fun () -> a.Value) (fun v -> seen.Add v))

                Expect.isEmpty seen "the action waits for the read"

                a.Settle 5
                Expect.sequenceEqual seen [ 5 ] "the action runs once, after the settle"
            }

            test "effectOn skips the action when the computed value is equal" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                g.Run (fun () -> createEffectOn (fun () -> s.Value % 2) (fun v -> seen.Add v))

                s.Value <- 3
                s.Value <- 4

                Expect.sequenceEqual seen [ 1; 0 ] "the write to 3 computes an equal value"
            }

            test "effectOn does not track reads in the action" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let t = Signal (g, 10)
                let seen = ResizeArray ()

                g.Run (fun () -> createEffectOn (fun () -> s.Value) (fun v -> seen.Add (v + t.Value)))

                t.Value <- 20
                s.Value <- 2

                Expect.sequenceEqual seen [ 11; 22 ] "the write to t re-runs nothing"
            }

            test "effectOn runs the action's cleanups before the next action" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                g.Run (fun () ->
                    createEffectOn (fun () -> s.Value) (fun v ->
                        log.Add $"act {v}"
                        onCleanup (fun () -> log.Add $"clean {v}")))

                s.Value <- 2

                Expect.sequenceEqual log [ "act 1"; "clean 1"; "act 2" ] "cleanup precedes the next action"
            }

            test "effectOn raises when compute creates a node" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                g.Run (fun () ->
                    createEffectOn
                        (fun () ->
                            createEffect ignore
                            s.Value)
                        (fun v -> seen.Add v))

                Expect.isEmpty seen "a compute that fails runs no action"
            }
        ]
