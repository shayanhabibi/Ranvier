namespace Ranvier.Docs.Maps.Authoring

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text

/// <summary>Where a replay script finds traced Ranvier and the map helpers, and where recordings are kept.</summary>
type ReplaySettings =
    {
        /// <summary>A traced build of <c>Ranvier.dll</c>.</summary>
        Ranvier: string
        /// <summary><c>Helpers.fs</c> and <c>Replay.fs</c>, in load order.</summary>
        Sources: string list
        /// <summary>The recordings, one file per script hash.</summary>
        Cache: string
    }

[<RequireQualifiedAccess>]
module ReplayRunner =

    let private marker = "// ---- replay ----"

    let private script (settings: ReplaySettings) (scenarioModule: string) (moduleName: string) =
        let loads =
            settings.Sources
            |> List.map (fun path -> $"#load @\"%s{path}\"")
            |> String.concat "\n"

        $"""#r @"%s{settings.Ranvier}"
%s{loads}

%s{scenarioModule}

let graph = new Ranvier.Graph ()

for control in %s{moduleName}.scenario graph do
    control.Run ()

printfn "%%s" "%s{marker}"
printfn "%%s" (Ranvier.Docs.Maps.Replay.literal (Ranvier.Trace.events graph))
"""

    let private hash (script: string) (settings: ReplaySettings) =
        use sha = SHA256.Create()
        let parts = Encoding.UTF8.GetBytes script :: List.map File.ReadAllBytes (settings.Ranvier :: settings.Sources)
        let all = Array.concat parts
        Convert.ToHexString(sha.ComputeHash all).Substring(0, 16).ToLowerInvariant()

    let private run (path: string) =
        let info =
            ProcessStartInfo("dotnet", $"fsi --quiet --define:RANVIER_TRACE \"%s{path}\"")

        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.UseShellExecute <- false
        use proc = Process.Start info
        let error = proc.StandardError.ReadToEndAsync()
        let output = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit()
        proc.ExitCode, output, error.Result

    /// <summary>
    /// The <c>Replay.literal</c> of the events a scenario records when every control runs once, in order. Recordings
    /// are cached by a hash of the script, the sources and the DLL.
    /// </summary>
    /// <param name="settings">The DLL, sources and cache.</param>
    /// <param name="scenarioModule">The module from <c>MapFence.scenario</c>.</param>
    /// <param name="moduleName">The module's name.</param>
    /// <returns>The literal, or the <c>fsi</c> output of a failed run.</returns>
    let record (settings: ReplaySettings) (scenarioModule: string) (moduleName: string) : Result<string, string> =
        let script = script settings scenarioModule moduleName
        let key = hash script settings
        let cached = Path.Combine(settings.Cache, key + ".txt")

        if File.Exists cached then
            Ok(File.ReadAllText cached)
        else
            Directory.CreateDirectory settings.Cache |> ignore
            let path = Path.Combine(settings.Cache, key + ".fsx")
            File.WriteAllText(path, script)
            let code, output, error = run path
            let at = output.IndexOf marker

            if code <> 0 || at < 0 then
                Error(String.concat "\n" [ error.Trim(); output.Trim() ])
            else
                let literal = output.Substring(at + marker.Length).Trim()
                File.WriteAllText(cached, literal)
                Ok literal
