namespace Ranvier.Docs.Maps.Authoring

open System
open System.Text.RegularExpressions

/// <summary>The words of a <c>map</c> fence's info string.</summary>
type MapFlags =
    {
        Timeline: bool
        /// <summary>Plays a build-time recording; implies <c>Timeline</c>.</summary>
        Replay: bool
    }

    static member parse(flags: string list) =
        let replay = List.contains "replay" flags

        {
            Timeline = replay || List.contains "timeline" flags
            Replay = replay
        }

/// <summary>A run of generated lines and the fence line it came from.</summary>
/// <remarks>
/// <c>Generated</c> counts from 1 within the generated code and <c>Body</c> from 1 within the fence's code;
/// <c>Indent = Int32.MaxValue</c> blames the fence's opening line.
/// </remarks>
type MapSpan =
    {
        Generated: int
        Length: int
        Body: int
        Indent: int
    }

/// <summary>The F# compiled in place of a <c>map</c> fence.</summary>
type MapFenceOutput =
    {
        /// <summary>Module-level declarations, from column zero.</summary>
        Code: string
        /// <summary>The <c>SignalMap</c> call the cell renders.</summary>
        Render: string
        Spans: MapSpan list
        /// <summary>(label, first line, last line) of each labelled binding, counting fence lines from 1.</summary>
        Bindings: (string * int * int) list
    }

[<RequireQualifiedAccess>]
module MapFence =

    /// <summary>The columns the scenario body sits at, inside its module and function.</summary>
    let private indent = 8

    let private binding =
        Regex(@"^let\s+(?:mutable\s+)?(?<name>[A-Za-z_][\w']*)\s*(?::[^=]*)?=\s*(?<rest>.*)$", RegexOptions.Compiled)

    // Effects and roots return no node to label.
    let private node =
        Regex(@"^create(?!Effect\b|Root\b)\w*\b", RegexOptions.Compiled)

    let private controls = Regex(@"^controls\b", RegexOptions.Compiled)

    let private blank (line: string) =
        let t = line.Trim()
        t = "" || t.StartsWith "//"

    /// <summary>A line at column zero that opens a new binding or statement.</summary>
    let private opens (line: string) =
        line.Length > 0 && (Char.IsLetter line[0] || line[0] = '_')

    /// <summary>The module holding a fence's scenario and recording.</summary>
    let moduleName (cellId: string) =
        "Map_" + Regex.Replace(cellId, @"[^A-Za-z0-9_]", "_")

    /// <summary>The top-level items of <c>lines</c> as (first, last) indices, trailing blank and comment lines excluded.</summary>
    let private items (lines: string[]) =
        let starts =
            [ for i in 0 .. lines.Length - 1 do
                  if opens lines[i] then
                      i ]

        starts
        |> List.mapi (fun k start ->
            let next =
                if k + 1 < starts.Length then
                    starts[k + 1]
                else
                    lines.Length

            let mutable last = next - 1

            while last > start && blank lines[last] do
                last <- last - 1

            start, last)

    /// <summary>The label of the node bound by the item, when its value comes from a <c>create…</c> call.</summary>
    let private labelOf (lines: string[]) (first: int, last: int) =
        let m = binding.Match lines[first]

        if not m.Success then
            None
        else
            let rest = m.Groups["rest"].Value.Trim()

            let value =
                if rest <> "" then
                    rest
                else
                    lines[first + 1 .. last]
                    |> Array.tryFind (blank >> not)
                    |> Option.map _.Trim()
                    |> Option.defaultValue ""

            if node.IsMatch value then
                Some m.Groups["name"].Value
            else
                None

    /// <summary>
    /// A module named by <c>moduleName cellId</c> whose <c>scenario</c> runs the fence's code against a graph and
    /// returns its controls, with a <c>Trace.label</c> after each binding of a node.
    /// </summary>
    /// <returns>The module's code, its spans and the bindings, or problems at fence lines.</returns>
    let scenario (cellId: string) (code: string) : Result<string * MapSpan list * (string * int * int) list, (int * string) list> =
        let lines =
            code.Split '\n'
            |> Array.map _.TrimEnd('\r')

        match items lines with
        | [] -> Error [ 1, "A map fence holds a scenario that ends with `controls [ ... ]`." ]
        | found when not (controls.IsMatch lines[fst (List.last found)]) ->
            Error [ snd (List.last found) + 1, "A map fence ends with `controls [ ... ]`, which lists the map's buttons." ]
        | found ->
            let output = ResizeArray<string>()
            let spans = ResizeArray<MapSpan>()
            let bindings = ResizeArray<string * int * int>()
            let pad = String(' ', indent)

            let pinned (line: string) =
                spans.Add
                    {
                        Generated = output.Count + 1
                        Length = 1
                        Body = 1
                        Indent = Int32.MaxValue
                    }

                output.Add line

            let copy (first: int) (last: int) =
                if last >= first then
                    spans.Add
                        {
                            Generated = output.Count + 1
                            Length = last - first + 1
                            Body = first + 1
                            Indent = indent
                        }

                    for line in lines[first..last] do
                        output.Add(if line.Trim() = "" then "" else pad + line)

            pinned $"module %s{moduleName cellId} ="
            pinned "    open Ranvier"
            pinned "    open Ranvier.Docs.Maps"
            pinned ""
            pinned "    let scenario (graph': Graph) : Control list ="
            pinned "        use _ = graph'.Activate ()"
            let mutable copied = 0

            for first, last in found do
                match labelOf lines (first, last) with
                | Some name ->
                    copy copied last
                    copied <- last + 1

                    spans.Add
                        {
                            Generated = output.Count + 1
                            Length = 1
                            Body = last + 1
                            Indent = indent
                        }

                    output.Add $"%s{pad}Trace.label (graph', %s{name}, \"%s{name}\")"
                    bindings.Add(name, first + 1, last + 1)
                | None -> ()

            copy copied (lines.Length - 1)
            Ok(String.concat "\n" output, List.ofSeq spans, List.ofSeq bindings)

    let private render (source: string) (bindings: (string * int * int) list) (timeline: bool) =
        let bindings =
            bindings
            |> List.map (fun (name, first, last) -> $"(\"%s{name}\", %d{first}, %d{last})")
            |> String.concat "; "

        let timeline = if timeline then "true" else "false"
        $"Ranvier.Docs.Maps.SignalMapComponent.SignalMap (%s{source}) [| %s{bindings} |] %s{timeline}"

    /// <summary>The F# for a <c>map</c> fence: a live scenario, or the recording when <c>flags.Replay</c>.</summary>
    /// <param name="cellId">The cell's id, which names the generated module.</param>
    /// <param name="flags">The fence's flags.</param>
    /// <param name="recorded">The <c>Replay.literal</c> of the scenario's events; read only when replaying.</param>
    /// <param name="code">The fence's code.</param>
    let generate (cellId: string) (flags: MapFlags) (recorded: string option) (code: string) : Result<MapFenceOutput, (int * string) list> =
        match scenario cellId code with
        | Error problems -> Error problems
        | Ok(live, spans, bindings) ->
            let name = moduleName cellId

            match flags.Replay, recorded with
            | false, _ ->
                Ok
                    {
                        Code = live
                        Render = render $"Ranvier.Docs.Maps.Live %s{name}.scenario" bindings flags.Timeline
                        Spans = spans
                        Bindings = bindings
                    }
            | true, None -> Error [ 1, "A replayed map fence needs its recording." ]
            | true, Some literal ->
                let body =
                    literal.Split '\n'
                    |> Array.map (fun line -> "        " + line.TrimEnd('\r'))

                let lines =
                    [|
                        $"module %s{name} ="
                        "    open Ranvier.Docs.Maps"
                        ""
                        "    let events: Ranvier.TraceEvent[] ="
                        yield! body
                    |]

                Ok
                    {
                        Code = String.concat "\n" lines
                        Render = render $"Ranvier.Docs.Maps.Recorded %s{name}.events" bindings true
                        Spans =
                            [
                                {
                                    Generated = 1
                                    Length = lines.Length
                                    Body = 1
                                    Indent = Int32.MaxValue
                                }
                            ]
                        Bindings = bindings
                    }
