#load "../authoring/MapFence.fs"

open System.IO
open System.Text.RegularExpressions
open Ranvier.Docs.Maps.Authoring

let docs = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "../.."))
let args = fsi.CommandLineArgs |> Array.skip 1

let content =
    if args.Length > 0 then
        Path.GetFullPath args[0]
    else
        Path.Combine (docs, "content")

let output =
    if args.Length > 1 then
        Path.GetFullPath args[1]
    else
        Path.Combine (docs, ".nacara/map-validation")

Directory.CreateDirectory output |> ignore

let fence =
    Regex (@"^```fsharp\s+map(?<flags>[^\r\n]*)\r?\n(?<body>.*?)^```[ \t]*\r?$", RegexOptions.Multiline ||| RegexOptions.Singleline)

let literate =
    Regex (
        @"^[ \t]*\(\*\*\*\s+map(?<flags>[^\r\n]*?)\s+\*\*\*\)[ \t]*\r?\n(?<body>.*?)(?=^[ \t]*\(\*\*|\z)",
        RegexOptions.Multiline ||| RegexOptions.Singleline
    )

let quote (value: string) =
    System.Text.Json.JsonSerializer.Serialize value

let modules = ResizeArray<string>()
let entries = ResizeArray<string>()

let sources =
    if args.Length = 0 then
        [ content; Path.Combine (docs, "maps/tests/fixtures") ]
    else
        [ content ]

for path in
    sources
    |> Seq.collect (fun directory -> Directory.EnumerateFiles (directory, "*", SearchOption.AllDirectories))
    |> Seq.sort do
    if
        Path.GetExtension path = ".md"
        || Path.GetExtension path = ".fsx"
    then
        let source = File.ReadAllText path

        let examples =
            seq {
                yield! fence.Matches source |> Seq.cast<Match>

                if Path.GetExtension path = ".fsx" then
                    yield! literate.Matches source |> Seq.cast<Match>
            }
            |> Seq.sortBy _.Index

        for matched in examples do
            let body = matched.Groups["body"].Value

            if Regex.IsMatch (body, @"\bexpect\b") then
                let flags =
                    matched.Groups["flags"].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList

                let line =
                    1
                    + (source.Substring (0, matched.Index)
                       |> Seq.filter ((=) '\n')
                       |> Seq.length)

                let location = $"{Path.GetRelativePath(content, path).Replace('\\', '/')}:{line}"
                let cell = $"check{entries.Count}"

                match MapFence.compile cell flags body, MapFlags.parse flags with
                | Ok compiled, Ok parsed ->
                    modules.Add compiled.Code
                    entries.Add ($"({quote location}, Ranvier.FlightPolicy.{parsed.Policy}, {MapFence.moduleName cell}.scenario)")
                | Error problems, _ -> failwithf "%s: %A" location problems
                | _, Error problem -> failwithf "%s: %s" location problem

let model =
    Path.Combine (docs, "maps/model/Ranvier.Docs.MapModel.fsproj")
    |> System.Security.SecurityElement.Escape

File.WriteAllText (
    Path.Combine (output, "Checks.fsproj"),
    $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><LangVersion>latest</LangVersion><RanvierTrace>true</RanvierTrace><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup><Compile Include="Scenarios.fs" /><Compile Include="Main.fs" /></ItemGroup>
  <ItemGroup><ProjectReference Include="{model}" /></ItemGroup>
</Project>"""
)

File.WriteAllText (
    Path.Combine (output, "Scenarios.fs"),
    "namespace MapChecks\n\n"
    + String.concat "\n\n" modules
)

File.WriteAllText (
    Path.Combine (output, "Main.fs"),
    """module MapChecks.Main
open Fable.Core
open Ranvier.Docs.Maps

[<Emit("process.exitCode = $0")>]
let exitCode (_: int) : unit = jsNative

let scenarios = [
"""
    + (entries
       |> Seq.map (fun entry -> "    " + entry)
       |> String.concat "\n")
    + """
]

async {
    let mutable failures = 0
    for location, policy, scenario in scenarios do
        try
            do! Replay.verify location policy scenario
        with ex ->
            failures <- failures + 1
            eprintfn "%s" ex.Message
    printfn "Map expectations: %d scenarios checked, %d failures" scenarios.Length failures
    if failures > 0 then exitCode 1
} |> Async.StartImmediate
"""
)

printfn "Generated checks for %d map scenarios" entries.Count
