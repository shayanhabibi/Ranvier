/// The `map` fence: a plain F# scenario rewritten into a live or replayed signal map.
module Docs.Maps

open Nacara.Plugins
open Ranvier.Docs.Maps.Authoring

let private spans (spans: MapSpan list) : SolidLineSpan list =
    spans
    |> List.map (fun s ->
        {
            Generated = s.Generated
            Length = s.Length
            Body = s.Body
            Indent = s.Indent
        })

/// <summary>Compiles a <c>map</c> fence into a <c>SignalMap</c> of its scenario.</summary>
let transform (input: SolidTransformInput) : SolidTransformOutput =
    match MapFence.compile input.CellId input.Flags input.Code with
    | Ok output ->
        SolidTransformOutput.Compiled
            {
                Code = output.Code
                Render = output.Render
                Spans = spans output.Spans
            }
    | Error problems -> SolidTransformOutput.Rejected problems
