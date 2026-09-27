namespace Ranvier

open System.Diagnostics

#if RANVIER_TRACE
open System

/// <summary>A graph's append-only event log, with the graph's clock and owner-id counter.</summary>
/// <remarks>The log holds ids and payloads only; it references no node, owner or graph.</remarks>
[<Sealed; AllowNullLiteral>]
type internal TraceLog() =
    let events = ResizeArray<TraceEvent>()
    let mutable clock = 0
    let mutable owners = 0

    /// <summary>Records one event and returns its <c>Seq</c>.</summary>
    /// <exception cref="T:System.InvalidOperationException">The graph's clock is at <c>Int32.MaxValue</c>.</exception>
    member _.Append(kind: TraceEventKind, node: int, other: int, arg: int, flag: int, cause: int, payload: obj) : int =
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

    /// <summary>Allocates the next owner id, starting at 1.</summary>
    member _.NextOwnerId() =
        owners <- owners + 1
        owners

    /// <summary>A copy of the recorded events, oldest first.</summary>
    member _.Events = events.ToArray ()

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
    /// <summary>Pending <c>Trace.named</c> labels on this thread, innermost last. Null once consumed.</summary>
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
#endif

    /// <summary>
    /// Gives <c>root</c>, a new graph's root owner, the graph's log and owner id 1, and records <c>GraphNew</c>.
    /// </summary>
    /// <remarks>The graph reaches its log through its root owner.</remarks>
    [<Conditional("RANVIER_TRACE")>]
    static member GraphNew(root: obj) =
#if RANVIER_TRACE
        let traced = root :?> ITraced
        let log = TraceLog ()
        traced.TraceLog <- log
        let id = log.NextOwnerId ()
        traced.TraceId <- id

        log.Append (TraceEventKind.GraphNew, 0, id, 0, 0, 0, null)
        |> ignore
#else
        ()
#endif
