module Ranvier.Docs.Maps.Tests.AuthoringTests

open System
open System.IO
open Expecto
open Ranvier.Docs.Maps.Authoring

let private cart =
    String.concat
        "\n"
        [
            "let desk = Desk<decimal>()"
            "let lines = createSignal [ 4m ]"
            ""
            "let subtotal ="
            "    createMemo (fun _ ->"
            "        lines.Value |> List.sum)"
            ""
            "// the quote waits on the desk"
            "let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)"
            "let total = createMemo (fun _ -> subtotal.Value + shipping.Value)"
            "createEffect (fun () -> total.TryValue |> ignore)"
            ""
            "controls ["
            "    \"Add tea\", fun () -> lines.Value <- [ 4m; 4m ]"
            "    \"Settle quote\", fun () -> desk.Settle 5m"
            "]"
        ]

let private scenario code =
    match MapFence.scenario "cart" code with
    | Ok result -> result
    | Error problems -> failtestf "rejected: %A" problems

/// The fence line a generated line maps back to, as the Solid plugin maps it.
let private locate (spans: MapSpan list) (line: int) =
    spans
    |> List.tryFind (fun s -> line >= s.Generated && line < s.Generated + s.Length)
    |> Option.map (fun s -> if s.Indent = Int32.MaxValue then 0 else s.Body + line - s.Generated)

/// The directory holding Ranvier.slnx, from RANVIER_ROOT or above the test assembly.
let private root () =
    match Environment.GetEnvironmentVariable "RANVIER_ROOT" with
    | null
    | "" ->
        let rec up (dir: DirectoryInfo) =
            if File.Exists(Path.Combine(dir.FullName, "Ranvier.slnx")) then
                dir.FullName
            else
                up dir.Parent

        up (DirectoryInfo AppContext.BaseDirectory)
    | path -> path

[<Tests>]
let tests =
    testList
        "MapFence"
        [
            test "a label follows the whole of a multi-line binding" {
                let code, _, bindings = scenario cart
                let lines = code.Split '\n'
                let at = lines |> Array.findIndex (fun l -> l.Contains "Trace.label (graph', subtotal")
                Expect.equal (lines[at - 1].Trim()) "lines.Value |> List.sum)" "the label comes after the binding's last line"
                Expect.contains bindings ("subtotal", 4, 6) "the binding spans its lines"
            }

            test "bindings of nodes are labelled, effects and plain values are not" {
                let code, _, bindings = scenario cart
                let names = bindings |> List.map (fun (n, _, _) -> n)
                Expect.equal names [ "lines"; "subtotal"; "shipping"; "total" ] "each node, in order"
                Expect.isFalse (code.Contains "Trace.label (graph', desk") "the desk is no node"
                Expect.stringContains code "        createEffect (fun () -> total.TryValue |> ignore)" "the effect stays as written"
            }

            test "spans map every author line back to itself" {
                let code, spans, _ = scenario cart
                let lines = code.Split '\n'
                let author = cart.Split '\n'

                for i in 0 .. lines.Length - 1 do
                    match locate spans (i + 1) with
                    | Some body when body > 0 && not (lines[i].Contains "Trace.label") ->
                        Expect.equal (lines[i].Trim()) (author[body - 1].Trim()) $"generated line %d{i + 1}"
                    | Some _ -> ()
                    | None -> failtestf "generated line %d has no span" (i + 1)

                let label = lines |> Array.findIndex (fun l -> l.Contains "Trace.label (graph', subtotal")
                Expect.equal (locate spans (label + 1)) (Some 6) "a label blames the binding's last line"
            }

            test "a fence without trailing controls is rejected at its last line" {
                let code = "let a = createSignal 1\nlet b = createMemo (fun _ -> a.Value)\n\n"

                Expect.equal
                    (MapFence.scenario "x" code |> Result.mapError (List.map fst))
                    (Error [ 2 ])
                    "the problem sits at the last line of code"
            }

            test "replay renders the recording" {
                let flags = MapFlags.parse [ "replay" ]
                Expect.isTrue flags.Timeline "replay implies the timeline"

                match MapFence.generate "cart" flags (Some "[|\n    Replay.event 1 1 1 0 1 0 0 null\n|]") cart with
                | Ok output ->
                    Expect.stringContains output.Render "Ranvier.Docs.Maps.Recorded Map_cart.events" "a recorded source"
                    Expect.stringContains output.Render "|] true" "with the timeline"
                    Expect.stringContains output.Code "        Replay.event 1 1 1 0 1 0 0 null" "the literal sits in the module"
                | Error problems -> failtestf "rejected: %A" problems

                Expect.isError (MapFence.generate "cart" flags None cart) "a replay needs its recording"
            }

            test "live renders the scenario" {
                match MapFence.generate "cart-live" (MapFlags.parse []) None cart with
                | Ok output ->
                    Expect.stringContains output.Code "module Map_cart_live =" "a module per cell"

                    Expect.stringContains
                        output.Render
                        "SignalMap (Ranvier.Docs.Maps.Live Map_cart_live.scenario) [| (\"lines\", 2, 2); (\"subtotal\", 4, 6);"
                        "the scenario and its bindings"

                    Expect.stringEnds output.Render "|] false" "no timeline"
                | Error problems -> failtestf "rejected: %A" problems
            }

            test "record runs the scenario and prints its events" {
                let dir = root ()
                let code, _, _ = scenario cart

                let settings =
                    {
                        Ranvier = typeof<Ranvier.Graph>.Assembly.Location
                        Sources =
                            [ "Helpers.fs"; "Replay.fs" ]
                            |> List.map (fun f -> Path.Combine(dir, "docs", "maps", "model", f))
                        Cache = Path.Combine(Path.GetTempPath(), "ranvier-maps-replay-tests", string (Guid.NewGuid()))
                    }

                match ReplayRunner.record settings code (MapFence.moduleName "cart") with
                | Ok literal ->
                    Expect.stringStarts literal "[|" "an array literal"
                    Expect.stringContains literal "Replay.event" "of events"
                    Expect.stringContains literal "\"5M\"" "the settled quote"
                    Expect.equal (ReplayRunner.record settings code (MapFence.moduleName "cart")) (Ok literal) "the second run reads the cache"
                | Error output -> failtestf "fsi failed:\n%s" output
            }

            test "record runs each control with the graph active" {
                let dir = root ()

                let batched =
                    "let a = createSignal 0\ncreateEffect (fun () -> printfn \"a %d\" a.Value)\n\ncontrols [\n    \"Batch\", fun () -> batch (fun () -> a.Value <- 7)\n]"

                let code, _, _ = scenario batched

                let settings =
                    {
                        Ranvier = typeof<Ranvier.Graph>.Assembly.Location
                        Sources =
                            [ "Helpers.fs"; "Replay.fs" ]
                            |> List.map (fun f -> Path.Combine(dir, "docs", "maps", "model", f))
                        Cache = Path.Combine(Path.GetTempPath(), "ranvier-maps-replay-tests", string (Guid.NewGuid()))
                    }

                match ReplayRunner.record settings code (MapFence.moduleName "cart") with
                | Ok literal -> Expect.stringContains literal "\"7\"" "the batched write"
                | Error output -> failtestf "fsi failed:\n%s" output
            }
        ]
