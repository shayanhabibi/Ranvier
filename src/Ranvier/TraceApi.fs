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
#else
    /// <summary>Runs <c>f</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    /// <remarks>Inlines to <c>f ()</c> on .NET and in Fable; a literal label costs nothing.</remarks>
    let inline named (_label: string) ([<InlineIfLambda>] f: unit -> 'T) : 'T = f ()
#endif
