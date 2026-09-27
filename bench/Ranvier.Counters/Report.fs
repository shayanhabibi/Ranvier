/// <summary>
/// Per-operation figures, the calibration verdict, and their Markdown and JSON
/// forms.
/// </summary>
module CounterBench.Report

open System
open System.Globalization
open System.Text

/// <summary>
/// One worker run, with the counter totals per region when PMC collection ran.
/// </summary>
type Run =
    {
        Worker: WorkerRun
        Pmc: Pmc.RegionCounts[] option
        /// <summary>
        /// A run of a <c>RanvierCounters</c> build, the source of the library
        /// counters when <c>Worker</c> ran a build without them.
        /// </summary>
        Counters: WorkerRun option
    }

/// <summary>
/// The run holding the library counters: <c>Counters</c> when present, otherwise
/// <c>Worker</c>.
/// </summary>
let counterRun (run: Run) =
    run.Counters |> Option.defaultValue run.Worker

/// <summary>
/// The case of <c>counters</c> matching <c>case</c> by scenario and engine.
/// </summary>
let private counterCase (counters: WorkerRun) (index: int) (case: CaseRun) =
    let other = counters.Cases[index]

    if other.Scenario <> case.Scenario || other.Engine <> case.Engine then
        failwith
            $"The counter worker ran %s{other.Scenario}/%s{other.Engine} where the measured worker ran %s{case.Scenario}/%s{case.Engine}."

    other

/// <summary>
/// The median and range of one figure across runs.
/// </summary>
type Stat = { Median: float; Min: float; Max: float }

let private stat (values: float[]) =
    let sorted = Array.sort values
    let middle = sorted.Length / 2

    {
        Median =
            if sorted.Length % 2 = 1 then
                sorted[middle]
            else
                (sorted[middle - 1] + sorted[middle]) / 2.0
        Min = sorted[0]
        Max = sorted[sorted.Length - 1]
    }

type PerOp = { Name: string; Value: float }

/// <summary>
/// A case's figures per operation: (m(2N) - m(N)) / N for every measure m.
/// </summary>
type Row =
    {
        Scenario: string
        Engine: string
        Unit: string
        N: int
        /// <summary>
        /// Processor counters in the configured source order. Empty without
        /// PMC collection.
        /// </summary>
        Pmc: PerOp[]
        /// <summary>
        /// Run intervals of the measured thread at N, then at 2N. Empty without
        /// PMC collection.
        /// </summary>
        Intervals: int[]
        BytesPerOp: float
        /// <summary>
        /// Signals, memos, effects and owners created. Present for
        /// Ranvier built with <c>RanvierCounters</c>.
        /// </summary>
        ObjectsPerOp: float option
        /// <summary>
        /// Every nonzero library counter.
        /// </summary>
        Counters: PerOp[]
        /// <summary>
        /// False when a collection ran inside either region.
        /// </summary>
        GcFree: bool
    }

/// <summary>True for Ranvier and its variants, such as <c>Ranvier (createEffect)</c>.</summary>
let private isRanvier (engine: string) =
    engine = "Ranvier" || engine.StartsWith "Ranvier ("

let private createdCounters =
    set [ "SignalsCreated"; "MemosCreated"; "EffectsCreated"; "OwnersCreated" ]

let private perOp (small: int64) (large: int64) (n: int) = float (large - small) / float n

let rows (sources: string[]) (run: Run) : Row[] =
    let regions =
        match run.Pmc with
        | Some counts -> counts |> Array.map (fun c -> c.Region, c) |> dict
        | None -> dict []

    let counterSource = counterRun run

    run.Worker.Cases
    |> Array.mapi (fun index case ->
        let small = case.Measurements[0]
        let large = case.Measurements[1]
        let n = large.Ops - small.Ops

        let pmc, intervals =
            match regions.TryGetValue small.Region, regions.TryGetValue large.Region with
            | (true, a), (true, b) ->
                sources
                |> Array.mapi (fun k name ->
                    {
                        Name = name
                        Value = perOp (int64 a.Values[k]) (int64 b.Values[k]) n
                    }),
                [| a.Intervals; b.Intervals |]
            | _ -> [||], [||]

        let counters =
            if counterSource.CountersCompiled && isRanvier case.Engine then
                let source = counterCase counterSource index case

                Array.map2
                    (fun (a: CounterValue) (b: CounterValue) ->
                        {
                            Name = a.Name
                            Value = perOp a.Value b.Value n
                        })
                    source.Measurements[0].Counters
                    source.Measurements[1].Counters
            else
                [||]

        {
            Scenario = case.Scenario
            Engine = case.Engine
            Unit = case.Unit
            N = n
            Pmc = pmc
            Intervals = intervals
            BytesPerOp = perOp small.Bytes large.Bytes n
            ObjectsPerOp =
                if counters.Length = 0 then
                    None
                else
                    counters
                    |> Array.filter (fun c -> createdCounters.Contains c.Name)
                    |> Array.sumBy (fun c -> c.Value)
                    |> Some
            Counters = counters |> Array.filter (fun c -> c.Value <> 0.0)
            GcFree = small.NoGcRegion && large.NoGcRegion
        })

/// <summary>
/// The rows of <c>runs</c>, each processor counter the median over runs. The other
/// columns are those of the first run.
/// </summary>
let medianRows (sources: string[]) (runs: Run[]) : Row[] =
    let perRun = runs |> Array.map (rows sources)

    perRun[0]
    |> Array.mapi (fun i row ->
        { row with
            Pmc =
                row.Pmc
                |> Array.mapi (fun k p ->
                    { p with
                        Value = (perRun |> Array.map (fun r -> r[i].Pmc[k].Value) |> stat).Median
                    })
        })

/// <summary>
/// A measure that differed between runs of the same case.
/// </summary>
type Divergence =
    {
        Scenario: string
        Engine: string
        Measure: string
        Values: int64[]
    }

/// <summary>
/// The spread of one processor counter's per-operation figure across runs.
/// </summary>
type Spread =
    {
        Scenario: string
        Engine: string
        Source: string
        Median: float
        Min: float
        Max: float
    }

type Calibration =
    {
        Runs: int
        /// <summary>
        /// Every allocation and library counter reading that differed between
        /// runs.
        /// </summary>
        Divergences: Divergence[]
        Spreads: Spread[]
    }

let calibrate (sources: string[]) (runs: Run[]) : Calibration =
    let first = runs[0].Worker

    let divergences =
        [|
            for caseIndex in 0 .. first.Cases.Length - 1 do
                let case = first.Cases[caseIndex]

                for m in 0 .. case.Measurements.Length - 1 do
                    let measurements = runs |> Array.map (fun r -> r.Worker.Cases[caseIndex].Measurements[m])

                    let counterMeasurements =
                        runs |> Array.map (fun r -> (counterRun r).Cases[caseIndex].Measurements[m])

                    let label = if m = 0 then "N" else "2N"
                    let bytes = measurements |> Array.map (fun x -> x.Bytes)

                    if Array.distinct(bytes).Length > 1 then
                        yield
                            {
                                Scenario = case.Scenario
                                Engine = case.Engine
                                Measure = $"bytes at %s{label}"
                                Values = bytes
                            }

                    for c in 0 .. counterMeasurements[0].Counters.Length - 1 do
                        let values = counterMeasurements |> Array.map (fun x -> x.Counters[c].Value)

                        if Array.distinct(values).Length > 1 then
                            yield
                                {
                                    Scenario = case.Scenario
                                    Engine = case.Engine
                                    Measure = $"%s{counterMeasurements[0].Counters[c].Name} at %s{label}"
                                    Values = values
                                }
        |]

    let perRun = runs |> Array.map (rows sources)

    let spreads =
        [|
            for i in 0 .. perRun[0].Length - 1 do
                let row = perRun[0][i]

                for k in 0 .. row.Pmc.Length - 1 do
                    let values = perRun |> Array.map (fun r -> r[i].Pmc[k].Value) |> stat

                    yield
                        {
                            Scenario = row.Scenario
                            Engine = row.Engine
                            Source = row.Pmc[k].Name
                            Median = values.Median
                            Min = values.Min
                            Max = values.Max
                        }
        |]

    {
        Runs = runs.Length
        Divergences = divergences
        Spreads = spreads
    }

let private invariant = CultureInfo.InvariantCulture

let format (value: float) =
    if Math.Abs (value - Math.Round value) < 1e-9 then
        value.ToString ("N0", invariant)
    else
        value.ToString ("N2", invariant)

/// <summary>
/// The engine followed by its version from <c>versions</c>, when present. A variant such as
/// <c>Ranvier (createEffect)</c> takes the version of the engine before the parenthesis.
/// </summary>
let engineLabel (versions: Map<string, string>) (engine: string) =
    let name, variant =
        match engine.IndexOf " (" with
        | -1 -> engine, ""
        | i -> engine.Substring (0, i), engine.Substring i

    match versions.TryFind name with
    | Some version -> $"%s{name} %s{version}%s{variant}"
    | None -> engine

let private versionLine (versions: Map<string, string>) =
    String.Join (", ", versions |> Seq.map (fun (KeyValue (name, version)) -> $"%s{name} %s{version}"))

let private shortName (source: string) =
    match source with
    | "InstructionRetired" -> "instr/op"
    | "TotalCycles" -> "cycles/op"
    | "BranchMispredictions" -> "br-miss/op"
    | other -> $"%s{other}/op"

type Header =
    {
        Commit: string
        Date: DateTime
        Machine: string
        Mode: string
        /// <summary>
        /// Where the library counter columns come from.
        /// </summary>
        Counters: string
        Environment: string
        /// <summary>
        /// Engine and runtime versions, by name.
        /// </summary>
        Versions: Map<string, string>
    }

let markdown (header: Header) (sources: string[]) (table: Row[]) (calibration: Calibration) =
    let text = StringBuilder ()
    let line (s: string) = text.Append(s).Append("\r\n") |> ignore

    line $"# Counter bench, %s{header.Commit}"
    line ""
    line ("- Date: " + header.Date.ToString ("u", invariant))
    line $"- Machine: %s{header.Machine}"
    line $"- Instructions: %s{header.Mode}"
    line $"- Library counters: %s{header.Counters}"
    line $"- Versions: %s{versionLine header.Versions}"
    line $"- Worker environment: %s{header.Environment}"
    line "- Every figure is (m(2N) - m(N)) / N. Processor counters are the median over runs."
    line "- Instruction counts differing by under 5 % are within run-to-run noise. Compare only reports taken at the same --scale."
    line ""
    line "## Engine differences"
    line ""
    line "- R3 pushes each write straight to its subscribers, without batching or glitch-free ordering. Its chain is `Select` operators holding no cached value."
    line "- FSharp.Data.Adaptive dispose removes the callback subscriptions only. The `AVal.map2` nodes stay in the weak output sets of their `cval`s until collected."
    line ""

    let hasPmc = table |> Array.exists (fun r -> r.Pmc.Length > 0)

    let pmcColumns =
        if hasPmc then
            [| yield! sources |> Array.map shortName; "intervals N/2N" |]
        else
            [| "instr/op" |]

    for scenario, group in table |> Array.groupBy (fun r -> r.Scenario) do
        let first = group[0]
        line $"## %s{scenario}: %s{first.Unit} (N = %d{first.N})"
        line ""
        line ("| Engine | " + String.Join (" | ", pmcColumns) + " | bytes/op | objects/op | counters/op |")
        line ("| --- |" + String.replicate pmcColumns.Length " ---: |" + " ---: | ---: | --- |")

        for row in group do
            let pmc =
                if row.Pmc.Length = 0 then
                    pmcColumns |> Array.map (fun _ -> "n/a")
                else
                    [|
                        yield! row.Pmc |> Array.map (fun p -> format p.Value)
                        String.Join ("/", row.Intervals)
                    |]

            let objects =
                match row.ObjectsPerOp with
                | Some v -> format v
                | None -> "n/a"

            let counters =
                if row.ObjectsPerOp.IsNone then
                    "n/a"
                elif row.Counters.Length = 0 then
                    "0"
                else
                    String.Join (", ", row.Counters |> Array.map (fun c -> c.Name + " " + format c.Value))

            let gc = if row.GcFree then "" else " (GC in region)"

            line
                ("| " + String.Join (" | ", [| engineLabel header.Versions row.Engine; yield! pmc; format row.BytesPerOp + gc; objects; counters |]) + " |")

        line ""

    line $"## Calibration, %d{calibration.Runs} run(s)"
    line ""

    if calibration.Runs < 2 then
        line "One run: nothing to compare."
    elif calibration.Divergences.Length = 0 then
        line "Allocations and library counters identical in every run."
    else
        line "| Scenario | Engine | Measure | Values |"
        line "| --- | --- | --- | --- |"

        for d in calibration.Divergences do
            let values = String.Join (", ", d.Values)
            line $"| %s{d.Scenario} | %s{d.Engine} | %s{d.Measure} | %s{values} |"

    if calibration.Spreads.Length > 0 && calibration.Runs > 1 then
        line ""
        line "| Scenario | Engine | Source | Median/op | Min/op | Max/op | Spread |"
        line "| --- | --- | --- | ---: | ---: | ---: | ---: |"

        for s in calibration.Spreads do
            let spread =
                if s.Min = 0.0 then
                    "n/a"
                else
                    ((s.Max - s.Min) / s.Min).ToString ("P2", invariant)

            line $"| %s{s.Scenario} | %s{s.Engine} | %s{s.Source} | %s{format s.Median} | %s{format s.Min} | %s{format s.Max} | %s{spread} |"

    text.ToString ()

/// <summary>
/// One processor counter's per-operation figure under Node.js.
/// </summary>
type NodePmc =
    {
        Name: string
        MainThread: Stat
        AllThreads: Stat
    }

/// <summary>
/// A Fable case's figures per operation, each (m(2N) - m(N)) / N, summarised
/// over the runs.
/// </summary>
type NodeRow =
    {
        Scenario: string
        Engine: string
        Unit: string
        N: int
        /// <summary>
        /// Empty without PMC collection.
        /// </summary>
        Pmc: NodePmc[]
        Bytes: Stat
        HeapUsedBytes: Stat
        /// <summary>
        /// False when a collection ran inside any region of any run.
        /// </summary>
        GcFree: bool
        /// <summary>
        /// Signals, memos, effects and owners created. Present for
        /// Ranvier when a <c>counters</c> build ran.
        /// </summary>
        ObjectsPerOp: float option
        /// <summary>
        /// Every nonzero library counter, from the first <c>counters</c> run.
        /// </summary>
        Counters: PerOp[]
        /// <summary>
        /// True when every <c>counters</c> run read the same library counters.
        /// </summary>
        CountersIdentical: bool
    }

type NodeSection =
    {
        Node: string
        Flags: string
        /// <summary>
        /// What the instruction columns hold.
        /// </summary>
        Mode: string
        /// <summary>
        /// Engine and runtime versions, by name.
        /// </summary>
        Versions: Map<string, string>
        AllocationRuns: NodeRuns.NodeRun[]
        CounterRuns: NodeRuns.NodeRun[]
        PmcRuns: NodeRuns.NodePmcRun[]
        Rows: NodeRow[]
    }

let private perOpF (small: float) (large: float) (n: int) = (large - small) / float n

let nodeRows
    (sources: string[])
    (allocation: NodeRuns.NodeRun[])
    (counters: NodeRuns.NodeRun[])
    (pmc: NodeRuns.NodePmcRun[])
    : NodeRow[] =
    allocation[0].Cases
    |> Array.mapi (fun i case ->
        let small = case.Measurements[0]
        let large = case.Measurements[1]
        let n = large.Ops - small.Ops

        let across (read: NodeRuns.NodeMeasurement -> float) =
            allocation
            |> Array.map (fun run -> perOpF (read run.Cases[i].Measurements[0]) (read run.Cases[i].Measurements[1]) n)
            |> stat

        let pmcStats =
            if pmc |> Array.exists (fun r -> r.MainThread.Length > 0) then
                let perRun (regions: Pmc.RegionCounts[]) (k: int) =
                    let byRegion = regions |> Array.map (fun c -> c.Region, c) |> dict
                    let a = byRegion[small.Region].Values[k]
                    let b = byRegion[large.Region].Values[k]
                    perOp (int64 a) (int64 b) n

                sources
                |> Array.mapi (fun k name ->
                    {
                        Name = name
                        MainThread = pmc |> Array.map (fun r -> perRun r.MainThread k) |> stat
                        AllThreads = pmc |> Array.map (fun r -> perRun r.AllThreads k) |> stat
                    })
            else
                [||]

        let counterCase =
            counters
            |> Array.tryHead
            |> Option.filter (fun run -> run.CountersCompiled && isRanvier case.Engine)
            |> Option.map (fun run -> run.Cases[i])

        let perOpCounters =
            match counterCase with
            | Some c ->
                Array.map2
                    (fun (a: NodeRuns.NodeCounter) (b: NodeRuns.NodeCounter) ->
                        {
                            Name = a.Name
                            Value = perOpF a.Value b.Value n
                        })
                    c.Measurements[0].Counters
                    c.Measurements[1].Counters
            | None -> [||]

        let countersOf (run: NodeRuns.NodeRun) =
            run.Cases[i].Measurements
            |> Array.map (fun m -> m.Counters |> Array.map (fun c -> c.Value))

        {
            Scenario = case.Scenario
            Engine = case.Engine
            Unit = case.Unit
            N = n
            Pmc = pmcStats
            Bytes = across (fun m -> m.Bytes)
            HeapUsedBytes = across (fun m -> m.HeapUsedBytes)
            GcFree =
                allocation
                |> Array.forall (fun run -> run.Cases[i].Measurements |> Array.forall (fun m -> m.NoGcRegion))
            ObjectsPerOp =
                if perOpCounters.Length = 0 then
                    None
                else
                    perOpCounters
                    |> Array.filter (fun c -> createdCounters.Contains c.Name)
                    |> Array.sumBy (fun c -> c.Value)
                    |> Some
            Counters = perOpCounters |> Array.filter (fun c -> c.Value <> 0.0)
            CountersIdentical =
                counters.Length < 2
                || counters |> Array.forall (fun run -> countersOf run = countersOf counters[0])
        })

let private formatStat (s: Stat) =
    if s.Min = s.Max then
        format s.Median
    else
        $"%s{format s.Median} (%s{format s.Min}..%s{format s.Max})"

let private range (s: Stat) =
    if s.Min = s.Max then
        "0"
    else
        $"%s{format s.Min}..%s{format s.Max}"

/// <summary>
/// The Fable section of the report.
/// </summary>
let nodeMarkdown (sources: string[]) (section: NodeSection) =
    let text = StringBuilder ()
    let line (s: string) = text.Append(s).Append("\r\n") |> ignore
    let runs = section.AllocationRuns.Length

    line $"# Fable under Node.js %s{section.Node}"
    line ""
    line $"- Versions: %s{versionLine section.Versions}"
    line $"- node flags: %s{section.Flags}"
    line $"- Instructions: %s{section.Mode}"

    line
        $"- bytes/op: growth of the new, old and large-object heap spaces; median of %d{runs} processes. bytes range: minimum..maximum, 0 when every process agreed."

    line "- heapUsed/op: growth of `process.memoryUsage().heapUsed`, which adds the code and trusted spaces."
    line "- Every figure is (m(2N) - m(N)) / N. A figure followed by a bracketed range differed between processes."
    line ""

    let hasPmc = section.Rows |> Array.exists (fun r -> r.Pmc.Length > 0)

    let pmcColumns =
        if hasPmc then
            sources
            |> Array.collect (fun s -> [| shortName s + " main"; shortName s + " all" |])
        else
            [| "instr/op" |]

    for scenario, group in section.Rows |> Array.groupBy (fun r -> r.Scenario) do
        let first = group[0]
        line $"## %s{scenario}: %s{first.Unit} (N = %d{first.N})"
        line ""

        line (
            "| Engine | "
            + String.Join (" | ", pmcColumns)
            + " | bytes/op | bytes range | heapUsed/op | objects/op | counters/op |"
        )

        line ("| --- |" + String.replicate pmcColumns.Length " ---: |" + " ---: | ---: | ---: | ---: | --- |")

        for row in group do
            let pmc =
                if row.Pmc.Length = 0 then
                    pmcColumns |> Array.map (fun _ -> "n/a")
                else
                    row.Pmc
                    |> Array.collect (fun p -> [| formatStat p.MainThread; formatStat p.AllThreads |])

            let objects =
                match row.ObjectsPerOp with
                | Some v -> format v
                | None -> "n/a"

            let counters =
                if row.ObjectsPerOp.IsNone then "n/a"
                elif row.Counters.Length = 0 then "0"
                else String.Join (", ", row.Counters |> Array.map (fun c -> c.Name + " " + format c.Value))

            let counters =
                if row.CountersIdentical then
                    counters
                else
                    counters + " (differed between runs)"

            let gc = if row.GcFree then "" else " (GC in region)"

            line (
                "| "
                + String.Join (
                    " | ",
                    [|
                        engineLabel section.Versions row.Engine
                        yield! pmc
                        format row.Bytes.Median + gc
                        range row.Bytes
                        formatStat row.HeapUsedBytes
                        objects
                        counters
                    |]
                )
                + " |"
            )

        line ""

    text.ToString ()
