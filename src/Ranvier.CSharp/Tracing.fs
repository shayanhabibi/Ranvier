namespace Ranvier.CSharp

open System
open System.Collections.Generic
open System.Diagnostics
open Ranvier

/// <summary>Labels and provenance queries over a graph's event log, as text.</summary>
/// <remarks>
/// <c>Named</c> and <c>Label</c> are present in every build. The queries are present only when Ranvier is built with
/// <c>RanvierTrace=true</c>; call them from code under <c>#if RANVIER_TRACE</c>.
/// </remarks>
/// <example>
/// <code lang="csharp">
/// var graph = new Graph();
/// graph.Run(() =>
/// {
///     var count = Tracing.Named("count", () => Signal(1));
///     var doubled = Tracing.Named("doubled", () => Memo(() => count.Value * 2));
///     _ = doubled.Value;
///     count.Value = 2;
///     _ = doubled.Value;
/// #if RANVIER_TRACE
///     Console.WriteLine(Tracing.Why(graph, doubled));
/// #endif
/// });
/// </code>
/// </example>
[<AbstractClass; Sealed>]
type Tracing =

    /// <summary>Runs <c>body</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    static member Named<'T>(label: string, body: Func<'T>) : 'T =
        Trace.named label body.Invoke

    /// <summary>Runs <c>body</c>. A traced build also labels the first node or owner it creates on this thread.</summary>
    static member Named(label: string, body: Action) : unit =
        Trace.named label body.Invoke

    /// <summary>Labels <c>node</c> in <c>graph</c>'s log with <c>text</c>, which may be computed.</summary>
    /// <remarks>
    /// The label replaces the node's path segment. A caller compiled without <c>RANVIER_TRACE</c> drops the call along
    /// with its argument expressions.
    /// </remarks>
    [<Conditional("RANVIER_TRACE")>]
    static member Label(graph: Graph, node: INode, text: string) : unit =
        Trace.label (graph, node, text)

#if RANVIER_TRACE
    /// <summary>A copy of the events <c>graph</c> has recorded, oldest first.</summary>
    static member Events(graph: Graph) : TraceEvent[] =
        Trace.events graph

    /// <summary>The creation record of <c>node</c>: its identity path, kind, <c>file:line</c> site and seq.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>NodeNew</c> for <c>node</c>.</exception>
    static member Origin(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.origin graph node)

    /// <summary>The cause chain of <c>node</c>'s last run, from its <c>RunStart</c> back to the write that caused it.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>RunStart</c> for <c>node</c>.</exception>
    static member Why(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.why graph node)

    /// <summary>The cause chain of run number <c>run</c>, from 1, of <c>node</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no such run.</exception>
    static member Why(graph: Graph, node: INode, run: int) : string =
        Trace.render graph (Trace.whyAt graph node run)

    /// <summary>The first <c>depth</c> steps of the cause chain of <c>node</c>'s last run.</summary>
    /// <exception cref="T:System.ArgumentException"><c>graph</c>'s log holds no <c>RunStart</c> for <c>node</c>.</exception>
    static member WhyDepth(graph: Graph, node: INode, depth: int) : string =
        Trace.render graph (Trace.whyDepth graph depth node)

    /// <summary>Why <c>node</c> has not run since its last run ended, or a line saying the log shows no reason.</summary>
    static member WhyNot(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.whyNot graph node)

    /// <summary>Every recorded run of <c>node</c>, one line per run, with its status, movement, cause and flush.</summary>
    static member History(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.history graph node)

    /// <summary>The pending sources read by <c>node</c>'s last run, and the node's flights with their results.</summary>
    static member WaitingOn(graph: Graph, node: INode) : string =
        Trace.render graph (Trace.waitingOn graph node)

    /// <summary>The live nodes and owners folded from every event in <c>graph</c>'s log.</summary>
    static member Snapshot(graph: Graph) : string =
        Trace.render graph (Trace.snapshot graph)

    /// <summary>The live nodes and owners folded from the events in <c>graph</c>'s log up to and including <c>seq</c>.</summary>
    static member Snapshot(graph: Graph, seq: int) : string =
        Trace.render graph (Trace.snapshotAt graph seq)

    /// <summary>
    /// The node id at identity path <c>path</c>: the <c>@k</c>-th holder, else the live holder, else the latest; null for an
    /// unknown path.
    /// </summary>
    static member Resolve(graph: Graph, path: string) : Nullable<int> =
        match Trace.resolve graph path with
        | Some id -> Nullable id
        | None -> Nullable ()

    /// <summary>The node at identity path <c>path</c>, resolved as for <c>Resolve</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member private At(graph: Graph, path: string) : INode =
        match Trace.resolve graph path with
        | Some id ->
            { new INode with
                member _.Id = id
                member _.Status = Status.None
            }
        | None -> raise (ArgumentException ($"The graph's trace log holds no node at path '{path}'.", nameof path))

    /// <summary><c>Origin</c> of the node at identity path <c>path</c>, such as a node labelled by <c>Named</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>NodeNew</c> for its node.</exception>
    static member Origin(graph: Graph, path: string) : string =
        Tracing.Origin (graph, Tracing.At (graph, path))

    /// <summary><c>Why</c> of the node at identity path <c>path</c>, such as an <c>EffectOn</c> labelled by <c>Named</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>RunStart</c> for its node.</exception>
    static member Why(graph: Graph, path: string) : string =
        Tracing.Why (graph, Tracing.At (graph, path))

    /// <summary>The cause chain of run number <c>run</c>, from 1, of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no such run.</exception>
    static member Why(graph: Graph, path: string, run: int) : string =
        Tracing.Why (graph, Tracing.At (graph, path), run)

    /// <summary><c>WhyDepth</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown, or the log holds no <c>RunStart</c> for its node.</exception>
    static member WhyDepth(graph: Graph, path: string, depth: int) : string =
        Tracing.WhyDepth (graph, Tracing.At (graph, path), depth)

    /// <summary><c>WhyNot</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member WhyNot(graph: Graph, path: string) : string =
        Tracing.WhyNot (graph, Tracing.At (graph, path))

    /// <summary><c>History</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member History(graph: Graph, path: string) : string =
        Tracing.History (graph, Tracing.At (graph, path))

    /// <summary><c>WaitingOn</c> of the node at identity path <c>path</c>.</summary>
    /// <exception cref="T:System.ArgumentException"><c>path</c> is unknown to <c>graph</c>'s log.</exception>
    static member WaitingOn(graph: Graph, path: string) : string =
        Tracing.WaitingOn (graph, Tracing.At (graph, path))

    /// <summary>
    /// The differences between <c>graph</c>'s live source lists and observer sets and those folded from its log, one
    /// line per node; empty when they match.
    /// </summary>
    static member Reconcile(graph: Graph) : IReadOnlyList<string> =
        Trace.reconcile graph |> List.toArray :> IReadOnlyList<string>

    /// <summary>The JSONL dump of <c>graph</c>'s log, schema 1.</summary>
    /// <exception cref="T:System.InvalidOperationException">
    /// Called off the graph's thread, or while a flush, a discharge or a run is in progress. A <c>Batch</c> is allowed.
    /// </exception>
    static member DumpText(graph: Graph) : string =
        Trace.dumpText graph

    /// <summary>Writes the JSONL dump of <c>graph</c>'s log, schema 1, to <c>path</c>, and returns the full path.</summary>
    /// <exception cref="T:System.InvalidOperationException">As for <c>DumpText</c>.</exception>
    static member Dump(graph: Graph, path: string) : string =
        Trace.dump graph path
#endif
