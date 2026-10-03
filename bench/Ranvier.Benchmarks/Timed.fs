module Ranvier.Benchmarks.Timed

open System
open BenchmarkDotNet.Attributes
open Ranvier

type private CountingClock() =
    inherit TimedClock()
    let timers = ResizeArray<Action * float ref>()
    let mutable now = 0.
    let mutable arms = 0
    member _.Arms = arms
    override _.NowMilliseconds = now

    override _.CreateTimer(callback) =
        let due = ref infinity
        timers.Add (callback, due)

        { new TimedTimer() with
            member _.Arm delay =
                arms <- arms + 1
                due.Value <- now + delay.TotalMilliseconds

            member _.Disarm() =
                due.Value <- infinity

            member _.Dispose() =
                due.Value <- infinity
        }

    member _.Advance(milliseconds) =
        now <- now + milliseconds

        for i in 0 .. timers.Count - 1 do
            let callback, due = timers[i]

            if due.Value <= now then
                due.Value <- infinity
                callback.Invoke ()

[<MemoryDiagnoser; BenchmarkCategory "Timed">]
type TimedBenchmarks() =
    let mutable graph = Unchecked.defaultof<Graph>
    let mutable clock = Unchecked.defaultof<CountingClock>
    let mutable input = Unchecked.defaultof<Signal<int>>
    let mutable outputs: Timed<int>[] = Array.empty
    let mutable next = 0

    [<Params(1, 64, 4096)>]
    member val Nodes = 1 with get, set

    [<Params("debounce", "first", "last", "both")>]
    member val Mode = "debounce" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        graph <- new Graph ()
        clock <- CountingClock ()
        input <- Signal (graph, 0)

        let factory =
            match this.Mode with
            | "first" -> throttleFirstWith
            | "last" -> throttleLastWith
            | "both" -> throttleWith
            | _ -> debounceWith

        outputs <-
            Array.init this.Nodes (fun _ -> factory { Clock = clock; Comparer = None } (TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph)

        input.Value <- 1
        next <- 1
        clock.Advance 100.

    [<Benchmark>]
    member _.ChangedCapture() =
        next <- next + 1
        input.Value <- next

    [<Benchmark>]
    member _.EqualWrite() =
        input.Value <- input.Peek

    [<Benchmark>]
    member this.Construct() =
        use fresh = new Graph ()
        let source = Signal (fresh, 0)

        let options =
            {
                Clock = CountingClock ()
                Comparer = None
            }

        for _ in 1 .. this.Nodes do
            debounceWith options (TimeSpan.FromMilliseconds 100.) (fun () -> source.Value) fresh
            |> ignore

    [<Benchmark>]
    member _.CaptureAndAdmission() =
        next <- next + 1
        input.Value <- next
        clock.Advance 100.
        outputs[0].Value

    [<GlobalCleanup>]
    member _.Cleanup() =
        graph.Dispose ()

[<MemoryDiagnoser; BenchmarkCategory "TimedPolicy">]
type DeadlinePolicyBenchmarks() =
    [<Params(1, 64, 4096)>]
    member val Inputs = 1 with get, set

    member private this.Burst(rearm: bool) =
        let clock = CountingClock ()
        let mutable deadline = 0.
        let mutable candidate = 0
        let mutable published = 0
        let mutable armed = false
        let mutable timer: TimedTimer = null

        let wake () =
            armed <- false

            if clock.NowMilliseconds >= deadline then
                published <- candidate
            else
                armed <- true
                timer.Arm (TimeSpan.FromMilliseconds (deadline - clock.NowMilliseconds))

        timer <- clock.CreateTimer (Action wake)

        for i in 1 .. this.Inputs do
            candidate <- i
            deadline <- clock.NowMilliseconds + 100.

            if rearm || not armed then
                armed <- true
                timer.Arm (TimeSpan.FromMilliseconds 100.)

            clock.Advance 1.

        clock.Advance 100.

        if published <> candidate then
            failwith "policy changed admission semantics"

        timer.Dispose ()
        clock.Arms

    [<Benchmark(Baseline = true)>]
    member this.RearmEveryInput() =
        this.Burst true

    [<Benchmark>]
    member this.LazyExtension() =
        this.Burst false
