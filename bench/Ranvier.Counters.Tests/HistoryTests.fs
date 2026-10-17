module CounterBench.Tests.HistoryTests

open System
open System.IO
open System.Globalization
open System.Text.Json
open Expecto
open CounterBench

// Synthetic instruction figures test history semantics independently of runner hardware.
let private ab head rows : AbRun =
    let proc side runtime i =
        {
            Side = side
            Commit = if side = "head" then head else "p"
            Runtime = runtime
            Process = i
            Scenarios =
                [|
                    for s, v in rows ->
                        {
                            Scenario = s
                            Engine = "Ranvier"
                            Gc = i = 0
                            Compiled = false
                            Metrics =
                                [|
                                    {
                                        Name = "instructions"
                                        Value = if side = "head" then v + float i else 1000.
                                    }
                                    { Name = "cpu_ns"; Value = v }
                                |]
                        }
                |]
        }

    {
        Leg = "arm64-counters"
        Machine = "synthetic CPU"
        Base = "p"
        Head = head
        Rounds = 5
        Versions = [| { Name = "head:node"; Value = "26" }; { Name = "base:node"; Value = "25" } |]
        NoBaseline = [||]
        Processes =
            [|
                for rt in [ "node"; "dotnet" ] do
                    for i in 0..4 do
                        for side in [ "base"; "head" ] -> proc side rt i
            |]
        Counters =
            [|
                for rt in [ "node"; "dotnet" ] do
                    for side in [ "base"; "head" ] do
                        let p = proc side rt 0

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

let private recorded sha date rows message =
    History.create sha "p" date ("subject " + sha) [ message ] [| ab sha rows |]

[<Tests>]
let tests =
    testList
        "History"
        [
            testCase "records raw figures explicit medians and scoped actual acceptance"
            <| fun _ ->
                let r =
                    recorded "h" DateTime.UtcNow [ "x", 1100.; "y", 900. ] "Perf-Accepted: x -- deliberate\nPerf-Accepted: y -- declared only"

                Expect.equal r.Accepted [ "x" ] "only actual gate regressions"
                Expect.equal r.Overrides.Length 2 "declarations retained"
                Expect.equal r.AcceptedRegressions.Length 2 "one finding per runtime"
                Expect.equal r.AcceptedRegressions[0].Reasons[0].Reason "deliberate" "scope"

                let m =
                    r.Medians
                    |> Array.find (fun m ->
                        m.Side = "head"
                        && m.Runtime = "node"
                        && m.Metric = "instructions"
                        && m.Scenario = "x")

                Expect.equal m.Value 1102. "median of five"
                Expect.equal r.Runs[0].Processes.Length 20 "raw samples"

                let roundtrip =
                    JsonSerializer.Deserialize<History.RecordedRun>(JsonSerializer.Serialize r)

                Expect.equal roundtrip r "JSON record roundtrip"
            testCase "timeline orders date then SHA and escapes table text"
            <| fun _ ->
                let date = DateTime (2026, 10, 16)
                let a = recorded "aaaaaaa" date [ "x", 69. ] ""

                let b =
                    { recorded "bbbbbbb" date [ "x", 36.; "new", 9000. ] "" with
                        Subject = "pipe | *subject*\nline"
                    }

                let old = CultureInfo.CurrentCulture

                try
                    CultureInfo.CurrentCulture <- CultureInfo ("th-TH")
                    let text = History.timeline [| b; a |]
                    Expect.isTrue (text.IndexOf ("aaaaaaa") < text.IndexOf ("bbbbbbb")) "SHA tie break"
                    Expect.stringContains text "| 71 |" "head median"
                    Expect.stringContains text "| — |" "missing scenario"
                    Expect.stringContains text "2026-10-16" "Gregorian"
                    Expect.stringContains text "pipe \\| \\*subject\\* line" "escaped subject"
                finally
                    CultureInfo.CurrentCulture <- old
            testCase "same SHA timeline uses side not commit"
            <| fun _ ->
                let a = ab "p" [ "x", 36. ]
                let r = History.create "p" "p" DateTime.UtcNow "same" [] [| a |]
                Expect.stringContains (History.timeline [| r |]) "| 38 |" "head only"
            testCase "unknown overrides identity mismatch and incomplete rounds fail"
            <| fun _ ->
                Expect.throws
                    (fun () ->
                        recorded "h" DateTime.UtcNow [ "x", 1100. ] "Perf-Accepted: typo -- mistake"
                        |> ignore)
                    "unknown"

                let a = ab "h" [ "x", 1100. ]

                for bad in [ a; { a with Rounds = 4 } ] do
                    Expect.throws
                        (fun () ->
                            History.create "wrong" "p" DateTime.UtcNow "s" [] [| bad |]
                            |> ignore)
                        "identity"

                Expect.throws
                    (fun () ->
                        History.create "h" "p" DateTime.UtcNow "s" [] [| { a with Rounds = 4 } |]
                        |> ignore)
                    "rounds"
            testCase "bootstrap keeps head medians without invented base samples or acceptance"
            <| fun _ ->
                let a = ab "h" [ "x", 1100. ]

                let onlyHead ps =
                    ps
                    |> Array.filter (fun (p: ProcessFigures) -> p.Side = "head")

                let a =
                    { a with
                        Processes = onlyHead a.Processes
                        Counters = onlyHead a.Counters
                        NoBaseline =
                            [|
                                { Name = "node"; Value = "old worker" }
                                {
                                    Name = "dotnet"
                                    Value = "old worker"
                                }
                            |]
                    }

                let r =
                    History.create "h" "p" DateTime.UtcNow "bootstrap" [ "Perf-Accepted: all -- declared" ] [| a |]

                Expect.isEmpty r.Accepted "bootstrap is not a regression"

                Expect.isTrue
                    (r.Medians
                     |> Array.forall (fun m -> m.Side = "head"))
                    "no fabricated baseline"

                Expect.equal r.Medians.Length 4 "all available timing metrics"
                Expect.equal r.Runs[0].Counters.Length 2 "single instrumented process per runtime side"
            testCase "both legs require matching parents and advisory changes are declarations only"
            <| fun _ ->
                let a = ab "h" [ "x", 1100. ]
                let x = { a with Leg = "x64-timing" }

                let r =
                    History.create "h" "p" DateTime.UtcNow "two legs" [ "Perf-Accepted: all -- declared" ] [| a; x |]

                Expect.equal r.Runs.Length 2 "both artifacts retained"

                Expect.isTrue
                    (r.AcceptedRegressions
                     |> Array.forall (fun f -> f.Leg = "arm64-counters"))
                    "instruction gate only"

                let advisory =
                    History.create "h" "p" DateTime.UtcNow "advisory" [ "Perf-Accepted: all -- declared" ] [| x |]

                Expect.isEmpty advisory.Accepted "advisory is not an accepted gate regression"

                Expect.throws
                    (fun () ->
                        History.create "h" "p" DateTime.UtcNow "mismatch" [] [| a; { x with Base = "wrong" } |]
                        |> ignore)
                    "leg parent mismatch"
            testCase "record CLI strict flags required JSON fields and roundtrip"
            <| fun _ ->
                let dir = Path.Combine (Path.GetTempPath (), Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory dir |> ignore

                try
                    let input = Path.Combine (dir, "ab.json")
                    let msg = Path.Combine (dir, "message.txt")
                    let output = Path.Combine (dir, "run.json")
                    File.WriteAllText (input, JsonSerializer.Serialize (ab "h" [ "x", 1100. ]))
                    File.WriteAllText (msg, "Perf-Accepted: x -- deliberate")

                    let args =
                        [
                            "record"
                            "--ab"
                            input
                            "--commit"
                            "h"
                            "--parent"
                            "p"
                            "--date"
                            "2026-10-16T00:00:00Z"
                            "--subject"
                            "--subject $(echo unsafe); `literal` | \"quoted\"\nnext line"
                            "--message"
                            msg
                            "--out"
                            output
                        ]

                    Expect.equal (History.main args) 0 "record"
                    let r = JsonSerializer.Deserialize<History.RecordedRun>(File.ReadAllText output)
                    Expect.equal r.Accepted [ "x" ] "actual accepted"
                    Expect.equal r.Subject "--subject $(echo unsafe); `literal` | \"quoted\"\nnext line" "subject is literal data"

                    let runDir = Path.Combine (dir, "runs")
                    Directory.CreateDirectory runDir |> ignore
                    File.Copy (output, Path.Combine (runDir, "h.json"))

                    Expect.equal
                        (History.main [ "timeline"; "--runs"; runDir; "--out"; Path.Combine (dir, "timeline.md") ])
                        0
                        "timeline reads recorded JSON"

                    Expect.throws
                        (fun () ->
                            History.main [ "timeline"; "--runs"; runDir; "--out"; output; "--runs"; runDir ]
                            |> ignore)
                        "timeline singleton"

                    for extra in [ [ "--typo"; "v" ]; [ "--commit"; "h" ]; [ "--out" ] ] do
                        Expect.throws (fun () -> History.main (args @ extra) |> ignore) "strict options"

                    File.WriteAllText (input, File.ReadAllText(input).Replace("\"Gc\":true,", ""))
                    Expect.throws (fun () -> History.main args |> ignore) "required field"
                finally
                    Directory.Delete (dir, true)
        ]
