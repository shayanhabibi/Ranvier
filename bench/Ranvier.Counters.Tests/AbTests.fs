module CounterBench.Tests.AbTests

open System
open System.Collections.Generic
open Expecto
open CounterBench

let private m ops region bytes instructions =
    {
        Ops = ops
        Region = region
        Bytes = bytes
        Gen0Collections = 0
        NoGcRegion = true
        Instructions = instructions
        Cycles = -1L
        BranchMisses = -1L
        CpuNs = -1L
        Counters =
            [|
                {
                    CounterValue.Name = "EffectRuns"
                    Value = int64 ops
                }
            |]
    }

let private worker =
    {
        ProcessId = 1
        ThreadId = 1
        CountersCompiled = false
        Cases =
            [|
                {
                    Scenario = "update"
                    Engine = "Ranvier"
                    Unit = "write"
                    Measurements = [| m 50 0 1000L 10000L; m 100 1 2000L 15000L |]
                }
            |]
    }

let private figure run =
    Ab.dotnetFigures "abc" 0 run

let private metric (f: ProcessFigures) name =
    f.Scenarios[0].Metrics
    |> Array.tryFind (fun x -> x.Name = name)
    |> Option.map _.Value

let private changed measurements =
    { worker with
        Cases =
            [|
                { worker.Cases[0] with
                    Measurements = measurements
                }
            |]
    }

[<Tests>]
let tests =
    testList
        "Ab"
        [
            test "differentials normalize N and omit unavailable metrics" {
                let f = figure worker
                Expect.equal (metric f "instructions") (Some 100.0) "instruction differential"
                Expect.equal (metric f "bytes") (Some 20.0) "allocation differential"
                Expect.equal (metric f "cycles") None "sentinel omitted"

                Expect.equal
                    (metric (Ab.dotnetCounterFigures "abc" { worker with CountersCompiled = true }) "lib:EffectRuns")
                    (Some 1.0)
                    "library differential"

                Expect.throws
                    (fun () ->
                        figure { worker with CountersCompiled = true }
                        |> ignore)
                    "timing rejects instrumentation"
            }
            test "GC in either region flags the scenario" {
                let ms = Array.copy worker.Cases[0].Measurements

                ms[0] <-
                    { ms[0] with
                        Gen0Collections = 1
                        NoGcRegion = false
                    }

                Expect.isTrue (figure (changed ms)).Scenarios[0].Gc "first region collected"
            }
            test "malformed pairs and duplicate scenario identities fail closed" {
                for ms in
                    [
                        [||]
                        [| m 0 0 0L 1L; m 0 1 0L 2L |]
                        [| m 50 0 0L 1L; m 99 1 0L 2L |]
                        [| m 50 0 0L 1L; m 100 0 0L 2L |]
                        [| m 50 0 0L -2L; m 100 1 0L 2L |]
                    ] do
                    Expect.throws (fun () -> figure (changed ms) |> ignore) "invalid pair"

                Expect.throws
                    (fun () ->
                        figure
                            { worker with
                                Cases = Array.append worker.Cases worker.Cases
                            }
                        |> ignore)
                    "duplicate case"
            }
            test "library instrumentation and counter key mismatches are rejected" {
                Expect.throws
                    (fun () ->
                        Ab.dotnetCounterFigures "a" { worker with CountersCompiled = false }
                        |> ignore)
                    "uninstrumented"

                let ms = Array.copy worker.Cases[0].Measurements
                ms[0] <- { ms[0] with Counters = [||] }
                Expect.throws (fun () -> Ab.dotnetCounterFigures "a" (changed ms) |> ignore) "missing small counter"
            }
            test "Node uses exact recorded regions and rejects absent regions or nonfinite input" {
                let nm ops region bytes : NodeRuns.NodeMeasurement =
                    {
                        Ops = ops
                        Region = region
                        Bytes = bytes
                        HeapUsedBytes = 0.
                        CodeBytes = 0.
                        Gen0Collections = 0
                        NoGcRegion = true
                        Counters = [||]
                    }

                let run: NodeRuns.NodeRun =
                    {
                        ProcessId = 2
                        CountersCompiled = false
                        Cases =
                            [|
                                {
                                    Scenario = "update"
                                    Engine = "Ranvier"
                                    Unit = "write"
                                    Measurements = [| nm 50 0 100.; nm 100 1 300. |]
                                }
                            |]
                    }

                let regions = Dictionary<int, LinuxPmc.Counts>()

                regions[0] <-
                    { LinuxPmc.Counts.Unmeasured with
                        Instructions = 1000L
                        CpuNs = 500L
                    }

                regions[1] <-
                    { LinuxPmc.Counts.Unmeasured with
                        Instructions = 3000L
                        CpuNs = 1500L
                    }

                let f = Ab.nodeFigures "abc" 0 run regions

                Expect.throws
                    (fun () ->
                        Ab.nodeFigures "abc" 0 { run with CountersCompiled = true } regions
                        |> ignore)
                    "Node timing rejects instrumentation"

                Expect.equal (metric f "instructions") (Some 40.) "hardware differential"
                Expect.equal (metric f "cpu_ns") (Some 20.) "CPU differential"
                Expect.equal (metric f "bytes") (Some 4.) "bytes differential"

                let shrinking =
                    { run with
                        Cases =
                            [|
                                { run.Cases[0] with
                                    Measurements = [| nm 50 0 -100.; nm 100 1 -300. |]
                                }
                            |]
                    }

                Expect.equal (metric (Ab.nodeFigures "abc" 0 shrinking regions) "bytes") (Some -4.) "signed heap growth remains a measured value"
                regions.Remove 1 |> ignore
                Expect.throws (fun () -> Ab.nodeFigures "abc" 0 run regions |> ignore) "missing region"

                let bad =
                    { run with
                        Cases =
                            [|
                                { run.Cases[0] with
                                    Measurements = [| nm 50 0 Double.NaN; nm 100 1 300. |]
                                }
                            |]
                    }

                Expect.throws (fun () -> Ab.nodeFigures "abc" 0 bad regions |> ignore) "NaN"

                let withCode small large =
                    { run with
                        Cases =
                            [|
                                { run.Cases[0] with
                                    Measurements =
                                        [|
                                            { nm 50 0 100. with CodeBytes = small }
                                            { nm 100 1 300. with CodeBytes = large }
                                        |]
                                }
                            |]
                    }

                regions[1] <-
                    { LinuxPmc.Counts.Unmeasured with
                        Instructions = 3000L
                        CpuNs = 1500L
                    }

                let compiled small large =
                    (Ab.nodeFigures "abc" 0 (withCode small large) regions).Scenarios[0].Compiled

                Expect.isFalse (compiled 0. 0.) "unchanged code spaces"
                Expect.isTrue (compiled 1. 0.) "compiled at N"
                Expect.isTrue (compiled 0. 1.) "compiled at 2N"
                Expect.isFalse (compiled -4096. 0.) "code freed by a collection"
                Expect.isFalse (figure worker).Scenarios[0].Compiled ".NET"

                Expect.throws
                    (fun () ->
                        Ab.nodeFigures "abc" 0 (withCode Double.NaN 0.) regions
                        |> ignore)
                    "NaN code growth"
            }
            test "Node results that predate the code-space field read as compilation-free" {
                let json =
                    """{"ProcessId":2,"CountersCompiled":false,"Cases":[{"Scenario":"s","Engine":"e","Unit":"u","Measurements":[{"Ops":1,"Region":0,"Bytes":0,"HeapUsedBytes":0,"Gen0Collections":0,"NoGcRegion":true,"Counters":[]}]}]}"""

                let run = Text.Json.JsonSerializer.Deserialize<NodeRuns.NodeRun> json
                Expect.isFalse (NodeRuns.compiled run.Cases[0].Measurements[0]) "missing CodeBytes"

                let figures =
                    Text.Json.JsonSerializer.Deserialize<ScenarioFigures> """{"Scenario":"s","Engine":"e","Gc":false,"Metrics":[]}"""

                Expect.isFalse figures.Compiled "missing Compiled"
            }
            test "invalid CLI arguments never create an artifact" {
                for args in
                    [
                        []
                        [ "--rounds"; "0" ]
                        [ "--scale"; "-1" ]
                        [ "--unknown" ]
                        [ "--leg"; "other" ]
                    ] do
                    Expect.throws (fun () -> Ab.main args |> ignore) "invalid invocation"
            }
        ]
