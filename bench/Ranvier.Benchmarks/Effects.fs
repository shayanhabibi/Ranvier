module Ranvier.Benchmarks.Effects

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// Effects are the push side: nothing reads them, so the scheduler runs them.
/// A write therefore costs a queue push and a flush, on top of the notify.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Effect">]
type EffectBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 0)
    let mutable sink = 0
    let mutable counter = 0

    [<Params(1, 8, 64)>]
    member val Effects = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        for _ in 1 .. this.Effects do
            new Effect (graph, (fun () -> sink <- sink + source.Value))
            |> ignore

    /// <summary>
    /// Write, and let the flush run every effect that the write invalidated.
    /// </summary>
    [<Benchmark>]
    member _.WriteAndFlush() =
        counter <- counter + 1
        source.Value <- counter

    /// <summary>
    /// The same writes inside a batch: one flush for all of them, which is the
    /// whole reason batching exists.
    /// </summary>
    [<Benchmark>]
    member _.BatchOfTenWrites() =
        graph.Batch (fun () ->
            for _ in 1..10 do
                counter <- counter + 1
                source.Value <- counter)
