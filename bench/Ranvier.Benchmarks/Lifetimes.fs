module Ranvier.Benchmarks.Lifetimes

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// Construction and teardown are not the hot path, but they are the path a UI
/// takes on every mount and unmount, and the one where an owner that only
/// grows shows up as a leak rather than as a slowdown.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Lifetime">]
type ConstructionBenchmarks() =
    let graph = new Graph ()

    [<Benchmark(Baseline = true)>]
    member _.CreateSignal() =
        Signal (graph, 0)

    // There is deliberately no create-without-dispose case for a memo. A memo
    // attaches itself to the enclosing owner, so one that is never disposed is
    // retained for the life of the graph — by design, and the same as upstream.
    // Benchmarked, that is not a constructor measurement: BenchmarkDotNet ran
    // ~19 million ops into one owner and the later iterations were measuring
    // gen2 collection over a child list of that size, at 4000x the cost of the
    // first. A construction number that only holds while the heap is small is
    // worse than no number.

    [<Benchmark>]
    member _.CreateAndDisposeMemo() =
        let memo = Memo (graph, (fun _ -> 1))
        memo.Dispose ()

    [<Benchmark>]
    member _.CreateAndDisposeEffect() =
        let effect = new Effect (graph, id)
        effect.Dispose ()

/// <summary>
/// Construction and teardown of nodes that read a source, so the edge is part
/// of the cost: linked on the first run, unlinked on disposal.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Lifetime">]
type ReadingNodeBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)

    [<Benchmark>]
    member _.CreateAndDisposeReadingEffect() =
        let effect = new Effect (graph, (fun () -> source.Value |> ignore))
        effect.Dispose ()

    [<Benchmark>]
    member _.CreateAndDisposeReadingMemo() =
        let memo = Memo (graph, (fun _ -> source.Value + 1))
        memo.TryValue |> ignore
        memo.Dispose ()

/// <summary>
/// <c>Nodes</c> computations on one source, created in a scope and torn down with
/// it. The fan-out crosses the observer list's index threshold between 1 and
/// 64, so the larger cases include building and draining the index.
/// </summary>
/// <remarks>
/// Divide by <c>Nodes</c> for the cost per node.
/// </remarks>
[<MemoryDiagnoser; BenchmarkCategory "Lifetime">]
type FanOutLifecycleBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)

    [<Params(1, 64, 1024)>]
    member val Nodes = 1 with get, set

    [<Benchmark>]
    member this.EffectsOnOneSource() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    new Effect (graph, (fun () -> source.Value |> ignore))
                    |> ignore

                owner)

        owner.Dispose ()

    [<Benchmark>]
    member this.MemosOnOneSource() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    let memo = Memo (graph, (fun _ -> source.Value + 1))
                    memo.TryValue |> ignore

                owner)

        owner.Dispose ()

/// <summary>
/// A scope with children, created and torn down as a unit. This is the shape a
/// component mount takes, and the one the owner's child list has to survive
/// being run millions of times.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Lifetime">]
type ScopeBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)

    [<Params(1, 8, 64)>]
    member val Children = 1 with get, set

    [<Benchmark>]
    member this.CreateAndDisposeScope() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Children do
                    let memo = Memo (graph, (fun _ -> source.Value + 1))
                    memo.TryValue |> ignore

                owner)

        owner.Dispose ()

    /// <summary>
    /// The same children, each disposed individually rather than with the
    /// scope. This is the path that used to leave every one of them in the
    /// owner's list for the life of the graph.
    /// </summary>
    [<Benchmark>]
    member this.DisposeChildrenIndividually() =
        let memos =
            Array.init this.Children (fun _ ->
                let memo = Memo (graph, (fun _ -> source.Value + 1))
                memo.TryValue |> ignore
                memo)

        for memo in memos do
            memo.Dispose ()
