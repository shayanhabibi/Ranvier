module Ranvier.Tests.TraceModelTests

open Expecto
open Ranvier

#if RANVIER_TRACE
let private ev kind node other arg flag : TraceEvent =
    {
        Seq = 0
        Kind = kind
        Node = node
        Other = other
        Arg = arg
        Flag = flag
        Cause = 0
        Payload = null
    }

/// <summary>Numbers <c>events</c> from seq 1.</summary>
let private numbered (events: TraceEvent list) =
    events
    |> List.mapi (fun i e -> { e with Seq = i + 1 })
    |> Array.ofList

let private nodeNew id owner (kind: TraceNodeKind) (site: string) =
    { ev TraceEventKind.NodeNew id owner (int kind) 0 with
        Payload = site
    }

let private label id (text: string) =
    { ev TraceEventKind.Label id 0 0 0 with
        Payload = text
    }

let private graphNew = ev TraceEventKind.GraphNew 0 1 0 0

let private pathsOf (events: TraceEvent[]) =
    let s = TraceModel.snapshot events

    s.Nodes
    |> Map.map (fun id _ -> TraceModel.pathOf s id)

let private path (events: TraceEvent[]) id =
    (pathsOf events)[id]

let private live (g: Graph) (node: INode) =
    let s = Trace.snapshot g
    TraceModel.pathOf s node.Id
#endif

[<Tests>]
let tests =
    testList
        "TraceModel"
        [
#if RANVIER_TRACE
            test "an ownerless node hangs off the graph root" {
                let events = numbered [ graphNew; nodeNew 1 0 TraceNodeKind.Signal "App.fs:3" ]
                Expect.equal (path events 1) "/App.fs:3" "the site is the only segment"
            }

            test "a segment is the label, else the site, else the kind" {
                let events =
                    numbered
                        [
                            graphNew
                            nodeNew 1 0 TraceNodeKind.Signal "App.fs:3"
                            label 1 "count"
                            nodeNew 2 0 TraceNodeKind.Memo "App.fs:4"
                            nodeNew 3 0 TraceNodeKind.Effect "?"
                        ]

                Expect.equal (path events 1) "/count" "a label wins"
                Expect.equal (path events 2) "/App.fs:4" "a site follows"
                Expect.equal (path events 3) "/Effect" "the kind is last"
            }

            test "createRoot owners are root#n under their parent" {
                use g = new Graph ()
                use _ = g.Activate ()
                let a = createRoot (fun _ -> Trace.named "a" (fun () -> createSignal 1))
                let b = createRoot (fun _ -> Trace.named "a" (fun () -> createSignal 2))

                let owners =
                    (Trace.snapshot g).Owners
                    |> Map.toList
                    |> List.map (fun (_, o) -> o.Path)

                Expect.containsAll owners [ "/root#0"; "/root#1" ] "each root counts its earlier siblings"
                Expect.equal (live g a) "/a" "an ownerless signal under a root hangs off the graph root"
                Expect.equal (live g b) "/a#1" "a repeated segment takes #n"
            }

            test "a run-scope host adds one segment and the scope adds none" {
                use g = new Graph ()
                use _ = g.Activate ()
                let mutable inner = Unchecked.defaultof<INode>

                let outer =
                    Trace.named "outer" (fun () ->
                        createMemoWith (fun () ->
                            let m = Trace.named "inner" (fun () -> createMemo (fun () -> 1))
                            inner <- m
                            m.Value))

                outer.Value |> ignore
                Expect.equal (live g inner) "/outer/inner" "the host's path is the scope's path"
            }

            test "#n resets when the owner re-runs, and @k tells incarnations apart" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let rows = ResizeArray<INode>()

                let list =
                    Trace.named "list" (fun () ->
                        createMemoWith (fun () ->
                            let a = Trace.named "row" (fun () -> createMemo (fun () -> 1))
                            let b = Trace.named "row" (fun () -> createMemo (fun () -> 2))
                            rows.Add a
                            rows.Add b
                            s.Value + a.Value + b.Value))

                list.Value |> ignore
                s.Value <- 2
                list.Value |> ignore
                let paths = rows |> Seq.map (live g) |> List.ofSeq

                Expect.equal paths [ "/list/row@1"; "/list/row#1@1"; "/list/row@2"; "/list/row#1@2" ] "re-created rows keep their paths"
                Expect.equal (Trace.resolve g "/list/row") (Some rows[2].Id) "a bare path resolves the live incarnation"
                Expect.equal (Trace.resolve g "/list/row@1") (Some rows[0].Id) "@k resolves the k-th incarnation"
                Expect.equal (Trace.resolve g "/list/nothing") None "an unknown path resolves to None"
            }

            test "a bare path resolves the latest incarnation when none is live" {
                let events =
                    numbered
                        [
                            graphNew
                            nodeNew 1 0 TraceNodeKind.Signal "App.fs:3"
                            ev TraceEventKind.Dispose 1 0 0 0
                            nodeNew 2 0 TraceNodeKind.Signal "App.fs:3"
                            ev TraceEventKind.Dispose 2 0 0 0
                        ]

                let s = TraceModel.snapshot events
                Expect.equal (s.Nodes[2].Path) "/App.fs:3#1" "the second node is a sibling of the first"
                Expect.equal (TraceModel.resolve s "/App.fs:3") (Some 1) "the only holder resolves"
            }

            test "labels escape / # [ ] @ and backslash" {
                let events =
                    numbered [ graphNew; nodeNew 1 0 TraceNodeKind.Signal "?"; label 1 @"a/b#c[d]e@f\g" ]

                Expect.equal (path events 1) @"/a\/b\#c\[d\]e\@f\\g" "every reserved character is escaped"
                Expect.equal (TraceModel.resolve (TraceModel.snapshot events) @"/a\/b\#c\[d\]e\@f\\g") (Some 1) "an escaped @ is not a suffix"
            }

            test "snapshotAt folds the events up to its seq" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun () -> s.Value + 1)
                m.Value |> ignore
                let before = (Trace.events g |> Array.last).Seq
                s.Value <- 2
                m.Value |> ignore
                Expect.equal (Trace.snapshotAt g before).Nodes[(m :> INode).Id].Runs 1 "one run at the earlier seq"
                Expect.equal (Trace.snapshot g).Nodes[(m :> INode).Id].Runs 2 "two runs now"
                Expect.equal (Trace.snapshot g).Sources[(m :> INode).Id] [ (s :> INode).Id ] "the fold holds the memo's source"
            }

            test "render folds 12 marks into one line" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let memos = List.init 12 (fun i -> createMemo (fun () -> s.Value + i))

                createEffect (fun () ->
                    for m in memos do
                        m.Value |> ignore)

                let from = (Trace.events g |> Array.last).Seq
                s.Value <- 2

                let write =
                    Trace.events g
                    |> Array.filter (fun e -> e.Seq > from)

                let text = Trace.render g write

                let folded =
                    text.Split '\n'
                    |> Array.filter (fun l -> l.Contains "× 12 memos marked")

                Expect.equal folded.Length 1 $"one line holds the twelve marks:\n{text}"
            }

            test "dumpText twice gives the same text" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun () -> s.Value * 2)
                createEffect (fun () -> m.Value |> ignore)
                s.Value <- 3
                Expect.equal (Trace.dumpText g) (Trace.dumpText g) "the dump is a function of the log"
            }

            test "a dump parses back to the same snapshot" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Trace.named "count" (fun () -> createSignal 1)
                let m = createMemo (fun () -> s.Value * 2)
                createEffect (fun () -> m.Value |> ignore)
                s.Value <- 3
                let text = Trace.dumpText g
                let lines = text.Split '\n'
                Expect.stringStarts lines[0] "{\"schema\":1,\"target\":\"net\"" "the header comes first"
                Expect.stringStarts lines[1] "{\"snapshot\":" "the snapshot comes second"
                let dump = TraceModel.parseDump text
                Expect.equal dump.SeqFrom 1 "an unsplit log starts at seq 1"
                Expect.equal (TraceModel.fold dump.Snapshot dump.Events) (Trace.snapshot g) "the dump folds to the live snapshot"
                Expect.equal (TraceModel.dumpText "net" null dump.Snapshot dump.Events) text "a parsed dump writes the same text"
            }

            test "dumpText inside an effect's run raises" {
                use g = new Graph ()
                use _ = g.Activate ()
                let mutable raised = false

                createEffect (fun () ->
                    try
                        Trace.dumpText g |> ignore
                    with :? System.InvalidOperationException ->
                        raised <- true)

                Expect.isTrue raised "a run in progress refuses the dump"
            }

            test "dump off the graph thread raises" {
                use g = new Graph ()
                use _ = g.Activate ()
                createSignal 1 |> ignore

                let file =
                    System.IO.Path.Combine (System.IO.Path.GetTempPath (), "partas-trace-offthread.jsonl")

                let raised = ref false

                let worker =
                    System.Threading.Thread (fun () ->
                        try
                            Trace.dump g file |> ignore
                        with :? System.InvalidOperationException ->
                            raised.Value <- true)

                worker.Start ()
                worker.Join ()
                Expect.isTrue raised.Value "another thread is refused"
            }

            test "dumpText inside batch succeeds" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1

                let text =
                    batch (fun () ->
                        s.Value <- 2
                        Trace.dumpText g)

                Expect.isNonEmpty text "a batch is between flushes"
            }
#endif
        ]
