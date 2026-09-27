namespace Ranvier

/// <summary>Provenance queries over a graph's event log, compiled in by <c>RanvierTrace=true</c>.</summary>
/// <remarks>An untraced build carries <c>Trace.named</c> alone.</remarks>
[<AbstractClass; Sealed>]
type Trace =
#if RANVIER_TRACE
    /// <summary>Runs <c>f</c> and labels the first node or owner it creates on this thread.</summary>
    /// <remarks>
    /// A label <c>f</c> leaves unused is recorded as a <c>Label</c> event with <c>Node = 0</c> on the ambient graph.
    /// Untraced, <c>named</c> inlines to <c>f ()</c>, and a literal label costs nothing.
    /// </remarks>
    static member named (label: string) (f: unit -> 'T) : 'T =
        Tracer.PushLabel label

        try
            f ()
        finally
            Tracer.PopLabel (
                match Graph.TryCurrent with
                | ValueSome graph -> box graph
                | ValueNone -> null
            )

    /// <summary>A copy of the events <c>graph</c> has recorded since its last checkpoint, oldest first.</summary>
    static member events(graph: Graph) : TraceEvent[] =
        ((box graph) :?> ITraced).TraceLog.Events
#else
    /// <summary>Runs <c>f</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    /// <remarks>Inlines to <c>f ()</c>; a literal label costs nothing.</remarks>
    static member inline named (_label: string) ([<InlineIfLambda>] f: unit -> 'T) : 'T = f ()
#endif
