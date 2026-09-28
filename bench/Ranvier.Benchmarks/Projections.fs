module Ranvier.Benchmarks.Projections

open System
open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// A projection of <c>Items</c> rows, each observed by one effect, which keeps the
/// projection scheduled.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Projection">]
type ProjectionBenchmarks() =
    let graph = new Graph ()
    let mutable source = Unchecked.defaultof<Signal<(int * int) list>>
    let mutable projection = Unchecked.defaultof<Projection<int, int>>
    let mutable sink = 0
    let mutable counter = 0

    [<Params(8, 64, 512)>]
    member val Items = 8 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        use _ = graph.Activate ()
        source <- Signal (graph, [ for i in 1 .. this.Items -> i, i ])
        projection <- createProjection fst snd (fun () -> source.Value)

        for i in 1 .. this.Items do
            new Effect (graph, (fun () -> sink <- projection.Get i))
            |> ignore

    /// <summary>
    /// A tracked read of a cached row.
    /// </summary>
    [<Benchmark>]
    member _.ReadRow() =
        projection.Get 1

    /// <summary>
    /// A write changing the value of the first row, which re-runs that row's
    /// effect.
    /// </summary>
    [<Benchmark>]
    member _.EditOneItem() =
        counter <- counter + 1
        source.Value <- (1, counter) :: List.tail source.Peek

    /// <summary>
    /// A write of the same keys and values in reverse order, which changes the
    /// key order and every row keeps its value.
    /// </summary>
    [<Benchmark>]
    member _.Reorder() =
        source.Value <- List.rev source.Peek

    [<GlobalCleanup>]
    member _.Cleanup() =
        (graph :> IDisposable).Dispose()

/// <summary>
/// A selector whose key 7 loses its last observer and is read again before the
/// next transition. Each case reuses the cell; a regression rebuilds it.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Projection">]
type LookupBenchmarks() =
    let graph = new Graph ()
    let selected = Signal (graph, 1)
    let flag = Signal (graph, false)
    let other = Signal (graph, 0)
    let mutable selector = Unchecked.defaultof<Lookup<int, bool>>
    let mutable sink = false

    [<GlobalSetup>]
    member _.Setup() =
        use _ = graph.Activate ()
        selector <- createSelector (fun () -> selected.Value)

        new Effect (
            graph,
            fun () ->
                if flag.Value then
                    other.Value |> ignore

                sink <- selector.Get 8
        )
        |> ignore

    /// <summary>
    /// Mounts and disposes the only reader of the key.
    /// </summary>
    [<Benchmark>]
    member _.RemountKey() =
        use _ = graph.Activate ()

        let owner =
            createRoot (fun owner ->
                createEffect (fun () -> sink <- selector.Get 7)
                owner)

        owner.Dispose ()

    /// <summary>
    /// Re-runs a reader that inserts or drops a read ahead of its <c>Get</c>.
    /// </summary>
    [<Benchmark>]
    member _.ReadAheadOfGet() =
        flag.Value <- not flag.Peek

    /// <summary>
    /// An untracked read of an unobserved key.
    /// </summary>
    [<Benchmark>]
    member _.UntrackedGet() =
        selector.Get 7
