module Ranvier.Tests.Owners

open System
open Expecto
open Ranvier

[<Tests>]
let tests =
    testList
        "Owners"
        [
            test "disposing a root disposes the effects created inside it" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let runs = ref 0

                let owner =
                    g.CreateRoot (fun owner ->
                        new Effect (
                            g,
                            (fun () ->
                                s.Value |> ignore
                                incr runs)
                        )
                        |> ignore

                        owner)

                Expect.equal runs.Value 1 "construction"
                s.Value <- 2
                Expect.equal runs.Value 2 "still live"

                owner.Dispose ()
                s.Value <- 3
                Expect.equal runs.Value 2 "the scope took the effect with it"
            }

            test "a cleanup runs before the next run of the same effect" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                new Effect (
                    g,
                    fun () ->
                        let v = s.Value
                        log.Add $"open %d{v}"
                        g.OnCleanup (fun () -> log.Add $"close %d{v}")
                )
                |> ignore

                s.Value <- 2

                Expect.sequenceEqual log [ "open 1"; "close 1"; "open 2" ] "the first run must be closed before the second opens"
            }

            test "a cleanup runs on disposal" {
                let g = new Graph ()
                let log = ResizeArray ()
                let e = new Effect (g, (fun () -> g.OnCleanup (fun () -> log.Add "closed")))

                Expect.isEmpty log "not yet"
                e.Dispose ()
                Expect.sequenceEqual log [ "closed" ] "disposal discharges the scope"
            }

            test "nested roots are torn down depth-first" {
                let g = new Graph ()
                let log = ResizeArray ()

                let outer =
                    g.CreateRoot (fun outer ->
                        g.OnCleanup (fun () -> log.Add "outer")

                        g.CreateRoot (fun _ -> g.OnCleanup (fun () -> log.Add "inner"))

                        outer)

                outer.Dispose ()
                Expect.sequenceEqual log [ "outer"; "inner" ] "own cleanups first, then children"
            }

            test "teardown order is last-created-first" {
                let g = new Graph ()
                let log = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        g.OnCleanup (fun () -> log.Add "first")
                        g.OnCleanup (fun () -> log.Add "second")
                        owner)

                owner.Dispose ()
                Expect.sequenceEqual log [ "second"; "first" ] "reverse of construction order"
            }

            test "a throwing cleanup does not strand the rest of the scope" {
                let g = new Graph ()
                let log = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        g.OnCleanup (fun () -> log.Add "ran")
                        g.OnCleanup (fun () -> failwith "boom")
                        owner)

                owner.Dispose ()
                Expect.sequenceEqual log [ "ran" ] "the surviving cleanup still ran"
                Expect.equal (Seq.length owner.Errors) 1 "and the failure was recorded"
            }

            test "a cleanup registered on a disposed owner runs at once" {
                let owner = new Owner ()
                owner.Dispose ()
                let mutable ran = 0
                owner.OnCleanup (fun () -> ran <- ran + 1)
                Expect.equal ran 1 "the cleanup ran on registration"
                owner.Dispose ()
                Expect.equal ran 1 "a later disposal does not run it again"
            }

            test "a throwing cleanup registered on a disposed owner is recorded" {
                let owner = new Owner ()
                owner.Dispose ()
                owner.OnCleanup (fun () -> failwith "boom")
                Expect.equal (Seq.length owner.Errors) 1 "the failure was recorded"
            }

            test "disposal is idempotent" {
                let g = new Graph ()
                let log = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        g.OnCleanup (fun () -> log.Add "once")
                        owner)

                owner.Dispose ()
                owner.Dispose ()
                Expect.sequenceEqual log [ "once" ] "the second disposal is a no-op"
                Expect.isTrue owner.IsDisposed "and it is marked disposed"
            }

            test "an effect created outside a root belongs to the graph root" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let runs = ref 0

                new Effect (
                    g,
                    (fun () ->
                        s.Value |> ignore
                        incr runs)
                )
                |> ignore

                s.Value <- 2
                Expect.equal runs.Value 2 "live"

                // No explicit root was created, so this must still be reachable
                // for disposal — an unowned effect would leak instead.
                (g :> IDisposable).Dispose()
                s.Value <- 3
                Expect.equal runs.Value 2 "disposing the graph disposes it"
            }
        ]
