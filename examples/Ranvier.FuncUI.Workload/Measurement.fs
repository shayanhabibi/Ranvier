namespace Workload

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Avalonia.Threading

type Trial =
    { Variant: string
      Workload: string
      Tasks: int
      Repetition: int
      Messages: int
      UiThreadId: int
      ElapsedMilliseconds: float
      AllocatedBytes: int64
      Gen0: int
      Gen1: int
      Gen2: int
      MessageMicroseconds: float array }

module Measurement =
    let run output =
        let trials = ResizeArray<Trial>()
        let counts = ResizeArray<obj>()
        let updates = ResizeArray<obj>()
        let frequency = float Stopwatch.Frequency
        for size in [100; 1000] do
            for workload in Workloads.names do
                let trace = Workloads.trace workload size
                let mutable warmModel = Model.init size
                for _ in 0 .. 1 do
                    for msg in trace do warmModel <- Model.update msg warmModel
                GC.KeepAlive warmModel
                for repetition in 0 .. 7 do
                    let mutable model = Model.init size
                    let allocated = GC.GetAllocatedBytesForCurrentThread()
                    let start = Stopwatch.GetTimestamp()
                    for msg in trace do model <- Model.update msg model
                    let elapsed = float (Stopwatch.GetTimestamp() - start) / frequency * 1000.
                    updates.Add(box {| Workload = workload; Tasks = size; Repetition = repetition; Messages = trace.Length
                                       ElapsedMilliseconds = elapsed; AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated |})
                    GC.KeepAlive model
                let replay variant repetition measured =
                    let runner = Runners.create variant (Model.init size) false
                    let window = Verification.attach runner
                    try
                        let samples = Array.zeroCreate<float> trace.Length
                        GC.Collect()
                        GC.WaitForPendingFinalizers()
                        GC.Collect()
                        let g0, g1, g2 = GC.CollectionCount 0, GC.CollectionCount 1, GC.CollectionCount 2
                        let allocated = GC.GetAllocatedBytesForCurrentThread()
                        let uiThread = Environment.CurrentManagedThreadId
                        let start = Stopwatch.GetTimestamp()
                        for i in 0 .. trace.Length - 1 do
                            let before = Stopwatch.GetTimestamp()
                            runner.Dispatch trace[i]
                            Dispatcher.UIThread.RunJobs()
                            if Environment.CurrentManagedThreadId <> uiThread then failwith "Measurement moved off its original UI thread"
                            samples[i] <- float (Stopwatch.GetTimestamp() - before) / frequency * 1e6
                        let elapsed = float (Stopwatch.GetTimestamp() - start) / frequency * 1000.
                        let bytes = GC.GetAllocatedBytesForCurrentThread() - allocated
                        if measured then
                            trials.Add
                                { Variant = variant; Workload = workload; Tasks = size; Repetition = repetition
                                  Messages = trace.Length; ElapsedMilliseconds = elapsed; AllocatedBytes = bytes
                                  UiThreadId = uiThread
                                  Gen0 = GC.CollectionCount 0 - g0; Gen1 = GC.CollectionCount 1 - g1; Gen2 = GC.CollectionCount 2 - g2
                                  MessageMicroseconds = samples }
                        let expected = Array.fold (fun model msg -> Model.update msg model) (Model.init size) trace
                        Verification.checkRendered runner expected
                    finally
                        runner.Dispose()
                        window.Close()
                for warmup in 0 .. 1 do
                    for variant in Runners.names do replay variant warmup false
                for repetition in 0 .. 7 do
                    for offset in 0 .. 2 do
                        replay Runners.names[(repetition + offset) % 3] repetition true
                for variant in Runners.names do
                    let runner = Runners.create variant (Model.init size) true
                    let window = Verification.attach runner
                    let before = runner.Counters
                    let root, sections, rows, hosts, selectors = before.RootViews, before.SectionViews, before.RowViews, before.HostUpdates, before.SelectorEvaluations
                    try
                        for msg in trace do runner.Dispatch msg; Dispatcher.UIThread.RunJobs()
                        counts.Add(box {| Variant = variant; Workload = workload; Tasks = size
                                          RootViews = before.RootViews - root; SectionViews = before.SectionViews - sections
                                          RowViews = before.RowViews - rows; HostUpdates = before.HostUpdates - hosts
                                          SelectorEvaluations = before.SelectorEvaluations - selectors |})
                    finally
                        runner.Dispose()
                        window.Close()
                printfn "MEASURED: %s, %d tasks" workload size
        let options = JsonSerializerOptions(WriteIndented = true)
        let result =
            {| TimestampUtc = DateTime.UtcNow; Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
               OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription; ProcessorCount = Environment.ProcessorCount
               StopwatchFrequency = Stopwatch.Frequency; Trials = trials.ToArray(); Counters = counts.ToArray(); PureUpdates = updates.ToArray()
               Notes = "Release, RanvierTrace=false, workstation GC; headless Avalonia controls, UI-thread dispatch plus RunJobs; no GPU/frame measurements. Allocations are current UI thread only. Two warmups and eight rotated repetitions per scenario; construction excluded; counters recorded separately." |}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath output)) |> ignore
        File.WriteAllText(output, JsonSerializer.Serialize(result, options))
        printfn "Saved %d trials to %s" trials.Count output
