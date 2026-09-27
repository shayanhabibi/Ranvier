namespace Ranvier

/// <summary>What a <c>TraceEvent</c> records. Each kind gives its own meaning to the event's fields.</summary>
type TraceEventKind =
    /// <summary>The graph was constructed. <c>Other</c>: the root owner id.</summary>
    | GraphNew = 1
    /// <summary>
    /// A node was constructed. <c>Other</c>: its owner id, or 0. <c>Arg</c>: its <c>TraceNodeKind</c>. <c>Cause</c>:
    /// the <c>RunStart</c> of the creating run, or 0.
    /// </summary>
    | NodeNew = 2
    /// <summary>
    /// An owner joined the log. <c>Other</c>: its parent owner id, or 0. <c>Arg</c>: the host node id of a run scope,
    /// or 0. <c>Flag</c>: 1 for a <c>createRoot</c> scope. <c>Cause</c>: as for <c>NodeNew</c>.
    /// </summary>
    | OwnerNew = 3
    /// <summary>A node was disposed.</summary>
    | Dispose = 4
    /// <summary>An owner was disposed.</summary>
    | OwnerDispose = 5
    /// <summary>
    /// A <c>Trace.named</c> label. <c>Node</c>: the labelled node or owner id, or 0 when the thunk created neither.
    /// <c>Payload</c>: the label.
    /// </summary>
    | Label = 6
    /// <summary>A signal was written. <c>Other</c>: the running computation, or 0. <c>Flag</c>: 1 when the value moved.</summary>
    | Write = 10
    /// <summary>A reader was marked. <c>Other</c>: the source. <c>Arg</c>: 1 check, 2 dirty.</summary>
    | Mark = 11
    /// <summary>A mark skipped the running reader. Fields as for <c>Mark</c>.</summary>
    | MarkSkip = 12
    /// <summary>A node was queued. <c>Arg</c>: the queue length ahead of it.</summary>
    | Schedule = 13
    /// <summary>A check walk started on a node.</summary>
    | CheckStart = 14
    /// <summary>
    /// A check walk resolved. <c>Other</c>: the source answered dirty, or 0. <c>Flag</c>: 1 dirty, 0 clean.
    /// </summary>
    | CheckResolved = 15
    /// <summary>A computation gained a source. <c>Other</c>: the source. <c>Arg</c>: the slot.</summary>
    | EdgeAdd = 16
    /// <summary>A computation lost a source. Fields as for <c>EdgeAdd</c>.</summary>
    | EdgeRemove = 17
    /// <summary>A source gained an observer. <c>Other</c>: the observer.</summary>
    | ObserverAdd = 18
    /// <summary>A source lost an observer. <c>Other</c>: the observer.</summary>
    | ObserverRemove = 19
    /// <summary>
    /// A run started. <c>Other</c>: the puller, or 0. <c>Arg</c>: the run number, from 1. <c>Cause</c>: the first
    /// dirty <c>Mark</c> since the previous run, or 0.
    /// </summary>
    | RunStart = 20
    /// <summary>A run moved the node's value. <c>Arg</c>: the run number.</summary>
    | Moved = 21
    /// <summary>A run ended. <c>Arg</c>: its <c>RunStatus</c>. <c>Flag</c>: 1 when the run recorded <c>Moved</c>.</summary>
    | RunEnd = 22
    /// <summary>A walker frame was unwound without its pop.</summary>
    | WalkAbandoned = 23
    /// <summary>A flush started. <c>Arg</c>: the flush number.</summary>
    | FlushStart = 30
    /// <summary>A flush ended. <c>Arg</c>: the flush number.</summary>
    | FlushEnd = 31
    /// <summary>A batch opened. <c>Arg</c>: the depth after the change.</summary>
    | BatchEnter = 32
    /// <summary>A batch closed. <c>Arg</c>: the depth after the change.</summary>
    | BatchExit = 33
    /// <summary>A run scope's discharge started. <c>Node</c>: the owner id. <c>Other</c>: the host node id.</summary>
    | DischargeStart = 34
    /// <summary>A run scope's discharge ended. Fields as for <c>DischargeStart</c>.</summary>
    | DischargeEnd = 35

/// <summary>The node type a <c>NodeNew</c> event records.</summary>
type TraceNodeKind =
    | Signal = 1
    | AsyncSource = 2
    | Memo = 3
    | Effect = 4
    | AsyncMemo = 5
    | Boundary = 6
    | Projection = 7
    | ProjectionBeacon = 8
    | RowWatch = 9
    | LookupCell = 10

/// <summary>How a run ended, in <c>RunEnd.Arg</c>.</summary>
type RunStatus =
    | Ok = 0
    | Pending = 1
    | Error = 2
    /// <summary>The run was closed by recovery: its frame unwound, or its flush ended with the run open.</summary>
    | Abandoned = 3

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

/// <summary>A node's creation record.</summary>
type TraceOrigin =
    {
        /// <summary>The <c>Seq</c> of the node's <c>NodeNew</c>.</summary>
        Seq: int
        Node: int
        Kind: TraceNodeKind
        /// <summary>The <c>Trace.named</c> label the node took, if any.</summary>
        Label: string option
        /// <summary>The owner id the node attached to, or 0.</summary>
        Owner: int
        /// <summary>The <c>RunStart</c> seq of the creating run, or 0.</summary>
        Run: int
        /// <summary>The creation site, or null when none was captured.</summary>
        Site: obj
    }
