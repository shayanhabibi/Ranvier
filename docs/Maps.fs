/// The `map` fence: a plain F# scenario rewritten into a live or replayed signal map.
module Docs.Maps

open Nacara.Plugins
open Nacara.Core
open System.Collections.Concurrent
open Ranvier.Docs.Maps.Authoring

let private codeOptions = ConcurrentDictionary<struct (string * string), MapFlags>()

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
        match MapFlags.parse input.Flags with
        | Ok flags -> codeOptions[struct (input.PageKey, input.CellId)] <- flags
        | Error _ -> ()

        SolidTransformOutput.Compiled
            {
                Code = output.Code
                Render = output.Render
                Spans = spans output.Spans
            }
    | Error problems -> SolidTransformOutput.Rejected problems

/// <summary>Adds native code disclosures to rendered map cards before HTML minification.</summary>
let register =
    Site.plugin
        { new IPlugin with
            member _.Name = "map-code"

            member _.Configure registry =
                registry
                |> Registry.assetTransform
                    {
                        Name = "map-code"
                        Extensions = [ ".html" ]
                        Transform =
                            fun context ->
                                MapCode.rewrite
                                    (fun page cell ->
                                        match codeOptions.TryGetValue (struct (page, cell)) with
                                        | true, options -> Some options
                                        | _ -> None)
                                    context.Content
                    }
        }
