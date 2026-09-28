module Ranvier.Tests.Scopes

open System
open Expecto
open Ranvier

/// <summary>
/// Ownership edges: disposal happening twice, out of order, from the wrong
/// place, or to something that is already mid-teardown. A reactive graph's
/// lifetime rules are the part a caller is most likely to abuse, because
/// disposal is usually driven by something outside the graph — a component
/// unmounting, a request finishing — and that thing has its own idea of order.
/// </summary>
[<Tests>]
let tests =
    testList
        "Scopes"
        [
            test "disposing a root twice runs its cleanups once" {
                let g = new Graph ()
                let cleanups = ref 0

                let owner =
                    g.CreateRoot (fun owner ->
                        g.OnCleanup (fun () -> cleanups.Value <- cleanups.Value + 1)
                        owner)

                owner.Dispose ()
                owner.Dispose ()
                Expect.equal cleanups.Value 1 "a cleanup is not a thing to run twice"
            }

            test "disposing a parent after a child is not a double teardown" {
                let g = new Graph ()
                let order = ResizeArray ()

                let outer =
                    g.CreateRoot (fun outer ->
                        let inner =
                            g.CreateRoot (fun inner ->
                                g.OnCleanup (fun () -> order.Add "inner")
                                inner)

                        g.OnCleanup (fun () -> order.Add "outer")
                        inner.Dispose ()
                        outer)

                Expect.sequenceEqual order [ "inner" ] "the child went first, on its own"

                outer.Dispose ()
                Expect.sequenceEqual order [ "inner"; "outer" ] "and the parent does not run it again"
            }

            test "a root disposed while an effect inside it is queued cancels that run" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let runs = ref 0

                let owner =
                    g.CreateRoot (fun owner ->
                        new Effect (
                            g,
                            fun () ->
                                s.Value |> ignore
                                runs.Value <- runs.Value + 1
                        )
                        |> ignore

                        owner)

                Expect.equal runs.Value 1 "constructed and run"

                // Dirty it and tear the scope down before anything drains.
                g.Batch (fun () ->
                    s.Value <- 1
                    owner.Dispose ())

                Expect.equal runs.Value 1 "a scheduled effect whose owner died does not run"
            }

            test "a memo disposed while it is stale still reports its last value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, (fun _ -> s.Value * 10))

                Expect.equal m.Value 10 "precondition"

                s.Value <- 2
                // Dirty but not yet read.
                m.Dispose ()

                Expect.equal m.Peek 10 "Peek is the last computed value, disposed or not"
                Expect.equal m.Value 10 "and a disposed memo does not recompute"
            }

            test "disposing the graph twice is a no-op" {
                let g = new Graph ()
                let s = Signal (g, 0)

                new Effect (g, (fun () -> s.Value |> ignore))
                |> ignore

                g.Dispose ()
                g.Dispose ()
                Expect.equal s.ObserverCount 0 "everything is unlinked, once"
            }

            test "a cleanup that throws does not stop the siblings after it" {
                let g = new Graph ()
                let ran = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        g.OnCleanup (fun () -> ran.Add "first")
                        g.OnCleanup (fun () -> failwith "boom")
                        g.OnCleanup (fun () -> ran.Add "third")
                        owner)

                owner.Dispose ()
                Expect.contains ran "first" "the one registered before the thrower ran"
                Expect.contains ran "third" "and so did the one registered after it"
                Expect.isNonEmpty owner.Errors "and the failure was recorded rather than lost"
            }

            test "an effect disposed during its own first run is not left linked" {
                let g = new Graph ()
                let s = Signal (g, 0)
                let self = ref Unchecked.defaultof<Effect>

                self.Value <-
                    new Effect (
                        g,
                        fun () ->
                            s.Value |> ignore

                            if not (isNull (box self.Value)) then
                                self.Value.Dispose ()
                    )

                // On the first run `self` is still null, so this only disposes
                // from the second run onwards — which is the point: the write
                // below is what triggers it, mid-flush.
                s.Value <- 1
                Expect.equal s.ObserverCount 0 "disposal from inside the body unlinks it"
            }

            test "nesting a root inside an effect ties it to that effect's run" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let cleanups = ref 0

                new Effect (
                    g,
                    fun () ->
                        trigger.Value |> ignore

                        g.CreateRoot (fun _ -> g.OnCleanup (fun () -> cleanups.Value <- cleanups.Value + 1))
                )
                |> ignore

                Expect.equal cleanups.Value 0 "nothing torn down yet"

                trigger.Value <- 1
                Expect.equal cleanups.Value 1 "the re-run disposed the root the previous run created"

                trigger.Value <- 2
                Expect.equal cleanups.Value 2 "and again"
            }
        ]
