/// <summary>
/// The report's appendix: SVG bar charts of instructions per operation, one panel per scenario, and the Markdown that
/// embeds them.
/// </summary>
module CounterBench.Charts

open System
open System.Globalization
open System.Net
open System.Text

/// <summary>One scenario's bars: an engine and its instructions per operation.</summary>
type Panel =
    {
        Scenario: string
        N: int
        Bars: (string * float)[]
    }

[<Literal>]
let private Instructions = "InstructionRetired"

/// <summary>Panels for the .NET rows; empty without PMC collection.</summary>
let dotnetPanels (rows: Report.Row[]) =
    rows
    |> Array.groupBy (fun r -> r.Scenario)
    |> Array.choose (fun (scenario, group) ->
        let bars =
            group
            |> Array.choose (fun r ->
                r.Pmc
                |> Array.tryFind (fun p -> p.Name = Instructions)
                |> Option.map (fun p -> r.Engine, p.Value))

        if bars.Length = 0 then
            None
        else
            Some
                {
                    Scenario = scenario
                    N = group[0].N
                    Bars = bars
                })

/// <summary>Panels for the Node.js rows, from the main-thread medians; empty without PMC collection.</summary>
let nodePanels (sources: string[]) (rows: Report.NodeRow[]) =
    match Array.tryFindIndex ((=) Instructions) sources with
    | None -> [||]
    | Some k ->
        rows
        |> Array.groupBy (fun r -> r.Scenario)
        |> Array.choose (fun (scenario, group) ->
            let bars =
                group
                |> Array.filter (fun r -> r.Pmc.Length > k)
                |> Array.map (fun r -> r.Engine, r.Pmc[k].MainThread.Median)

            if bars.Length = 0 then
                None
            else
                Some
                    {
                        Scenario = scenario
                        N = group[0].N
                        Bars = bars
                    })

/// <summary>Engines in color-slot order. An engine keeps its slot in every chart.</summary>
let private engineOrder =
    [| "Ranvier"; "FSharp.Data.Adaptive"; "R3"; "Fable.Ripple" |]

let private lightSlots =
    [|
        "#2a78d6"
        "#eb6834"
        "#1baf7a"
        "#eda100"
        "#e87ba4"
        "#008300"
        "#4a3aa7"
        "#e34948"
    |]

let private darkSlots =
    [|
        "#3987e5"
        "#d95926"
        "#199e70"
        "#c98500"
        "#d55181"
        "#008300"
        "#9085e9"
        "#e66767"
    |]

let private invariant = CultureInfo.InvariantCulture

let private compact (value: float) =
    if Math.Abs value >= 100.0 then
        value.ToString ("N0", invariant)
    else
        value.ToString ("N1", invariant)

let private escape (s: string) =
    WebUtility.HtmlEncode s

let private label = Report.engineLabel

[<Literal>]
let private Width = 760.0

[<Literal>]
let private LabelWidth = 220.0

[<Literal>]
let private ValueWidth = 110.0

[<Literal>]
let private BarHeight = 14.0

[<Literal>]
let private BarStep = 20.0

[<Literal>]
let private PanelTitle = 26.0

[<Literal>]
let private PanelGap = 14.0

/// <summary>
/// A standalone SVG of <c>panels</c>, stacked vertically with one bar per engine. Each panel scales from zero to its
/// largest bar; light and dark colors follow <c>prefers-color-scheme</c>.
/// </summary>
let svg (title: string) (versions: Map<string, string>) (panels: Panel[]) =
    let present =
        panels
        |> Array.collect (fun p -> Array.map fst p.Bars)
        |> Array.distinct

    let unknown =
        present
        |> Array.filter (fun e -> not (Array.contains e engineOrder))

    let engines =
        Array.append
            (engineOrder
             |> Array.filter (fun e -> Array.contains e present))
            unknown

    let slot (engine: string) =
        match Array.tryFindIndex ((=) engine) engineOrder with
        | Some i -> i
        | None ->
            engineOrder.Length
            + Array.findIndex ((=) engine) unknown

    if
        engines
        |> Array.exists (fun e -> slot e >= lightSlots.Length)
    then
        failwith $"""More than %d{lightSlots.Length} engines: %s{String.Join (", ", engines)}"""

    let text = StringBuilder ()

    let add (s: string) =
        text.Append(s).Append('\n') |> ignore

    let plot = Width - LabelWidth - ValueWidth

    let legend =
        let mutable x = 16.0
        let mutable row = 0

        [|
            for engine in engines do
                let name = label versions engine
                let width = 14.0 + float name.Length * 6.6

                if x > 16.0 && x + width > Width - 16.0 then
                    x <- 16.0
                    row <- row + 1

                yield engine, name, x, 56.0 + float row * 18.0
                x <- x + width + 18.0
        |]

    let header =
        (legend
         |> Array.map (fun (_, _, _, y) -> y)
         |> Array.max)
        + 20.0

    let height =
        header
        + (panels
           |> Array.sumBy (fun p ->
               PanelTitle
               + float p.Bars.Length * BarStep
               + PanelGap))

    let px (v: float) =
        v.ToString ("0.#", invariant)

    add
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="%s{px Width}" height="%s{px height}" viewBox="0 0 %s{px Width} %s{px height}" role="img">"""

    add $"<title>%s{escape title}</title>"
    add "<style>"
    add "text{font-family:system-ui,-apple-system,'Segoe UI',sans-serif;font-size:12px}"
    add ".surface{fill:#fcfcfb}.ink{fill:#0b0b0b}.ink2{fill:#52514e}.muted{fill:#898781}.axis{stroke:#c3c2b7;stroke-width:1}"

    for engine in engines do
        add $".s%d{slot engine}{{fill:%s{lightSlots[slot engine]}}}"

    add "@media (prefers-color-scheme: dark){"
    add ".surface{fill:#1a1a19}.ink{fill:#ffffff}.ink2{fill:#c3c2b7}.axis{stroke:#383835}"

    for engine in engines do
        add $".s%d{slot engine}{{fill:%s{darkSlots[slot engine]}}}"

    add "}"
    add "</style>"
    add $"""<rect class="surface" width="%s{px Width}" height="%s{px height}" rx="8"/>"""
    add $"""<text class="ink" x="16" y="26" style="font-size:15px;font-weight:600">%s{escape title}</text>"""

    add """<text class="ink2" x="16" y="44">Instructions per operation. Each panel scales from zero to its largest bar.</text>"""

    for engine, name, x, y in legend do
        add $"""<rect class="s%d{slot engine}" x="%s{px x}" y="%s{px y}" width="10" height="10" rx="2"/>"""
        add $"""<text class="ink2" x="%s{px (x + 14.0)}" y="%s{px (y + 9.0)}">%s{escape name}</text>"""

    let mutable y = header

    for panel in panels do
        add
            $"""<text class="ink" x="16" y="%s{px (y + 16.0)}" style="font-weight:600">%s{escape panel.Scenario}<tspan class="muted" style="font-weight:400"> N = %d{panel.N}</tspan></text>"""

        let top = y + PanelTitle

        let largest =
            panel.Bars
            |> Array.map (snd >> max 0.0)
            |> Array.fold max 0.0

        let x0 = LabelWidth

        add
            $"""<line class="axis" x1="%s{px x0}" y1="%s{px (top - 3.0)}" x2="%s{px x0}" y2="%s{px (top + float panel.Bars.Length * BarStep - 3.0)}"/>"""

        panel.Bars
        |> Array.iteri (fun i (engine, value) ->
            let by = top + float i * BarStep

            let w =
                if largest > 0.0 then
                    max 0.0 value / largest * plot
                else
                    0.0

            let name = label versions engine

            let tip =
                $"%s{panel.Scenario}, %s{name}: %s{compact value} instructions per operation"

            add $"""<text class="ink2" x="%s{px (x0 - 8.0)}" y="%s{px (by + 11.0)}" text-anchor="end">%s{escape name}</text>"""

            let r = min 4.0 w

            add
                $"""<path class="s%d{slot engine}" d="M%s{px x0} %s{px by}h%s{px (w - r)}a%s{px r} %s{px r} 0 0 1 %s{px r} %s{px r}v%s{px (BarHeight - 2.0 * r)}a%s{px r} %s{px r} 0 0 1 -%s{px r} %s{px r}h-%s{px (w - r)}z"><title>%s{escape tip}</title></path>"""

            add $"""<text class="ink" x="%s{px (x0 + w + 6.0)}" y="%s{px (by + 11.0)}">%s{compact value}</text>""")

        y <- top + float panel.Bars.Length * BarStep + PanelGap

    add "</svg>"
    text.ToString ()

/// <summary>
/// The appendix Markdown linking the chart files <c>dotnet</c> and <c>node</c>, relative to the report.
/// </summary>
let appendix (dotnet: string option) (node: string option) =
    let text = StringBuilder ()

    let line (s: string) =
        text.Append(s).Append("\r\n") |> ignore

    if dotnet.IsSome || node.IsSome then
        line "# Appendix: instructions per operation"
        line ""
        line "The instr/op medians from the tables above; Node.js bars are main-thread figures. Each panel has its own scale."
        line ""

        dotnet
        |> Option.iter (fun file ->
            line "## .NET"
            line ""
            line $"![.NET instructions per operation](%s{file})"
            line "")

        node
        |> Option.iter (fun file ->
            line "## Fable under Node.js"
            line ""
            line $"![Node.js instructions per operation](%s{file})"
            line "")

    text.ToString ()
