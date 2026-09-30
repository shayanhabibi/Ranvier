module Ranvier.Docs.Maps.Tests.AuthoringTests

open System
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
            "    button \"Add tea\" (fun () -> lines.Value <- [ 4m; 4m ])"
            "    button \"Settle quote\" (fun () -> desk.Settle 5m)"
            "]"
        ]

let private scenario code =
    match MapFence.scenario "cart" code with
    | Ok result -> result
    | Error problems -> failtestf "rejected: %A" problems

/// The fence line a generated line maps back to, as the Solid plugin maps it.
let private locate (spans: MapSpan list) (line: int) =
    spans
    |> List.tryFind (fun s ->
        line >= s.Generated
        && line < s.Generated + s.Length)
    |> Option.map (fun s ->
        if s.Indent = Int32.MaxValue then
            0
        else
            s.Body + line - s.Generated)

[<Tests>]
let tests =
    testList
        "MapFence"
        [
            test "a label follows the whole of a multi-line binding" {
                let code, _, bindings = scenario cart
                let lines = code.Split '\n'

                let at =
                    lines
                    |> Array.findIndex (fun l -> l.Contains "Trace.label (graph', subtotal")

                Expect.equal (lines[at - 1].Trim()) "lines.Value |> List.sum)" "the label comes after the binding's last line"
                Expect.contains bindings ("subtotal", 4, 6) "the binding spans its lines"
            }

            test "a one-line lookup binding runs in Trace.named" {
                let code, _, bindings =
                    scenario (
                        String.concat
                            "
"
                            [
                                "let selected = createSignal 1"
                                "let isSelected = createSelector (fun () -> selected.Value)"
                                "controls []"
                            ]
                    )

                Expect.stringContains
                    code
                    "let isSelected = Trace.named \"isSelected\" (fun () -> createSelector (fun () -> selected.Value))"
                    "the lookup is named through its owner"

                Expect.isFalse (code.Contains "Trace.label (graph', isSelected") "a lookup is no node"
                Expect.contains bindings ("isSelected", 2, 2) "the binding is still highlighted"
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

                let label =
                    lines
                    |> Array.findIndex (fun l -> l.Contains "Trace.label (graph', subtotal")

                Expect.equal (locate spans (label + 1)) (Some 6) "a label blames the binding's last line"
            }

            test "a fence without trailing controls is rejected at its last line" {
                let code = "let a = createSignal 1\nlet b = createMemo (fun _ -> a.Value)\n\n"

                Expect.equal
                    (MapFence.scenario "x" code
                     |> Result.mapError (List.map fst))
                    (Error [ 2 ])
                    "the problem sits at the last line of code"
            }

            test "live renders the scenario" {
                match MapFence.compile "cart-live" [] cart with
                | Ok output ->
                    Expect.stringContains output.Code "module Map_cart_live =" "a module per cell"

                    Expect.stringContains
                        output.Render
                        "SignalMap (Ranvier.Docs.Maps.Live Map_cart_live.scenario) Ranvier.FlightPolicy.CancelPrevious [| (\"lines\", 2, 2); (\"subtotal\", 4, 6);"
                        "the scenario and its bindings"

                    Expect.stringEnds output.Render "|] false Ranvier.Docs.Maps.Grouping.Expand" "no timeline, collections expanded"
                | Error problems -> failtestf "rejected: %A" problems
            }

            test "replay renders the scenario, replayed" {
                match MapFence.compile "cart" [ "replay" ] cart with
                | Ok output ->
                    Expect.stringContains output.Code "let scenario (graph': Graph) : Control list =" "the scenario module"
                    Expect.stringContains output.Render "Ranvier.Docs.Maps.Replayed Map_cart.scenario" "a replayed source"
                    Expect.stringContains output.Render "|] true " "with the timeline"
                | Error problems -> failtestf "rejected: %A" problems
            }

            test "the policy defaults to cancel-previous" {
                match MapFence.compile "cart" [] cart with
                | Ok output -> Expect.stringContains output.Render ") Ranvier.FlightPolicy.CancelPrevious [|" "the default"
                | Error problems -> failtestf "rejected: %A" problems
            }

            test "policy=queue, policy=keep-latest and policy=finish-current render their policies" {
                for flag, policy in
                    [
                        "policy=queue", "Queue"
                        "policy=keep-latest", "KeepLatest"
                        "policy=finish-current", "FinishCurrent"
                    ] do
                    match MapFence.compile "cart" [ flag ] cart with
                    | Ok output -> Expect.stringContains output.Render $"Ranvier.FlightPolicy.%s{policy} [|" flag
                    | Error problems -> failtestf "rejected: %A" problems
            }

            test "an unknown or empty policy is rejected at the opening line" {
                for flag in [ "policy=fifo"; "policy=" ] do
                    match MapFence.compile "cart" [ flag ] cart with
                    | Error [ 0, message ] -> Expect.stringContains message "cancel-previous, keep-latest, queue or finish-current" flag
                    | other -> failtestf "%s: %A" flag other
            }

            test "groups=collapse renders collapsed collections, and an unknown grouping is rejected" {
                match MapFence.compile "cart" [ "groups=collapse" ] cart with
                | Ok output -> Expect.stringEnds output.Render " Ranvier.Docs.Maps.Grouping.Collapse" "collapsed"
                | Error problems -> failtestf "rejected: %A" problems

                match MapFence.compile "cart" [ "groups=nest" ] cart with
                | Error [ 0, message ] -> Expect.stringContains message "expand or collapse" "the problem names the choices"
                | other -> failtestf "%A" other
            }

            test "a fence of buttons and inputs generates" {
                let code =
                    String.concat
                        "\n"
                        [
                            "let qty = createSignal 1"
                            "let total = createMemo (fun _ -> 4 * qty.Value)"
                            ""
                            "controls ["
                            "    slider \"Qty\" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)"
                            "    button \"Reset qty\" (fun () -> qty.Value <- 1)"
                            "]"
                        ]

                match MapFence.compile "inputs" [ "timeline" ] code with
                | Ok output ->
                    Expect.stringContains output.Code "        slider \"Qty\" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)" "the controls as written"
                    Expect.stringContains output.Render "[| (\"qty\", 1, 1); (\"total\", 2, 2) |] true " "bindings and timeline"
                | Error problems -> failtestf "rejected: %A" problems
            }
        ]
