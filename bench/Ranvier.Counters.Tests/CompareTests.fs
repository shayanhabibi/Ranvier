module CounterBench.Tests.CompareTests

open System
open System.IO
open System.Text.Json
open Expecto
open CounterBench
open CounterBench.Compare

let private five x =
    Array.replicate 5 x

let private none = acceptance false [] Set.empty

// Synthetic figures exercise verdict rules independently of measurement noise.
let private run b h =
    let proc side runtime i (value: float) : ProcessFigures =
        {
            Side = side
            Commit = "same"
            Runtime = runtime
            Process = i
            Scenarios =
                [|
                    {
                        Scenario = "x"
                        Engine = "Ranvier"
                        Gc = (i = 0)
                        Compiled = false
                        Metrics =
                            [|
                                { Name = "instructions"; Value = value }
                                { Name = "cpu_ns"; Value = value }
                                { Name = "bytes"; Value = 0.0 }
                            |]
                    }
                |]
        }

    {
        Leg = "arm64-counters"
        Machine = "synthetic"
        Base = "same"
        Head = "same"
        Rounds = 5
        Versions = [||]
        NoBaseline = [||]
        Processes =
            [|
                for runtime in [ "node"; "dotnet" ] do
                    for i in 0..4 do
                        yield proc "base" runtime i b
                        yield proc "head" runtime i h
            |]
        Counters =
            [|
                for runtime in [ "node"; "dotnet" ] do
                    for side in [ "base"; "head" ] do
                        let p = proc side runtime 0 0.

                        yield
                            { p with
                                Scenarios =
                                    p.Scenarios
                                    |> Array.map (fun s ->
                                        { s with
                                            Metrics = [| { Name = "lib:runs"; Value = 1. } |]
                                        })
                            }
            |]
    }

let private verdictOf r =
    verdict "instructions" 0.02 20.0 true none r

let private mapScenarios f (ps: ProcessFigures[]) =
    ps
    |> Array.map (fun p ->
        { p with
            Scenarios = p.Scenarios |> Array.map (f p)
        })

/// <summary>
/// <c>run</c> with per-process gate values for scenario <c>x</c> and, when given, a <c>control</c> scenario in every
/// process of both runtimes.
/// </summary>
let private sampled (b: float[]) (h: float[]) (control: (float[] * float[]) option) =
    let r = run 0. 0.

    let pick (p: ProcessFigures) (bs: float[], hs: float[]) =
        if p.Side = "base" then bs[p.Process] else hs[p.Process]

    let valued samples (p: ProcessFigures) (s: ScenarioFigures) =
        { s with
            Metrics =
                s.Metrics
                |> Array.map (fun m ->
                    if m.Name = "bytes" then
                        m
                    else
                        { m with Value = pick p samples })
        }

    let withControl (p: ProcessFigures) =
        match control with
        | None -> p
        | Some samples ->
            { p with
                Scenarios =
                    Array.append
                        p.Scenarios
                        [|
                            valued
                                samples
                                p
                                { p.Scenarios[0] with
                                    Scenario = Control.Scenario
                                    Engine = "Control"
                                }
                        |]
            }

    { r with
        Processes =
            r.Processes
            |> Array.map (fun p ->
                { p with
                    Scenarios = p.Scenarios |> Array.map (valued (b, h) p)
                }
                |> withControl)
        Counters =
            r.Counters
            |> Array.map (fun p ->
                match control with
                | None -> p
                | Some _ ->
                    { p with
                        Scenarios =
                            Array.append
                                p.Scenarios
                                [|
                                    { p.Scenarios[0] with
                                        Scenario = Control.Scenario
                                        Engine = "Control"
                                    }
                                |]
                    })
    }

let private gateKind runtime scenario (v: Verdict) =
    let f =
        v.Findings
        |> Array.find (fun f ->
            f.Runtime = runtime
            && f.Scenario = scenario
            && f.Metric = v.Gate)

    f.Kind

[<Tests>]
let tests =
    testList
        "Compare"
        [
            testCase "strict percentage and absolute boundaries"
            <| fun _ ->
                for b, h, kind in
                    [
                        10000., 10200., Unchanged
                        10000., 10201., Regressed
                        300., 320., Unchanged
                        300., 321., Regressed
                        300., 280., Unchanged
                        300., 279., Improved
                        10000., 9800., Unchanged
                        10000., 9799., Improved
                    ] do
                    Expect.equal (classify 0.02 20. (five b) (five h)) kind "strict boundary"
            testCase "separation excludes overlap and ties in either direction"
            <| fun _ ->
                Expect.equal (classify 0.02 20. (five 1000.) [| 1000.; 1100.; 1100.; 1100.; 1100. |]) Unchanged "tie"
                Expect.equal (classify 0.02 20. (five 1000.) [| 1001.; 900.; 900.; 900.; 900. |]) Unchanged "overlap"
                Expect.equal (classify 0.02 20. (five 1000.) [| 1000.; 900.; 900.; 900.; 900. |]) Unchanged "fall tie"
                Expect.equal (classify 0.02 20. (five 1000.) (five 900.)) Improved "fall"
            testCase "lower mode ends at the first gap wider than the limit"
            <| fun _ ->
                Expect.equal (lowerMode 0.02 20. [| 1100.; 1000.; 1020.; 1200. |]) [| 1000.; 1020. |] "gap of the floor joins"
                Expect.equal (lowerMode 0.02 20. [| 1000.; 1021. |]) [| 1000. |] "gap past the floor splits"
                Expect.equal (lowerMode 0.02 20. [| 10000.; 10200.; 10400. |]) [| 10000.; 10200.; 10400. |] "relative limit chains"
                Expect.equal (lowerMode 0.02 20. [| -808.; -1632.; -1631. |]) [| -1632.; -1631. |] "signed values"
                Expect.equal (lowerMode 0.02 20. [| 5. |]) [| 5. |] "single value"
                Expect.equal (lowerMode 0.02 20. [| 100.; 120. |]) [| 100.; 120. |] "floor beyond the relative limit"
                Expect.equal (lowerMode 0.02 20. [| -1970.; -2000. |]) [| -2000.; -1970. |] "relative limit of a negative minimum"
            testCase "unimodal regression fails"
            <| fun _ ->
                let b = [| 1000.; 1001.; 1002.; 1000.; 1001. |]
                let h = [| 1100.; 1101.; 1102.; 1100.; 1103. |]
                Expect.equal (classify 0.02 20. b h) Regressed "classified"
                let v = verdictOf (sampled b h None)
                Expect.isTrue v.Fails "fails"
                Expect.equal (gateKind "node" "x" v) Regressed "finding"
            testCase "bimodal flip with equal lower modes is a state shift and passes"
            <| fun _ ->
                let b = [| 1000.; 1001.; 1000.; 1001.; 1000. |]
                let h = [| 1002.; 1100.; 1003.; 1100.; 1100. |]
                Expect.equal (classify 0.02 20. b h) StateShift "head flip"
                Expect.equal (classify 0.02 20. [| 1000.; 1100.; 1001.; 1100.; 1100. |] (five 999.)) StateShift "base flip"
                let v = verdictOf (sampled b h None)
                Expect.isFalse v.Fails "passes"
                Expect.equal (gateKind "dotnet" "x" v) StateShift "finding"
                Expect.stringContains (markdown none [ v ]) "State shifts" "own section"
                Expect.stringContains (markdown none [ v ]) "no unaccepted regression" "heading"
            testCase "PR 79 .NET app-table steps are a state shift and Node's uniform state is a regression"
            <| fun _ ->
                Expect.equal
                    (classify 0.02 20. [| 9255829.; 9255733.; 9255717.; 9255717.; 9255717. |] [| 9488690.; 9488863.; 9488816.; 9259253.; 9259218. |])
                    StateShift
                    "two head processes in the lower state"

                Expect.equal
                    (classify 0.02 20. [| 6329262.; 6319160.; 6333139.; 6346065.; 6347849. |] [| 8329393.; 8357194.; 13328080.; 8349579.; 8352106. |])
                    Regressed
                    "every head process in the upper state"
            testCase "lower modes decide a separated movement at the limit"
            <| fun _ ->
                Expect.equal (classify 0.02 20. (five 1000.) [| 1021.; 1021.; 1100.; 1100.; 1100. |]) Regressed "lower modes still regress"
                Expect.equal (classify 0.02 20. (five 1000.) [| 1020.; 1020.; 1100.; 1100.; 1100. |]) StateShift "lower modes at the limit"
            testCase "control shift makes a regression inconclusive"
            <| fun _ ->
                let v =
                    verdictOf (sampled (five 1000.) (five 1100.) (Some (five 5000., [| 5400.; 5401.; 5402.; 5400.; 5401. |])))

                Expect.isFalse v.Fails "passes"
                Expect.equal (gateKind "node" "x" v) Inconclusive "finding"
                Expect.equal (Set.ofArray v.ControlMoved) (Set.ofList [ "node"; "dotnet" ]) "runtimes"
                Expect.equal (gateKind "node" Control.Scenario v) Regressed "control finding"
                let text = markdown none [ v ]
                Expect.stringContains text "## Performance: inconclusive" "heading"
                Expect.stringContains text "the control moved" "reason"

                let falling =
                    verdictOf (sampled (five 1000.) (five 1100.) (Some (five 5000., five 4000.)))

                Expect.isFalse falling.Fails "a falling control also moves"

                let accepted =
                    verdict
                        "instructions"
                        0.02
                        20.
                        true
                        (acceptance false [ "Perf-Accepted: x -- expected" ] (Set.singleton "x"))
                        (sampled (five 1000.) (five 1100.) (Some (five 5000., five 4000.)))

                Expect.isFalse
                    (accepted.Findings
                     |> Array.exists (fun f -> f.Kind = Inconclusive && f.Accepted))
                    "an inconclusive finding carries no acceptance"
            testCase "regression with a stable control fails"
            <| fun _ ->
                for name, control in
                    [
                        "equal", (five 5000., five 5000.)
                        "overlapping", ([| 5000.; 5400.; 5000.; 5000.; 5000. |], [| 5400.; 5400.; 5000.; 5400.; 5400. |])
                        "within the limit", (five 5000., five 5100.)
                        "lower modes agree", (five 5000., [| 5001.; 5400.; 5400.; 5400.; 5400. |])
                    ] do
                    let v = verdictOf (sampled (five 1000.) (five 1100.) (Some control))
                    Expect.isTrue v.Fails name
                    Expect.isEmpty v.ControlMoved name
                    Expect.equal (gateKind "node" "x" v) Regressed name
            testCase "control measured on one side only leaves regressions gating"
            <| fun _ ->
                let r = sampled (five 1000.) (five 1100.) (Some (five 5000., five 9000.))

                let headOnly (ps: ProcessFigures[]) =
                    ps
                    |> Array.map (fun p ->
                        if p.Side = "base" then
                            { p with
                                Scenarios =
                                    p.Scenarios
                                    |> Array.filter (fun s -> s.Scenario <> Control.Scenario)
                            }
                        else
                            p)

                let v =
                    verdictOf
                        { r with
                            Processes = headOnly r.Processes
                            Counters = headOnly r.Counters
                        }

                Expect.isTrue v.Fails "fails"
                Expect.equal (gateKind "node" Control.Scenario v) Added "control added"
            testCase "pure classifier rejects empty nonfinite or invalid thresholds"
            <| fun _ ->
                for xs in [ [||]; [| nan |]; [| infinity |] ] do
                    Expect.throws (fun () -> classify 0.02 20. xs (five 1.) |> ignore) "invalid values"

                for t, f in [ -1., 0.; nan, 0.; 0., infinity; 0., -1. ] do
                    Expect.throws (fun () -> classify t f (five 1.) (five 2.) |> ignore) "invalid configuration"
            testCase "same SHA uses sides and Ranvier prefix alone gates"
            <| fun _ ->
                Expect.isTrue (verdictOf (run 1000. 1100.)).Fails "same SHA regression"
                let r = run 1000. 1100.

                let replace ps =
                    ps
                    |> Array.map (fun p ->
                        { p with
                            Scenarios =
                                p.Scenarios
                                |> Array.map (fun s -> { s with Engine = "Reference" })
                        })

                let reference =
                    { r with
                        Processes = replace r.Processes
                        Counters = replace r.Counters
                    }

                Expect.isFalse (verdictOf reference).Fails "reference"
                Expect.hasLength (verdictOf reference).Findings 8 "all metrics and runtimes retained"
            testCase "artifact validation fails closed"
            <| fun _ ->
                let r = run 1000. 1000.

                let corruptions =
                    [
                        { r with Rounds = 4 }
                        { r with Leg = "arm64" }
                        { r with Processes = r.Processes[1..] }
                        { r with
                            Processes = Array.append r.Processes [| r.Processes[0] |]
                        }
                        { r with
                            Processes =
                                r.Processes
                                |> Array.filter (fun p -> p.Runtime = "node")
                        }
                        { r with
                            Processes =
                                r.Processes
                                |> Array.map (fun p -> { p with Commit = "wrong" })
                        }
                        { r with
                            Processes =
                                r.Processes
                                |> Array.map (fun p ->
                                    { p with
                                        Scenarios =
                                            p.Scenarios
                                            |> Array.map (fun s ->
                                                { s with
                                                    Metrics = [| { Name = "bytes"; Value = 0. } |]
                                                })
                                    })
                        }
                    ]

                for bad in corruptions do
                    Expect.throws (fun () -> verdictOf bad |> ignore) "invalid artifact"
            testCase "bootstrap requires explicit missing base and complete head"
            <| fun _ ->
                let r = run 1000. 1000.

                let bootstrap =
                    { r with
                        Processes =
                            r.Processes
                            |> Array.filter (fun p -> p.Side = "head")
                        Counters =
                            r.Counters
                            |> Array.filter (fun p -> p.Side = "head")
                        NoBaseline =
                            [|
                                { Name = "node"; Value = "old worker" }
                                {
                                    Name = "dotnet"
                                    Value = "old worker"
                                }
                            |]
                    }

                Expect.isFalse (verdictOf bootstrap).Fails "bootstrap"

                Expect.isTrue
                    ((verdictOf bootstrap).Findings
                     |> Array.forall (fun f -> f.Kind = NoBaseline))
                    "missing source is not an added scenario"

                Expect.throws
                    (fun () ->
                        verdictOf
                            { bootstrap with
                                Processes = bootstrap.Processes[1..]
                            }
                        |> ignore)
                    "head incomplete"

                Expect.throws
                    (fun () ->
                        verdictOf
                            { r with
                                NoBaseline = bootstrap.NoBaseline
                            }
                        |> ignore)
                    "contradictory baseline"
            testCase "unavailable base instrumentation retains head counters without claiming additions"
            <| fun _ ->
                let r = run 1000. 1000.

                let r =
                    { r with
                        Counters =
                            r.Counters
                            |> Array.filter (fun p -> not (p.Side = "base" && p.Runtime = "node"))
                        Versions =
                            [|
                                {
                                    Name = "base:node:counters"
                                    Value = "unavailable: old instrumented worker"
                                }
                            |]
                    }

                let v = verdictOf r
                Expect.hasLength v.CounterUnavailable 1 "source reason"

                let f =
                    v.Findings
                    |> Array.find (fun f -> f.Runtime = "node" && f.Metric = "lib:runs")

                Expect.equal f.Kind NoBaseline "no measured source"
                Expect.hasLength f.Head 1 "head retained"
                Expect.stringContains (markdown none [ v ]) "base library counters unavailable" "explicit report reason"
            testCase "duplicate missing and nonfinite metric coverage is rejected"
            <| fun _ ->
                let r = run 1000. 1000.

                let corruptions =
                    [
                        { r with Counters = [||] }
                        { r with Counters = r.Counters[1..] }
                        { r with
                            Counters = Array.append r.Counters [| r.Counters[0] |]
                        }
                        { r with
                            Processes =
                                mapScenarios
                                    (fun _ s ->
                                        { s with
                                            Metrics = Array.append s.Metrics [| s.Metrics[0] |]
                                        })
                                    r.Processes
                        }
                        { r with
                            Processes =
                                mapScenarios
                                    (fun _ s ->
                                        { s with
                                            Metrics =
                                                s.Metrics
                                                |> Array.map (fun m -> { m with Value = nan })
                                        })
                                    r.Processes
                        }
                        { r with
                            Processes =
                                mapScenarios
                                    (fun p s ->
                                        if p.Process = 0 then
                                            { s with Metrics = s.Metrics[0..1] }
                                        else
                                            s)
                                    r.Processes
                        }
                        { r with
                            Processes =
                                r.Processes
                                |> Array.map (fun p ->
                                    { p with
                                        Scenarios = Array.append p.Scenarios p.Scenarios
                                    })
                        }
                    ]

                for bad in corruptions do
                    Expect.throws (fun () -> verdictOf bad |> ignore) "invalid coverage"
            testCase "added removed and head-only accepted scenario names are retained"
            <| fun _ ->
                let r = run 1000. 1000.

                let rename (p: ProcessFigures) (s: ScenarioFigures) =
                    { s with
                        Scenario = if p.Side = "head" then "new" else "gone"
                    }

                let r =
                    { r with
                        Processes = mapScenarios rename r.Processes
                        Counters = mapScenarios rename r.Counters
                    }

                let a = acceptance false [ "Perf-Accepted: new -- new scenario" ] (scenarioNames r)
                Expect.isEmpty a.Unknown "head-only known"
                let v = verdict "instructions" 0.02 20. true a r
                Expect.isFalse v.Fails "added and removed"

                Expect.isTrue
                    (v.Findings
                     |> Array.exists (fun f -> f.Scenario = "new" && f.Kind = Added))
                    "new"

                Expect.isTrue
                    (v.Findings
                     |> Array.exists (fun f -> f.Scenario = "gone" && f.Kind = Removed))
                    "gone"
            testCase "exact library changes include added and removed metrics"
            <| fun _ ->
                let r = run 1000. 1000.

                let cs =
                    mapScenarios
                        (fun p s ->
                            { s with
                                Metrics =
                                    [|
                                        {
                                            Name = "lib:runs"
                                            Value = if p.Side = "head" then 1.000000000001 else 1.
                                        }
                                        {
                                            Name = if p.Side = "head" then "lib:new" else "lib:old"
                                            Value = 1.
                                        }
                                    |]
                            })
                        r.Counters

                let v = verdictOf { r with Counters = cs }

                Expect.hasLength
                    (v.Findings
                     |> Array.filter (fun f -> f.Kind = CounterChanged))
                    2
                    "sub-epsilon changes exact"

                Expect.hasLength
                    (v.Findings
                     |> Array.filter (fun f -> f.Metric = "lib:new" && f.Kind = Added))
                    2
                    "added metric"

                Expect.hasLength
                    (v.Findings
                     |> Array.filter (fun f -> f.Metric = "lib:old" && f.Kind = Removed))
                    2
                    "removed metric"

                Expect.stringContains (markdown none [ v ]) "Library counter changes" "own section"
                Expect.stringContains (markdown none [ v ]) "1.000000000001" "exact counter rendering"
            testCase "signed Node heap values and unchanged reference tables survive"
            <| fun _ ->
                let r = run 1000. 1000.

                let ps =
                    mapScenarios
                        (fun p s ->
                            if p.Runtime = "node" then
                                { s with
                                    Metrics =
                                        s.Metrics
                                        |> Array.map (fun m -> if m.Name = "bytes" then { m with Value = -8. } else m)
                                }
                            else
                                s)
                        r.Processes

                let v = verdictOf { r with Processes = ps }

                Expect.isTrue
                    (v.Findings
                     |> Array.exists (fun f ->
                         f.Runtime = "node"
                         && f.Metric = "bytes"
                         && f.Head[0] = -8.))
                    "signed heap"

                Expect.stringStarts (markdown none [ v ]) "<!-- ranvier-perf -->" "comment marker"
            testCase "trailer unicode ASCII reasons unknown and malformed"
            <| fun _ ->
                Expect.equal
                    (parseTrailers [ "Perf-Accepted: x \u2014 reason"; "Perf-Accepted: all -- rewrite" ])
                    [ ([ "x" ], "reason"); ([ "all" ], "rewrite") ]
                    "separators"

                let a = acceptance true [ "Perf-Accepted: typo -- reason" ] (Set.singleton "x")
                Expect.equal a.Unknown [ "typo" ] "label cannot hide typo"
                Expect.isTrue (verdict "instructions" 0.02 20. true a (run 1000. 1000.)).Fails "config failure"

                Expect.throws
                    (fun () ->
                        parseTrailers [ "Perf-Accepted: all -- " ]
                        |> ignore)
                    "missing reason"
            testCase "override preserves scoped reasons and acceptance"
            <| fun _ ->
                let a =
                    acceptance false [ "Perf-Accepted: x -- expected"; "Perf-Accepted: y -- separate" ] (Set.ofList [ "x"; "y" ])

                let v = verdict "instructions" 0.02 20. true a (run 1000. 1100.)
                Expect.isFalse v.Fails "accepted"
                let text = markdown a [ v ]
                Expect.stringContains text "x: expected" "scoped reason"
                Expect.stringContains text "y: separate" "separate scope"
                Expect.stringContains text "<details>" "collapsed table"
                Expect.stringContains text "GC" "GC flag"
            testCase "a compiled region in any process flags its side"
            <| fun _ ->
                let r = run 1000. 1000.

                let compiledHead =
                    { r with
                        Processes =
                            r.Processes
                            |> Array.map (fun p ->
                                if
                                    p.Side = "head"
                                    && p.Runtime = "node"
                                    && p.Process = 3
                                then
                                    { p with
                                        Scenarios =
                                            p.Scenarios
                                            |> Array.map (fun s -> { s with Compiled = true })
                                    }
                                else
                                    p)
                    }

                let v = verdict "instructions" 0.02 20. true none compiledHead

                let finding runtime =
                    v.Findings
                    |> Array.find (fun f -> f.Runtime = runtime && f.Metric = "instructions")

                Expect.isFalse (finding "node").BaseCompiled "base node"
                Expect.isTrue (finding "node").HeadCompiled "head node"
                Expect.isFalse (finding "dotnet").HeadCompiled "head dotnet"
                let text = markdown none [ v ]
                Expect.stringContains text "| GC base/head | JIT base/head |" "flag column"
                Expect.stringContains text "| x | Ranvier | yes/yes | no/yes |" "node row"
                Expect.stringContains text "| x | Ranvier | yes/yes | no/no |" "dotnet row"
            testCase "x64 regressions are advisory"
            <| fun _ ->
                let r =
                    { run 1000. 1200. with
                        Leg = "x64-timing"
                    }

                let v = verdict "cpu_ns" 0.15 0. false none r
                Expect.isFalse v.Fails "advisory"
                Expect.hasLength v.Warnings 2 "each runtime"
                let typo = acceptance true [ "Perf-Accepted: typo -- mistake" ] (Set.singleton "x")
                Expect.isTrue (verdict "cpu_ns" 0.15 0. false typo r).Fails "unknown override remains fatal"
            testCase "CLI writes readable DTO on rejected regression and validates overrides"
            <| fun _ ->
                let dir = Path.Combine (Path.GetTempPath (), Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory dir |> ignore

                try
                    let path = Path.Combine (dir, "ab.json")
                    File.WriteAllText (path, JsonSerializer.Serialize (run 1000. 1100.))
                    Expect.equal (main [ "--ab"; path; "--out"; dir ]) 1 "regression exit"

                    use json =
                        JsonDocument.Parse (File.ReadAllText (Path.Combine (dir, "verdict.json")))

                    let first =
                        json.RootElement.GetProperty("Verdicts").[0].GetProperty("Findings").EnumerateArray()
                        |> Seq.find (fun f -> f.GetProperty("Metric").GetString() = "instructions")

                    Expect.equal (first.GetProperty("Kind").GetString()) "Regressed" "union string"
                    Expect.equal (first.GetProperty("Delta").ValueKind) JsonValueKind.Number "delta numeric"
                    Expect.floatClose Accuracy.high (first.GetProperty("Delta").GetDouble()) 0.1 "relative delta"
                    Expect.equal (main [ "--ab"; path; "--out"; dir; "--accept"; "x"; "--accept-reason"; "expected" ]) 0 "CLI override"
                    Expect.equal (main [ "--ab"; path; "--out"; dir; "--accept"; "typo"; "--accept-reason"; "typo" ]) 1 "unknown override"
                    Expect.throws (fun () -> main [] |> ignore) "no artifact"
                    Expect.throws (fun () -> main [ "--ab"; path; "--accept"; "all" ] |> ignore) "reason required"
                    Expect.equal (main [ "--ab"; path; "--out"; dir; "--threshold"; "0.2"; "--floor"; "0" ]) 0 "threshold option"
                    Expect.equal (main [ "--ab"; path; "--out"; dir; "--threshold"; "0"; "--floor"; "100" ]) 0 "strict floor option"

                    Expect.throws
                        (fun () ->
                            main [ "--ab"; path; "--threshold"; "NaN" ]
                            |> ignore)
                        "finite CLI threshold"

                    let malformed =
                        JsonSerializer.Serialize(run 1000. 1000.).Replace("\"Gc\":true,", "")

                    File.WriteAllText (path, malformed)
                    Expect.throws (fun () -> main [ "--ab"; path ] |> ignore) "required Gc field"
                    File.WriteAllText (path, JsonSerializer.Serialize (run 0. 25.))
                    Expect.equal (main [ "--ab"; path; "--out"; dir ]) 1 "zero-base separated regression"

                    use zero =
                        JsonDocument.Parse (File.ReadAllText (Path.Combine (dir, "verdict.json")))

                    let f =
                        zero.RootElement.GetProperty("Verdicts").[0].GetProperty("Findings").EnumerateArray()
                        |> Seq.find (fun f -> f.GetProperty("Metric").GetString() = "instructions")

                    Expect.equal (f.GetProperty("Delta").ValueKind) JsonValueKind.Null "undefined percentage is JSON null"
                    Expect.stringContains (File.ReadAllText (Path.Combine (dir, "comment.md"))) "n/a" "honest zero-base percentage"
                finally
                    Directory.Delete (dir, true)
        ]
