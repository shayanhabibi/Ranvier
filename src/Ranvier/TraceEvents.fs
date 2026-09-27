namespace Ranvier

/// <summary>What a <c>TraceEvent</c> records. Each kind gives its own meaning to the event's fields.</summary>
type TraceEventKind =
    /// <summary>The graph was constructed. <c>Other</c>: the root owner id.</summary>
    | GraphNew = 1
    /// <summary>
    /// A <c>Trace.named</c> label. <c>Node</c>: the labelled node or owner id, or 0 when the thunk created neither.
    /// <c>Payload</c>: the label.
    /// </summary>
    | Label = 6

/// <summary>One entry of a graph's trace log.</summary>
/// <remarks>
/// Node ids are the graph's own ids; owner ids come from the log's counter. <c>Cause</c> is the <c>Seq</c> of the
/// causing event, or 0. A payload is never a node, owner, graph or projection.
/// </remarks>
[<Struct; NoEquality; NoComparison>]
type TraceEvent =
    {
        /// <summary>The graph's clock at this event, starting at 1.</summary>
        Seq: int
        Kind: TraceEventKind
        Node: int
        Other: int
        Arg: int
        Flag: int
        Cause: int
        Payload: obj
    }
