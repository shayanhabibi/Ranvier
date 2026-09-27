module Ranvier.Tests.Tracing

open System.Reflection
open System.Threading.Tasks
open Expecto
open Ranvier

let private traced () =
    typeof<Graph>.Assembly.GetCustomAttributes(typeof<AssemblyMetadataAttribute>, false)
    |> Array.exists (fun a ->
        let a = a :?> AssemblyMetadataAttribute
        a.Key = "RanvierTrace" && a.Value = "true")

exception private Boom

#if RANVIER_TRACE
let private ofKind (kind: TraceEventKind) (g: Graph) =
    Trace.events g |> Array.filter (fun e -> e.Kind = kind)

let private labels (g: Graph) =
    ofKind TraceEventKind.Label g
    |> Array.map (fun e -> e.Node, string e.Payload)
#endif

[<Tests>]
let tests =
    testList
        "Tracing"
        [
            test "the assembly is traced exactly when RANVIER_TRACE is defined" {
#if RANVIER_TRACE
                let expected = true
#else
                let expected = false
#endif
                Expect.equal (traced ()) expected "AssemblyMetadata(\"RanvierTrace\") must match the test build's define"
            }

            test "named returns the thunk's result" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Trace.named "total" (fun () -> Signal (g, 3))
                Expect.equal s.Value 3 "named must return what the thunk returns"
                Expect.equal (Trace.named "answer" (fun () -> 42)) 42 "named must return a plain value"
            }

#if RANVIER_TRACE
            test "a new graph records GraphNew first, naming the root owner" {
                use g = new Graph ()
                let events = Trace.events g
                Expect.isNonEmpty events "a graph must record its construction"
                let first = events[0]
                Expect.equal first.Kind TraceEventKind.GraphNew "the first event must be GraphNew"
                Expect.equal first.Seq 1 "the clock must start at 1"
                Expect.equal first.Other 1 "the root owner must take owner id 1"
            }

            test "named pops its label when the thunk throws" {
                use g = new Graph ()
                use _ = g.Activate ()

                let result =
                    Trace.named "outer" (fun () ->
                        try
                            Trace.named "inner" (fun () -> raise Boom)
                        with Boom ->
                            ()

                        7)

                Expect.equal result 7 "the outer thunk must complete"

                let labels =
                    Trace.events g
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.Label)
                    |> Array.map (fun e -> e.Node, string e.Payload)

                Expect.equal labels [| 0, "inner"; 0, "outer" |] "each label must close once, innermost first"
            }

            test "an Unchecked graph creating nodes from two threads records every NodeNew" {
                use g = new Graph ({ GraphOptions.Default with ThreadAffinity = Unchecked })

                let work () =
                    for _ in 1..20000 do
                        Signal (g, 0) |> ignore

                Task.WaitAll (Task.Run work, Task.Run work)

                Expect.equal (ofKind TraceEventKind.NodeNew g).Length 40000 "each construction must record one NodeNew"
            }

            test "createRoot records OwnerNew Flag 1 under the graph root" {
                use g = new Graph ()
                use _ = g.Activate ()
                createRoot ignore
                let created = ofKind TraceEventKind.OwnerNew g |> Array.exactlyOne
                Expect.equal created.Other 1 "the scope's parent must be the root owner"
                Expect.equal created.Flag 1 "a createRoot scope must be flagged"
                Expect.equal created.Arg 0 "a createRoot scope has no host node"
            }

            test "new Owner() records OwnerNew when appended" {
                use g = new Graph ()
                let owner = new Owner ()
                Expect.isEmpty (ofKind TraceEventKind.OwnerNew g) "an unattached owner must not be recorded"
                g.Root.Attach owner
                let created = ofKind TraceEventKind.OwnerNew g |> Array.exactlyOne
                Expect.equal (created.Other, created.Flag) (1, 0) "the owner must join under the root, unflagged"
                owner.Dispose ()

                Expect.equal
                    (ofKind TraceEventKind.OwnerDispose g |> Array.map (fun e -> e.Node))
                    [| created.Node |]
                    "disposing the owner must record OwnerDispose"
            }

            test "a run scope's OwnerNew names its host node" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let m =
                    createMemoWith (fun () ->
                        createMemo (fun () -> 0) |> ignore
                        s.Value)

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore
                let scope = ofKind TraceEventKind.OwnerNew g |> Array.exactlyOne
                Expect.equal scope.Arg (m :> INode).Id "the run scope must name the memo as its host"
                Expect.equal scope.Other 1 "the run scope's parent must be the memo's owner"

                let discharge =
                    Trace.events g
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.DischargeStart || e.Kind = TraceEventKind.DischargeEnd)
                    |> Array.map (fun e -> e.Kind, e.Node, e.Other)

                Expect.equal
                    discharge
                    [|
                        TraceEventKind.DischargeStart, scope.Node, scope.Arg
                        TraceEventKind.DischargeEnd, scope.Node, scope.Arg
                    |]
                    "the re-run must discharge the scope once, naming its host"
            }

            test "each node kind records NodeNew with its TraceNodeKind" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                createAsyncSource<int> () |> ignore
                let m = createMemo (fun () -> s.Value)
                createEffect (fun () -> m.Value |> ignore)
                createAsync (fun _ -> Task.FromResult 1) |> ignore
                let b = createSuspense (fun _ -> 0) (fun () -> s.Value)
                b.Value |> ignore
                let p = createProjection id (fun x -> x * 10) (fun () -> [ s.Value ])
                p.Keys |> ignore
                let l = createSelector (fun () -> s.Value)
                createEffect (fun () -> l.Get 1 |> ignore)

                let kinds =
                    ofKind TraceEventKind.NodeNew g
                    |> Array.map (fun e -> enum<TraceNodeKind> e.Arg)
                    |> Array.distinct
                    |> Array.sort

                Expect.equal
                    kinds
                    [|
                        TraceNodeKind.Signal
                        TraceNodeKind.AsyncSource
                        TraceNodeKind.Memo
                        TraceNodeKind.Effect
                        TraceNodeKind.AsyncMemo
                        TraceNodeKind.Boundary
                        TraceNodeKind.Projection
                        TraceNodeKind.ProjectionBeacon
                        TraceNodeKind.RowWatch
                        TraceNodeKind.LookupCell
                    |]
                    "every node kind must be recorded"
            }

            test "named labels the first node only" {
                use g = new Graph ()
                use _ = g.Activate ()

                let a, _ =
                    Trace.named "first" (fun () -> createSignal 1, createSignal 2)

                Expect.equal (labels g) [| (a :> INode).Id, "first" |] "only the first node must take the label"
            }

            test "nested named labels each first node" {
                use g = new Graph ()
                use _ = g.Activate ()

                let a, b =
                    Trace.named "outer" (fun () ->
                        let a = createSignal 1
                        a, Trace.named "inner" (fun () -> createSignal 2))

                Expect.equal
                    (labels g)
                    [| (a :> INode).Id, "outer"; (b :> INode).Id, "inner" |]
                    "each label must go to the first node its own thunk creates"
            }

            test "a projection takes its label ahead of the nodes it creates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let p = Trace.named "rows" (fun () -> createProjection id id (fun () -> [ 1 ]))
                Expect.equal (labels g) [| (p :> INode).Id, "rows" |] "the projection itself must take the label"
            }

            test "a thunk creating no node records an unused Label" {
                use g = new Graph ()
                use _ = g.Activate ()
                Trace.named "none" (fun () -> 5) |> ignore
                Expect.equal (labels g) [| 0, "none" |] "the unused label must be recorded with Node 0"
            }

            test "origin reports a node's kind, label and owner" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = Trace.named "total" (fun () -> createMemo (fun () -> 1))
                let origin = Trace.origin g m

                Expect.equal
                    (origin.Node, origin.Kind, origin.Label, origin.Owner, origin.Run)
                    ((m :> INode).Id, TraceNodeKind.Memo, Some "total", 1, 0)
                    "the origin must describe the memo's NodeNew"

                let created = (Trace.events g)[origin.Seq - 1]
                Expect.equal created.Kind TraceEventKind.NodeNew "Seq must point at the NodeNew"
            }
#endif
        ]
