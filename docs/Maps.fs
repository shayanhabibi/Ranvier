/// The `map` fence: a plain F# scenario rewritten into a live or replayed signal map.
module Docs.Maps

open System
open System.Diagnostics
open System.IO
open Nacara.Plugins
open Ranvier.Docs.Maps.Authoring

let private repo = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let private replaySettings =
    {
        Ranvier = Path.Combine(repo, "src", "Ranvier", "bin", "Debug", "net10.0", "Ranvier.dll")
        Sources =
            [ "Helpers.fs"; "Replay.fs" ]
            |> List.map (fun name -> Path.Combine(repo, "docs", "maps", "model", name))
        Cache = Path.Combine(__SOURCE_DIRECTORY__, ".nacara", "maps-replay")
    }

/// The traced Debug build of Ranvier that replay scripts reference, built once per run.
let private tracedRanvier =
    lazy
        (let project = Path.Combine(repo, "src", "Ranvier", "Ranvier.fsproj")

         let info =
            ProcessStartInfo("dotnet", $"build \"%s{project}\" -c Debug -p:RanvierTrace=true --nologo -v q")

         info.RedirectStandardOutput <- true
         info.RedirectStandardError <- true
         info.UseShellExecute <- false
         use proc = Process.Start info
         let error = proc.StandardError.ReadToEndAsync()
         let output = proc.StandardOutput.ReadToEnd()
         proc.WaitForExit()

         if proc.ExitCode = 0 then
             Ok()
         else
             Error(output + error.Result))

let private spans (spans: MapSpan list) : SolidLineSpan list =
    spans
    |> List.map (fun s ->
        {
            Generated = s.Generated
            Length = s.Length
            Body = s.Body
            Indent = s.Indent
        })

let private recording (input: SolidTransformInput) =
    match MapFence.scenario input.CellId input.Code with
    | Error problems -> Error problems
    | Ok(scenario, _, _) ->
        tracedRanvier.Force()
        |> Result.bind (fun () -> ReplayRunner.record replaySettings scenario (MapFence.moduleName input.CellId))
        |> Result.map Some
        |> Result.mapError (fun output -> [ 1, "The replay failed:\n" + output ])

/// <summary>Compiles a <c>map</c> fence into a <c>SignalMap</c> of its scenario.</summary>
let transform (input: SolidTransformInput) : SolidTransformOutput =
    let flags = MapFlags.parse input.Flags

    let recorded =
        if flags.Replay then recording input else Ok None

    match recorded |> Result.bind (fun recorded -> MapFence.generate input.CellId flags recorded input.Code) with
    | Ok output ->
        SolidTransformOutput.Compiled
            {
                Code = output.Code
                Render = output.Render
                Spans = spans output.Spans
            }
    | Error problems -> SolidTransformOutput.Rejected problems
