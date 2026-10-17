/// <summary>Validated per-operation projections and alternating base/head process collection.</summary>
module CounterBench.Ab

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json

let private require condition message =
    if not condition then
        invalidArg "measurement" message

let private nonempty value =
    not (String.IsNullOrWhiteSpace value)

let private finite value =
    require (Double.IsFinite value) "A measurement must be finite."

let private pair (ops: int[]) (regions: int[]) =
    require (ops.Length = 2) "Expected exactly two measurements."
    require (ops[0] > 0 && int64 ops[1] = 2L * int64 ops[0]) "Expected positive N followed by 2N."

    require
        (regions[0] >= 0
         && regions[1] >= 0
         && regions[0] <> regions[1])
        "Expected distinct nonnegative regions."

    ops[0]

let private unique what (values: 'T seq) =
    let seen = HashSet<'T>()

    for value in values do
        require (seen.Add value) $"Repeated {what}."

let private identities cases =
    let cases = Array.ofSeq cases

    for scenario, engine in cases do
        require (nonempty scenario && nonempty engine) "Scenario and engine must be nonempty."

    unique "scenario and engine" cases

let private regions values =
    unique "region" values

let private perOp small large n =
    let value = (large - small) / float n
    finite value
    value

let private metrics n pairs =
    pairs
    |> List.choose (fun (name, small, large, optional) ->
        finite small
        finite large

        if optional then
            require
                ((small = -1. && large = -1.)
                 || (small >= 0. && large >= 0.))
                "Invalid or inconsistent unavailable counter sentinel."

        if optional && small = -1. then
            None
        else
            Some
                {
                    Metric.Name = name
                    Value = perOp small large n
                })
    |> List.toArray

let private library n (a: (string * float)[]) (b: (string * float)[]) =
    for counters in [ a; b ] do
        unique "counter name" (counters |> Seq.map fst)

        for name, value in counters do
            require (nonempty name && value >= 0.) "Invalid library counter."
            finite value

    require (Set.ofArray (Array.map fst a) = Set.ofArray (Array.map fst b)) "Library counter names differ between N and 2N."
    require (a.Length > 0) "Instrumented run has no library counters."
    let small = Map.ofArray a

    b
    |> Array.map (fun (name, value) ->
        {
            Metric.Name = "lib:" + name
            Value = perOp small[name] value n
        })

let private validateDotnet (run: WorkerRun) =
    require
        (not (isNull (box run))
         && not (isNull run.Cases)
         && run.Cases.Length > 0)
        "Worker selected no scenarios."

    require (run.ProcessId > 0 && run.ThreadId > 0) "Invalid worker process or thread."

    identities (
        run.Cases
        |> Seq.map (fun c -> c.Scenario, c.Engine)
    )

    regions (
        run.Cases
        |> Seq.collect (fun c -> c.Measurements |> Seq.map _.Region)
    )

    for case in run.Cases do
        pair (case.Measurements |> Array.map _.Ops) (case.Measurements |> Array.map _.Region)
        |> ignore

        for m in case.Measurements do
            require
                (m.Bytes >= 0L
                 && m.Gen0Collections >= 0
                 && not (m.NoGcRegion && m.Gen0Collections > 0))
                "Invalid GC state."

let private validateNode (run: NodeRuns.NodeRun) =
    require
        (not (isNull (box run))
         && not (isNull run.Cases)
         && run.Cases.Length > 0)
        "Node selected no scenarios."

    require (run.ProcessId > 0) "Invalid Node process."

    identities (
        run.Cases
        |> Seq.map (fun c -> c.Scenario, c.Engine)
    )

    regions (
        run.Cases
        |> Seq.collect (fun c -> c.Measurements |> Seq.map _.Region)
    )

    for case in run.Cases do
        pair (case.Measurements |> Array.map _.Ops) (case.Measurements |> Array.map _.Region)
        |> ignore

        for m in case.Measurements do
            finite m.Bytes
            finite m.HeapUsedBytes
            finite m.CodeBytes

            require
                (m.Gen0Collections >= 0
                 && not (m.NoGcRegion && m.Gen0Collections > 0))
                "Invalid Node allocation or GC state."

let private processFigures commit runtime index scenarios =
    require (nonempty commit && index >= 0) "Invalid commit or process index."

    {
        Side = ""
        Commit = commit
        Runtime = runtime
        Process = index
        Scenarios = scenarios
    }

/// <summary>Projects validated N/2N worker regions; unavailable native metrics are omitted. The caller assigns <c>Side</c>.</summary>
let dotnetFigures commit index (run: WorkerRun) =
    validateDotnet run
    require (not run.CountersCompiled) "Timing worker must be built without RanvierCounters."

    run.Cases
    |> Array.map (fun case ->
        let a, b = case.Measurements[0], case.Measurements[1]

        {
            Scenario = case.Scenario
            Engine = case.Engine
            Gc = a.Gen0Collections > 0 || b.Gen0Collections > 0
            Compiled = false
            Metrics =
                metrics
                    a.Ops
                    [
                        "instructions", float a.Instructions, float b.Instructions, true
                        "cycles", float a.Cycles, float b.Cycles, true
                        "branch_misses", float a.BranchMisses, float b.BranchMisses, true
                        "cpu_ns", float a.CpuNs, float b.CpuNs, true
                        "bytes", float a.Bytes, float b.Bytes, false
                    ]
        })
    |> processFigures commit "dotnet" index

/// <summary>Projects instrumented worker counters, rejecting missing or inconsistent counter names.</summary>
let dotnetCounterFigures commit (run: WorkerRun) =
    validateDotnet run
    require run.CountersCompiled "Worker was built without RanvierCounters."

    run.Cases
    |> Array.map (fun case ->
        let a, b = case.Measurements[0], case.Measurements[1]

        {
            Scenario = case.Scenario
            Engine = case.Engine
            Gc = a.Gen0Collections > 0 || b.Gen0Collections > 0
            Compiled = false
            Metrics =
                library
                    a.Ops
                    (a.Counters
                     |> Array.map (fun c -> c.Name, float c.Value))
                    (b.Counters
                     |> Array.map (fun c -> c.Name, float c.Value))
        })
    |> processFigures commit "dotnet" 0

/// <summary>Projects Node measurements using exactly the regions recorded by its handshake; missing regions are errors.</summary>
let nodeFigures commit index (run: NodeRuns.NodeRun) (recorded: IReadOnlyDictionary<int, LinuxPmc.Counts>) =
    validateNode run
    require (not run.CountersCompiled) "Timing Node harness must be built without RanvierCounters."

    let expected =
        run.Cases
        |> Seq.collect (fun c -> c.Measurements |> Seq.map _.Region)
        |> Set.ofSeq

    require (expected = Set.ofSeq recorded.Keys) "Node recorded regions differ from its measurements."

    run.Cases
    |> Array.map (fun case ->
        let a, b = case.Measurements[0], case.Measurements[1]
        let ca, cb = recorded[a.Region], recorded[b.Region]

        {
            Scenario = case.Scenario
            Engine = case.Engine
            Gc = a.Gen0Collections > 0 || b.Gen0Collections > 0
            Compiled = NodeRuns.compiled a || NodeRuns.compiled b
            Metrics =
                metrics
                    a.Ops
                    [
                        "instructions", float ca.Instructions, float cb.Instructions, true
                        "cycles", float ca.Cycles, float cb.Cycles, true
                        "branch_misses", float ca.BranchMisses, float cb.BranchMisses, true
                        "cpu_ns", float ca.CpuNs, float cb.CpuNs, true
                        "bytes", a.Bytes, b.Bytes, false
                    ]
        })
    |> processFigures commit "node" index

/// <summary>Projects instrumented Node counters, rejecting missing or inconsistent counter names.</summary>
let nodeCounterFigures commit (run: NodeRuns.NodeRun) =
    validateNode run
    require run.CountersCompiled "Node harness was built without RanvierCounters."

    run.Cases
    |> Array.map (fun case ->
        let a, b = case.Measurements[0], case.Measurements[1]

        {
            Scenario = case.Scenario
            Engine = case.Engine
            Gc = a.Gen0Collections > 0 || b.Gen0Collections > 0
            Compiled = NodeRuns.compiled a || NodeRuns.compiled b
            Metrics = library a.Ops (a.Counters |> Array.map (fun c -> c.Name, c.Value)) (b.Counters |> Array.map (fun c -> c.Name, c.Value))
        })
    |> processFigures commit "node" 0

type private Side =
    {
        Name: string
        Commit: string
        Directory: string
    }

type private Options =
    {
        Leg: string
        Base: Side option
        Head: Side option
        Rounds: int
        Scale: int
        Only: string option
        Node: string
        Out: string
    }

let private positive (text: string) =
    match Int32.TryParse text with
    | true, value when value > 0 -> value
    | _ -> invalidArg "argument" "Expected a positive integer."

let rec private parse (o: Options) args =
    match args with
    | [] -> o
    | "--leg" :: v :: rest -> parse { o with Leg = v } rest
    | "--base" :: sha :: dir :: rest ->
        parse
            { o with
                Base =
                    Some
                        {
                            Name = "base"
                            Commit = sha
                            Directory = Path.GetFullPath dir
                        }
            }
            rest
    | "--head" :: sha :: dir :: rest ->
        parse
            { o with
                Head =
                    Some
                        {
                            Name = "head"
                            Commit = sha
                            Directory = Path.GetFullPath dir
                        }
            }
            rest
    | "--rounds" :: v :: rest -> parse { o with Rounds = positive v } rest
    | "--scale" :: v :: rest -> parse { o with Scale = positive v } rest
    | "--only" :: v :: rest when nonempty v -> parse { o with Only = Some v } rest
    | "--node" :: v :: rest when nonempty v -> parse { o with Node = v } rest
    | "--out" :: v :: rest when nonempty v -> parse { o with Out = Path.GetFullPath v } rest
    | unknown :: _ -> failwith $"Invalid ab argument '{unknown}'."

let private scratch body =
    let directory =
        Path.Combine (Path.GetTempPath (), "ranvier-ab-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory directory |> ignore

    try
        body directory
    finally
        Directory.Delete (directory, true)

let private command executable args =
    let info =
        ProcessStartInfo (executable, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)

    for arg in args do
        info.ArgumentList.Add arg

    use p = Process.Start info

    try
        let error = p.StandardError.ReadToEndAsync ()
        let output = p.StandardOutput.ReadToEnd ()
        p.WaitForExit ()

        if p.ExitCode <> 0 then
            failwith $"{executable} exited with {p.ExitCode}: {error.Result}"

        require (nonempty output) $"{executable} returned empty metadata."
        output.Trim ()
    finally
        if not p.HasExited then
            p.Kill true
            p.WaitForExit ()

let private machine () =
    if OperatingSystem.IsLinux () then
        let text = command "lscpu" [ "--json" ]
        use json = JsonDocument.Parse text

        json.RootElement.GetProperty("lscpu").EnumerateArray()
        |> Seq.tryPick (fun row ->
            if row.GetProperty("field").GetString() = "Model name:" then
                Some (row.GetProperty("data").GetString())
            else
                None)
        |> Option.filter nonempty
        |> Option.defaultValue "unavailable: CPU model absent from lscpu"
    elif OperatingSystem.IsWindows () then
        Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER"
        |> fun value ->
            if nonempty value then
                value
            else
                "unavailable: PROCESSOR_IDENTIFIER missing"
    else
        "unavailable: CPU model unsupported on this OS"

let private versions (side: Side) =
    let file = Path.Combine (side.Directory, "versions.json")
    let fields = [ "Dotnet", ".NET"; "Fable", "Fable"; "FableLibrary", "fable-library" ]

    if not (File.Exists file) then
        fields
        |> List.map (fun (_, name) ->
            {
                Label.Name = side.Name + ":" + name
                Value = "unavailable: versions.json missing"
            })
    else
        use json = JsonDocument.Parse (File.ReadAllText file)
        require (json.RootElement.ValueKind = JsonValueKind.Object) "versions.json must be an object."

        fields
        |> List.map (fun (field, name) ->
            let mutable value = Unchecked.defaultof<JsonElement>

            let text =
                if
                    json.RootElement.TryGetProperty (field, &value)
                    && value.ValueKind = JsonValueKind.String
                    && nonempty (value.GetString ())
                then
                    value.GetString ()
                else
                    $"unavailable: versions.json {field} missing"

            {
                Label.Name = side.Name + ":" + name
                Value = text
            })

let private matching (timing: ProcessFigures) (instrumented: ProcessFigures) =
    let keys f =
        f.Scenarios
        |> Array.map (fun c -> c.Scenario, c.Engine)
        |> Set.ofArray

    require (keys timing = keys instrumented) "Instrumented scenarios differ from timing scenarios."

let private hardware leg (values: seq<int64 * int64 * int64>) =
    if leg = "arm64-counters" then
        for instructions, cycles, misses in values do
            require (instructions > 0L && cycles > 0L && misses >= 0L) "arm64 requires measured hardware counters in every region."

/// <summary>
/// Collects Node then .NET in alternating base/head order and writes an <c>AbRun</c> only after every head run succeeds.
/// A failing timing baseline is explicitly marked unavailable; missing base instrumentation preserves valid timing.
/// </summary>
let main args =
    let o =
        parse
            {
                Leg = ""
                Base = None
                Head = None
                Rounds = 5
                Scale = 1
                Only = None
                Node = "node"
                Out = ""
            }
            args

    require (o.Leg = "arm64-counters" || o.Leg = "x64-timing") "Expected --leg arm64-counters or x64-timing."
    require (nonempty o.Out) "ab needs --out."

    let baseSide =
        o.Base
        |> Option.defaultWith (fun () -> failwith "ab needs --base <sha> <dir>.")

    let head =
        o.Head
        |> Option.defaultWith (fun () -> failwith "ab needs --head <sha> <dir>.")

    require (nonempty baseSide.Commit && nonempty head.Commit) "Commit identifiers must be nonempty."
    let processes = ResizeArray<ProcessFigures>()
    let counters = ResizeArray<ProcessFigures>()
    let unavailable = ResizeArray<Label>()
    let labels = ResizeArray<Label>(List.append (versions baseSide) (versions head))

    labels.Add
        {
            Name = "Node.js"
            Value = command o.Node [ "--version" ]
        }

    let cpu = machine ()

    // One Node process per scenario; a harness without --list runs every scenario in one process.
    let nodeRegions (script: string) (dir: string) =
        let measure (run: (int -> bool -> int -> unit) -> NodeRuns.NodeRun) =
            use recorder = new LinuxPmc.RegionRecorder ()
            let result = run recorder.Mark
            result, (Dictionary recorder.Regions :> IReadOnlyDictionary<_, _>)

        match NodeRuns.scenarios o.Node script with
        | Some names ->
            names
            |> Array.filter (fun name ->
                o.Only
                |> Option.forall (fun prefix -> name.StartsWith (prefix, StringComparison.Ordinal)))
            |> Array.mapi (fun i name ->
                let run, regions =
                    measure (NodeRuns.runScenario o.Node script o.Scale name (Path.Combine (dir, $"node-%d{i}.json")))

                name, run, regions)
            |> NodeRuns.merge
        | None -> measure (NodeRuns.runHandshake o.Node script o.Scale o.Only (Path.Combine (dir, "node.json")))

    let timing runtime side index =
        scratch (fun dir ->
            let output = Path.Combine (dir, "run.json")

            let figure =
                if runtime = "dotnet" then
                    let run =
                        Workers.runWorker (Workers.workerAt (Path.Combine (side.Directory, "worker", "Ranvier.Counters.dll"))) o.Scale o.Only output

                    hardware
                        o.Leg
                        (run.Cases
                         |> Seq.collect (fun c ->
                             c.Measurements
                             |> Seq.map (fun m -> m.Instructions, m.Cycles, m.BranchMisses)))

                    dotnetFigures side.Commit index run
                else
                    let run, regions =
                        nodeRegions (Path.Combine (side.Directory, "fable", "plain", "Main.fs.js")) dir

                    hardware
                        o.Leg
                        (regions.Values
                         |> Seq.map (fun m -> m.Instructions, m.Cycles, m.BranchMisses))

                    nodeFigures side.Commit index run regions

            { figure with Side = side.Name })

    let instrumented runtime side =
        scratch (fun dir ->
            let output = Path.Combine (dir, "run.json")

            let figure =
                if runtime = "dotnet" then
                    Workers.runWorker
                        (Workers.workerAt (Path.Combine (side.Directory, "counters-worker", "Ranvier.Counters.dll")))
                        o.Scale
                        o.Only
                        output
                    |> dotnetCounterFigures side.Commit
                else
                    NodeRuns.run o.Node (Path.Combine (side.Directory, "fable", "counters", "Main.fs.js")) o.Scale o.Only output
                    |> nodeCounterFigures side.Commit

            { figure with Side = side.Name })

    for runtime in [ "node"; "dotnet" ] do
        let mutable baseAvailable = true

        for index in 0 .. o.Rounds - 1 do
            for side in [ baseSide; head ] do
                if side.Name = "head" || baseAvailable then
                    try
                        let figure = timing runtime side index

                        processes
                        |> Seq.tryFind (fun f -> f.Runtime = runtime && f.Side = side.Name)
                        |> Option.iter (fun first -> matching first figure)

                        processes.Add figure
                    with e when side.Name = "base" ->
                        baseAvailable <- false

                        processes.RemoveAll (fun f -> f.Runtime = runtime && f.Side = "base")
                        |> ignore

                        unavailable.Add { Name = runtime; Value = e.Message }
                        eprintfn "Base %s could not be measured: %s" runtime e.Message

        for side in [ baseSide; head ] do
            if side.Name = "head" || baseAvailable then
                try
                    let figure = instrumented runtime side

                    let first =
                        processes
                        |> Seq.find (fun f -> f.Runtime = runtime && f.Side = side.Name)

                    matching first figure
                    counters.Add figure
                with e when side.Name = "base" ->
                    labels.Add
                        {
                            Name = "base:" + runtime + ":counters"
                            Value = "unavailable: " + e.Message
                        }

                    eprintfn "Base %s instrumentation unavailable: %s" runtime e.Message

    let run =
        {
            Leg = o.Leg
            Machine = cpu
            Base = baseSide.Commit
            Head = head.Commit
            Rounds = o.Rounds
            Versions = labels.ToArray ()
            NoBaseline = unavailable.ToArray ()
            Processes = processes.ToArray ()
            Counters = counters.ToArray ()
        }

    let parent = Path.GetDirectoryName o.Out
    Directory.CreateDirectory parent |> ignore
    File.WriteAllText (o.Out, JsonSerializer.Serialize (run, JsonSerializerOptions (WriteIndented = true)))
    0
