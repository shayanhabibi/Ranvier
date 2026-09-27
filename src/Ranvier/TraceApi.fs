namespace Ranvier

/// <summary>Provenance queries over a graph's event log, compiled in by <c>RanvierTrace=true</c>.</summary>
/// <remarks>An untraced build carries <c>Trace.named</c> alone.</remarks>
[<RequireQualifiedAccess>]
module Trace =
#if RANVIER_TRACE
    /// <summary>Runs <c>f</c> and labels the first node or owner it creates on this thread.</summary>
    /// <remarks>
    /// A label <c>f</c> leaves unused is recorded as a <c>Label</c> event with <c>Node = 0</c> on the ambient graph.
    /// Untraced, <c>named</c> inlines to <c>f ()</c>, and a literal label costs nothing.
    /// </remarks>
    let named (label: string) (f: unit -> 'T) : 'T =
        Tracer.PushLabel label

        try
            f ()
        finally
            Tracer.PopLabel (
                match Graph.TryCurrent with
                | ValueSome graph -> box graph
                | ValueNone -> null
            )

    /// <summary>A copy of the events <c>graph</c> has recorded, oldest first.</summary>
    let events (graph: Graph) : TraceEvent[] = ((box graph) :?> ITraced).TraceLog.Events

    /// <summary>The creation record of <c>node</c> in <c>graph</c>'s log.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>NodeNew</c> for <c>node</c>.</exception>
    let origin (graph: Graph) (node: INode) : TraceOrigin =
        let events = events graph
        let id = node.Id

        match events |> Array.tryFindIndex (fun e -> e.Kind = TraceEventKind.NodeNew && e.Node = id) with
        | None -> invalidArg (nameof node) $"The graph's trace log holds no NodeNew for node {id}."
        | Some i ->
            let created = events[i]

            let label =
                if i + 1 < events.Length
                   && events[i + 1].Kind = TraceEventKind.Label
                   && events[i + 1].Node = id then
                    Some (string events[i + 1].Payload)
                else
                    None

            {
                Seq = created.Seq
                Node = id
                Kind = enum<TraceNodeKind> created.Arg
                Label = label
                Owner = created.Other
                Run = created.Cause
                Site = created.Payload
            }

    /// <summary>The cause chain of <c>node</c>'s last run in <c>graph</c>'s log.</summary>
    /// <exception cref="T:System.ArgumentException">The log holds no <c>RunStart</c> for <c>node</c>.</exception>
    let why (graph: Graph) (node: INode) : Why = TraceModel.why (events graph) null node.Id 0

    /// <summary>The cause chain of run number <c>run</c>, from 1, of <c>node</c> in <c>graph</c>'s log.</summary>
    /// <exception cref="T:System.ArgumentException">The log holds no such run.</exception>
    let whyAt (graph: Graph) (node: INode) (run: int) : Why =
        if run < 1 then
            invalidArg (nameof run) "A run number starts at 1."

        TraceModel.why (events graph) null node.Id run

    /// <summary>The first <c>depth</c> steps of the cause chain of <c>node</c>'s last run in <c>graph</c>'s log.</summary>
    /// <exception cref="T:System.ArgumentException">The log holds no <c>RunStart</c> for <c>node</c>.</exception>
    let whyDepth (graph: Graph) (depth: int) (node: INode) : Why =
        TraceModel.whyDepth (events graph) null depth node.Id 0

    /// <summary>Why <c>node</c> has not run since its last run ended, or <c>None</c> when the log shows no reason.</summary>
    let whyNot (graph: Graph) (node: INode) : WhyNotReason option = TraceModel.whyNot (events graph) node.Id

    /// <summary>
    /// The differences between <c>graph</c>'s live source lists and observer sets and those folded from its log, one
    /// line per node; empty when they match.
    /// </summary>
    let reconcile (graph: Graph) : string list =
        let log = ((box graph) :?> ITraced).TraceLog
        let events = log.Events
        let sources = TraceModel.sources events
        let observers = TraceModel.observers events

        [
            for set in log.EdgeSets do
                let live = set.Ids

                if set.IsSources then
                    let folded = sources |> Map.tryFind set.Owner |> Option.defaultValue []

                    if List.ofArray live <> folded then
                        yield $"sources of {set.Owner}: live %A{live}, folded %A{folded}"
                else
                    let folded = observers |> Map.tryFind set.Owner |> Option.defaultValue Set.empty

                    if Set.ofArray live <> folded then
                        yield $"observers of {set.Owner}: live %A{live}, folded %A{folded}"
        ]
#else
    /// <summary>Runs <c>f</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    /// <remarks>Inlines to <c>f ()</c> on .NET and in Fable; a literal label costs nothing.</remarks>
    let inline named (_label: string) ([<InlineIfLambda>] f: unit -> 'T) : 'T = f ()
#endif
