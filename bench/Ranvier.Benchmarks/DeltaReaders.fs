module Ranvier.Benchmarks.DeltaReaders

open System
open System.Collections.Generic
open BenchmarkDotNet.Attributes
open Ranvier
open Ranvier.Benchmarks.Projections

/// <summary>
/// A projection of <c>Items</c> keys whose source toggles key <c>Items / 2</c> out and back in on every write, with one key
/// reader. Each case compares finding that one change by the reader against a set difference of two <c>Keys</c> arrays.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Projection">]
type DeltaReaderBenchmarks() =
    let graph = new Graph ()
    let mutable source = Unchecked.defaultof<Signal<int[]>>
    let mutable projection = Unchecked.defaultof<Projection<int, int>>
    let mutable reader = Unchecked.defaultof<ProjectionReader<int>>
    let mutable full = Array.empty<int>
    let mutable without = Array.empty<int>
    let mutable previous = Array.empty<int>
    let mutable sink = 0

    [<Params(64, 512, 10_000)>]
    member val Items = 64 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        use _ = graph.Activate ()
        full <- [| 1 .. this.Items |]

        without <-
            full
            |> Array.filter (fun k -> k <> this.Items / 2)

        source <- Signal (graph, full)
        projection <- createProjection id id (fun () -> source.Value)
        reader <- projection.NewKeyReader ()
        previous <- projection.Keys
        reader.Read () |> ignore

    /// <summary>Removes or restores key <c>Items / 2</c>.</summary>
    member private _.Toggle() =
        source.Value <-
            if obj.ReferenceEquals (source.Peek, full) then
                without
            else
                full

    /// <summary>
    /// The toggle, then the change found by diffing the previous and current <c>Keys</c> as sets: O(N) per read.
    /// </summary>
    [<Benchmark(Baseline = true)>]
    member this.SetDiffAfterOneRemoval() =
        this.Toggle ()
        let current = projection.Keys
        let before = HashSet<int>(previous)
        let after = HashSet<int>(current)
        let mutable changed = 0

        for k in current do
            if not (before.Contains k) then
                changed <- changed + 1

        for k in previous do
            if not (after.Contains k) then
                changed <- changed + 1

        previous <- current
        changed

    /// <summary>The toggle, then the change read from the key reader.</summary>
    [<Benchmark>]
    member this.ReadAfterOneRemoval() =
        this.Toggle ()
        reader.Read().Changes.Count

    /// <summary>A read of the key reader with nothing changed since the previous read.</summary>
    [<Benchmark>]
    member _.ReadIdle() =
        reader.Read().Changes.Count

    [<GlobalCleanup>]
    member _.Cleanup() =
        (graph :> IDisposable).Dispose()

/// <summary>
/// <c>ProjectionBenchmarks.ChurnOneKey</c> with <c>Readers</c> key readers, each read by its own effect after every write.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Projection">]
type DeltaReaderChurnBenchmarks() =
    let graph = new Graph ()
    let mutable churn = Unchecked.defaultof<Churn>
    let mutable sink = 0

    [<Params(8, 64, 512)>]
    member val Items = 8 with get, set

    [<Params(1, 4)>]
    member val Readers = 1 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        use _ = graph.Activate ()
        churn <- Churn (graph, this.Items)

        for _ in 1 .. this.Readers do
            let reader = churn.Projection.NewKeyReader ()

            new Effect (graph, (fun () -> sink <- reader.Read().Changes.Count))
            |> ignore

    /// <summary>A write removing the source's last key and adding a new one, then one read per reader.</summary>
    [<Benchmark>]
    member _.ChurnOneKey() =
        churn.Write ()

    [<GlobalCleanup>]
    member _.Cleanup() =
        (graph :> IDisposable).Dispose()
