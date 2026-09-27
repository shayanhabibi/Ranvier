module Ranvier.Benchmarks.Memos

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// Memos are pull-based: nothing runs them, a read does. So the two numbers
/// that matter are the cache hit — which every read pays — and the
/// recomputation, which only an invalidated read pays.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Memo">]
type MemoBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)
    let memo = Memo (graph, (fun () -> source.Value * 2))
    let mutable counter = 0

    [<GlobalSetup>]
    member _.Setup() =
        memo.TryValue |> ignore

    /// <summary>
    /// A read of a clean memo. This is the common case by a wide margin, and
    /// it should be a flag check and a field read.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member _.CachedRead() = memo.Peek

    /// <summary>
    /// The same read, tracked, so it also links an edge when something is
    /// listening. Nothing is, here: this is the tracking check.
    /// </summary>
    [<Benchmark>]
    member _.CachedTrackedRead() =
        memo.Value

    /// <summary>
    /// Invalidate, then read. Pays for one body run plus the edge re-collection
    /// that comes with it.
    /// </summary>
    [<Benchmark; BenchmarkCategory "Sentinel">]
    member _.Recompute() =
        counter <- counter + 1
        source.Value <- counter
        memo.Value

/// <summary>
/// Propagation through a chain, which is where an implementation either scales
/// or does not. Depth is the number of memos between the signal and the read.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Memo">]
type ChainBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)
    let mutable tail: Memo<int> = Unchecked.defaultof<Memo<int>>
    let mutable counter = 0

    [<Params(1, 4, 16, 64)>]
    member val Depth = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let mutable previous = Memo (graph, (fun () -> source.Value + 1))

        for _ in 2 .. this.Depth do
            let inner = previous
            previous <- Memo (graph, (fun () -> inner.Value + 1))

        tail <- previous
        tail.TryValue |> ignore

    /// <summary>
    /// One write at the head, one read at the tail. Every memo in between is
    /// invalidated and recomputed exactly once.
    /// </summary>
    [<Benchmark; BenchmarkCategory "Sentinel">]
    member _.WriteThenReadTail() =
        counter <- counter + 1
        source.Value <- counter
        tail.Value

    /// <summary>
    /// The same chain, read without an intervening write: every memo is clean,
    /// so only the tail is touched. The gap between the two is the propagation.
    /// </summary>
    [<Benchmark>]
    member _.ReadTailClean() =
        tail.Value

/// <summary>
/// The diamond: two paths from one source reconverging on one reader. The
/// shape that catches an implementation recomputing a node twice per write, or
/// pairing a value from before the write with one from after it.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Memo">]
type DiamondBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)
    let left = Memo (graph, (fun () -> source.Value + 1))
    let right = Memo (graph, (fun () -> source.Value * 2))
    let join = Memo (graph, (fun () -> left.Value + right.Value))
    let mutable counter = 0

    [<GlobalSetup>]
    member _.Setup() =
        join.TryValue |> ignore

    [<Benchmark>]
    member _.WriteThenRead() =
        counter <- counter + 1
        source.Value <- counter
        join.Value
