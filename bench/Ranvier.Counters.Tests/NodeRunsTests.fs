module CounterBench.Tests.NodeRunsTests

open System
open System.Diagnostics
open System.IO
open Expecto
open CounterBench

let private nodeAvailable () =
    try
        NodeRuns.version "node" |> ignore
    with :? System.ComponentModel.Win32Exception ->
        skiptest "Node executable is unavailable on PATH"

let private fixture boundaries regions wrongPid body =
    nodeAvailable ()

    let directory =
        Path.Combine (
            Path.GetTempPath (),
            "ranvier-handshake-test-"
            + Guid.NewGuid().ToString "N"
        )

    Directory.CreateDirectory directory |> ignore

    try
        let script = Path.Combine (directory, "fixture.cjs")
        let result = Path.Combine (directory, "result.json")
        let pidFile = Path.Combine (directory, "pid")

        let source =
            "const fs = require('fs');\n"
            + "fs.writeFileSync(__dirname + '/pid', String(process.pid));\n"
            + "for (const line of "
            + System.Text.Json.JsonSerializer.Serialize boundaries
            + ") {\n"
            + "  fs.writeSync(1, line + '\\n'); fs.readSync(0, Buffer.alloc(1), 0, 1, null);\n}\n"
            + "const measurements = "
            + System.Text.Json.JsonSerializer.Serialize regions
            + ";\n"
            + "fs.writeFileSync(process.argv[process.argv.indexOf('--out') + 1], JSON.stringify({ProcessId: process.pid"
            + (if wrongPid then " + 1" else "")
            + ", CountersCompiled:false, Cases:[{Scenario:'fixture', Engine:'Test', Unit:'op', Measurements:measurements.map(Region => ({Region, Ops:1, Bytes:0, HeapUsedBytes:0, Gen0Collections:0, NoGcRegion:true, Counters:[]}))}]}));\n"

        File.WriteAllText (script, source)
        body script result pidFile
    finally
        Directory.Delete (directory, true)

let private expectExited pidFile =
    let pid = int (File.ReadAllText pidFile)

    let running =
        try
            use child = Process.GetProcessById pid
            not child.HasExited
        with :? ArgumentException ->
            false

    Expect.isFalse running "failed handshake child was terminated and reaped"

[<Tests>]
let tests =
    testList
        "Node handshake"
        [
            test "callback receives the real child PID and every boundary" {
                fixture [| "BEGIN 7"; "END 7"; "BEGIN 8"; "END 8" |] [| 7; 8 |] false (fun script result _ ->
                    let marks = ResizeArray<int * bool * int>()

                    let run =
                        NodeRuns.runHandshake "node" script 1 None result (fun pid beginRegion region -> marks.Add (pid, beginRegion, region))

                    Expect.equal
                        (Seq.toList marks)
                        [
                            run.ProcessId, true, 7
                            run.ProcessId, false, 7
                            run.ProcessId, true, 8
                            run.ProcessId, false, 8
                        ]
                        "ordered callback boundaries")
            }
            test "callback failure terminates and waits for a child blocked on reply" {
                fixture [| "BEGIN 7" |] [| 7 |] false (fun script result pidFile ->
                    Expect.throws
                        (fun () ->
                            NodeRuns.runHandshake "node" script 1 None result (fun _ _ _ -> failwith "callback failed")
                            |> ignore)
                        "callback failure propagates"

                    expectExited pidFile)
            }
            test "malformed and inconsistent protocols fail and clean up their children" {
                for boundaries, regions, wrongPid in
                    [
                        [| "BEGIN nope" |], [| 7 |], false
                        [| "BEGIN -1" |], [| 7 |], false
                        [| "BEGIN\t7" |], [| 7 |], false
                        [| "BEGIN 7 extra" |], [| 7 |], false
                        [| "END 7" |], [| 7 |], false
                        [| "BEGIN 7"; "BEGIN 8" |], [| 7; 8 |], false
                        [| "BEGIN 7"; "END 8" |], [| 7 |], false
                        [| "BEGIN 7"; "END 7"; "BEGIN 7" |], [| 7 |], false
                        [| "BEGIN 7"; "END 7"; "END 7" |], [| 7 |], false
                        [| "BEGIN 7" |], [| 7 |], false
                        [| "BEGIN 7"; "END 7" |], [| 8 |], false
                        [| "BEGIN 7"; "END 7" |], [| 7; 7 |], false
                        [| "BEGIN 7"; "END 7" |], [| 7 |], true
                    ] do
                    fixture boundaries regions wrongPid (fun script result pidFile ->
                        Expect.throws
                            (fun () ->
                                NodeRuns.runHandshake "node" script 1 None result (fun _ _ _ -> ())
                                |> ignore)
                            "invalid protocol rejected"

                        expectExited pidFile)
            }
        ]
    |> testSequenced

let private measurement region : NodeRuns.NodeMeasurement =
    {
        Ops = 1 + region % 2
        Region = region
        Bytes = 0.
        HeapUsedBytes = 0.
        CodeBytes = 0.
        Gen0Collections = 0
        NoGcRegion = true
        Counters = [||]
    }

let private nodeRun pid (cases: (string * int list) list) : NodeRuns.NodeRun =
    {
        ProcessId = pid
        CountersCompiled = false
        Cases =
            [|
                for scenario, regions in cases ->
                    {
                        Scenario = scenario
                        Engine = "Ranvier"
                        Unit = "op"
                        Measurements = regions |> List.map measurement |> List.toArray
                    }
            |]
    }

let private recorded (pairs: (int * string) list) =
    readOnlyDict pairs

let private script (source: string) body =
    nodeAvailable ()

    let directory =
        Path.Combine (Path.GetTempPath (), "ranvier-list-test-" + Guid.NewGuid().ToString "N")

    Directory.CreateDirectory directory |> ignore

    try
        let path = Path.Combine (directory, "fixture.cjs")
        File.WriteAllText (path, source)
        body directory path
    finally
        Directory.Delete (directory, true)

[<Tests>]
let perScenario =
    testList
        "Node per-scenario runs"
        [
            test "merge keeps each run's own scenario and renumbers its regions in order" {
                let run, regions =
                    NodeRuns.merge
                        [|
                            "create", nodeRun 11 [ "create", [ 0; 1 ]; "create-on", [ 2; 3 ] ], recorded [ 0, "a"; 1, "b"; 2, "x"; 3, "y" ]
                            "keyed", nodeRun 12 [ "keyed", [ 0; 1 ]; "keyed", [ 2; 3 ] ], recorded [ 0, "c"; 1, "d"; 2, "e"; 3, "f" ]
                        |]

                Expect.equal run.ProcessId 11 "first process ID"
                Expect.equal (run.Cases |> Array.map _.Scenario) [| "create"; "keyed"; "keyed" |] "own scenarios in order"

                Expect.equal
                    (run.Cases
                     |> Array.collect (fun c ->
                         c.Measurements
                         |> Array.map (fun m -> m.Region, m.Ops)))
                    [| 0, 1; 1, 2; 2, 1; 3, 2; 4, 1; 5, 2 |]
                    "regions renumbered, measurements kept"

                Expect.equal [ for i in 0..5 -> regions[i] ] [ "a"; "b"; "c"; "d"; "e"; "f" ] "counts follow their regions"
                Expect.equal regions.Count 6 "no other regions"
            }
            test "merge rejects an empty selection, a run without its scenario and an unrecorded region" {
                Expect.throws
                    (fun () ->
                        NodeRuns.merge ([||]: (string * NodeRuns.NodeRun * Collections.Generic.IReadOnlyDictionary<int, string>)[])
                        |> ignore)
                    "no runs"

                Expect.throws
                    (fun () ->
                        NodeRuns.merge [| "create", nodeRun 1 [ "create-on", [ 0; 1 ] ], recorded [ 0, "a"; 1, "b" ] |]
                        |> ignore)
                    "scenario absent from its run"

                Expect.throws
                    (fun () ->
                        NodeRuns.merge [| "create", nodeRun 1 [ "create", [ 0; 1 ] ], recorded [ 0, "a" ] |]
                        |> ignore)
                    "region absent from the recording"
            }
            test "scenarios reads the harness list and passes every V8 flag" {
                script
                    "if (process.argv.includes('--list')) { process.stdout.write('alpha\\nbeta\\n\\n'); require('fs').writeFileSync(__dirname + '/flags', process.execArgv.join(' ')); } else process.exit(3);"
                    (fun directory path ->
                        Expect.equal (NodeRuns.scenarios "node" path) (Some [| "alpha"; "beta" |]) "listed names"
                        Expect.equal (File.ReadAllText (Path.Combine (directory, "flags"))) (String.Join (" ", NodeRuns.flags)) "flags")
            }
            test "scenarios is None for a harness that rejects --list, lists nothing or fails" {
                script "console.error('Unknown argument'); process.exit(1);" (fun _ path -> Expect.isNone (NodeRuns.scenarios "node" path) "rejected")

                script "" (fun _ path -> Expect.isNone (NodeRuns.scenarios "node" path) "empty")

                script "process.stdout.write('alpha\\n'); process.exit(1);" (fun _ path ->
                    Expect.isNone (NodeRuns.scenarios "node" path) "names from a failed run")
            }
            test "runScenario selects its scenario by exact name" {
                script
                    "const fs = require('fs'); const a = process.argv; const name = a[a.indexOf('--scenario') + 1]; fs.writeFileSync(a[a.indexOf('--out') + 1], JSON.stringify({ProcessId: process.pid, CountersCompiled: false, Cases: [{Scenario: name, Engine: 'Test', Unit: 'op', Measurements: []}]}));"
                    (fun directory path ->
                        let run =
                            NodeRuns.runScenario "node" path 1 "keyed-remove" (Path.Combine (directory, "out.json")) (fun _ _ _ -> ())

                        Expect.equal run.Cases[0].Scenario "keyed-remove" "scenario argument")
            }
        ]
    |> testSequenced
