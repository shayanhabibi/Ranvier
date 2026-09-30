module Ranvier.Benchmarks.Models

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// One field of an N-field model changed per write, with one effect per field. Compares a signal per field (the
/// record-of-signals pattern), a root signal read through N selector memos, the same through <c>Mvu</c>, and the
/// model copy alone. The model is an <c>int[]</c>, so a copy costs what copying an N-field record costs.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Model">]
type FieldWriteBenchmarks() =
    let fieldGraph = new Graph ()
    let selectorGraph = new Graph ()
    let mvuGraph = new Graph ()
    let mutable sink = 0
    let mutable counter = 0

    [<Params(8, 64, 256)>]
    member val Fields = 8 with get, set

    member val private FieldSignals: Signal<int>[] = Array.empty with get, set
    member val private Root: Signal<int[]> = Unchecked.defaultof<_> with get, set
    member val private Bridge: Mvu<int[], int> = Unchecked.defaultof<_> with get, set
    member val private Model: int[] = Array.empty with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let n = this.Fields
        this.FieldSignals <- Array.init n (fun _ -> Signal (fieldGraph, 0))

        for s in this.FieldSignals do
            new Effect (fieldGraph, (fun () -> sink <- sink + s.Value))
            |> ignore

        let root = Signal (selectorGraph, Array.zeroCreate<int> n)
        this.Root <- root

        for i in 0 .. n - 1 do
            let selector = Memo (selectorGraph, (fun _ -> root.Value[i]))

            new Effect (selectorGraph, (fun () -> sink <- sink + selector.Value))
            |> ignore

        let bridge =
            Mvu<int[], int>(
                mvuGraph,
                Array.zeroCreate<int> n,
                (fun field model ->
                    let copy = Array.copy model
                    copy[field] <- copy[field] + 1
                    copy),
                Unchecked.defaultof<_>,
                false
            )

        this.Bridge <- bridge

        for i in 0 .. n - 1 do
            let selector = bridge.Select (fun model -> model[i])

            new Effect (mvuGraph, (fun () -> sink <- sink + selector.Value))
            |> ignore

        this.Model <- Array.zeroCreate<int> n

    /// <summary>A write to one field's signal, waking its one effect. Flat in N.</summary>
    [<Benchmark(Baseline = true)>]
    member this.FieldSignalWrite() =
        counter <- counter + 1
        this.FieldSignals[counter % this.Fields].Value <- counter

    /// <summary>A copy of the root with one field changed, re-running all N selectors and waking one effect.</summary>
    [<Benchmark>]
    member this.SelectorMemoWrite() =
        counter <- counter + 1
        let copy = Array.copy this.Root.Peek
        copy[counter % this.Fields] <- counter
        this.Root.Value <- copy

    /// <summary><c>SelectorMemoWrite</c> through <c>Mvu.Dispatch</c> and <c>Mvu.Select</c>.</summary>
    [<Benchmark>]
    member this.MvuDispatch() =
        counter <- counter + 1
        this.Bridge.Dispatch (counter % this.Fields)

    /// <summary>The model copy alone, for the part of <c>SelectorMemoWrite</c> spent outside the graph.</summary>
    [<Benchmark>]
    member this.ModelCopyOnly() =
        counter <- counter + 1
        let copy = Array.copy this.Model
        copy[counter % this.Fields] <- counter
        this.Model <- copy
        copy

/// <summary>
/// The writable derived value (<c>createEditable</c>) against a plain signal: a local edit, and an upstream change, each
/// read by one effect.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Editable">]
type EditableBenchmarks() =
    let plainGraph = new Graph ()
    let editGraph = new Graph ()
    let upstreamGraph = new Graph ()
    let plain = Signal (plainGraph, 0)
    let editSource = Signal (editGraph, 0)
    let edited = Editable<int>(editGraph, (fun _ -> editSource.Value), false)
    let upstreamSource = Signal (upstreamGraph, 0)
    let upstream = Editable<int>(upstreamGraph, (fun _ -> upstreamSource.Value), false)
    let mutable sink = 0
    let mutable counter = 0

    [<GlobalSetup>]
    member _.Setup() =
        new Effect (plainGraph, (fun () -> sink <- sink + plain.Value))
        |> ignore

        new Effect (editGraph, (fun () -> sink <- sink + edited.Value))
        |> ignore

        new Effect (upstreamGraph, (fun () -> sink <- sink + upstream.Value))
        |> ignore

    /// <summary>One signal write, waking one effect.</summary>
    [<Benchmark(Baseline = true)>]
    member _.PlainSignalWrite() =
        counter <- counter + 1
        plain.Value <- counter

    /// <summary>A local edit, waking the effect that reads the editable.</summary>
    [<Benchmark>]
    member _.LocalEdit() =
        counter <- counter + 1
        edited.Value <- counter

    /// <summary>An upstream write propagating through source, seed, editable and effect.</summary>
    [<Benchmark>]
    member _.UpstreamChange() =
        counter <- counter + 1
        upstreamSource.Value <- counter
