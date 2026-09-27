module Ranvier.Benchmarks.Signals

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// The write path is the hottest path in the library, and the one every
/// optimisation so far has been aimed at: a typed comparer so the cutoff test
/// does not box, and a reused buffer so notification does not allocate. Both
/// claims are measurable here.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Signal">]
type SignalBenchmarks() =
    let graph = new Graph ()
    let signal = Signal (graph, 0)
    let mutable counter = 0

    /// <summary>
    /// How many memos observe the signal being written.
    /// </summary>
    [<Params(0, 1, 8, 64)>]
    member val Observers = 0 with get, set

    member val private Observing: Memo<int>[] = Array.empty with get, set

    [<GlobalSetup>]
    member this.Setup() =
        this.Observing <- Array.init this.Observers (fun _ -> Memo (graph, (fun () -> signal.Value * 2)))

        for memo in this.Observing do
            memo.TryValue |> ignore

    /// <summary>
    /// A tracked read with no computation running, which is the shape a read
    /// from outside the graph takes.
    /// </summary>
    [<Benchmark>]
    member _.Read() =
        signal.Value

    /// <summary>
    /// An untracked read, for the cost of the tracking check itself.
    /// </summary>
    [<Benchmark>]
    member _.Peek() =
        signal.Peek

    /// <summary>
    /// A write that changes the value, so the cutoff does not stop it and
    /// every observer is notified. With Observers = 0 this is the floor.
    /// </summary>
    [<Benchmark>]
    member _.Write() =
        counter <- counter + 1
        signal.Value <- counter

    /// <summary>
    /// A write that does not change the value. Everything downstream is
    /// skipped, so this is the cutoff test on its own — the one that used to
    /// box its operands.
    /// </summary>
    [<Benchmark>]
    member _.WriteCutoff() =
        signal.Value <- signal.Peek

    /// <summary>
    /// Write, then read every observer back. Unlike Write, this pays for the
    /// recomputations the write invalidated, so the difference between the two
    /// is what propagation actually costs.
    /// </summary>
    [<Benchmark>]
    member this.WriteAndPropagate() =
        counter <- counter + 1
        signal.Value <- counter
        let mutable sum = 0

        for memo in this.Observing do
            sum <- sum + memo.Peek

        sum

/// <summary>
/// A reference-typed signal, where the cutoff is identity rather than a value
/// comparison, and a structural one, where it is a deep comparison. The gap
/// between them is the cost of choosing StructuralPolicy.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Signal">]
type EqualityBenchmarks() =
    let identity = new Graph ()

    let structural =
        new Graph (
            { GraphOptions.Default with
                Equality = StructuralPolicy ()
            }
        )

    let boxed = Signal (identity, "a")
    let record = Signal (structural, {| Id = 1; Name = "a" |})
    let mutable counter = 0

    [<Benchmark(Baseline = true)>]
    member _.IdentityCutoff() =
        boxed.Value <- boxed.Peek

    [<Benchmark>]
    member _.StructuralCutoff() =
        record.Value <- record.Peek

    [<Benchmark>]
    member _.StructuralWrite() =
        counter <- counter + 1
        record.Value <- {| Id = counter; Name = "a" |}
