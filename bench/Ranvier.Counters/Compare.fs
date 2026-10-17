/// <summary>CI artifact validation, regression verdicts, acceptance metadata, and Markdown reports.</summary>
module CounterBench.Compare

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Globalization

/// <summary>The comparison status of a scenario metric or exact library counter.</summary>
type Kind =
    | Regressed
    | Improved
    | Unchanged
    | Added
    | Removed
    | CounterChanged
    | NoBaseline
    /// <summary>The full samples separate beyond the limit while the lower modes of the two sides lie within it.</summary>
    | StateShift
    /// <summary>A gate regression in a runtime whose control moved between the sides.</summary>
    | Inconclusive

/// <summary>Both sides' process samples and their comparison, including GC, compilation and acceptance flags.</summary>
type Finding =
    {
        Runtime: string
        Scenario: string
        Engine: string
        Metric: string
        Base: float[]
        Head: float[]
        Kind: Kind
        /// <summary>Finite relative median change; absent when either side, a nonzero baseline, or a finite ratio is unavailable.</summary>
        Delta: float option
        Accepted: bool
        BaseGc: bool
        HeadGc: bool
        BaseCompiled: bool
        HeadCompiled: bool
    }

/// <summary>An acceptance reason scoped to exact scenario names or <c>all</c>, with its source.</summary>
type AcceptanceEntry =
    {
        Names: string list
        Reason: string
        Source: string
    }

/// <summary>Effective acceptance settings, scoped reasons, and invalid scenario names.</summary>
type Acceptance =
    {
        All: bool
        Names: string list
        Reasons: string list
        Unknown: string list
        Source: string
        Entries: AcceptanceEntry list
    }

/// <summary>A leg's complete findings, gate result, baseline availability, and advisory warnings.</summary>
type Verdict =
    {
        Leg: string
        Gate: string
        Fails: bool
        Findings: Finding[]
        NoBaseline: Label[]
        CounterUnavailable: Label[]
        /// <summary>Runtimes whose control moved between the sides; their gate regressions are <c>Inconclusive</c>.</summary>
        ControlMoved: string[]
        Warnings: string[]
    }

let private require condition message =
    if not condition then
        invalidArg "figures" message

let private finite name x =
    require (Double.IsFinite x) (name + " must be finite.")

let private nonempty name (s: string) =
    require (not (String.IsNullOrWhiteSpace s)) (name + " is required.")

let private configuration threshold floor =
    finite "threshold" threshold
    finite "floor" floor
    require (threshold >= 0. && floor >= 0.) "Threshold and floor must be nonnegative."

/// <summary>Returns the median of a nonempty finite sample.</summary>
let median (xs: float[]) =
    require (not (isNull xs) && xs.Length > 0) "A sample must be nonempty."
    xs |> Array.iter (finite "sample")
    let s = Array.sort xs
    let n = s.Length

    if n % 2 = 1 then
        s[n / 2]
    else
        s[n / 2 - 1] / 2. + s[n / 2] / 2.

let private separation threshold floor (baseValues: float[]) (headValues: float[]) =
    let b, h = median baseValues, median headValues
    let limit = max (threshold * abs b) floor

    if
        h - b > limit
        && Array.min headValues > Array.max baseValues
    then
        Regressed
    elif
        b - h > limit
        && Array.max headValues < Array.min baseValues
    then
        Improved
    else
        Unchanged

/// <summary>
/// The lowest cluster of a nonempty finite sample: its sorted values up to the first gap between neighbours wider than
/// <c>max(threshold·|minimum|, floor)</c>.
/// </summary>
let lowerMode threshold floor (xs: float[]) =
    configuration threshold floor
    require (not (isNull xs) && xs.Length > 0) "A sample must be nonempty."
    xs |> Array.iter (finite "sample")
    let s = Array.sort xs
    let gap = max (threshold * abs s[0]) floor
    let mutable n = 1

    while n < s.Length && s[n] - s[n - 1] <= gap do
        n <- n + 1

    s[.. n - 1]

/// <summary>
/// Classifies strict median movement beyond <c>max(threshold·|base median|, floor)</c> with complete sample separation.
/// A movement is <c>Regressed</c> or <c>Improved</c> only when the lower modes of the two sides move the same way by the
/// same rule; otherwise it is <c>StateShift</c>.
/// </summary>
let classify threshold floor (baseValues: float[]) (headValues: float[]) =
    configuration threshold floor

    match separation threshold floor baseValues headValues with
    | Unchanged -> Unchanged
    | kind when separation threshold floor (lowerMode threshold floor baseValues) (lowerMode threshold floor headValues) = kind -> kind
    | _ -> StateShift

let private trailer = Regex (@"^Perf-Accepted:\s*(.+?)\s+(?:\u2014|--)\s+(.+?)\s*$")

let private names (value: string) =
    let result =
        value.Split (',')
        |> Array.map _.Trim()
        |> List.ofArray

    require
        (result
         |> List.forall (String.IsNullOrWhiteSpace >> not))
        "Acceptance names must be nonempty."

    result |> List.distinct

/// <summary>Parses explicit acceptance lines; malformed lines raise an argument error.</summary>
let parseTrailers (messages: string seq) =
    [
        for message in messages do
            for line in message.Replace("\r\n", "\n").Split('\n') do
                if line.TrimStart().StartsWith("Perf-Accepted:", StringComparison.OrdinalIgnoreCase) then
                    let m = trailer.Match (line.Trim ())
                    require m.Success "Malformed Perf-Accepted trailer; expected names and a meaningful reason separated by an em dash or --."
                    yield names m.Groups[1].Value, m.Groups[2].Value.Trim()
    ]

let private fromEntries (known: Set<string>) (entries: AcceptanceEntry list) =
    let ns = entries |> List.collect _.Names |> List.distinct

    {
        All = List.contains "all" ns
        Names = ns |> List.filter ((<>) "all")
        Reasons = entries |> List.map _.Reason
        Unknown =
            ns
            |> List.filter (fun n -> n <> "all" && not (Set.contains n known))
        Source =
            entries
            |> List.map _.Source
            |> List.distinct
            |> String.concat "; "
        Entries = entries
    }

/// <summary>Combines label and trailer acceptances while retaining unknown names as configuration errors.</summary>
let acceptance label messages known =
    let entries =
        [
            if label then
                yield
                    {
                        Names = [ "all" ]
                        Reason = "perf-accepted label"
                        Source = "label"
                    }
            for ns, reason in parseTrailers messages do
                yield
                    {
                        Names = ns
                        Reason = reason
                        Source = "Perf-Accepted trailer"
                    }
        ]

    fromEntries known entries

/// <summary>Returns exact scenario names present on either measured side.</summary>
let scenarioNames (run: AbRun) =
    run.Processes
    |> Seq.collect (fun p -> p.Scenarios |> Seq.map _.Scenario)
    |> Set.ofSeq

let private unique name key xs =
    let keys = xs |> Array.map key
    require ((Set.ofArray keys).Count = keys.Length) ("Duplicate " + name + ".")

let private scenarioKey (s: ScenarioFigures) =
    s.Scenario, s.Engine

let private scenarioSet (p: ProcessFigures) =
    p.Scenarios
    |> Array.map scenarioKey
    |> Set.ofArray

/// <summary>Requires a complete five-round CI artifact with both head runtimes and explicit bootstrap reasons for missing baselines.</summary>
let validate (gate: string) (run: AbRun) =
    require (not (isNull (box run))) "Missing AB artifact."

    require
        (run.Leg = "arm64-counters"
         || run.Leg = "x64-timing")
        "Unknown benchmark leg."

    require
        (gate =
            (if run.Leg = "arm64-counters" then
                 "instructions"
             else
                 "cpu_ns"))
        "Gate differs from the benchmark leg."

    require (run.Rounds = 5) "CI comparison requires five rounds."
    nonempty "base commit" run.Base
    nonempty "head commit" run.Head
    nonempty "machine" run.Machine

    for a in [ box run.Processes; box run.Counters; box run.NoBaseline; box run.Versions ] do
        require (not (isNull a)) "Missing artifact collection."

    unique "baseline runtime" _.Name run.NoBaseline

    for nb in run.NoBaseline do
        require (nb.Name = "node" || nb.Name = "dotnet") "Unknown no-baseline runtime."
        nonempty "baseline reason" nb.Value

    let absent = run.NoBaseline |> Array.map _.Name |> Set.ofArray

    let validateRows counter (rows: ProcessFigures[]) =
        unique "process identity" (fun p -> p.Side, p.Runtime, p.Process) rows

        for p in rows do
            require (p.Side = "base" || p.Side = "head") "Invalid process side."
            require (p.Runtime = "node" || p.Runtime = "dotnet") "Invalid process runtime."
            require (p.Commit = (if p.Side = "base" then run.Base else run.Head)) "Process commit differs from its side."

            require
                (p.Process >= 0
                 && p.Process < (if counter then 1 else 5))
                "Invalid process index."

            require (not (isNull p.Scenarios) && p.Scenarios.Length > 0) "Missing scenarios."
            unique "scenario/engine" scenarioKey p.Scenarios

            for s in p.Scenarios do
                nonempty "scenario" s.Scenario
                nonempty "engine" s.Engine
                require (not (isNull s.Metrics) && s.Metrics.Length > 0) "Missing metrics."
                unique "metric" (fun (m: Metric) -> m.Name) s.Metrics

                for m in s.Metrics do
                    nonempty "metric" m.Name
                    finite m.Name m.Value
                    require (counter = m.Name.StartsWith ("lib:", StringComparison.Ordinal)) "Metric has the wrong collection source."

                if not counter then
                    require (s.Metrics |> Array.exists (fun m -> m.Name = gate)) ("Missing gate metric " + gate + ".")

        for runtime in [ "node"; "dotnet" ] do
            for side in [ "base"; "head" ] do
                let ps =
                    rows
                    |> Array.filter (fun p -> p.Side = side && p.Runtime = runtime)

                if side = "base" && absent.Contains runtime then
                    require (ps.Length = 0) "NoBaseline contradicts base figures."
                elif not counter then
                    require
                        (ps.Length = 5)
                        ("Incomplete "
                         + side
                         + " "
                         + runtime
                         + " process coverage.")

                if ps.Length > 0 then
                    let expected = scenarioSet ps[0]

                    for p in ps do
                        require (scenarioSet p = expected) "Scenario coverage differs between rounds."

                        for s in p.Scenarios do
                            let first =
                                ps[0].Scenarios
                                |> Array.find (fun x -> scenarioKey x = scenarioKey s)

                            require
                                ((s.Metrics |> Array.map _.Name |> Set.ofArray) = (first.Metrics |> Array.map _.Name |> Set.ofArray))
                                "Metric coverage differs between rounds."

    validateRows false run.Processes
    validateRows true run.Counters

    for p in run.Counters do
        let timing =
            run.Processes
            |> Array.find (fun t -> t.Side = p.Side && t.Runtime = p.Runtime)

        require (scenarioSet p = scenarioSet timing) "Library and timing scenario coverage differs."

    for runtime in [ "node"; "dotnet" ] do
        for side in [ "base"; "head" ] do
            let cs =
                run.Counters
                |> Array.filter (fun p -> p.Side = side && p.Runtime = runtime)

            let unavailable =
                run.Versions
                |> Array.exists (fun x ->
                    x.Name = side + ":" + runtime + ":counters"
                    && x.Value.StartsWith ("unavailable:", StringComparison.Ordinal))

            if side = "base" && unavailable then
                require (cs.Length = 0) "Unavailable instrumentation contradicts base counter figures."

            if
                not (
                    side = "base"
                    && (absent.Contains runtime || unavailable)
                )
            then
                require (cs.Length = 1) "Incomplete library counter coverage."

        let bs =
            run.Processes
            |> Array.filter (fun p -> p.Side = "base" && p.Runtime = runtime)

        let hs =
            run.Processes
            |> Array.filter (fun p -> p.Side = "head" && p.Runtime = runtime)

        if bs.Length > 0 then
            for b in bs[0].Scenarios do
                match
                    hs[0].Scenarios
                    |> Array.tryFind (fun h -> scenarioKey h = scenarioKey b)
                with
                | Some h ->
                    require
                        ((b.Metrics |> Array.map _.Name |> Set.ofArray) = (h.Metrics |> Array.map _.Name |> Set.ofArray))
                        "Metric coverage differs between sides."
                | None -> ()

let private isRanvier (engine: string) =
    engine.StartsWith ("Ranvier", StringComparison.Ordinal)

/// <summary>Validates CI coverage and compares all metrics; unaccepted Ranvier gate regressions fail a gating leg.</summary>
/// <remarks>
/// Unknown acceptance names fail every leg, including advisory legs. When the gate metric of a runtime's
/// <c>control</c> scenario is <c>Regressed</c> or <c>Improved</c>, every gate regression of that runtime is
/// <c>Inconclusive</c> and passes.
/// </remarks>
let verdict gate threshold floor failing (accept: Acceptance) (run: AbRun) =
    configuration threshold floor
    validate gate run

    let counterUnavailable =
        run.Versions
        |> Array.choose (fun label ->
            [ "node"; "dotnet" ]
            |> List.tryFind (fun runtime ->
                label.Name = "base:" + runtime + ":counters"
                && label.Value.StartsWith ("unavailable:", StringComparison.Ordinal))
            |> Option.map (fun runtime -> { Name = runtime; Value = label.Value }))

    let collect counter (source: ProcessFigures[]) =
        let keys =
            source
            |> Array.collect (fun p ->
                p.Scenarios
                |> Array.collect (fun s ->
                    s.Metrics
                    |> Array.map (fun m -> p.Runtime, s.Scenario, s.Engine, m.Name)))
            |> Set.ofArray

        [|
            for runtime, scenario, engine, metric in keys do
                let samples side =
                    source
                    |> Array.filter (fun p -> p.Side = side && p.Runtime = runtime)
                    |> Array.sortBy _.Process
                    |> Array.choose (fun p ->
                        p.Scenarios
                        |> Array.tryFind (fun s -> s.Scenario = scenario && s.Engine = engine)
                        |> Option.bind (fun s ->
                            s.Metrics
                            |> Array.tryFind (fun m -> m.Name = metric)
                            |> Option.map (fun m -> m.Value, s.Gc, s.Compiled)))

                let bs, hs = samples "base", samples "head"
                let value (v, _, _) = v
                let gc (_, g, _) = g
                let compiled (_, _, c) = c
                let b, h = Array.map value bs, Array.map value hs

                let kind =
                    if
                        (run.NoBaseline
                         |> Array.exists (fun label -> label.Name = runtime))
                        || (counter
                            && counterUnavailable
                               |> Array.exists (fun label -> label.Name = runtime))
                    then
                        NoBaseline
                    elif b.Length = 0 then
                        Added
                    elif h.Length = 0 then
                        Removed
                    elif counter then
                        if median b = median h then Unchanged else CounterChanged
                    elif metric = gate then
                        classify threshold floor b h
                    else
                        Unchanged

                let delta =
                    if b.Length = 0 || h.Length = 0 || median b = 0. then
                        None
                    else
                        let value = median h / median b - 1.
                        if Double.IsFinite value then Some value else None

                yield
                    {
                        Runtime = runtime
                        Scenario = scenario
                        Engine = engine
                        Metric = metric
                        Base = b
                        Head = h
                        Kind = kind
                        Delta = delta
                        Accepted =
                            kind = Regressed
                            && (accept.All || List.contains scenario accept.Names)
                        BaseGc = bs |> Array.exists gc
                        HeadGc = hs |> Array.exists gc
                        BaseCompiled = bs |> Array.exists compiled
                        HeadCompiled = hs |> Array.exists compiled
                    }
        |]

    let measured = collect false run.Processes

    let controlMoved =
        measured
        |> Array.filter (fun f ->
            f.Scenario = Control.Scenario
            && f.Metric = gate
            && (f.Kind = Regressed || f.Kind = Improved))
        |> Array.map _.Runtime
        |> Array.distinct

    let findings =
        Array.append
            (measured
             |> Array.map (fun f ->
                 if
                     f.Kind = Regressed
                     && f.Scenario <> Control.Scenario
                     && Array.contains f.Runtime controlMoved
                 then
                     { f with
                         Kind = Inconclusive
                         Accepted = false
                     }
                 else
                     f))
            (collect true run.Counters)

    let regressions =
        findings
        |> Array.filter (fun f ->
            f.Metric = gate
            && isRanvier f.Engine
            && f.Kind = Regressed
            && not f.Accepted)

    {
        Leg = run.Leg
        Gate = gate
        Fails =
            not accept.Unknown.IsEmpty
            || (failing && regressions.Length > 0)
        Findings = findings
        NoBaseline = run.NoBaseline
        CounterUnavailable = counterUnavailable
        ControlMoved = controlMoved
        Warnings =
            if failing then
                [||]
            else
                regressions
                |> Array.map (fun f -> $"{run.Leg} {f.Runtime} {f.Scenario} ({f.Engine}): {gate} regression")
    }

let private escape (s: string) =
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("|", "&#124;").Replace("\r", " ").Replace("\n", " ")

let private figure (xs: float[]) =
    if xs.Length = 0 then
        "—"
    else
        (median xs).ToString("G8", CultureInfo.InvariantCulture)

let private metricFigure (f: Finding) xs =
    if
        f.Metric.StartsWith ("lib:", StringComparison.Ordinal)
        && Array.length xs > 0
    then
        (median xs).ToString("R", CultureInfo.InvariantCulture)
    else
        figure xs

let private change (f: Finding) =
    match f.Delta with
    | Some d ->
        (d * 100.).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)
        + "%"
    | None -> "n/a"

/// <summary>Renders changed findings, scoped acceptance reasons, and collapsed complete tables per leg and runtime.</summary>
let markdown (accept: Acceptance) (verdicts: Verdict list) =
    let sb = StringBuilder ()

    let line (s: string) =
        sb.AppendLine s |> ignore

    line "<!-- ranvier-perf -->"

    line (
        if verdicts |> List.exists _.Fails then
            "## Performance: regression or invalid acceptance"
        elif
            verdicts
            |> List.exists (fun v ->
                v.Findings
                |> Array.exists (fun f -> f.Kind = Inconclusive))
        then
            "## Performance: inconclusive"
        else
            "## Performance: no unaccepted regression"
    )

    line ""

    if not accept.Unknown.IsEmpty then
        line (
            "Unknown acceptance names: "
            + (accept.Unknown
               |> List.map escape
               |> String.concat ", ")
        )

    for entry in accept.Entries do
        let scope = String.concat ", " entry.Names
        line ($"Accepted by {escape entry.Source}: {escape scope}: {escape entry.Reason}")

    for v in verdicts do
        line ""
        line ($"### {escape v.Leg} ({escape v.Gate})")

        for nb in v.NoBaseline do
            line ($"- {escape nb.Name}: no baseline ({escape nb.Value})")

        for unavailable in v.CounterUnavailable do
            line ($"- {escape unavailable.Name}: base library counters unavailable ({escape unavailable.Value})")

        for runtime in v.ControlMoved do
            line ($"- {escape runtime}: the control moved between base and head; its {escape v.Gate} regressions are inconclusive")

        let table rows =
            line "| Runtime | Scenario | Engine | Metric | Base | Head | Change | Status |"
            line "|---|---|---|---|---:|---:|---:|---|"

            for f in rows do
                let status =
                    string f.Kind
                    + (if f.Accepted then " accepted" else "")

                line (
                    $"| {escape f.Runtime} | {escape f.Scenario} | {escape f.Engine} | {escape f.Metric} | {metricFigure f f.Base} | {metricFigure f f.Head} | {change f} | {status} |"
                )

        for title, predicate in
            [
                "Regressions", (fun f -> f.Kind = Regressed)
                "Inconclusive (control moved)", (fun f -> f.Kind = Inconclusive)
                "State shifts (lower modes agree)", (fun f -> f.Kind = StateShift)
                "Improvements", (fun f -> f.Kind = Improved)
                "Library counter changes",
                (fun f ->
                    f.Metric.StartsWith ("lib:", StringComparison.Ordinal)
                    && List.contains f.Kind [ CounterChanged; Added; Removed ])
                "Added and removed scenarios",
                (fun f ->
                    not (f.Metric.StartsWith ("lib:", StringComparison.Ordinal))
                    && (f.Kind = Added || f.Kind = Removed))
            ] do
            let rows = v.Findings |> Array.filter predicate

            if rows.Length > 0 then
                line ""
                line ($"**{title}**")
                line ""
                table rows

        for runtime, rows in v.Findings |> Array.groupBy _.Runtime do
            line ""
            line "<details>"
            line ($"<summary>{escape v.Leg} / {escape runtime}: all metrics</summary>")
            line ""
            line "Base → head medians; identical values appear once. GC and JIT flags cover any measured process."
            line ""

            let metrics =
                rows
                |> Array.map _.Metric
                |> Array.distinct
                |> Array.sort

            line (
                "| Scenario | Engine | GC base/head | JIT base/head | "
                + (metrics |> Array.map escape |> String.concat " | ")
                + " |"
            )

            line (
                "|---|---|---|---|"
                + (metrics
                   |> Array.map (fun _ -> "---:")
                   |> String.concat "|")
                + "|"
            )

            for (scenario, engine), fs in
                rows
                |> Array.groupBy (fun f -> f.Scenario, f.Engine) do
                let flag side =
                    if fs |> Array.exists side then "yes" else "no"

                let cells =
                    metrics
                    |> Array.map (fun metric ->
                        match fs |> Array.tryFind (fun f -> f.Metric = metric) with
                        | Some f when
                            f.Base.Length > 0
                            && f.Head.Length > 0
                            && median f.Base = median f.Head
                            ->
                            metricFigure f f.Base
                        | Some f ->
                            metricFigure f f.Base
                            + " → "
                            + metricFigure f f.Head
                        | None -> "—")

                line (
                    $"| {escape scenario} | {escape engine} | {flag _.BaseGc}/{flag _.HeadGc} | {flag _.BaseCompiled}/{flag _.HeadCompiled} | "
                    + String.concat " | " cells
                    + " |"
                )

            line ""
            line "</details>"

    sb.ToString ()

type private Options =
    {
        Ab: string list
        Label: bool
        Messages: string option
        Out: string
        Accept: string option
        Reason: string option
        Threshold: float option
        Floor: float option
    }

let private number (value: string) =
    Double.Parse (value, NumberStyles.Float, CultureInfo.InvariantCulture)

let rec private parse o args =
    match args with
    | [] -> o
    | "--ab" :: v :: rest -> parse { o with Ab = o.Ab @ [ v ] } rest
    | "--label" :: v :: rest -> parse { o with Label = Boolean.Parse v } rest
    | "--messages" :: v :: rest -> parse { o with Messages = Some v } rest
    | "--out" :: v :: rest -> parse { o with Out = v } rest
    | "--accept" :: v :: rest -> parse { o with Accept = Some v } rest
    | "--accept-reason" :: v :: rest -> parse { o with Reason = Some v } rest
    | "--threshold" :: v :: rest -> parse { o with Threshold = Some (number v) } rest
    | "--floor" :: v :: rest -> parse { o with Floor = Some (number v) } rest
    | unknown :: _ -> invalidArg "args" ($"Unknown or incomplete compare argument '{unknown}'.")

/// <summary>
/// Writes verdict.json and comment.md from complete AB artifacts; returns 1 for an unaccepted regression or unknown acceptance name.
/// </summary>
/// <remarks>The JSON envelope contains Acceptance and Verdicts; finding Kind is a string and Delta is a nullable relative change.</remarks>
/// <exception cref="T:System.ArgumentException">Artifact coverage, CLI options, or an explicit acceptance trailer is invalid.</exception>
let main args =
    let o =
        parse
            {
                Ab = []
                Label = false
                Messages = None
                Out = "."
                Accept = None
                Reason = None
                Threshold = None
                Floor = None
            }
            args

    require (not o.Ab.IsEmpty) "At least one --ab artifact is required."

    let readOptions =
        JsonSerializerOptions (RespectRequiredConstructorParameters = true)

    let runs =
        o.Ab
        |> List.map (fun path -> JsonSerializer.Deserialize<AbRun>(File.ReadAllText path, readOptions))

    for r in runs do
        require (not (isNull (box r))) "Missing AB artifact."

        let gate =
            if r.Leg = "arm64-counters" then
                "instructions"
            else
                "cpu_ns"

        validate gate r

    require ((runs |> List.map _.Leg |> Set.ofList).Count = runs.Length) "Duplicate benchmark leg."

    require
        ((runs
          |> List.map (fun r -> r.Base, r.Head)
          |> Set.ofList)
            .Count
            =
            1)
        "Leg commit identities differ."

    let known = runs |> List.map scenarioNames |> Set.unionMany

    let messages =
        o.Messages
        |> Option.map (fun path -> File.ReadAllText(path).Split('\u0000') :> seq<string>)
        |> Option.defaultValue Seq.empty

    let initial = acceptance o.Label messages known

    let entries =
        match o.Accept, o.Reason with
        | None, None -> initial.Entries
        | Some ns, Some reason ->
            nonempty "acceptance reason" reason

            initial.Entries
            @ [
                {
                    Names = names ns
                    Reason = reason.Trim ()
                    Source = "CLI --accept"
                }
            ]
        | _ -> invalidArg "args" "--accept and --accept-reason must be supplied together."

    let accept = fromEntries known entries

    let verdicts =
        runs
        |> List.map (fun r ->
            let arm = r.Leg = "arm64-counters"

            verdict
                (if arm then "instructions" else "cpu_ns")
                (defaultArg o.Threshold (if arm then 0.02 else 0.15))
                (defaultArg o.Floor (if arm then 20. else 0.))
                arm
                accept
                r)

    let dto =
        {|
            Acceptance = accept
            Verdicts =
                verdicts
                |> List.map (fun v ->
                    {|
                        Leg = v.Leg
                        Gate = v.Gate
                        Fails = v.Fails
                        NoBaseline = v.NoBaseline
                        CounterUnavailable = v.CounterUnavailable
                        ControlMoved = v.ControlMoved
                        Warnings = v.Warnings
                        Findings =
                            v.Findings
                            |> Array.map (fun f ->
                                {|
                                    Runtime = f.Runtime
                                    Scenario = f.Scenario
                                    Engine = f.Engine
                                    Metric = f.Metric
                                    Base = f.Base
                                    Head = f.Head
                                    Kind = string f.Kind
                                    Delta = f.Delta |> Option.toNullable
                                    Accepted = f.Accepted
                                    BaseGc = f.BaseGc
                                    HeadGc = f.HeadGc
                                    BaseCompiled = f.BaseCompiled
                                    HeadCompiled = f.HeadCompiled
                                |})
                    |})
        |}

    Directory.CreateDirectory o.Out |> ignore
    File.WriteAllText (Path.Combine (o.Out, "verdict.json"), JsonSerializer.Serialize (dto, JsonSerializerOptions (WriteIndented = true)))
    File.WriteAllText (Path.Combine (o.Out, "comment.md"), markdown accept verdicts)
    if verdicts |> List.exists _.Fails then 1 else 0
