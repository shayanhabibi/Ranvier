module CounterBench.Program

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text.Json

let private usage =
    """Ranvier.Counters: instructions, allocations and library operation counts per operation.

Usage: Ranvier.Counters [options]

  --no-pmc          Skip processor counters. Runs without elevation.
  --counters-worker <path>
                    Read the library counters from a second worker: the
                    Ranvier.Counters.dll or .exe of a build with
                    -p:RanvierCounters=true. Every other figure comes from
                    this build.
  --repeat <k>      Run the worker k times and compare the runs (default 1).
  --scale <k>       Multiply every scenario's N by k (default 1).
  --sources <list>  Comma-separated profile sources
                    (default InstructionRetired,TotalCycles,BranchMispredictions).
  --list-sources    Print the profile sources this machine offers, and exit.
  --out <dir>       Output directory (default docs/.ai/benchmarks/counters).
  --fable <dir>     Also measure the Fable harness compiled into <dir>/plain,
                    and read library counters from <dir>/counters when present.
  --node <path>     The node executable (default node).
  --pmc-dry-run     Drive the Fable harness's region handshake without ETW
                    sessions. Implies --no-pmc; runs without elevation.
  --reconcile       Run each Ranvier case once on its own graph and
                    compare the library counters and live edges with the
                    trace log. Needs -p:RanvierCounters=true -p:RanvierTrace=true;
                    exits 1 when any case fails.
  --help            Print this text.

Processor counters need an elevated process. Build with -p:RanvierCounters=true,
or pass --counters-worker, for the library counter columns. counters.ps1 at the
repository root compiles both Fable builds and runs this tool with --fable."""

/// <summary>
/// The environment the worker runs in. Tiered compilation off: every method is
/// compiled once, fully optimised, on first call. No PGO: the code is the same
/// in every run. ReadyToRun off: framework code is JIT-compiled at the same
/// optimisation level, rather than served precompiled.
/// </summary>
let workerEnvironment =
    [
        "DOTNET_TieredCompilation", "0"
        "DOTNET_TieredPGO", "0"
        "DOTNET_ReadyToRun", "0"
        "DOTNET_gcServer", "0"
    ]

type private Options =
    {
        Worker: string option
        CountersWorker: string option
        Pmc: bool
        Repeat: int
        Scale: int
        Sources: string[]
        ListSources: bool
        Out: string option
        Fable: string option
        Node: string
        PmcDryRun: bool
        Reconcile: bool
        Help: bool
    }

let rec private parse (options: Options) (args: string list) =
    match args with
    | [] -> options
    | "--worker" :: path :: rest -> parse { options with Worker = Some path } rest
    | "--counters-worker" :: path :: rest ->
        parse
            { options with
                CountersWorker = Some (Path.GetFullPath path)
            }
            rest
    | "--no-pmc" :: rest -> parse { options with Pmc = false } rest
    | "--repeat" :: k :: rest -> parse { options with Repeat = int k } rest
    | "--scale" :: k :: rest -> parse { options with Scale = int k } rest
    | "--sources" :: list :: rest ->
        parse
            { options with
                Sources =
                    list.Split (
                        ',',
                        StringSplitOptions.RemoveEmptyEntries
                        ||| StringSplitOptions.TrimEntries
                    )
            }
            rest
    | "--list-sources" :: rest -> parse { options with ListSources = true } rest
    | "--out" :: dir :: rest -> parse { options with Out = Some dir } rest
    | "--fable" :: dir :: rest -> parse { options with Fable = Some dir } rest
    | "--node" :: path :: rest -> parse { options with Node = path } rest
    | "--pmc-dry-run" :: rest ->
        parse
            { options with
                Pmc = false
                PmcDryRun = true
            }
            rest
    | "--reconcile" :: rest -> parse { options with Reconcile = true } rest
    | ("--help" | "-h") :: rest -> parse { options with Help = true } rest
    | unknown :: _ -> failwith $"Unknown argument '%s{unknown}'. See --help."

/// <summary>
/// The directory holding <c>Ranvier.slnx</c>, searched upwards from the
/// working directory and then from the executable.
/// </summary>
let private repositoryRoot () =
    let rec search (dir: DirectoryInfo) =
        if isNull dir then
            None
        elif File.Exists (Path.Combine (dir.FullName, "Ranvier.slnx")) then
            Some dir.FullName
        else
            search dir.Parent

    search (DirectoryInfo Environment.CurrentDirectory)
    |> Option.orElse (search (DirectoryInfo AppContext.BaseDirectory))

let private git (root: string) (args: string) =
    let info = ProcessStartInfo ("git", $"-C \"%s{root}\" %s{args}")
    info.RedirectStandardOutput <- true
    info.UseShellExecute <- false
    use p = Process.Start info
    let output = p.StandardOutput.ReadToEnd ()
    p.WaitForExit ()
    if p.ExitCode = 0 then Some (output.Trim ()) else None

/// <summary>
/// The short commit hash, suffixed <c>-dirty</c> when tracked files differ from it.
/// </summary>
let private commit (root: string option) =
    match root with
    | None -> "unknown"
    | Some root ->
        match git root "rev-parse --short HEAD" with
        | None -> "unknown"
        | Some sha ->
            match git root "status --porcelain --untracked-files=no" with
            | Some "" -> sha
            | _ -> sha + "-dirty"

/// <summary>
/// The package version of the assembly defining <c>t</c>, without source-revision metadata.
/// </summary>
let private packageVersion (t: Type) =
    match t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
    | null -> string (t.Assembly.GetName().Version)
    | a -> a.InformationalVersion.Split('+')[0]

/// <summary>
/// Package versions of the Fable build in <c>output</c>, read from its <c>fable_modules</c> directory names.
/// </summary>
let private fableVersions (output: string) =
    let modules =
        Path.Combine (
            NodeRuns.script output "plain"
            |> Path.GetDirectoryName,
            "fable_modules"
        )

    if Directory.Exists modules then
        Directory.GetDirectories modules
        |> Array.choose (fun dir ->
            let m =
                Text.RegularExpressions.Regex.Match (Path.GetFileName dir, @"^(.+?)\.(\d.*)$")

            if m.Success then
                Some (m.Groups[1].Value, m.Groups[2].Value)
            else
                None)
    else
        [||]

/// <summary>
/// This executable, as a host and the arguments preceding the worker's own.
/// </summary>
let private selfWorker () =
    let host = Environment.ProcessPath

    if Path.GetFileNameWithoutExtension host = "dotnet" then
        host, [ Assembly.GetEntryAssembly().Location ]
    else
        host, []

/// <summary>
/// The worker at <c>path</c>: a <c>.dll</c> runs under <c>dotnet</c>, anything else runs
/// directly.
/// </summary>
let private workerAt (path: string) =
    if not (File.Exists path) then
        failwith $"No counter worker at %s{path}."

    if Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) then
        "dotnet", [ path ]
    else
        path, []

/// <summary>
/// Starts the worker in worker mode and waits for its result.
/// </summary>
let private runWorker (host: string, prefix: string list) (scale: int) (resultFile: string) =
    let info = ProcessStartInfo host

    for arg in
        prefix
        @ [ "--worker"; resultFile; "--scale"; string scale ] do
        info.ArgumentList.Add arg

    info.UseShellExecute <- false

    for name, value in workerEnvironment do
        info.Environment[name] <- value

    use p = Process.Start info
    p.WaitForExit ()

    if p.ExitCode <> 0 then
        failwith $"The worker exited with %d{p.ExitCode}."

    JsonSerializer.Deserialize<WorkerRun>(File.ReadAllText resultFile)

let private measure (options: Options) (index: int) =
    let directory =
        Path.Combine (Path.GetTempPath (), $"ranvier-counters-%d{Environment.ProcessId}-%d{index}")

    Directory.CreateDirectory directory |> ignore

    try
        let resultFile = Path.Combine (directory, "worker.json")

        let counters =
            options.CountersWorker
            |> Option.map (fun path ->
                let run =
                    runWorker (workerAt path) options.Scale (Path.Combine (directory, "counters.json"))

                if not run.CountersCompiled then
                    failwith $"The counter worker at %s{path} was built without RanvierCounters."

                run)

        if options.Pmc then
            let collector = Pmc.start directory options.Sources

            let worker =
                try
                    runWorker (selfWorker ()) options.Scale resultFile
                finally
                    collector.Stop ()

            {
                Report.Worker = worker
                Report.Pmc = Some (Pmc.analyse collector worker.ProcessId)
                Report.Counters = counters
            }
        else
            {
                Report.Worker = runWorker (selfWorker ()) options.Scale resultFile
                Report.Pmc = None
                Report.Counters = counters
            }
    finally
        try
            Directory.Delete (directory, true)
        with _ ->
            ()

/// <summary>
/// A fresh scratch directory, deleted after <c>body</c> returns.
/// </summary>
let private withScratch (name: string) (body: string -> 'T) =
    let directory =
        Path.Combine (Path.GetTempPath (), $"ranvier-counters-%d{Environment.ProcessId}-%s{name}")

    Directory.CreateDirectory directory |> ignore

    try
        body directory
    finally
        try
            Directory.Delete (directory, true)
        with _ ->
            ()

/// <summary>
/// Brackets a region of the node process with this process's markers.
/// </summary>
let private mark (isBegin: bool) (region: int) =
    if isBegin then
        Markers.Log.Begin region
    else
        Markers.Log.End region

/// <summary>
/// Every Fable measurement: allocations from the <c>plain</c> build, library
/// counters from the <c>counters</c> build, and processor counters from <c>plain</c>
/// runs with the region handshake.
/// </summary>
let private measureNode (options: Options) (sha: string) (output: string) : Report.NodeSection =
    let plain = NodeRuns.script output "plain"
    let counters = NodeRuns.script output "counters"

    if not (File.Exists plain) then
        failwith $"No Fable harness at %s{plain}. counters.ps1 compiles it."

    let runs (script: string) (name: string) =
        Array.init options.Repeat (fun i ->
            withScratch $"%s{name}-%d{i}" (fun dir -> NodeRuns.run options.Node script options.Scale (Path.Combine (dir, "node.json"))))

    let allocation = runs plain "node-plain"

    let counterRuns =
        if File.Exists counters then
            runs counters "node-counters"
        else
            [||]

    let pmc =
        if options.Pmc then
            Array.init options.Repeat (fun i ->
                withScratch $"node-pmc-%d{i}" (fun dir ->
                    let collector = Pmc.start dir options.Sources

                    let run =
                        try
                            NodeRuns.runHandshake options.Node plain options.Scale (Path.Combine (dir, "node.json")) mark
                        finally
                            collector.Stop ()

                    let main, all = Pmc.analyseProcess collector Environment.ProcessId run.ProcessId

                    {
                        NodeRuns.Run = run
                        NodeRuns.MainThread = main
                        NodeRuns.AllThreads = all
                    }))
        elif options.PmcDryRun then
            Array.init options.Repeat (fun i ->
                withScratch $"node-dry-%d{i}" (fun dir ->
                    {
                        NodeRuns.Run = NodeRuns.runHandshake options.Node plain options.Scale (Path.Combine (dir, "node.json")) mark
                        NodeRuns.MainThread = [||]
                        NodeRuns.AllThreads = [||]
                    }))
        else
            [||]

    let sources = if options.Pmc then options.Sources else [||]

    let mode =
        if options.Pmc then
            "per-context-switch PMC counters ("
            + String.Join (", ", sources)
            + "); main: the main thread, all: every thread of the process"
        elif options.PmcDryRun then
            $"not collected (--pmc-dry-run: %d{pmc.Length} handshake runs completed without ETW sessions)"
        else
            "not collected (--no-pmc)"

    let node = NodeRuns.version options.Node

    {
        Node = node
        Flags = String.Join (" ", NodeRuns.flags)
        Mode = mode
        Versions = Map [ yield "Ranvier", sha; yield "Node.js", node; yield! fableVersions output ]
        AllocationRuns = allocation
        CounterRuns = counterRuns
        PmcRuns = pmc
        Rows = Report.nodeRows sources allocation counterRuns pmc
    }

let private elevationMessage =
    """Processor counters need an elevated process: ETW kernel sessions and PMC
configuration are restricted to administrators.

Run again from an administrator shell, or pass --no-pmc to measure allocations
and library counters only."""

[<EntryPoint>]
let main argv =
    let options =
        parse
            {
                Worker = None
                CountersWorker = None
                Pmc = true
                Repeat = 1
                Scale = 1
                Sources = Pmc.DefaultSources
                ListSources = false
                Out = None
                Fable = None
                Node = "node"
                PmcDryRun = false
                Reconcile = false
                Help = false
            }
            (List.ofArray argv)

    match options.Worker with
    | Some output ->
        Worker.run options.Scale output
        0
    | None when options.Help ->
        printfn "%s" usage
        0
    | None when options.Reconcile -> if Reconcile.run options.Scale = 0 then 0 else 1
    | None when options.ListSources ->
        for name, id in Pmc.availableSources () do
            printfn "%3d  %s" id name

        0
    | None when options.Pmc && not (Pmc.isElevated ()) ->
        eprintfn "%s" elevationMessage
        2
    | None ->
        let root = repositoryRoot ()
        let sha = commit root
        let runs = Array.init options.Repeat (measure options)
        let sources = if options.Pmc then options.Sources else [||]
        let table = Report.medianRows sources runs
        let calibration = Report.calibrate sources runs

        let header: Report.Header =
            {
                Commit = sha
                Date = DateTime.UtcNow
                Machine =
                    String.Join (
                        ", ",
                        [|
                            Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER"
                            $"%d{Environment.ProcessorCount} logical processors"
                            Runtime.InteropServices.RuntimeInformation.OSDescription
                            $".NET %s{string Environment.Version}"
                        |]
                    )
                Mode =
                    if options.Pmc then
                        "per-context-switch PMC counters ("
                        + String.Join (", ", sources)
                        + ")"
                    else
                        "not collected (--no-pmc)"
                Counters =
                    match options.CountersWorker with
                    | Some _ -> "from a separate RanvierCounters build (--counters-worker)"
                    | None when Worker.countersCompiled -> "from the measured build"
                    | None -> "not collected (built without RanvierCounters)"
                Environment =
                    String.Join (
                        " ",
                        workerEnvironment
                        |> List.map (fun (k, v) -> $"%s{k}=%s{v}")
                    )
                Versions =
                    Map
                        [
                            "Ranvier", sha
                            "FSharp.Data.Adaptive", packageVersion typeof<FSharp.Data.Adaptive.AdaptiveToken>
                            "R3", packageVersion typeof<R3.Unit>
                            ".NET", string Environment.Version
                        ]
                RanvierTrace = Report.ranvierTrace ()
            }

        let node =
            options.Fable
            |> Option.map (measureNode options sha)

        let outDirectory =
            match options.Out, root with
            | Some dir, _ -> dir
            | None, Some root -> Path.Combine (root, "docs", ".ai", "benchmarks", "counters")
            | None, None -> Environment.CurrentDirectory

        Directory.CreateDirectory outDirectory |> ignore
        let stem = Path.Combine (outDirectory, sha + (if options.Pmc then "" else "-nopmc"))

        let chart (suffix: string) (title: string) (versions: Map<string, string>) (panels: Charts.Panel[]) =
            if panels.Length = 0 then
                None
            else
                let file = stem + suffix
                File.WriteAllText (file, Charts.svg title versions panels)
                Some (Path.GetFileName file)

        let dotnetChart =
            chart "-dotnet.svg" $".NET, %s{sha}" header.Versions (Charts.dotnetPanels table)

        let nodeChart =
            node
            |> Option.bind (fun section ->
                chart "-node.svg" $"Fable under Node.js, %s{sha}" section.Versions (Charts.nodePanels sources section.Rows))

        let markdown =
            Report.markdown header sources table calibration
            + (node
               |> Option.map (fun section -> "\r\n" + Report.nodeMarkdown sources section)
               |> Option.defaultValue "")
            + "\r\n"
            + Charts.appendix dotnetChart nodeChart

        printfn "%s" markdown

        let json =
            JsonSerializer.Serialize (
                {|
                    Header = header
                    Sources = sources
                    Rows = table
                    Calibration = calibration
                    Runs = runs
                    Fable = Option.toObj (node |> Option.map box)
                |},
                JsonSerializerOptions (WriteIndented = true)
            )

        File.WriteAllText (stem + ".json", json)
        File.WriteAllText (stem + ".md", markdown)
        eprintfn "Wrote %s.json and %s.md" stem stem
        0
