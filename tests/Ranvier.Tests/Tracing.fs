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

[<CustomEquality; NoComparison>]
type private ThrowingEquals =
    | ThrowingEquals of int

    override _.Equals(_: obj) =
        raise Boom

    override _.GetHashCode() = 0

#if RANVIER_TRACE
let private ofKind (kind: TraceEventKind) (g: Graph) =
    Trace.events g
    |> Array.filter (fun e -> e.Kind = kind)

let private labels (g: Graph) =
    ofKind TraceEventKind.Label g
    |> Array.map (fun e -> e.Node, string e.Payload)

/// <summary>The puller named by the last <c>RunStart</c> of node <c>id</c>.</summary>
let private pullerOf (g: Graph) (id: int) =
    let run =
        ofKind TraceEventKind.RunStart g
        |> Array.findBack (fun e -> e.Node = id)

    run.Other

/// <summary>The ids of the nodes of <c>kind</c>, in creation order.</summary>
let private idsOf (kind: TraceNodeKind) (g: Graph) =
    ofKind TraceEventKind.NodeNew g
    |> Array.filter (fun e -> e.Arg = int kind)
    |> Array.map (fun e -> e.Node)

let private siteOf (g: Graph) (node: INode) =
    string (Trace.origin g node).Site

let private here (line: string) =
    System.IO.Path.GetFileName __SOURCE_FILE__
    + ":"
    + line
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
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            ThreadAffinity = Unchecked
                        }
                    )

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

                let created =
                    ofKind TraceEventKind.OwnerNew g
                    |> Array.exactlyOne

                Expect.equal created.Other 1 "the scope's parent must be the root owner"
                Expect.equal created.Flag 1 "a createRoot scope must be flagged"
                Expect.equal created.Arg 0 "a createRoot scope has no host node"
            }

            test "new Owner() records OwnerNew when appended" {
                use g = new Graph ()
                let owner = new Owner ()
                Expect.isEmpty (ofKind TraceEventKind.OwnerNew g) "an unattached owner must not be recorded"
                g.Root.Attach owner

                let created =
                    ofKind TraceEventKind.OwnerNew g
                    |> Array.exactlyOne

                Expect.equal (created.Other, created.Flag) (1, 0) "the owner must join under the root, unflagged"
                owner.Dispose ()

                Expect.equal
                    (ofKind TraceEventKind.OwnerDispose g
                     |> Array.map (fun e -> e.Node))
                    [| created.Node |]
                    "disposing the owner must record OwnerDispose"
            }

            test "a run scope's OwnerNew names its host node" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let m =
                    createMemoWith (fun _ ->
                        createMemo (fun _ -> 0) |> ignore
                        s.Value)

                m.Value |> ignore
                s.Value <- 2
                m.Value |> ignore

                let scope =
                    ofKind TraceEventKind.OwnerNew g
                    |> Array.exactlyOne

                Expect.equal scope.Arg (m :> INode).Id "the run scope must name the memo as its host"
                Expect.equal scope.Other 1 "the run scope's parent must be the memo's owner"

                let discharge =
                    Trace.events g
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.DischargeStart
                        || e.Kind = TraceEventKind.DischargeEnd)
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
                createAsyncSource<int>() |> ignore
                let m = createMemo (fun _ -> s.Value)
                createEffect (fun () -> m.Value |> ignore)
                createAsync (fun _ _ -> Task.FromResult 1) |> ignore
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

                let a, _ = Trace.named "first" (fun () -> createSignal 1, createSignal 2)

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

            test "label gives a node computed text as its path segment" {
                use g = new Graph ()
                use _ = g.Activate ()
                let i = 7
                let a = createSignal 1
                Trace.label (g, a, $"row {i}")
                let s = Trace.snapshot g

                Expect.equal s.Nodes[(a :> INode).Id].Path "/row 7" "the label must replace the node's segment"
                Expect.equal s.Nodes[(a :> INode).Id].Label (Some "row 7") "the snapshot must carry the label"
                Expect.equal (Trace.origin g a).Label (Some "row 7") "origin must report the label"
                Expect.equal (Trace.resolve g "/row 7") (Some (a :> INode).Id) "the labelled path must resolve"
            }

            test "label moves the paths beneath the node" {
                use g = new Graph ()
                use _ = g.Activate ()
                let count = createSignal 1
                let mutable inner = Unchecked.defaultof<Memo<int>>

                let m =
                    Trace.named "outer" (fun () ->
                        createMemoWith (fun _ ->
                            inner <- Trace.named "inner" (fun () -> createMemo (fun _ -> 0))
                            count.Value))

                m.Value |> ignore
                let before = (Trace.snapshot g).Nodes[(inner :> INode).Id].Path
                Trace.label (g, m, "renamed")
                let after = (Trace.snapshot g).Nodes[(inner :> INode).Id].Path

                Expect.equal (before, after) ("/outer/inner", "/renamed/inner") "a child path must follow its host's label"
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
                let m = Trace.named "total" (fun () -> createMemo (fun _ -> 1))
                let origin = Trace.origin g m

                Expect.equal
                    (origin.Node, origin.Kind, origin.Label, origin.Owner, origin.Run)
                    ((m :> INode).Id, TraceNodeKind.Memo, Some "total", 1, 0)
                    "the origin must describe the memo's NodeNew"

                let created = (Trace.events g)[origin.Seq - 1]
                Expect.equal created.Kind TraceEventKind.NodeNew "Seq must point at the NodeNew"
            }

            test "origin reports the owner chain and the identity path" {
                use g = new Graph ()
                use _ = g.Activate ()
                let mutable inner = Unchecked.defaultof<Memo<int>>

                createRoot (fun _ -> inner <- Trace.named "inner" (fun () -> createMemo (fun _ -> 1)))
                |> ignore

                let origin = Trace.origin g inner
                Expect.equal origin.Path "/root#0/inner" "the path runs through the createRoot owner"
                Expect.equal origin.Owners [ origin.Owner; 1 ] "the chain climbs from the owner to the graph root"
            }

            test "folded edges equal live sources after a diamond, a dynamic dependency switch and a dispose" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 1
                let flag = createSignal true
                let b = createMemo (fun _ -> a.Value + 1)
                let c = createMemo (fun _ -> a.Value * 2)
                let d = createMemo (fun _ -> b.Value + c.Value)
                let e = createMemo (fun _ -> if flag.Value then b.Value else c.Value)
                d.Value + e.Value |> ignore
                a.Value <- 2
                d.Value + e.Value |> ignore
                Expect.isEmpty (Trace.reconcile g) "the diamond's edges must fold to the live lists"
                flag.Value <- false
                e.Value |> ignore
                Expect.isEmpty (Trace.reconcile g) "the switched branch's edges must fold to the live lists"
                d.Dispose ()
                Expect.isEmpty (Trace.reconcile g) "the disposed memo's edges must fold away"
                Expect.isNonEmpty (ofKind TraceEventKind.EdgeRemove g) "the switch and the dispose must remove edges"
            }

            test "a RowWatch subscription records ObserverAdd" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let flight = createAsyncSource<int>()
                let proj = createProjection id (fun _ -> flight.Value) (fun () -> items.Value)

                createEffect (fun () ->
                    for key in proj.Keys do
                        try
                            proj.Get key |> ignore
                        with :? NotReadyException ->
                            ())

                let watches =
                    ofKind TraceEventKind.NodeNew g
                    |> Array.filter (fun e -> e.Arg = int TraceNodeKind.RowWatch)
                    |> Array.map (fun e -> e.Node)
                    |> Set.ofArray

                Expect.isTrue
                    (ofKind TraceEventKind.ObserverAdd g
                     |> Array.exists (fun e -> watches.Contains e.Other))
                    "the pending row must record the row watch as an observer"
            }

            test "a write marks each reader with the Write as Cause" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let m1 = createMemo (fun _ -> s.Value + 1)
                let m2 = createMemo (fun _ -> s.Value + 2)
                m1.Value + m2.Value |> ignore
                s.Value <- 5
                let write = (ofKind TraceEventKind.Write g)[0]
                let marks = ofKind TraceEventKind.Mark g

                Expect.equal
                    (marks
                     |> Array.map (fun e -> e.Node, e.Arg, e.Cause)
                     |> Set.ofArray)
                    (set [ (m1 :> INode).Id, 2, write.Seq; (m2 :> INode).Id, 2, write.Seq ])
                    "each reader must be marked dirty with the Write as Cause"
            }

            test "a reader writing its own source records MarkSkip" {
                use g = new Graph ()
                use _ = g.Activate ()
                let t = createSignal 0
                let s = createSignal 0
                let m = createMemo (fun _ -> s.Value * 2)
                let mutable reader = 0

                createEffect (fun () ->
                    s.Value <- t.Value
                    m.Value |> ignore)

                reader <- (ofKind TraceEventKind.NodeNew g |> Array.last).Node
                t.Value <- 1

                Expect.exists
                    (ofKind TraceEventKind.MarkSkip g)
                    (fun e -> e.Node = reader && e.Other = (m :> INode).Id)
                    "the memo's notification must skip the running effect"
            }

            test "a write inside batch schedules with no RunStart until BatchExit" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let effect = (ofKind TraceEventKind.NodeNew g |> Array.last).Node
                let before = (Trace.events g).Length
                batch (fun () -> s.Value <- 1)
                let window = (Trace.events g)[before..]

                let at kind =
                    window
                    |> Array.findIndex (fun e -> e.Kind = kind && (e.Node = effect || e.Node = 0))

                let schedule = at TraceEventKind.Schedule
                let exit = at TraceEventKind.BatchExit
                let start = at TraceEventKind.RunStart
                Expect.isTrue (schedule < exit && exit < start) "Schedule, then BatchExit, then RunStart"
            }

            test "a write inside a memo records the memo as Other" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0

                let m =
                    createMemo (fun _ ->
                        s.Value <- 1
                        0)

                m.Value |> ignore
                let write = (ofKind TraceEventKind.Write g)[0]
                let start = (ofKind TraceEventKind.RunStart g)[0]
                Expect.equal (write.Other, write.Cause) ((m :> INode).Id, start.Seq) "the writer and its RunStart"
            }

            test "a node created in a run records that RunStart as Cause" {
                use g = new Graph ()
                use _ = g.Activate ()
                createEffect (fun () -> createSignal 0 |> ignore)
                let start = (ofKind TraceEventKind.RunStart g)[0]
                let created = ofKind TraceEventKind.NodeNew g |> Array.last
                Expect.equal created.Cause start.Seq "the signal's NodeNew must name the effect's RunStart"
            }

            test "a cutoff run ends with Flag 0" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> s.Value % 2)
                m.Value |> ignore
                s.Value <- 3
                m.Value |> ignore
                let ends = ofKind TraceEventKind.RunEnd g
                Expect.equal (ends |> Array.map (fun e -> e.Arg, e.Flag)) [| 0, 1; 0, 0 |] "moved, then cut off"
            }

            test "a throwing body ends Error" {
                use g = new Graph ()
                use _ = g.Activate ()
                createEffect (fun () -> raise Boom)
                let ended = (ofKind TraceEventKind.RunEnd g)[0]
                Expect.equal (enum<RunStatus> ended.Arg) RunStatus.Error "the run must end Error"
            }

            test "a pending body ends Pending" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int>()
                let m = createMemo (fun _ -> source.Value)
                m.TryValue |> ignore
                let ended = (ofKind TraceEventKind.RunEnd g)[0]
                Expect.equal (enum<RunStatus> ended.Arg) RunStatus.Pending "the run must end Pending"
            }

            test "an async memo and a projection record their runs" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createAsync (fun _ _ -> Task.FromResult 1)
                let p = createProjection id id (fun () -> [ 1 ])
                a.TryValue |> ignore
                p.Keys |> ignore

                for node in [ a :> INode; p :> INode ] do
                    let run =
                        ofKind TraceEventKind.RunStart g
                        |> Array.find (fun e -> e.Node = node.Id)

                    Expect.exists
                        (ofKind TraceEventKind.RunEnd g)
                        (fun e -> e.Node = node.Id && e.Cause = run.Seq)
                        "each RunStart must be closed by its RunEnd"
            }

            test "RunStart Cause is the first dirty mark" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 0
                let b = createSignal 0
                let m = createMemo (fun _ -> a.Value + b.Value)
                m.Value |> ignore
                a.Value <- 1
                b.Value <- 1
                m.Value |> ignore
                let first = (ofKind TraceEventKind.Mark g)[0]
                let run = (ofKind TraceEventKind.RunStart g)[1]
                Expect.equal run.Cause first.Seq "the second run's cause must be the first mark"
            }

            test "a re-run after a discharge write is two RunStarts in one flush" {
                use g = new Graph ()
                use _ = g.Activate ()
                let u = createSignal 0
                let s = createSignal 0
                let t = createSignal 0
                createEffect (fun () -> t.Value <- u.Value + s.Value * 0)

                createEffect (fun () ->
                    let v = t.Value
                    onCleanup (fun () -> s.Value <- v + 100))

                let writer = (ofKind TraceEventKind.NodeNew g).[3].Node
                let before = (Trace.events g).Length
                u.Value <- 1
                let window = (Trace.events g)[before..]

                let flush =
                    window
                    |> Array.findIndex (fun e -> e.Kind = TraceEventKind.FlushStart)

                let flushEnd =
                    window
                    |> Array.findIndex (fun e -> e.Kind = TraceEventKind.FlushEnd)

                let inFlush = window[flush..flushEnd]

                Expect.equal
                    (window
                     |> Array.filter (fun e -> e.Kind = TraceEventKind.FlushStart))
                        .Length
                    1
                    "a single write must flush once"

                Expect.exists inFlush (fun e -> e.Kind = TraceEventKind.DischargeStart) "the flush must discharge"

                let starts =
                    inFlush
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.RunStart
                        && e.Node = writer)

                Expect.equal starts.Length 2 "the discharge write must re-run the writer within the flush"
            }

            test "a projection whose keys change ends its run with Flag 1" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal [ 1; 2 ]
                let p = createProjection id id (fun () -> s.Value)
                p.Keys |> ignore
                s.Value <- [ 1; 2; 3 ]
                p.Keys |> ignore
                let node = (p :> INode).Id

                let ended =
                    ofKind TraceEventKind.RunEnd g
                    |> Array.filter (fun e -> e.Node = node)

                Expect.equal (ended |> Array.map (fun e -> e.Flag)) [| 1; 1 |] "both passes publish new keys"
                Expect.exists (ofKind TraceEventKind.Moved g) (fun e -> e.Node = node) "the keys publish records Moved"
            }

            test "an async memo's settle marks its readers with its Moved as cause" {
                use g = new Graph ()
                use _ = g.Activate ()
                let gate = TaskCompletionSource<int>()
                let a = createAsync (fun _ _ -> gate.Task)
                createEffect (fun () -> a.TryValue |> ignore)
                gate.SetResult 5
                let node = (a :> INode).Id

                let moved =
                    ofKind TraceEventKind.Moved g
                    |> Array.findBack (fun e -> e.Node = node)

                let mark =
                    ofKind TraceEventKind.Mark g
                    |> Array.findBack (fun e -> e.Other = node)

                Expect.equal mark.Cause moved.Seq "the settle's Moved causes the mark"
            }

            test "a run left open by a throwing comparer ends Abandoned" {
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> ThrowingEquals s.Value)
                m.Value |> ignore
                s.Value <- 2
                Expect.throws (fun () -> m.Value |> ignore) "the comparer throws out of the run"
                Trace.dumpText g |> ignore
                let node = (m :> INode).Id

                let starts =
                    ofKind TraceEventKind.RunStart g
                    |> Array.filter (fun e -> e.Node = node)

                let ends =
                    ofKind TraceEventKind.RunEnd g
                    |> Array.filter (fun e -> e.Node = node)

                Expect.equal ends.Length starts.Length "every RunStart has its RunEnd"
                Expect.equal (enum<RunStatus> (Array.last ends).Arg) RunStatus.Abandoned "the open run ends Abandoned"
            }

            test "a run left open inside another run ends Abandoned" {
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = StructuralPolicy ()
                        }
                    )

                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> ThrowingEquals s.Value)
                m.Value |> ignore
                s.Value <- 2

                let reader =
                    createMemo (fun _ ->
                        try
                            m.Value |> ignore
                        with Boom ->
                            ())

                reader.Value
                let node = (m :> INode).Id
                let events = Trace.events g

                let abandoned =
                    events
                    |> Array.filter (fun e ->
                        e.Kind = TraceEventKind.RunEnd
                        && e.Node = node
                        && enum<RunStatus> e.Arg = RunStatus.Abandoned)

                Expect.isNonEmpty abandoned "a run left open is closed Abandoned"

                for run in
                    events
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.RunStart && e.Node = node) do
                    Expect.exists
                        events
                        (fun e ->
                            e.Kind = TraceEventKind.RunEnd
                            && e.Cause = run.Seq)
                        "each RunStart is closed"
            }

            test "why ends at UserWrite" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 1
                let b = createMemo (fun _ -> a.Value + 1)
                let d = createMemo (fun _ -> b.Value * 2)
                d.Value |> ignore
                a.Value <- 2
                d.Value |> ignore
                let write = (ofKind TraceEventKind.Write g)[0]
                let why = Trace.why g d
                Expect.equal why.Root (Some (UserWrite write.Seq)) "the chain must end at the user's write"

                Expect.equal
                    (why.Steps |> List.map (fun s -> s.Kind))
                    [
                        TraceEventKind.RunStart
                        TraceEventKind.Mark
                        TraceEventKind.Moved
                        TraceEventKind.RunStart
                        TraceEventKind.Mark
                        TraceEventKind.Write
                    ]
                    "RunStart, Mark, Moved through the memo, back to the Write"

                Expect.equal (Trace.whyDepth g 2 d).Root None "whyDepth stops before the root"
            }

            test "why ends at Created for a first run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = createMemo (fun _ -> 1)
                m.Value |> ignore
                let created = (ofKind TraceEventKind.NodeNew g)[0]
                Expect.equal (Trace.whyAt g m 1).Root (Some (Created created.Seq)) "run 1 roots at NodeNew"
            }

            test "why ends at Pulled for a run with no dirty mark" {
                let events =
                    [|
                        {
                            Seq = 1
                            Kind = TraceEventKind.NodeNew
                            Node = 2
                            Other = 0
                            Arg = 3
                            Flag = 0
                            Cause = 0
                            Payload = null
                        }
                        {
                            Seq = 2
                            Kind = TraceEventKind.RunStart
                            Node = 2
                            Other = 5
                            Arg = 2
                            Flag = 0
                            Cause = 0
                            Payload = null
                        }
                    |]

                Expect.equal (TraceModel.why events null 2 0).Root (Some (Pulled 5)) "the puller is the root"
            }

            test "why ends at BeforeCheckpoint for a cause older than the events" {
                let events =
                    [|
                        {
                            Seq = 40
                            Kind = TraceEventKind.Mark
                            Node = 2
                            Other = 1
                            Arg = 2
                            Flag = 0
                            Cause = 12
                            Payload = null
                        }
                        {
                            Seq = 41
                            Kind = TraceEventKind.RunStart
                            Node = 2
                            Other = 0
                            Arg = 4
                            Flag = 0
                            Cause = 40
                            Payload = null
                        }
                    |]

                Expect.equal
                    (TraceModel.why events "cp-1.jsonl" 2 0).Root
                    (Some (BeforeCheckpoint "cp-1.jsonl"))
                    "the write at seq 12 lies in the checkpoint"
            }

            test "whyNot reports Disposed" {
                use g = new Graph ()
                use _ = g.Activate ()
                let m = createMemo (fun _ -> 1)
                m.Value |> ignore
                m.Dispose ()
                let disposed = (ofKind TraceEventKind.Dispose g)[0]
                Expect.equal (Trace.whyNot g m) (Some (Disposed disposed.Seq)) "the dispose is the reason"
            }

            test "whyNot reports Queued inside a batch" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                let effect = (ofKind TraceEventKind.NodeNew g |> Array.last).Node

                batch (fun () ->
                    s.Value <- 1
                    let schedule = ofKind TraceEventKind.Schedule g |> Array.last

                    Expect.equal (TraceModel.whyNot (Trace.events g) effect) (Some (Queued schedule.Seq)) "the effect waits for the batch")
            }

            test "whyNot reports Unobserved" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                let m = createMemo (fun _ -> s.Value)
                m.Value |> ignore
                s.Value <- 1
                let mark = (ofKind TraceEventKind.Mark g)[0]
                Expect.equal (Trace.whyNot g m) (Some (Unobserved mark.Seq)) "nothing reads the marked memo"
            }

            test "whyNot reports CheckedClean for an effect behind a cutoff memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> s.Value % 2)
                createEffect (fun () -> m.Value |> ignore)
                let effect = (ofKind TraceEventKind.NodeNew g |> Array.last).Node
                s.Value <- 3
                Expect.exists (ofKind TraceEventKind.Schedule g) (fun e -> e.Node = effect) "the write schedules the effect"

                let resolved =
                    ofKind TraceEventKind.CheckResolved g
                    |> Array.findBack (fun e -> e.Node = effect)

                Expect.equal
                    (TraceModel.whyNot (Trace.events g) effect)
                    (Some (CheckedClean (resolved.Seq, [ (m :> INode).Id ])))
                    "the flush checked the effect clean"
            }

            test "whyNot reports CheckedClean for a pulled memo behind a cutoff memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value % 2)
                let m = createMemo (fun _ -> a.Value)
                m.Value |> ignore
                s.Value <- 3
                m.Value |> ignore

                let resolved =
                    ofKind TraceEventKind.CheckResolved g
                    |> Array.findBack (fun e -> e.Node = (m :> INode).Id)

                Expect.equal (Trace.whyNot g m) (Some (CheckedClean (resolved.Seq, [ (a :> INode).Id ]))) "the pull checked the memo clean"
            }

            test "whyNot reports SkippedAsRunningReader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let t = createSignal 0
                let s = createSignal 0
                let m = createMemo (fun _ -> s.Value * 2)

                createEffect (fun () ->
                    s.Value <- t.Value
                    m.Value |> ignore)

                let reader = (ofKind TraceEventKind.NodeNew g |> Array.last).Node
                t.Value <- 1
                let skip = (ofKind TraceEventKind.MarkSkip g)[0]

                Expect.equal
                    (TraceModel.whyNot (Trace.events g) reader)
                    (Some (SkippedAsRunningReader skip.Seq))
                    "the memo skipped its running reader"
            }

            test "whyNot reports NotReached at an equal write" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> s.Value)
                createEffect (fun () -> m.Value |> ignore)
                s.Value <- 1
                let write = ofKind TraceEventKind.Write g |> Array.last
                Expect.equal write.Flag 0 "the write left the value"
                Expect.equal (Trace.whyNot g m) (Some (NotReached write.Seq)) "propagation stopped at the write"
            }
            test "RunStart.Other names the walking reader: memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                let b = createMemo (fun _ -> a.Value + 1)
                b.Value |> ignore
                s.Value <- 2
                b.Value |> ignore
                Expect.equal (pullerOf g (a :> INode).Id) (b :> INode).Id "the memo resolving Check pulled the source"

                let resolved =
                    ofKind TraceEventKind.CheckResolved g
                    |> Array.last

                let start = ofKind TraceEventKind.CheckStart g |> Array.last
                Expect.equal resolved.Node (b :> INode).Id "the walk resolved on the reader"
                Expect.equal resolved.Cause start.Seq "CheckResolved closes its CheckStart"
                Expect.equal resolved.Flag 1 "the source moved, so the walk answered dirty"
                Expect.equal resolved.Other (a :> INode).Id "the dirty answer names the source that moved"
            }

            test "RunStart.Other names the walking reader: effect" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                createEffect (fun () -> a.Value |> ignore)
                let effect = idsOf TraceNodeKind.Effect g |> Array.exactlyOne
                s.Value <- 2
                Expect.equal (pullerOf g (a :> INode).Id) effect "the effect resolving Check pulled the source"
            }

            test "RunStart.Other names the walking reader: async memo" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                let am = createAsync (fun _ _ -> Task.FromResult a.Value)
                am.TryValue |> ignore
                s.Value <- 2
                am.TryValue |> ignore
                Expect.equal (pullerOf g (a :> INode).Id) (am :> INode).Id "the async memo resolving Check pulled the source"
            }

            test "RunStart.Other names the walking reader: boundary" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                let b = createSuspense (fun _ -> 0) (fun () -> a.Value)
                b.Value |> ignore
                s.Value <- 2
                b.Value |> ignore
                Expect.equal (pullerOf g (a :> INode).Id) (b :> INode).Id "the boundary resolving Check pulled the source"
            }

            test "RunStart.Other names the walking reader: projection resolve" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                let p = createProjection id id (fun () -> [ a.Value ])
                p.Keys |> ignore
                s.Value <- 2
                p.Keys |> ignore
                Expect.equal (pullerOf g (a :> INode).Id) (p :> INode).Id "the projection resolving Check pulled the source"
            }

            test "RunStart.Other names the walking reader: projection row refresh" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                let p = createProjection id (fun k -> k * a.Value) (fun () -> [ 1; 2 ])
                p.Snapshot |> ignore
                let rows = idsOf TraceNodeKind.Memo g |> Array.skip 1
                s.Value <- 2
                p.Snapshot |> ignore

                for row in rows do
                    Expect.equal (pullerOf g row) (p :> INode).Id "the projection refreshing its rows pulled each row"
            }

            test "RunStart.Other names the walking reader: beacon" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1; 2; 3; 4 ]
                let rows = createProjection id id (fun () -> source.Value)
                let filtered = rows |> Projection.filter (fun n -> n % 2 = 0)
                let view = filtered |> Projection.map (fun n -> n * 3)

                createEffect (fun () ->
                    for k in view.Keys do
                        view.Get k |> ignore)

                source.Value <- [ 1; 3; 4 ]
                Expect.equal (pullerOf g (filtered :> INode).Id) (view :> INode).Id "the view's beacon walk pulled the upstream pass"
            }

            test "RunStart.Other names the walking reader: lookup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let a = createMemo (fun _ -> s.Value * 2)
                let l = createSelector (fun () -> a.Value)
                createEffect (fun () -> l.Get 2 |> ignore)
                // The lookup's source memo and refresh effect, created ahead of the reader.
                let state = idsOf TraceNodeKind.Memo g |> Array.last
                let refresh = idsOf TraceNodeKind.Effect g |> Array.head
                s.Value <- 2
                Expect.equal (pullerOf g state) refresh "the lookup's refresh walk pulled its source"
            }

            test "the b820ad6 check-walk case names the reader" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createSignal [ 1; 2; 3; 4 ]
                let rows = createProjection id id (fun () -> source.Value)

                let view =
                    rows
                    |> Projection.filter (fun n -> n % 2 = 0)
                    |> Projection.map (fun n -> n * 3)

                let mutable runs = 0

                createEffect (fun () ->
                    runs <- runs + 1

                    for k in view.Keys do
                        view.Get k |> ignore)

                let effect = idsOf TraceNodeKind.Effect g |> Array.exactlyOne
                let before = (Trace.events g |> Array.last).Seq
                source.Value <- [ 1; 3; 4 ]
                Expect.equal runs 2 "the reader runs once for the removing write"
                Expect.equal (pullerOf g (view :> INode).Id) effect "the view's pass names the effect walking it"

                let pulled =
                    ofKind TraceEventKind.RunStart g
                    |> Array.filter (fun e ->
                        e.Seq > before
                        && e.Node <> (rows :> INode).Id
                        && e.Node <> effect)

                Expect.isNonEmpty pulled "the downstream passes ran in the walk"
                Expect.all pulled (fun e -> e.Other <> 0) "every pass pulled in the walk names its reader"
            }

            test "an affinity violation inside a walk leaves an empty walker stack after the flush" {
                let mutable foreign = Unchecked.defaultof<Signal<int>>
                let thread = System.Threading.Thread (fun () -> foreign <- Signal (new Graph (), 0))
                thread.Start ()
                thread.Join ()
                let armed = ref false

                // A comparer that writes to a graph owned by another thread, once armed.
                let policy =
                    { new IEqualityPolicy with
                        member _.Comparer<'T>() =
                            { new System.Collections.Generic.IEqualityComparer<'T> with
                                member _.Equals(x, y) =
                                    if armed.Value then
                                        armed.Value <- false
                                        foreign.Value <- 1

                                    System.Collections.Generic.EqualityComparer<'T>.Default.Equals(x, y)

                                member _.GetHashCode x =
                                    System.Collections.Generic.EqualityComparer<'T>.Default.GetHashCode x
                            }
                    }

                use g =
                    new Graph (
                        { GraphOptions.Default with
                            Equality = policy
                        }
                    )

                use _ = g.Activate ()
                let s = createSignal 1

                let a =
                    createMemo (fun _ ->
                        let v = s.Value * 2

                        if v = 4 then
                            armed.Value <- true

                        v)

                createEffect (fun () -> a.Value |> ignore)
                let effect = idsOf TraceNodeKind.Effect g |> Array.exactlyOne
                Expect.throwsT<System.InvalidOperationException> (fun () -> s.Value <- 2) "the foreign write raises"

                let kinds =
                    Trace.events g
                    |> Array.map (fun e -> e.Kind, e.Node)

                let abandoned =
                    Array.findIndexBack (fun k -> k = (TraceEventKind.WalkAbandoned, effect)) kinds

                let flushEnd = Array.findIndexBack (fun (k, _) -> k = TraceEventKind.FlushEnd) kinds
                Expect.isLessThan abandoned flushEnd "the flush unwound the effect's walker frame"

                s.Value <- 3
                Expect.equal (pullerOf g (a :> INode).Id) effect "the next walk starts from an empty stack"
            }

            test "a node reports the test's file:line" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s, line = createSignal 1, __LINE__
                Expect.equal (siteOf g s) (here line) "the site is the creating line"
            }

            test "a node created through a combinator reports the user's line" {
                use g = new Graph ()
                use _ = g.Activate ()
                let rows = createProjection id id (fun () -> [ 1 ])
                let view, line = rows |> Projection.map (fun n -> n + 1), __LINE__
                let m, memoLine = createMemo (fun _ -> 1), __LINE__
                Expect.equal (siteOf g view) (here line) "the view's site is the user's line"
                Expect.equal (siteOf g m) (here memoLine) "createMemo reports the user's line"
            }

            test "history lists each run with its status, movement, root and flush" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 1
                let positive = createMemo (fun _ -> a.Value > 0)
                let runs = ref 0

                createEffect (fun () ->
                    positive.Value |> ignore
                    incr runs)

                a.Value <- 2
                a.Value <- -1
                let writes = ofKind TraceEventKind.Write g
                let history = Trace.history g positive

                Expect.equal history.Node (positive :> INode).Id "the history names its node"
                Expect.equal (history.Runs |> List.map _.Run) [ 1; 2; 3 ] "one record per run"

                Expect.isTrue
                    (history.Runs
                     |> List.forall (fun r -> r.Status = Some RunStatus.Ok))
                    "every run ended Ok"

                Expect.equal (history.Runs |> List.map _.Moved) [ true; false; true ] "only the sign change moved"

                Expect.equal
                    (history.Runs
                     |> List.map (fun r -> r.Value |> Option.map unbox<bool>))
                    [ Some true; Some true; Some false ]
                    "a run that did not move keeps the earlier value"

                Expect.equal
                    (history.Runs |> List.map _.Root)
                    [
                        Created (Trace.origin g positive).Seq
                        UserWrite writes[0].Seq
                        UserWrite writes[1].Seq
                    ]
                    "each run's root"

                Expect.isTrue
                    (history.Runs
                     |> List.skip 1
                     |> List.forall (fun r -> r.Flush > 0))
                    "a write's runs start in a flush"

                Expect.stringContains (Trace.render g history) "run 2 #" "render lists the runs"
            }

            test "waitingOn names the pending source the last run read" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int>()
                let m = createMemo (fun _ -> source.Value + 1)
                Expect.equal m.TryValue Pending "the memo is pending on the source"
                Expect.equal (Trace.waitingOn g m).Sources [ (source :> INode).Id ] "the memo waits on the source"
                Expect.stringContains (Trace.render g (Trace.waitingOn g m)) "suspended on" "render names the source"

                source.Settle 1
                Expect.equal m.TryValue (Ready 2) "the settle published"
                Expect.isEmpty (Trace.waitingOn g m).Sources "the settled run waits on nothing"
            }

            test "why through an async source ends at its settle" {
                use g = new Graph ()
                use _ = g.Activate ()
                let source = createAsyncSource<int>()
                let m = createMemo (fun _ -> source.Value + 1)
                m.TryValue |> ignore
                source.Settle 1
                m.TryValue |> ignore
                let settle = (ofKind TraceEventKind.Settle g)[0]
                Expect.equal settle.Node (source :> INode).Id "the settle names the source"
                Expect.equal (Trace.why g m).Root (Some (Settled settle.Seq)) "the chain ends at the settle"
            }

            test "why through an async memo walks past the settle to the flight's run" {
                use g = new Graph ()
                use _ = g.Activate ()
                let flight = TaskCompletionSource<int>()
                let a = createAsync (fun _ _ -> flight.Task)
                let d = createMemo (fun _ -> a.Value * 2)
                d.TryValue |> ignore
                flight.SetResult 5
                Expect.equal d.TryValue (Ready 10) "the flight published"
                let why = Trace.why g d
                let kinds = why.Steps |> List.map _.Kind
                Expect.contains kinds TraceEventKind.Settle "the chain passes the settle"
                Expect.contains kinds TraceEventKind.FlightStart "and the flight's start"
                Expect.equal why.Root (Some (Created (Trace.origin g a).Seq)) "and ends at the memo's first run"
                Expect.isTrue (Trace.history g a).Runs.Head.Moved "the settle moved the flight's run"
            }

            test "why steps, history runs and snapshot nodes carry recorded values" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createSignal 1
                let source = createAsyncSource<int>()
                let m = createMemo (fun _ -> a.Value + source.Value)
                createEffect (fun () -> m.TryValue |> ignore)
                source.Settle 10
                a.Value <- 2
                Expect.equal m.TryValue (Ready 12) "the memo read both sources"

                let values =
                    (Trace.why g m).Steps
                    |> List.choose (fun s ->
                        s.Value
                        |> Option.map (fun v -> s.Kind, unbox<int> v))

                Expect.contains values (TraceEventKind.Write, 2) "the write step carries the written value"
                Expect.equal (List.last (Trace.history g m).Runs).Value (Some (box 12)) "the last run carries its value"

                let nodes = (Trace.snapshot g).Nodes
                Expect.equal nodes[(a :> INode).Id].Value (Some "2") "the signal holds its last write"
                Expect.equal nodes[(source :> INode).Id].Value (Some "10") "the source holds its settle"
                Expect.equal nodes[(m :> INode).Id].Value (Some "12") "the memo holds its last move"

                source.Fail (System.InvalidOperationException "quote down")
                let failed = (Trace.snapshot g).Nodes[(source :> INode).Id].Value
                Expect.equal failed (Some "InvalidOperationException: quote down") "a failure holds its exception"
            }

            test "valueText renders a value as one line" {
                Expect.equal (TraceModel.valueText (box {| Sku = "tea"; Qty = 2 |})) "{| Qty = 2; Sku = \"tea\" |}" "a record joins its fields"
                Expect.equal (TraceModel.valueText (box "a\nb")) "a b" "a string collapses its lines"
                let long = TraceModel.valueText (box [ 1..100 ])
                Expect.equal long.Length 60 "a long value is truncated"
                Expect.isTrue (long.EndsWith "…") "with an ellipsis"
            }

            test "waitingOn reports superseded, settled and failed flights" {
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            FlightPolicy = KeepLatest
                        }
                    )

                use _ = g.Activate ()
                let s = createSignal 1
                let flights = ResizeArray<TaskCompletionSource<int>>()

                let a =
                    createAsync (fun _ _ ->
                        s.Value |> ignore
                        let flight = TaskCompletionSource<int>()
                        flights.Add flight
                        flight.Task)

                a.TryValue |> ignore
                s.Value <- 2
                a.TryValue |> ignore

                Expect.equal
                    ((Trace.waitingOn g a).Flights |> List.map _.State)
                    [ TraceFlightState.InFlight; TraceFlightState.InFlight ]
                    "two flights in progress"

                flights[0].SetResult 1
                flights[1].SetResult 2
                let waiting = Trace.waitingOn g a

                match waiting.Flights with
                | [ second; first ] ->
                    Expect.equal (second.Flight, second.Run) (2, 2) "the newest flight comes first, with its run"
                    Expect.isTrue (second.State.IsSettled) "the current flight settled"

                    match first.State with
                    | TraceFlightState.Dropped (_, reason) -> Expect.equal reason TraceDropReason.Superseded "the older flight was superseded"
                    | other -> failtestf "expected Dropped, got %A" other
                | other -> failtestf "expected two flights, got %A" other

                Expect.equal ((Trace.history g a).Runs |> List.map _.Moved) [ false; true ] "only the current flight moved"

                s.Value <- 3
                a.TryValue |> ignore
                flights[2].SetException(exn "down")

                Expect.isTrue
                    (match (Trace.waitingOn g a).Flights.Head.State with
                     | TraceFlightState.Failed (_, false) -> true
                     | _ -> false)
                    "a faulted flight reads Failed"

                Expect.stringContains (Trace.render g (Trace.waitingOn g a)) "dropped #" "render shows the drop"
            }
#endif
        ]
