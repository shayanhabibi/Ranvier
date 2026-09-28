module Ranvier.Benchmarks.Suspension

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// The pending channel is what this library is for, and suspension is what it
/// costs. A transparent read of a pending source aborts the reading body by
/// throwing, which on .NET is the expensive operation in the whole design.
/// </summary>
/// <remarks>
/// Each iteration writes a trigger the chain's first memo reads, so every read
/// re-runs the chain. <c>ThrowThroughChain</c> aborts on a source that never
/// settles; the baseline completes over a settled one. The difference is the
/// price of the throw.
/// </remarks>
[<MemoryDiagnoser; BenchmarkCategory "Suspension">]
type SuspensionBenchmarks() =
    let graph = new Graph ()
    let trigger = Signal (graph, 0)
    let mutable pendingTail: Memo<int> = Unchecked.defaultof<Memo<int>>
    let mutable settledTail: Memo<int> = Unchecked.defaultof<Memo<int>>
    let mutable tick = 0

    /// <summary>
    /// Frames between the suspending read and the reader.
    /// </summary>
    [<Params(1, 4, 16)>]
    member val Depth = 1 with get, set

    member private _.BuildChain(read: unit -> int, depth) =
        let mutable previous = Memo (graph, (fun _ -> trigger.Value + read ()))

        for _ in 2..depth do
            let inner = previous
            previous <- Memo (graph, (fun _ -> inner.Value + 1))

        previous

    [<GlobalSetup>]
    member this.Setup() =
        let pending = AsyncSource<int>(graph)
        let settled = AsyncSource<int>(graph)
        settled.Settle 1
        pendingTail <- this.BuildChain ((fun () -> pending.Value), this.Depth)
        settledTail <- this.BuildChain ((fun () -> settled.Value), this.Depth)

    /// <summary>
    /// Re-runs the chain to completion over a settled source.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.RecomputeSettledChain() =
        tick <- tick + 1
        trigger.Value <- tick
        settledTail.TryValue

    /// <summary>
    /// Re-runs the chain and aborts it on a source that never settles.
    /// </summary>
    [<Benchmark>]
    member _.ThrowThroughChain() =
        tick <- tick + 1
        trigger.Value <- tick
        pendingTail.TryValue

/// <summary>
/// A boundary catches the channel instead of letting it propagate, so the cost
/// of a suspended subtree is bounded by where the boundary sits rather than by
/// the depth of the whole graph. Each iteration writes a trigger both bodies
/// read, so every read re-runs the body.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Suspension">]
type BoundaryBenchmarks() =
    let graph = new Graph ()
    let trigger = Signal (graph, 0)
    let source = AsyncSource<int>(graph)
    let settledSource = Signal (graph, 1)
    let mutable tick = 0

    let pending =
        Boundary<int>.Suspense(graph, (fun () -> trigger.Value + source.Value), (fun _ -> 0))

    let settled =
        Boundary<int>.Suspense(graph, (fun () -> trigger.Value + settledSource.Value), (fun _ -> 0))

    [<GlobalSetup>]
    member _.Setup() =
        pending.TryValue |> ignore
        settled.TryValue |> ignore

    /// <summary>
    /// Re-runs a body that is still waiting and catches its throw.
    /// </summary>
    [<Benchmark>]
    member _.CatchingPending() =
        tick <- tick + 1
        trigger.Value <- tick
        pending.TryValue

    /// <summary>
    /// Re-runs a body over settled sources: the price of the boundary when
    /// nothing is in flight.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.CleanBoundary() =
        tick <- tick + 1
        trigger.Value <- tick
        settled.TryValue

/// <summary>
/// Settling a source is where a pending subtree becomes a live one, and it is
/// the only path in the library that can arrive from another thread.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Suspension">]
type SettleBenchmarks() =
    let graph = new Graph ()

    /// <summary>
    /// Settle on the graph's own thread, which takes the inline fast path and
    /// never touches the inbox. The dispatcher's job is to be skipped.
    /// </summary>
    [<Benchmark>]
    member _.SettleInline() =
        let source = AsyncSource<int>(graph)
        source.Settle 1
        source.TryValue

    /// <summary>
    /// Constructing and reading a source that is already settled, for the
    /// floor that SettleInline is measured against.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.CreateAndRead() =
        let source = AsyncSource<int>(graph)
        source.TryValue
