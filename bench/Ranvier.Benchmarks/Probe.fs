module Ranvier.Benchmarks.Probe

open System.Collections.Generic
open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// Decomposes one memo recomputation into its parts, so a change to the edge
/// model can be aimed at a measurement rather than a guess — and so the effect
/// of that change is visible per part, not only in the total.
/// </summary>
/// <remarks>
/// What it said on first run (Ryzen 9 9900X, short job): the total is linear
/// in the number of edges at ~20 ns per edge on a fixed ~19 ns of frame, and
/// ~14 of those 20 ns are set churn — see <c>SetChurnProbe</c>. The body itself is
/// free. The cost is entirely in rebuilding the dependency edges.
/// </remarks>
[<MemoryDiagnoser; BenchmarkCategory "Probe">]
type FanInProbe() =
    let graph = new Graph ()
    let mutable signals: Signal<int>[] = Array.empty
    let mutable memo: Memo<int> = Unchecked.defaultof<Memo<int>>
    let mutable counter = 0

    /// <summary>
    /// The slope across these is the per-edge cost of a recomputation: one
    /// RemoveObserver, one source-set add, one observer-set add and one
    /// tracked read.
    /// </summary>
    [<Params(1, 2, 4, 8)>]
    member val Sources = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        signals <- Array.init this.Sources (fun i -> Signal (graph, i))

        memo <-
            Memo (
                graph,
                fun _ ->
                    let mutable total = 0

                    for s in signals do
                        total <- total + s.Value

                    total
            )

        memo.TryValue |> ignore

    /// <summary>
    /// Write one source, read the memo: one full invalidate-and-recompute.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.WriteThenRead() =
        counter <- counter + 1
        signals[0].Value <- counter
        memo.Value

    /// <summary>
    /// The same write with nothing listening to the memo's own recomputation,
    /// minus the recomputation: the invalidation half on its own.
    /// </summary>
    [<Benchmark>]
    member _.WriteOnly() =
        counter <- counter + 1
        signals[0].Value <- counter

    /// <summary>
    /// The body alone, run untracked: no detach, no edge rebuild, no try
    /// frame. The floor for "what the user's own code costs".
    /// </summary>
    [<Benchmark>]
    member _.BodyOnly() =
        let mutable total = 0

        for s in signals do
            total <- total + s.Peek

        total

/// <summary>
/// The set churn a recomputation performs, in isolation: what detaching N
/// edges and re-adding them costs with nothing else in the frame.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Probe">]
type SetChurnProbe() =
    let sources = HashSet<obj>(HashIdentity.Reference)
    let observers = Array.init 8 (fun _ -> HashSet<obj>(HashIdentity.Reference))
    let items = Array.init 8 (fun i -> box i)
    let self = obj ()

    [<Params(1, 2, 4, 8)>]
    member val Sources = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        for i in 0 .. this.Sources - 1 do
            sources.Add items[i] |> ignore
            observers[i].Add self |> ignore

    /// <summary>
    /// detach () followed by the re-collection the body performs.
    /// </summary>
    [<Benchmark>]
    member this.DetachAndRelink() =
        for i in 0 .. this.Sources - 1 do
            observers[i].Remove self |> ignore

        sources.Clear ()

        for i in 0 .. this.Sources - 1 do
            observers[i].Add self |> ignore
            sources.Add items[i] |> ignore

    /// <summary>
    /// The same edges held in an array instead, positionally: what the
    /// alternative design would cost if the dependency list is unchanged,
    /// which is the overwhelmingly common case.
    /// </summary>
    [<Benchmark>]
    member this.PositionalCompare() =
        let mutable same = true

        for i in 0 .. this.Sources - 1 do
            same <- same && obj.ReferenceEquals (items[i], items[i])

        same

/// <summary>
/// The notification half. <c>SignalBenchmarks</c> already says a write with one
/// observer costs 3x a write with none; this asks what that 7 ns is.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Probe">]
type NotifyProbe() =
    let members = HashSet<obj>(HashIdentity.Reference)
    let mutable buffer: obj[] = Array.empty
    let self = obj ()

    [<GlobalSetup>]
    member _.Setup() =
        members.Add self |> ignore
        buffer <- Array.zeroCreate 8

    /// <summary>
    /// Exactly what ObserverSet.NotifyDirty does around the callback: copy the
    /// set into a reusable buffer, walk it, then clear the buffer so a dropped
    /// observer is not retained by a stale slot.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.CopyWalkAndClear() =
        let count = members.Count
        members.CopyTo buffer
        let mutable touched = 0

        for i in 0 .. count - 1 do
            if not (isNull buffer[i]) then
                touched <- touched + 1

        System.Array.Fill (buffer, null, 0, count)
        touched

    /// <summary>
    /// Walking the set directly, which is what the buffer exists to avoid —
    /// the enumerator is invalidated if a callback drops an edge.
    /// </summary>
    [<Benchmark>]
    member _.EnumerateDirectly() =
        let mutable touched = 0

        for _ in members do
            touched <- touched + 1

        touched

/// <summary>
/// A stand-in observer, so the edge operations can be driven directly rather
/// than inferred from a recomputation that also does other work.
/// </summary>
type private DummyComputation(id: int) =
    interface INode with
        member _.Id = id
        member _.Status = Status.None

    interface IComputation with
        member _.MarkDirty() = ()
        member _.MarkCheck() = ()
        member _.AddSource _ = ()

/// <summary>
/// <c>AddObserver</c> and <c>RemoveObserver</c> on their own: the exact pair a
/// recomputation performs per edge, with nothing else in the frame.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Probe">]
type EdgeChurnProbe() =
    let graph = new Graph ()
    let mutable sources: ISource[] = Array.empty
    let observer = DummyComputation 0 :> IComputation

    [<Params(1, 8)>]
    member val Sources = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        sources <- Array.init this.Sources (fun i -> Signal (graph, i) :> ISource)

        for s in sources do
            s.AddObserver observer

    /// <summary>
    /// Detach then re-attach, once per source.
    /// </summary>
    [<Benchmark>]
    member _.RemoveThenAdd() =
        for s in sources do
            s.RemoveObserver observer

        for s in sources do
            s.AddObserver observer
