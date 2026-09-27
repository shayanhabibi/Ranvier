namespace Ranvier

open System.Collections.Generic

/// <summary>Where a <c>Trace.why</c> chain ends.</summary>
type WhyRoot =
    /// <summary>A write from outside every run: the <c>Write</c> seq.</summary>
    | UserWrite of write: int
    /// <summary>The node's first run, with no dirty mark: the <c>NodeNew</c> seq, or 0 when it is not in the log.</summary>
    | Created of nodeNew: int
    /// <summary>A run with no dirty mark, started by a reader's pull: the reader's node id.</summary>
    | Pulled of reader: int
    /// <summary>A cause older than the events in the log: the checkpoint file that holds it, or null.</summary>
    | BeforeCheckpoint of file: string
    /// <summary>A run or mark whose cause the log does not record: the seq of the last step.</summary>
    | Unrecorded of last: int

/// <summary>One event of a <c>Trace.why</c> chain.</summary>
type WhyStep =
    {
        Seq: int
        Kind: TraceEventKind
        Node: int
        Other: int
    }

/// <summary>The cause chain of one run, from its <c>RunStart</c> back to a <c>WhyRoot</c>.</summary>
type Why =
    {
        Node: int
        /// <summary>The run number explained.</summary>
        Run: int
        /// <summary>The steps from the run's <c>RunStart</c> outwards, newest first.</summary>
        Steps: WhyStep list
        /// <summary>The chain's end, or <c>None</c> when <c>whyDepth</c> stopped it first.</summary>
        Root: WhyRoot option
    }

/// <summary>Why a node has not run since its last run ended, in <c>Trace.whyNot</c>.</summary>
type WhyNotReason =
    /// <summary>The node was disposed at <c>seq</c>.</summary>
    | Disposed of seq: int
    /// <summary>The node is scheduled and its run has not started: a batch is open or the flush is not reached.</summary>
    | Queued of schedule: int
    /// <summary>The node was marked at <c>mark</c> and has no observer and no pull since.</summary>
    | Unobserved of mark: int
    /// <summary>A check walk resolved the node clean at <c>resolvedAt</c>; <c>upstream</c> are its sources then.</summary>
    | CheckedClean of resolvedAt: int * upstream: int list
    /// <summary>A source's notification skipped the node as the running reader, at <c>seq</c>.</summary>
    | SkippedAsRunningReader of seq: int
    /// <summary>
    /// Propagation stopped upstream at <c>stopAt</c>: a <c>Write</c> that left its value, or a <c>RunEnd</c> that did
    /// not move.
    /// </summary>
    | NotReached of stopAt: int

/// <summary>Pure queries over an array of trace events, oldest first.</summary>
/// <remarks>
/// An array may start after seq 1 when earlier events were written to a checkpoint; a cause before the first event
/// resolves to <c>BeforeCheckpoint</c>.
/// </remarks>
[<RequireQualifiedAccess>]
module TraceModel =
    let private find (events: TraceEvent[]) (seq: int) : TraceEvent voption =
        if events.Length = 0 then
            ValueNone
        else
            let i = seq - events[0].Seq

            if i >= 0 && i < events.Length && events[i].Seq = seq then
                ValueSome events[i]
            else
                match events |> Array.tryFind (fun e -> e.Seq = seq) with
                | Some e -> ValueSome e
                | None -> ValueNone

    /// <summary>
    /// The source lists folded from <c>EdgeAdd</c>/<c>EdgeRemove</c>, by computation id, in slot order. Lists that
    /// fold to empty are left out.
    /// </summary>
    let sources (events: TraceEvent[]) : Map<int, int list> =
        let lists = Dictionary<int, ResizeArray<int>>()

        let listOf node =
            match lists.TryGetValue node with
            | true, list -> list
            | _ ->
                let list = ResizeArray ()
                lists[node] <- list
                list

        for e in events do
            match e.Kind with
            | TraceEventKind.EdgeAdd ->
                let list = listOf e.Node

                if list.Count > e.Arg then
                    list.RemoveRange (e.Arg, list.Count - e.Arg)

                list.Add e.Other
            | TraceEventKind.EdgeRemove ->
                let list = listOf e.Node

                if list.Count > e.Arg then
                    list.RemoveRange (e.Arg, list.Count - e.Arg)
            | _ -> ()

        lists
        |> Seq.filter (fun kv -> kv.Value.Count > 0)
        |> Seq.map (fun kv -> kv.Key, List.ofSeq kv.Value)
        |> Map.ofSeq

    /// <summary>
    /// The observer sets folded from <c>ObserverAdd</c>/<c>ObserverRemove</c>, by source id. Sets that fold to empty
    /// are left out.
    /// </summary>
    let observers (events: TraceEvent[]) : Map<int, Set<int>> =
        let sets = Dictionary<int, HashSet<int>>()

        for e in events do
            match e.Kind with
            | TraceEventKind.ObserverAdd ->
                match sets.TryGetValue e.Node with
                | true, set -> set.Add e.Other |> ignore
                | _ -> sets[e.Node] <- HashSet [ e.Other ]
            | TraceEventKind.ObserverRemove ->
                match sets.TryGetValue e.Node with
                | true, set -> set.Remove e.Other |> ignore
                | _ -> ()
            | _ -> ()

        sets
        |> Seq.filter (fun kv -> kv.Value.Count > 0)
        |> Seq.map (fun kv -> kv.Key, Set.ofSeq kv.Value)
        |> Map.ofSeq

    /// <summary>
    /// The cause chain of run number <c>run</c> of <c>node</c>, or of its last run when <c>run</c> is 0, stopped after
    /// <c>depth</c> steps when <c>depth</c> is positive. <c>checkpoint</c> names the file holding earlier events, or is
    /// null.
    /// </summary>
    /// <exception cref="T:System.ArgumentException">The events hold no matching <c>RunStart</c> for <c>node</c>.</exception>
    let whyDepth (events: TraceEvent[]) (checkpoint: string) (depth: int) (node: int) (run: int) : Why =
        let start =
            events
            |> Array.tryFindBack (fun e ->
                e.Kind = TraceEventKind.RunStart
                && e.Node = node
                && (run = 0 || e.Arg = run))

        match start with
        | None -> invalidArg (nameof run) $"The trace log holds no RunStart for node {node} run {run}."
        | Some start ->
            let steps = ResizeArray<WhyStep>()

            let step (e: TraceEvent) =
                steps.Add
                    {
                        Seq = e.Seq
                        Kind = e.Kind
                        Node = e.Node
                        Other = e.Other
                    }

            let rec walk (e: TraceEvent) : WhyRoot option =
                if depth > 0 && steps.Count >= depth then
                    None
                else
                    step e

                    let next (cause: int) (orElse: unit -> WhyRoot) =
                        if cause = 0 then
                            Some (orElse ())
                        else
                            match find events cause with
                            | ValueSome c -> walk c
                            | ValueNone when events.Length > 0 && cause < events[0].Seq -> Some (BeforeCheckpoint checkpoint)
                            | ValueNone -> Some (Unrecorded e.Seq)

                    match e.Kind with
                    | TraceEventKind.RunStart ->
                        next e.Cause (fun () ->
                            if e.Arg = 1 then
                                events
                                |> Array.tryFind (fun n -> n.Kind = TraceEventKind.NodeNew && n.Node = e.Node)
                                |> Option.map (fun n -> n.Seq)
                                |> Option.defaultValue 0
                                |> Created
                            elif e.Other <> 0 then
                                Pulled e.Other
                            else
                                Unrecorded e.Seq)
                    | TraceEventKind.Write ->
                        if e.Other = 0 then
                            Some (UserWrite e.Seq)
                        else
                            next e.Cause (fun () -> Unrecorded e.Seq)
                    | _ -> next e.Cause (fun () -> Unrecorded e.Seq)

            let root = walk start

            {
                Node = node
                Run = start.Arg
                Steps = List.ofSeq steps
                Root = root
            }

    /// <summary>The full cause chain of run number <c>run</c> of <c>node</c>, or of its last run when <c>run</c> is 0.</summary>
    let why (events: TraceEvent[]) (checkpoint: string) (node: int) (run: int) : Why =
        whyDepth events checkpoint 0 node run

    /// <summary>
    /// The first <c>WhyNotReason</c> matching the events after <c>node</c>'s last <c>RunEnd</c>, or after its
    /// <c>NodeNew</c> when it has not run; <c>None</c> when none matches.
    /// </summary>
    /// <remarks>
    /// Reasons are tried in the order <c>Disposed</c>, <c>Queued</c>, <c>Unobserved</c>, <c>CheckedClean</c>,
    /// <c>SkippedAsRunningReader</c>, <c>NotReached</c>. <c>SkippedAsRunningReader</c> also matches a skip inside
    /// the last run.
    /// </remarks>
    let whyNot (events: TraceEvent[]) (node: int) : WhyNotReason option =
        let from =
            let last =
                events
                |> Array.tryFindIndexBack (fun e ->
                    (e.Kind = TraceEventKind.RunEnd || e.Kind = TraceEventKind.NodeNew)
                    && e.Node = node)

            defaultArg (Option.map ((+) 1) last) 0

        let window = events[from..]
        let on kind (e: TraceEvent) = e.Kind = kind && e.Node = node
        let first kind = window |> Array.tryFind (on kind)

        let disposed () =
            first TraceEventKind.Dispose |> Option.map (fun e -> Disposed e.Seq)

        let queued () =
            window
            |> Array.tryFindIndexBack (on TraceEventKind.Schedule)
            |> Option.bind (fun i ->
                if window[i + 1 ..] |> Array.exists (on TraceEventKind.RunStart) then
                    None
                else
                    Some (Queued window[i].Seq))

        let unobserved () =
            match first TraceEventKind.Mark with
            | Some mark when
                not (observers events |> Map.containsKey node)
                && not (window |> Array.exists (on TraceEventKind.RunStart))
                && not (window |> Array.exists (on TraceEventKind.Schedule))
                ->
                Some (Unobserved mark.Seq)
            | _ -> None

        let checkedClean () =
            window
            |> Array.tryFindBack (fun e -> on TraceEventKind.CheckResolved e && e.Flag = 0)
            |> Option.map (fun e ->
                let upstream = sources events[.. e.Seq - events[0].Seq] |> Map.tryFind node |> Option.defaultValue []
                CheckedClean (e.Seq, upstream))

        let skipped () =
            let lastRun =
                events
                |> Array.tryFindIndexBack (on TraceEventKind.RunStart)
                |> Option.defaultValue from

            events[min lastRun from ..]
            |> Array.tryFind (on TraceEventKind.MarkSkip)
            |> Option.map (fun e -> SkippedAsRunningReader e.Seq)

        let notReached () =
            let lists = sources events
            let seen = HashSet<int>()
            let upstream = ResizeArray<int>()

            let rec visit n =
                for s in lists |> Map.tryFind n |> Option.defaultValue [] do
                    if seen.Add s then
                        upstream.Add s
                        visit s

            visit node

            window
            |> Array.tryFindBack (fun e ->
                seen.Contains e.Node
                && e.Flag = 0
                && (e.Kind = TraceEventKind.Write || e.Kind = TraceEventKind.RunEnd))
            |> Option.map (fun e -> NotReached e.Seq)

        // A clean check with no moving write since a skip is the skip's consequence.
        let skippedThenClean () =
            match skipped (), checkedClean () with
            | Some (SkippedAsRunningReader skip as reason), Some (CheckedClean (resolved, _)) when
                skip < resolved
                && not (
                    events
                    |> Array.exists (fun e ->
                        e.Seq > skip
                        && e.Seq < resolved
                        && e.Kind = TraceEventKind.Write
                        && e.Flag = 1)
                )
                ->
                Some reason
            | _ -> None

        [ disposed; queued; unobserved; skippedThenClean; checkedClean; skipped; notReached ]
        |> List.tryPick (fun reason -> reason ())
