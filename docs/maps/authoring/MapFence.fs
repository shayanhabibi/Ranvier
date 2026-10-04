namespace Ranvier.Docs.Maps.Authoring

open System
open System.Globalization
open System.Text.RegularExpressions

/// <summary>The words of a <c>map</c> fence's info string.</summary>
type MapFlags =
    {
        Timeline: bool
        /// <summary>Presses every control once, in order, for the timeline to play back; implies <c>Timeline</c>.</summary>
        Replay: bool
        /// <summary>The case name of the map graph's <c>FlightPolicy</c>.</summary>
        Policy: string
        /// <summary>The case name of the map's <c>Grouping</c>.</summary>
        Groups: string
        /// <summary>The initial playback multiplier, from 0.25 to 4; 1 is normal speed.</summary>
        Speed: float
    }

    /// <summary>The flags of a fence, or the problem with its policy, grouping or speed.</summary>
    static member parse(flags: string list) : Result<MapFlags, string> =
        let replay = List.contains "replay" flags

        let policy =
            match
                flags
                |> List.tryFind (fun f -> f.StartsWith "policy=")
            with
            | None
            | Some "policy=cancel-previous" -> Ok "CancelPrevious"
            | Some "policy=keep-latest" -> Ok "KeepLatest"
            | Some "policy=queue" -> Ok "Queue"
            | Some "policy=finish-current" -> Ok "FinishCurrent"
            | Some flag -> Error $"%s{flag}: the policy is cancel-previous, keep-latest, queue or finish-current."

        let groups =
            match
                flags
                |> List.tryFind (fun f -> f.StartsWith "groups=")
            with
            | None
            | Some "groups=expand" -> Ok "Expand"
            | Some "groups=collapse" -> Ok "Collapse"
            | Some flag -> Error $"%s{flag}: the groups are expand or collapse."

        let speed =
            match
                flags
                |> List.tryFind (fun flag -> flag.StartsWith "speed=")
            with
            | None -> Ok 1.0
            | Some flag ->
                match Double.TryParse (flag.Substring (6), NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, value when value >= 0.25 && value <= 4.0 -> Ok value
                | _ -> Error $"{flag}: speed must be a number from 0.25 to 4; 1 is normal speed."

        match policy, groups, speed with
        | Ok policy, Ok groups, Ok speed ->
            Ok
                {
                    Timeline = replay || List.contains "timeline" flags
                    Replay = replay
                    Policy = policy
                    Groups = groups
                    Speed = speed
                }
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

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
        Regex (@"^let\s+(?:mutable\s+)?(?<name>[A-Za-z_][\w']*)\s*(?::[^=]*)?=\s*(?<rest>.*)$", RegexOptions.Compiled)

    // Lookups and editable values are labelled during creation.
    let private node =
        Regex (
            @"^(?:create(?!Effect\b|Root\b|Lookup\b|Selector\b|Editable\b|Draft\b)\w*|debounce(?:With)?|throttle(?:First|Last)?(?:With)?)\b",
            RegexOptions.Compiled
        )

    let private named =
        Regex (@"^create(?:Lookup|Selector|Editable|Draft)\b", RegexOptions.Compiled)

    let private controls = Regex (@"^controls\b", RegexOptions.Compiled)

    let private character =
        Regex (@"\G'(?:\\(?:u[0-9a-fA-F]{4}|[0-9]{3}|.)|[^'\\\r\n])'", RegexOptions.Compiled)

    let private mask (source: string) =
        let code = source.ToCharArray ()
        let syntax = source.ToCharArray ()
        let literalStarts = Collections.Generic.HashSet<int>()

        let erase (target: char[]) first last =
            for index in first .. last - 1 do
                if target[index] <> '\n' then
                    target[index] <- ' '

        let mutable i = 0

        while i < source.Length do
            let starts (token: string) =
                i + token.Length <= source.Length
                && String.CompareOrdinal (source, i, token, 0, token.Length) = 0

            let first = i

            if starts "//" then
                while i < source.Length && source[i] <> '\n' do
                    i <- i + 1

                erase code first i
                erase syntax first i
            elif starts "(*" then
                i <- i + 2
                let mutable depth = 1

                while i < source.Length && depth > 0 do
                    if starts "(*" then
                        depth <- depth + 1
                        i <- i + 2
                    elif starts "*)" then
                        depth <- depth - 1
                        i <- i + 2
                    else
                        i <- i + 1

                erase code first i
                erase syntax first i
            elif
                source[i] = '\''
                && character.Match(source, i).Success
            then
                i <- i + character.Match(source, i).Length
                erase syntax first i
                syntax[first] <- '\''
            elif starts "\"\"\"" || starts "@\"" || starts "\"" then
                let triple = starts "\"\"\""
                let verbatim = starts "@\""

                i <-
                    i
                    + (if triple then 3
                       elif verbatim then 2
                       else 1)

                let mutable closed = false

                while i < source.Length && not closed do
                    if triple && starts "\"\"\"" then
                        i <- i + 3
                        closed <- true
                    elif not triple && source[i] = '"' then
                        if verbatim && starts "\"\"" then
                            i <- i + 2
                        else
                            i <- i + 1
                            closed <- true
                    elif not triple && not verbatim && source[i] = '\\' then
                        i <- min source.Length (i + 2)
                    else
                        i <- i + 1

                erase syntax first i
                syntax[first] <- '"'
                syntax[i - 1] <- '"'

                for index in first .. i - 1 do
                    if source[index] = '\n' then
                        literalStarts.Add (index + 1) |> ignore
            else
                i <- i + 1

        String (code), String (syntax), literalStarts

    let private valueOf (lines: string[]) first last =
        let matched = binding.Match lines[first]

        if matched.Success then
            let value =
                String.concat "\n" (Array.append [| matched.Groups["rest"].Value |] lines[first + 1 .. last])

            Some (matched, value.Trim ())
        else
            None

    let private aggregate =
        Regex (@"^Projection\.(?:sumBy|countBy|exists|forall|fold|foldGroup)\b", RegexOptions.Compiled)

    let private pipelineResult (value: string) =
        let mutable depth = 0
        let mutable last = -1

        for i in 0 .. value.Length - 2 do
            match value[i] with
            | '('
            | '['
            | '{' -> depth <- depth + 1
            | ')'
            | ']'
            | '}' -> depth <- depth - 1
            | '|' when depth = 0 && value[i + 1] = '>' -> last <- i + 2
            | _ -> ()

        if last >= 0 then
            value.Substring(last).TrimStart()
        else
            value

    let private blank (line: string) =
        let t = line.Trim ()
        t = "" || t.StartsWith "//"

    /// <summary>A line at column zero that opens a new binding or statement.</summary>
    let private opens (line: string) =
        line.Length > 0
        && (Char.IsLetter line[0] || line[0] = '_')

    /// <summary>The module holding a fence's scenario.</summary>
    let moduleName (cellId: string) =
        "Map_"
        + Regex.Replace (cellId, @"[^A-Za-z0-9_]", "_")

    /// <summary>The top-level items of <c>lines</c> as (first, last) indices, trailing blank and comment lines excluded.</summary>
    let private items (lines: string[]) =
        let starts =
            [
                for i in 0 .. lines.Length - 1 do
                    if opens lines[i] then
                        i
            ]

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

    /// <summary>The label of a node returned by a factory call or projection aggregate pipeline.</summary>
    let private labelOf (lines: string[]) (first: int, last: int) =
        match valueOf lines first last with
        | Some (matched, value) ->
            let result = pipelineResult value

            if node.IsMatch result || aggregate.IsMatch result then
                Some matched.Groups["name"].Value
            else
                None
        | None -> None

    /// <summary>The binding with its lookup or editable creation wrapped in <c>Trace.named</c>.</summary>
    let private namedBinding (lines: string[]) (code: string[]) (syntax: string[]) (literal: bool[]) (first: int, last: int) =
        let m = binding.Match lines[first]
        let rest = m.Groups["rest"]

        let value =
            valueOf syntax first last
            |> Option.map snd
            |> Option.defaultValue ""

        if m.Success && named.IsMatch value then
            let name = m.Groups["name"].Value

            let wrapped =
                lines[first..last]
                |> Array.mapi (fun offset line ->
                    let line =
                        if offset = last - first then
                            let column = code[last].TrimEnd().Length
                            line.Insert (column, ")")
                        else
                            line

                    let line =
                        if offset = 0 then
                            line.Substring(0, rest.Index).TrimEnd()
                            + $" Trace.named \"%s{name}\" (fun () ->"
                            + (if rest.Value = "" && first <> last then
                                   ""
                               else
                                   " " + line.Substring (rest.Index))
                        elif literal[first + offset] then
                            line
                        else
                            "    " + line

                    line)

            Some (name, wrapped)
        else
            None

    /// <summary>
    /// A module named by <c>moduleName cellId</c> whose <c>scenario</c> runs the fence's code against a graph and
    /// returns its controls, with <c>Trace.label</c> after node bindings and <c>Trace.named</c> around lookup and editable creation.
    /// </summary>
    /// <returns>The module's code, its spans and the bindings, or problems at fence lines.</returns>
    let scenario (cellId: string) (code: string) : Result<string * MapSpan list * (string * int * int) list, (int * string) list> =
        let lines = code.Split '\n' |> Array.map _.TrimEnd('\r')
        let uncommented, syntax, literalStarts = mask (String.concat "\n" lines)
        let mutable offset = 0

        let literal =
            lines
            |> Array.map (fun line ->
                let inside = literalStarts.Contains offset
                offset <- offset + line.Length + 1
                inside)

        let uncommented = uncommented.Split '\n'
        let syntax = syntax.Split '\n'

        match items syntax with
        | [] -> Error [ 1, "A map fence holds a scenario that ends with `controls [ ... ]`." ]
        | found when not (controls.IsMatch lines[fst (List.last found)]) ->
            Error
                [
                    snd (List.last found) + 1, "A map fence ends with `controls [ ... ]`, which lists the map's buttons."
                ]
        | found ->
            let output = ResizeArray<string>()
            let spans = ResizeArray<MapSpan>()
            let bindings = ResizeArray<string * int * int>()
            let pad = String (' ', indent)

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
                    for index in first..last do
                        let line = lines[index]

                        spans.Add
                            {
                                Generated = output.Count + 1
                                Length = 1
                                Body = index + 1
                                Indent = if literal[index] then 0 else indent
                            }

                        output.Add (
                            if literal[index] then line
                            elif line.Trim () = "" then ""
                            else pad + line
                        )

            pinned $"module %s{moduleName cellId} ="
            pinned "    open Ranvier"
            pinned "    open Ranvier.Docs.Maps"
            pinned ""
            pinned "    let scenario (graph': Graph) : Control list ="
            pinned "        use _ = graph'.Activate ()"
            let mutable copied = 0

            for first, last in found do
                match labelOf syntax (first, last) with
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
                    bindings.Add (name, first + 1, last + 1)
                | None ->
                    match namedBinding lines uncommented syntax literal (first, last) with
                    | Some (name, wrapped) ->
                        copy copied (first - 1)
                        copied <- last + 1

                        spans.Add
                            {
                                Generated = output.Count + 1
                                Length = 1
                                Body = first + 1
                                Indent = indent
                            }

                        for index in first + 1 .. last do
                            spans.Add
                                {
                                    Generated = output.Count + index - first + 1
                                    Length = 1
                                    Body = index + 1
                                    Indent = if literal[index] then 0 else indent + 4
                                }

                        for index in 0 .. wrapped.Length - 1 do
                            output.Add (
                                if literal[first + index] then
                                    wrapped[index]
                                else
                                    pad + wrapped[index]
                            )

                        bindings.Add (name, first + 1, last + 1)
                    | None -> ()

            copy copied (lines.Length - 1)
            Ok (String.concat "\n" output, List.ofSeq spans, List.ofSeq bindings)

    let private render (source: string) (flags: MapFlags) (bindings: (string * int * int) list) =
        let bindings =
            bindings
            |> List.map (fun (name, first, last) -> $"(\"%s{name}\", %d{first}, %d{last})")
            |> String.concat "; "

        let timeline = if flags.Timeline then "true" else "false"

        let renderCall =
            if flags.Speed = 1.0 then
                "SignalMap"
            else
                let value = flags.Speed.ToString ("R", CultureInfo.InvariantCulture)

                let literal =
                    if value.Contains ('.') || value.Contains ('E') then
                        value
                    else
                        value + ".0"

                "SignalMapWithSpeed " + literal

        $"Ranvier.Docs.Maps.SignalMapComponent.%s{renderCall} (%s{source}) Ranvier.FlightPolicy.%s{flags.Policy} [| %s{bindings} |] %s{timeline} Ranvier.Docs.Maps.Grouping.%s{flags.Groups}"

    /// <summary>The F# for a <c>map</c> fence: its scenario, live or replayed when <c>flags.Replay</c>.</summary>
    /// <param name="cellId">The cell's id, which names the generated module.</param>
    /// <param name="flags">The fence's flags.</param>
    /// <param name="code">The fence's code.</param>
    let generate (cellId: string) (flags: MapFlags) (code: string) : Result<MapFenceOutput, (int * string) list> =
        scenario cellId code
        |> Result.map (fun (live, spans, bindings) ->
            let source = if flags.Replay then "Replayed" else "Live"

            {
                Code = live
                Render = render $"Ranvier.Docs.Maps.%s{source} %s{moduleName cellId}.scenario" flags bindings
                Spans = spans
                Bindings = bindings
            })

    /// <summary>The F# for a <c>map</c> fence from its raw flags; a flag problem is reported at the opening line, 0.</summary>
    let compile (cellId: string) (flags: string list) (code: string) : Result<MapFenceOutput, (int * string) list> =
        match MapFlags.parse flags with
        | Ok flags -> generate cellId flags code
        | Error problem -> Error [ 0, problem ]
