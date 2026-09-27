/// <summary>
/// Runs of the Fable harness in <c>fable/Ranvier.Counters</c> under Node.js,
/// and the JSON it writes.
/// </summary>
module CounterBench.NodeRuns

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json

type NodeCounter = { Name: string; Value: float }

type NodeMeasurement =
    {
        Ops: int
        Region: int
        /// <summary>
        /// Growth of the heap spaces holding JavaScript objects over the
        /// region: new, old and both large-object spaces.
        /// </summary>
        Bytes: float
        /// <summary>
        /// Growth of <c>process.memoryUsage().heapUsed</c> over the region, which
        /// adds the code and trusted spaces.
        /// </summary>
        HeapUsedBytes: float
        /// <summary>
        /// Garbage collections during the region.
        /// </summary>
        Gen0Collections: int
        NoGcRegion: bool
        /// <summary>
        /// <c>Ranvier.Counters.Snapshot()</c> after the region. Empty when
        /// the harness was compiled without <c>RanvierCounters</c>.
        /// </summary>
        Counters: NodeCounter[]
    }

type NodeCase =
    {
        Scenario: string
        Engine: string
        Unit: string
        /// <summary>
        /// The measurement at N, then the measurement at 2N.
        /// </summary>
        Measurements: NodeMeasurement[]
    }

type NodeRun =
    {
        ProcessId: int
        CountersCompiled: bool
        Cases: NodeCase[]
    }

/// <summary>
/// A run with processor counter totals per region: for the main thread, then
/// for every thread of the process.
/// </summary>
type NodePmcRun =
    {
        Run: NodeRun
        MainThread: Pmc.RegionCounts[]
        AllThreads: Pmc.RegionCounts[]
    }

/// <summary>
/// V8 flags for every run. V8 compiles and collects on the main thread, and
/// the heap after a region is the same in every run.
/// </summary>
let flags = [ "--expose-gc"; "--single-threaded" ]

/// <summary>
/// The harness entry point of the build under <c>output</c>, <c>plain</c> or <c>counters</c>.
/// </summary>
let script (output: string) (build: string) =
    Path.Combine (output, build, "Main.fs.js")

let private startInfo (node: string) (script: string) (args: string list) =
    let info = ProcessStartInfo node

    for arg in flags @ [ script ] @ args do
        info.ArgumentList.Add arg

    info.UseShellExecute <- false
    info

let private finish (p: Process) (resultFile: string) =
    p.WaitForExit ()

    if p.ExitCode <> 0 then
        failwith $"node exited with %d{p.ExitCode}."

    JsonSerializer.Deserialize<NodeRun>(File.ReadAllText resultFile)

/// <summary>
/// Runs the harness <c>script</c> to completion.
/// </summary>
let run (node: string) (script: string) (scale: int) (resultFile: string) =
    use p =
        Process.Start (startInfo node script [ "--out"; resultFile; "--scale"; string scale ])

    finish p resultFile

/// <summary>
/// Runs the harness <c>script</c> with <c>--handshake</c>. <c>mark</c> receives each region
/// boundary, <c>true</c> for a <c>BEGIN</c>, while the harness waits for the reply. Fails
/// unless every measured region was marked at both ends.
/// </summary>
let runHandshake (node: string) (script: string) (scale: int) (resultFile: string) (mark: bool -> int -> unit) =
    let info =
        startInfo node script [ "--out"; resultFile; "--scale"; string scale; "--handshake" ]

    info.RedirectStandardInput <- true
    info.RedirectStandardOutput <- true
    use p = Process.Start info

    let reply () =
        p.StandardInput.Write '\n'
        p.StandardInput.Flush ()

    let marked = HashSet<struct (bool * int)>()
    let mutable line = p.StandardOutput.ReadLine ()

    while not (isNull line) do
        match line.Split ' ' with
        | [| ("BEGIN" | "END") as marker; region |] ->
            let isBegin = marker = "BEGIN"
            mark isBegin (int region)

            marked.Add (struct (isBegin, int region))
            |> ignore

            reply ()
        | _ -> eprintfn "%s" line

        line <- p.StandardOutput.ReadLine ()

    let run = finish p resultFile

    for case in run.Cases do
        for m in case.Measurements do
            if
                not (
                    marked.Contains (struct (true, m.Region))
                    && marked.Contains (struct (false, m.Region))
                )
            then
                failwith $"Region %d{m.Region} of %s{case.Scenario}/%s{case.Engine} was not marked at both ends."

    run

/// <summary>
/// The Node.js version <c>node</c> reports.
/// </summary>
let version (node: string) =
    let info = ProcessStartInfo (node, "--version")
    info.RedirectStandardOutput <- true
    info.UseShellExecute <- false
    use p = Process.Start info
    let output = p.StandardOutput.ReadToEnd ()
    p.WaitForExit ()
    output.Trim ()
