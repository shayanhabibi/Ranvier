namespace Ranvier

open System.Diagnostics

#if RANVIER_TRACE
open System
open System.Collections.Generic

/// <summary>A node's live observer set or source list, read by edge reconciliation.</summary>
type internal ITracedEdges =
    /// <summary>The owning node id.</summary>
    abstract Owner: int
    /// <summary>True for a source list, false for an observer set.</summary>
    abstract IsSources: bool
    /// <summary>The observer ids, or the source ids in slot order.</summary>
    abstract Ids: int[]

/// <summary>A graph's append-only event log, with the graph's clock, owner-id counter and walk state.</summary>
/// <remarks>
/// The log holds ids and payloads only; it references no node, owner or graph. A log built with <c>locked</c>
/// serialises every member, so an <c>Unchecked</c> graph used from several threads records every event.
/// </remarks>
[<Sealed; AllowNullLiteral>]
type internal TraceLog(locked: bool) =
    let gate = obj ()
    let events = ResizeArray<TraceEvent>()
    let mutable clock = 0
    let mutable owners = 0

    /// Node id to the owner id recorded by its `NodeNew`.
    let nodeOwners = Dictionary<int, int>()

    /// Run-scope owner id to its host node id.
    let scopeHosts = Dictionary<int, int>()

    /// Node id to the `RunStart` seq of its open run.
    let openRuns = Dictionary<int, int>()

    /// Node id to the first dirty `Mark` seq since its last `RunStart`.
    let firstDirty = Dictionary<int, int>()

    /// Node ids of the running computations, innermost last.
    let running = ResizeArray<int>()

    /// Node id to the label held for its `NodeNew`.
    let reserved = Dictionary<int, string>()

    /// Node ids of the walker frames, innermost last.
    let walkers = ResizeArray<int>()

    /// `Write` and `Moved` seqs whose notification is in progress, innermost last.
    let causes = ResizeArray<int>()

    /// Node id to the run number of its open run.
    let runNumbers = Dictionary<int, int>()

    /// Node id to the number of runs recorded for it.
    let runCounts = Dictionary<int, int>()

    /// Node ids whose open run recorded `Moved`.
    let movedRuns = HashSet<int>()

    /// The seq and target of the latest `Mark`.
    let mutable lastMark = 0
    let mutable lastMarkTarget = 0

    let mutable flushes = 0

    /// Flush number and running-stack depth of each open flush, innermost last.
    let flushFrames = ResizeArray<struct (int * int)>()

    /// Observer sets and source lists bound to the log.
    let edgeSets = ResizeArray<WeakReference<ITracedEdges>>()

    let sync (f: unit -> 'T) : 'T = if locked then lock gate f else f ()

    let append kind node other arg flag cause (payload: obj) =
        if clock = Int32.MaxValue then
            invalidOp "The graph's trace log is full: it records at most Int32.MaxValue events."

        clock <- clock + 1

        events.Add
            {
                Seq = clock
                Kind = kind
                Node = node
                Other = other
                Arg = arg
                Flag = flag
                Cause = cause
                Payload = payload
            }

        clock

    let lookup (map: Dictionary<int, int>) key =
        match map.TryGetValue key with
        | true, value -> value
        | _ -> 0

    /// <summary>Records one event and returns its <c>Seq</c>.</summary>
    /// <exception cref="T:System.InvalidOperationException">The graph's clock is at <c>Int32.MaxValue</c>.</exception>
    member _.Append(kind: TraceEventKind, node: int, other: int, arg: int, flag: int, cause: int, payload: obj) : int =
        sync (fun () -> append kind node other arg flag cause payload)

    /// <summary>Allocates the next owner id, starting at 1.</summary>
    member _.NextOwnerId() =
        sync (fun () ->
            owners <- owners + 1
            owners)

    /// <summary>The <c>RunStart</c> seq of the innermost running computation's open run, or 0.</summary>
    member _.CreatingRun =
        sync (fun () -> if running.Count = 0 then 0 else lookup openRuns running[running.Count - 1])

    /// <summary>The owner id recorded for <c>node</c>, or 0.</summary>
    member _.OwnerOf(node: int) = sync (fun () -> lookup nodeOwners node)

    /// <summary>The host node id of the run scope <c>owner</c>, or 0.</summary>
    member _.HostOf(owner: int) = sync (fun () -> lookup scopeHosts owner)

    /// <summary>Records <c>owner</c> as the owner of <c>node</c>.</summary>
    member _.SetOwner(node: int, owner: int) = sync (fun () -> nodeOwners[node] <- owner)

    /// <summary>Records <c>host</c> as the host node of the run scope <c>owner</c>.</summary>
    member _.SetHost(owner: int, host: int) = sync (fun () -> scopeHosts[owner] <- host)

    /// <summary>The <c>RunStart</c> seq of <c>node</c>'s open run, or 0.</summary>
    member _.OpenRun(node: int) = sync (fun () -> lookup openRuns node)

    /// <summary>
    /// Opens run number <c>run</c> of <c>node</c> at <c>seq</c>: pushes the node on the running stack and clears its
    /// first dirty mark.
    /// </summary>
    member _.StartRun(node: int, seq: int, run: int) =
        sync (fun () ->
            openRuns[node] <- seq
            runNumbers[node] <- run
            runCounts[node] <- run
            movedRuns.Remove node |> ignore
            firstDirty.Remove node |> ignore
            running.Add node)

    /// <summary>The number of runs recorded for <c>node</c>.</summary>
    member _.RunCount(node: int) = sync (fun () -> lookup runCounts node)

    /// <summary>The run number of <c>node</c>'s open run, or 0.</summary>
    member _.RunNumber(node: int) = sync (fun () -> lookup runNumbers node)

    /// <summary>Records that <c>node</c>'s open run moved its value.</summary>
    member _.NoteMoved(node: int) = sync (fun () -> movedRuns.Add node |> ignore)

    /// <summary>Whether <c>node</c>'s open run moved its value.</summary>
    member _.MovedInRun(node: int) = sync (fun () -> movedRuns.Contains node)

    /// <summary>Closes <c>node</c>'s open run and removes the node from the running stack.</summary>
    member _.EndRun(node: int) =
        sync (fun () ->
            openRuns.Remove node |> ignore
            runNumbers.Remove node |> ignore
            movedRuns.Remove node |> ignore
            let i = running.LastIndexOf node

            if i >= 0 then
                running.RemoveAt i)

    /// <summary>The node ids with an open run, innermost last.</summary>
    member _.Running = sync (fun () -> running.ToArray ())

    /// <summary>The first dirty <c>Mark</c> seq since <c>node</c>'s last run, or 0.</summary>
    member _.FirstDirty(node: int) = sync (fun () -> lookup firstDirty node)

    /// <summary>Records <c>seq</c> as <c>node</c>'s first dirty mark unless one is pending.</summary>
    member _.NoteDirty(node: int, seq: int) =
        sync (fun () ->
            if not (firstDirty.ContainsKey node) then
                firstDirty[node] <- seq)

    /// <summary>Opens the notification of the <c>Write</c> or <c>Moved</c> at <c>seq</c>.</summary>
    member _.PushCause(seq: int) = sync (fun () -> causes.Add seq)

    /// <summary>Closes the innermost notification.</summary>
    member _.PopCause() =
        sync (fun () ->
            if causes.Count > 0 then
                causes.RemoveAt (causes.Count - 1))

    /// <summary>The seq of the innermost notification in progress, or 0.</summary>
    member _.Cause = sync (fun () -> if causes.Count = 0 then 0 else causes[causes.Count - 1])

    /// <summary>Records <c>seq</c> as the latest <c>Mark</c>, on <c>target</c>.</summary>
    member _.NoteMark(target: int, seq: int) =
        sync (fun () ->
            lastMark <- seq
            lastMarkTarget <- target)

    /// <summary>The seq of the latest <c>Mark</c> when its target is <c>node</c>, or 0.</summary>
    member _.MarkOf(node: int) = sync (fun () -> if lastMarkTarget = node then lastMark else 0)

    /// <summary>Opens a flush and returns its number, starting at 1.</summary>
    member _.EnterFlush() =
        sync (fun () ->
            flushes <- flushes + 1
            flushFrames.Add (struct (flushes, running.Count))
            flushes)

    /// <summary>
    /// Closes the innermost flush. Returns its number and the node ids whose runs opened during it and are still open,
    /// innermost first.
    /// </summary>
    member _.ExitFlush() : struct (int * int[]) =
        sync (fun () ->
            if flushFrames.Count = 0 then
                struct (0, [||])
            else
                let struct (number, depth) = flushFrames[flushFrames.Count - 1]
                flushFrames.RemoveAt (flushFrames.Count - 1)
                let open' = [| for i in running.Count - 1 .. -1 .. depth -> running[i] |]
                struct (number, open'))

    /// <summary>Holds <c>label</c> for the <c>NodeNew</c> of <c>node</c>.</summary>
    member _.Reserve(node: int, label: string) = sync (fun () -> reserved[node] <- label)

    /// <summary>Removes and returns the label held for <c>node</c>, or null.</summary>
    member _.TakeReserved(node: int) : string =
        sync (fun () ->
            match reserved.TryGetValue node with
            | true, label ->
                reserved.Remove node |> ignore
                label
            | _ -> null)

    /// <summary>Pushes a walker frame for <c>node</c>.</summary>
    member _.Push(node: int) = sync (fun () -> walkers.Add node)

    /// <summary>
    /// Pops the innermost frame for <c>node</c> and records <c>WalkAbandoned</c> for each frame above it. The stack
    /// stays as it is when <c>node</c> has no frame.
    /// </summary>
    member _.Pop(node: int) =
        sync (fun () ->
            let i = walkers.LastIndexOf node

            if i >= 0 then
                for j in walkers.Count - 1 .. -1 .. i + 1 do
                    append TraceEventKind.WalkAbandoned walkers[j] 0 0 0 0 null
                    |> ignore

                walkers.RemoveRange (i, walkers.Count - i))

    /// <summary>The node id of the innermost walker frame, or 0.</summary>
    member _.Walker = sync (fun () -> if walkers.Count = 0 then 0 else walkers[walkers.Count - 1])

    /// <summary>The node id of the innermost open run, or 0.</summary>
    member _.Current = sync (fun () -> if running.Count = 0 then 0 else running[running.Count - 1])

    /// <summary>Registers a node's observer set or source list for edge reconciliation.</summary>
    member _.Register(set: ITracedEdges) = sync (fun () -> edgeSets.Add (WeakReference<ITracedEdges> set))

    /// <summary>The registered observer sets and source lists that are still alive.</summary>
    member _.EdgeSets: ITracedEdges[] =
        sync (fun () ->
            [|
                for weak in edgeSets do
                    match weak.TryGetTarget () with
                    | true, set -> yield set
                    | _ -> ()
            |])

    /// <summary>Empties the walker stack without recording.</summary>
    member _.ClearWalkers() = sync (fun () -> walkers.Clear ())

    /// <summary>A copy of the recorded events, oldest first.</summary>
    member _.Events = sync (fun () -> events.ToArray ())

/// <summary>Traced state carried by an engine object: its graph's log and its own trace id.</summary>
type internal ITraced =
    abstract TraceLog: TraceLog with get, set
    abstract TraceId: int with get, set
#endif

/// <summary>The trace hooks. Every hook is removed, with its arguments, from a build without <c>RANVIER_TRACE</c>.</summary>
/// <remarks>
/// A hook takes engine objects as <c>obj</c> and reaches their log through <c>ITraced</c>. Untraced, each hook
/// body is empty.
/// </remarks>
[<AbstractClass; Sealed>]
type internal Tracer =
#if RANVIER_TRACE
    /// <summary>Pending <c>Trace.named</c> labels on this thread, innermost last. A consumed label is null.</summary>
    [<ThreadStatic; DefaultValue>]
    static val mutable private labels: ResizeArray<string>

    /// <summary>Opens a <c>Trace.named</c> label on this thread.</summary>
    static member PushLabel(label: string) =
        if isNull Tracer.labels then
            Tracer.labels <- ResizeArray ()

        Tracer.labels.Add label

    /// <summary>
    /// Closes the innermost label. An unconsumed label is recorded as a <c>Label</c> event with <c>Node = 0</c> on
    /// <c>graph</c>'s log, when <c>graph</c> is not null.
    /// </summary>
    static member PopLabel(graph: obj) =
        let labels = Tracer.labels
        let last = labels.Count - 1
        let label = labels[last]
        labels.RemoveAt last

        if not (isNull label || isNull graph) then
            (graph :?> ITraced).TraceLog.Append(TraceEventKind.Label, 0, 0, 0, 0, 0, label)
            |> ignore

    /// <summary>Records a <c>Label</c> for <c>id</c> when the innermost pending label on this thread is unconsumed.</summary>
    static member private Consume(log: TraceLog, id: int) =
        let labels = Tracer.labels

        if not (isNull labels) && labels.Count > 0 then
            let last = labels.Count - 1
            let label = labels[last]

            if not (isNull label) then
                labels[last] <- null

                log.Append (TraceEventKind.Label, id, 0, 0, 0, 0, label)
                |> ignore

    /// <summary>Records <c>NodeNew</c> for node <c>id</c> under <c>owner</c>, an owner or null.</summary>
    static member private Node(graph: obj, id: int, kind: TraceNodeKind, owner: obj) =
        let log = (graph :?> ITraced).TraceLog
        let ownerId = if isNull owner then 0 else (owner :?> ITraced).TraceId

        if ownerId <> 0 then
            log.SetOwner (id, ownerId)

        log.Append (TraceEventKind.NodeNew, id, ownerId, int kind, 0, log.CreatingRun, null)
        |> ignore

        match log.TakeReserved id with
        | null -> Tracer.Consume (log, id)
        | label ->
            log.Append (TraceEventKind.Label, id, 0, 0, 0, 0, label)
            |> ignore

    /// <summary>Gives <c>owner</c> <c>log</c> and the next owner id, and records its <c>OwnerNew</c>.</summary>
    static member private Joined(log: TraceLog, owner: ITraced, parent: int, host: int, root: int) =
        owner.TraceLog <- log
        let id = log.NextOwnerId ()
        owner.TraceId <- id

        if host <> 0 then
            log.SetHost (id, host)

        log.Append (TraceEventKind.OwnerNew, id, parent, host, root, log.CreatingRun, null)
        |> ignore

        Tracer.Consume (log, id)

    /// <summary>The log of <c>traced</c>, an <c>ITraced</c>.</summary>
    static member private LogOf(traced: obj) = (traced :?> ITraced).TraceLog

    static member private IdOf(node: obj) =
        match node with
        | :? INode as node -> node.Id
        | _ -> 0

    /// <summary>Records an edge event of <c>sources</c>, a bound source list, for <c>source</c> at <c>slot</c>.</summary>
    static member private Edge(kind: TraceEventKind, sources: obj, source: obj, slot: int) =
        let traced = sources :?> ITraced
        let log = traced.TraceLog

        if not (isNull log) then
            log.Append (kind, traced.TraceId, Tracer.IdOf source, slot, 0, 0, null)
            |> ignore

    /// <summary>Records an observer event of <c>observers</c>, a bound observer set, for <c>observer</c>.</summary>
    static member private Observer(kind: TraceEventKind, observers: obj, observer: obj) =
        let traced = observers :?> ITraced
        let log = traced.TraceLog

        if not (isNull log) then
            log.Append (kind, traced.TraceId, Tracer.IdOf observer, 0, 0, 0, null)
            |> ignore

    /// <summary>Records a <c>Mark</c> or <c>MarkSkip</c> from the source of <c>observers</c> on <c>observer</c>.</summary>
    static member private Marked(kind: TraceEventKind, observers: obj, observer: obj, arg: int) =
        let traced = observers :?> ITraced
        let log = traced.TraceLog

        if not (isNull log) then
            let target = Tracer.IdOf observer
            let seq = log.Append (kind, target, traced.TraceId, arg, 0, log.Cause, null)

            if kind = TraceEventKind.Mark then
                log.NoteMark (target, seq)

                if arg = 2 then
                    log.NoteDirty (target, seq)

    /// <summary>Records <c>RunEnd</c> with <c>outcome</c> for node <c>id</c>'s open run, when it has one, and closes it.</summary>
    static member private Close(log: TraceLog, id: int, outcome: RunStatus) =
        let run = log.OpenRun id

        if run <> 0 then
            let moved = if log.MovedInRun id then 1 else 0

            log.Append (TraceEventKind.RunEnd, id, 0, int outcome, moved, run, null)
            |> ignore

            log.EndRun id
#endif

    /// <summary>
    /// Gives <c>root</c>, a new graph's root owner, a new log and owner id 1, and records <c>GraphNew</c>. The log
    /// serialises its members when <c>guarded</c> is false.
    /// </summary>
    /// <remarks>The graph reaches its log through its root owner.</remarks>
    [<Conditional("RANVIER_TRACE")>]
    static member GraphNew(root: obj, guarded: bool) =
#if RANVIER_TRACE
        let traced = root :?> ITraced
        let log = TraceLog (not guarded)
        traced.TraceLog <- log
        let id = log.NextOwnerId ()
        traced.TraceId <- id

        log.Append (TraceEventKind.GraphNew, 0, id, 0, 0, 0, null)
        |> ignore
#else
        ()
#endif

    /// <summary>
    /// Binds <c>set</c>, a node's <c>ObserverSet</c> or <c>SourceList</c>, to <c>graph</c>'s log and node <c>id</c>.
    /// </summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Bind(set: obj, graph: obj, id: int) =
#if RANVIER_TRACE
        let traced = set :?> ITraced
        let log = (graph :?> ITraced).TraceLog
        traced.TraceLog <- log
        traced.TraceId <- id

        match set with
        | :? ITracedEdges as edges -> log.Register edges
        | _ -> ()
#else
        ()
#endif

    /// <summary>
    /// Takes the innermost pending label on this thread for node <c>id</c>, whose <c>NodeNew</c> follows the nodes
    /// its constructor creates.
    /// </summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Reserve(graph: obj, id: int) =
#if RANVIER_TRACE
        let labels = Tracer.labels

        if not (isNull labels) && labels.Count > 0 then
            let last = labels.Count - 1
            let label = labels[last]

            if not (isNull label) then
                labels[last] <- null
                (graph :?> ITraced).TraceLog.Reserve (id, label)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a signal.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member SignalNew(graph: obj, id: int) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Signal, null)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for an async source.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member AsyncSourceNew(graph: obj, id: int) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.AsyncSource, null)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a memo attached to <c>owner</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member MemoNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Memo, owner)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for an effect attached to <c>owner</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member EffectNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Effect, owner)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for an async memo attached to <c>owner</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member AsyncMemoNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.AsyncMemo, owner)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a boundary attached to <c>owner</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member BoundaryNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Boundary, owner)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a projection attached to <c>owner</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member ProjectionNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Projection, owner)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a projection beacon.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member BeaconNew(graph: obj, id: int) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.ProjectionBeacon, null)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a projection's row watch.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member RowWatchNew(graph: obj, id: int) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.RowWatch, null)
#else
        ()
#endif

    /// <summary>Records <c>NodeNew</c> for a lookup cell.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member LookupCellNew(graph: obj, id: int) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.LookupCell, null)
#else
        ()
#endif

    /// <summary>Records <c>Dispose</c> for node <c>id</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member NodeDispose(graph: obj, id: int) =
#if RANVIER_TRACE
        (graph :?> ITraced).TraceLog.Append(TraceEventKind.Dispose, id, 0, 0, 0, 0, null)
        |> ignore
#else
        ()
#endif

    /// <summary>
    /// Gives <c>scope</c>, the run scope of node <c>host</c>, <c>graph</c>'s log and an owner id, and records its
    /// <c>OwnerNew</c> under the host's owner.
    /// </summary>
    [<Conditional("RANVIER_TRACE")>]
    static member ScopeNew(scope: obj, graph: obj, host: int) =
#if RANVIER_TRACE
        let log = (graph :?> ITraced).TraceLog
        Tracer.Joined (log, (scope :?> ITraced), log.OwnerOf host, host, 0)
#else
        ()
#endif

    /// <summary>
    /// Records <c>OwnerNew</c> for <c>child</c>, an owner joining <c>parent</c>, when <c>child</c> has no log and
    /// <c>parent</c> has one. <c>root</c> marks a <c>createRoot</c> scope. A <c>child</c> of another type is ignored.
    /// </summary>
    [<Conditional("RANVIER_TRACE")>]
    static member OwnerAdopt(parent: obj, child: obj, root: bool) =
#if RANVIER_TRACE
        match child with
        | :? ITraced as child when isNull child.TraceLog ->
            let parent = parent :?> ITraced

            if not (isNull parent.TraceLog) then
                Tracer.Joined (parent.TraceLog, child, parent.TraceId, 0, (if root then 1 else 0))
        | _ -> ()
#else
        ()
#endif

    /// <summary>Records <c>OwnerDispose</c> for <c>owner</c> when it has a log.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member OwnerDispose(owner: obj) =
#if RANVIER_TRACE
        let owner = owner :?> ITraced

        if not (isNull owner.TraceLog) then
            owner.TraceLog.Append(TraceEventKind.OwnerDispose, owner.TraceId, 0, 0, 0, 0, null)
            |> ignore
#else
        ()
#endif

    /// <summary>Records <c>DischargeStart</c> for the run scope <c>owner</c> when it has a log.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member DischargeStart(owner: obj) =
#if RANVIER_TRACE
        let owner = owner :?> ITraced
        let log = owner.TraceLog

        if not (isNull log) then
            log.Append (TraceEventKind.DischargeStart, owner.TraceId, log.HostOf owner.TraceId, 0, 0, 0, null)
            |> ignore
#else
        ()
#endif

    /// <summary>Records <c>DischargeEnd</c> for the run scope <c>owner</c> when it has a log.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member DischargeEnd(owner: obj) =
#if RANVIER_TRACE
        let owner = owner :?> ITraced
        let log = owner.TraceLog

        if not (isNull log) then
            log.Append (TraceEventKind.DischargeEnd, owner.TraceId, log.HostOf owner.TraceId, 0, 0, 0, null)
            |> ignore
#else
        ()
#endif

    /// <summary>Records <c>EdgeAdd</c>: <c>sources</c>, a node's source list, gained <c>source</c> at <c>slot</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member EdgeAdd(sources: obj, source: obj, slot: int) =
#if RANVIER_TRACE
        Tracer.Edge (TraceEventKind.EdgeAdd, sources, source, slot)
#else
        ()
#endif

    /// <summary>Records <c>EdgeRemove</c>: <c>sources</c>, a node's source list, lost <c>source</c> at <c>slot</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member EdgeRemove(sources: obj, source: obj, slot: int) =
#if RANVIER_TRACE
        Tracer.Edge (TraceEventKind.EdgeRemove, sources, source, slot)
#else
        ()
#endif

    /// <summary>Records <c>ObserverAdd</c>: <c>observers</c>, a node's observer set, gained <c>observer</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member ObserverAdd(observers: obj, observer: obj) =
#if RANVIER_TRACE
        Tracer.Observer (TraceEventKind.ObserverAdd, observers, observer)
#else
        ()
#endif

    /// <summary>Records <c>ObserverRemove</c>: <c>observers</c>, a node's observer set, lost <c>observer</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member ObserverRemove(observers: obj, observer: obj) =
#if RANVIER_TRACE
        Tracer.Observer (TraceEventKind.ObserverRemove, observers, observer)
#else
        ()
#endif

    /// <summary>Records a <c>Mark</c> on <c>observer</c> from the node owning <c>observers</c>, caused by the innermost notification.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Mark(observers: obj, observer: obj, dirty: bool) =
#if RANVIER_TRACE
        Tracer.Marked (TraceEventKind.Mark, observers, observer, (if dirty then 2 else 1))
#else
        ()
#endif

    /// <summary>Records a dirty <c>MarkSkip</c> on <c>observer</c>, the running reader, from the node owning <c>observers</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member MarkSkip(observers: obj, observer: obj) =
#if RANVIER_TRACE
        Tracer.Marked (TraceEventKind.MarkSkip, observers, observer, 2)
#else
        ()
#endif

    /// <summary>Records a <c>Write</c> to the node owning <c>observers</c>. A write that <c>moved</c> the value opens a notification, closed by <c>Notified</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Write(observers: obj, moved: bool) =
#if RANVIER_TRACE
        let traced = observers :?> ITraced
        let log = traced.TraceLog

        if not (isNull log) then
            let current = log.Current
            let cause = if current = 0 then 0 else log.OpenRun current
            let seq = log.Append (TraceEventKind.Write, traced.TraceId, current, 0, (if moved then 1 else 0), cause, null)

            if moved then
                log.PushCause seq
#else
        ()
#endif

    /// <summary>Closes the innermost notification on the log of <c>traced</c>, a graph or bound observer set.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Notified(traced: obj) =
#if RANVIER_TRACE
        let log = Tracer.LogOf traced

        if not (isNull log) then
            log.PopCause ()
#else
        ()
#endif

    /// <summary>Records <c>Moved</c> for node <c>id</c>'s open run and opens a notification, closed by <c>Notified</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Moved(graph: obj, id: int) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        let run = log.OpenRun id
        let seq = log.Append (TraceEventKind.Moved, id, 0, log.RunNumber id, 0, run, null)
        log.NoteMoved id
        log.PushCause seq
#else
        ()
#endif

    /// <summary>Records <c>Schedule</c> for <c>item</c>, a scheduled node, with <c>ahead</c> items queued before it.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member Schedule(graph: obj, item: obj, ahead: int) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        let id = Tracer.IdOf item

        log.Append (TraceEventKind.Schedule, id, 0, ahead, 0, log.MarkOf id, null)
        |> ignore
#else
        ()
#endif

    /// <summary>Records <c>BatchEnter</c> at batch depth <c>depth</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member BatchEnter(graph: obj, depth: int) =
#if RANVIER_TRACE
        (Tracer.LogOf graph).Append(TraceEventKind.BatchEnter, 0, 0, depth, 0, 0, null)
        |> ignore
#else
        ()
#endif

    /// <summary>Records <c>BatchExit</c> at batch depth <c>depth</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member BatchExit(graph: obj, depth: int) =
#if RANVIER_TRACE
        (Tracer.LogOf graph).Append(TraceEventKind.BatchExit, 0, 0, depth, 0, 0, null)
        |> ignore
#else
        ()
#endif

    /// <summary>Records <c>FlushStart</c> and opens a flush.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member FlushStart(graph: obj) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        let number = log.EnterFlush ()

        log.Append (TraceEventKind.FlushStart, 0, 0, number, 0, 0, null)
        |> ignore
#else
        ()
#endif

    /// <summary>Closes the innermost flush: records <c>RunEnd</c> with <c>Abandoned</c> for each run it left open, then <c>FlushEnd</c>.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member FlushEnd(graph: obj) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        let struct (number, left) = log.ExitFlush ()

        for id in left do
            Tracer.Close (log, id, RunStatus.Abandoned)

        log.Append (TraceEventKind.FlushEnd, 0, 0, number, 0, 0, null)
        |> ignore
#else
        ()
#endif

    /// <summary>Records <c>RunStart</c> for run number <c>run</c> of node <c>id</c>, or its next run number when <c>run</c> is 0, and opens the run.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member RunStart(graph: obj, id: int, run: int) =
#if RANVIER_TRACE
        let log = Tracer.LogOf graph
        let walker = log.Walker
        let puller = if walker <> 0 then walker else log.Current
        let run = if run = 0 then log.RunCount id + 1 else run
        let seq = log.Append (TraceEventKind.RunStart, id, puller, run, 0, log.FirstDirty id, null)
        log.StartRun (id, seq, run)
#else
        ()
#endif

    /// <summary>Records <c>RunEnd</c> for node <c>id</c>'s open run, ended with <c>status</c>, and closes the run.</summary>
    [<Conditional("RANVIER_TRACE")>]
    static member RunEnd(graph: obj, id: int, status: Status) =
#if RANVIER_TRACE
        // Status bits: 1 pending, 2 failed.
        let bits = byte status

        let outcome =
            if bits &&& 1uy <> 0uy then RunStatus.Pending
            elif bits &&& 2uy <> 0uy then RunStatus.Error
            else RunStatus.Ok

        Tracer.Close (Tracer.LogOf graph, id, outcome)
#else
        ()
#endif
