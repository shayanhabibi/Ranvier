module Ranvier.Tests.AmbientGraph

open System.Threading.Tasks
open Expecto
open Ranvier

/// <summary>
/// The graph the module-level functions resolve against while the engine runs a body or a cleanup.
/// </summary>
[<Tests>]
let tests =
    testList
        "AmbientGraph"
        [
            test "an effect woken by a write outside the graph registers its cleanups" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                let e =
                    g.Run (fun () ->
                        new Effect (
                            g,
                            fun () ->
                                let v = s.Value
                                log.Add $"show {v}"
                                onCleanup (fun () -> log.Add $"hide {v}")
                        ))

                s.Value <- 2
                s.Value <- 3

                Expect.isNull e.Error "the run raised nothing"
                Expect.sequenceEqual log [ "show 1"; "hide 1"; "show 2"; "hide 2"; "show 3" ] "every run's cleanup ran"
            }

            test "an effect woken by a settle outside the graph creates nodes" {
                let g = new Graph ()
                let a = AsyncSource<int>(g)
                let seen = ResizeArray ()

                let e =
                    g.Run (fun () ->
                        new Effect (
                            g,
                            fun () ->
                                let v = a.Value
                                let doubled = createMemo (fun () -> v * 2)
                                seen.Add doubled.Value
                        ))

                a.Settle 4

                Expect.isNull e.Error "the run raised nothing"
                Expect.sequenceEqual seen [ 8 ] "the node was created in the run"
            }

            test "an owning memo pulled outside the graph creates nodes" {
                let g = new Graph ()
                let s = Signal (g, 1)

                let m =
                    g.Run (fun () -> createMemoWith (fun () -> (createMemo (fun () -> s.Value + 1)).Value))

                s.Value <- 5

                Expect.equal m.Value 6 "the pull ran the body with its graph ambient"
            }

            test "a cleanup run outside the graph reaches its graph" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let inner = ref 0

                g.Run (fun () ->
                    createEffect (fun () ->
                        s.Value |> ignore
                        onCleanup (fun () -> untrack (fun () -> inner.Value <- inner.Value + 1))))

                s.Value <- 2

                Expect.equal inner.Value 1 "the cleanup's untrack ran"
            }

            test "a body sees its own graph while another is ambient" {
                let g = new Graph ()
                let other = new Graph ()
                let s = Signal (g, 1)
                let seen = ResizeArray ()

                g.Run (fun () -> createEffect (fun () -> seen.Add (obj.ReferenceEquals (Graph.Current, g), s.Value)))

                use _ = other.Activate ()
                s.Value <- 2

                Expect.sequenceEqual seen [ (true, 1); (true, 2) ] "the effect ran against g"
                Expect.isTrue (obj.ReferenceEquals (Graph.Current, other)) "the caller's graph is restored"
            }

            test "a body that pulls another graph's memo keeps its own graph" {
                let a = new Graph ()
                let b = new Graph ()
                let sa = Signal (a, 0)
                let sb = Signal (b, 1)

                let m =
                    b.Run (fun () -> createMemoWith (fun () -> (createMemo (fun () -> sb.Value * 10)).Value))

                let seen = ResizeArray ()

                a.Run (fun () ->
                    createEffect (fun () ->
                        ignore sa.Value
                        let v = m.Value
                        seen.Add (v, obj.ReferenceEquals (Graph.Current, a))))

                sb.Value <- 2
                sa.Value <- 1

                Expect.sequenceEqual seen [ (10, true); (20, true) ] "a is ambient again after b's memo recomputed"
            }
        ]
