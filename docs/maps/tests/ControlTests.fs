module Ranvier.Docs.Maps.Tests.ControlTests

open Expecto

#if RANVIER_TRACE
open System.Threading.Tasks
open Ranvier.Docs.Maps
open Ranvier

let private outcome (t: Task<'T>) =
    if t.IsCanceled then
        "cancelled"
    elif t.IsFaulted then
        "failed: " + t.Exception.InnerException.Message
    elif t.IsCompleted then
        $"settled: %A{t.Result}"
    else
        "pending"

[<Tests>]
let tests =
    testList
        "Controls"
        [
            test "a button has one silent step that runs its action" {
                let mutable pressed = 0
                let c = button "Add" (fun () -> pressed <- pressed + 1)
                Expect.equal c.Label "Add" "the label"
                Expect.equal (c.Steps |> List.map _.Log) [ None ] "one step, no log line"
                c.Steps |> List.iter (fun s -> s.Run ())
                Expect.equal pressed 1 "the step presses the button"
            }

            test "descriptions preserve controls and accompany every replay value" {
                let written = ResizeArray<int>()

                let c =
                    slider "Qty" (1, 10) 1 [ 3; 5 ] written.Add
                    |> describe "The subtotal follows quantity."

                Expect.equal c.Label "Qty" "the widget label stays short"

                Expect.equal
                    (c.Steps |> List.map _.Caption)
                    [ Some "The subtotal follows quantity."; Some "The subtotal follows quantity." ]
                    "a caption per action"

                c.Steps |> List.iter (fun s -> s.Run ())
                Expect.equal (List.ofSeq written) [ 3; 5 ] "the actions still run"
            }

            test "expectations run after actions and fail with the author's message" {
                let mutable value = 0

                let c =
                    button "Set" (fun () -> value <- 2)
                    |> expect "value is two" (fun () -> value = 2)
                    |> expect "value stays below three" (fun () -> value < 3)

                let step = c.Steps.Head
                Expect.throwsC (fun () -> Controls.check step) (fun ex -> Expect.stringContains ex.Message "value is two" "the failed expectation")
                step.Run ()
                Controls.check step
                value <- 3

                Expect.throwsC (fun () -> Controls.check step) (fun ex ->
                    Expect.stringContains ex.Message "value is two" "checks remain in author order")
            }

            test "captions follow scrubbing and clear for undescribed actions" {
                let marks = [ 4, Some "Write"; 8, Some "Read"; 12, None ]
                Expect.equal (Replay.captionAt marks 3) None "setup has no action caption"
                Expect.equal (Replay.captionAt marks 6) (Some "Write") "keep the caption through the action's events"
                Expect.equal (Replay.captionAt marks 8) (Some "Read") "a new action replaces it"
                Expect.equal (Replay.captionAt marks 12) None "an undescribed action clears it"
                Expect.equal (Replay.captionAt marks 4) (Some "Write") "scrubbing backwards restores it"
            }

            test "replay verification checks async results after continuations settle" {
                let scenario (_: Graph) =
                    let desk = Desk<int>()
                    let quote = createAsync (fun _ _ -> desk.Quote ())
                    createEffect (fun () -> quote.TryValue |> ignore)

                    controls
                        [
                            button "Answer" (fun () -> desk.Settle 4)
                            |> expect "quote settles at four" (fun () -> quote.Peek = 4)
                        ]

                Replay.verify "async example" FlightPolicy.CancelPrevious scenario
                |> Async.RunSynchronously
            }

            test "replay verification reports setup failures at the source fence" {
                Expect.throwsC
                    (fun () ->
                        Replay.verify "guide/example.md:12" FlightPolicy.CancelPrevious (fun _ -> failwith "broken setup")
                        |> Async.RunSynchronously)
                    (fun ex ->
                        Expect.stringContains ex.Message "guide/example.md:12" "source location"
                        Expect.stringContains ex.Message "setup" "setup context")
            }

            test "replay verification reports the scenario and control on failure" {
                let scenario (_: Graph) =
                    controls
                        [
                            button "Write" ignore
                            |> expect "wrong result" (fun () -> false)
                        ]

                Expect.throwsC
                    (fun () ->
                        Replay.verify "guide/example.md:12" FlightPolicy.CancelPrevious scenario
                        |> Async.RunSynchronously)
                    (fun ex ->
                        Expect.stringContains ex.Message "guide/example.md:12" "source location"
                        Expect.stringContains ex.Message "Write" "control label"
                        Expect.stringContains ex.Message "wrong result" "expectation")
            }

            test "a slider has a step per replay value, in order" {
                let written = ResizeArray<int>()
                let c = slider "Qty" (1, 10) 1 [ 3; 5 ] written.Add
                Expect.equal (c.Steps |> List.map _.Log) [ Some "set Qty = 3"; Some "set Qty = 5" ] "one line per value"
                c.Steps |> List.iter (fun s -> s.Run ())
                Expect.equal (List.ofSeq written) [ 3; 5 ] "3 then 5"

                match c.Widget with
                | Slider (1, 10, 1, _) -> ()
                | other -> failtestf "widget: %A" other
            }

            test "an input with no replay values has no steps" {
                Expect.isEmpty (text "Name" "Ada" [] ignore).Steps "text"
                Expect.isEmpty (toggle "Gift" false [] ignore).Steps "toggle"
                Expect.isEmpty (number "Price" 4.0 [] ignore).Steps "number"
            }

            test "number, text and toggle steps pass their values" {
                let seen = ResizeArray<string>()

                let steps =
                    (number "Price" 4.0 [ 6.5 ] (fun v -> seen.Add (string v))).Steps
                    @ (text "Name" "Ada" [ "Grace" ] seen.Add).Steps
                    @ (toggle "Gift" false [ true ] (fun b -> seen.Add (string b))).Steps

                steps |> List.iter (fun s -> s.Run ())
                Expect.equal (List.ofSeq seen) [ "6.5"; "Grace"; "True" ] "each value"
                Expect.equal (steps |> List.map _.Log) [ Some "set Price = 6.5"; Some "set Name = Grace"; Some "set Gift = true" ] "each line"
            }

            test "a number field reads only numbers" {
                Expect.equal (Controls.parseNumber "6.5") (Some 6.5) "a number"
                Expect.equal (Controls.parseNumber " 7 ") (Some 7.0) "padded"
                Expect.equal (Controls.parseNumber "") None "empty"
                Expect.equal (Controls.parseNumber "abc") None "text"
            }

            test "a latest-wins desk cancels the older request" {
                let desk = Desk<int>()
                let first = desk.Quote ()
                let second = desk.Quote ()
                Expect.equal (outcome first) "cancelled" "the older request"
                Expect.equal desk.Pending 1 "one waiting"
                desk.SettleNewest 2
                Expect.equal (outcome second) "settled: 2" "newest and pending are the same request"
                Expect.equal desk.Pending 0 "none waiting"
            }

            test "a queued desk answers the oldest, or the newest on request" {
                let desk = Desk<int>(queued = true)
                let a = desk.Quote ()
                let b = desk.Quote ()
                let c = desk.Quote ()
                Expect.equal desk.Pending 3 "three waiting"
                desk.SettleNewest 3
                Expect.equal (outcome c) "settled: 3" "the newest"
                desk.Settle 1
                Expect.equal (outcome a) "settled: 1" "the oldest"
                desk.Fail "down"
                Expect.equal (outcome b) "failed: down" "the one left"
                Expect.equal desk.Pending 0 "none waiting"
            }

            test "answering an empty desk leaves it empty" {
                for desk in [ Desk<int>(); Desk<int>(queued = true) ] do
                    desk.Settle 1
                    desk.Fail "x"
                    desk.SettleNewest 1
                    desk.FailNewest "x"
                    Expect.equal desk.Pending 0 "still empty"
                    let t = desk.Quote ()
                    Expect.equal (outcome t) "pending" "a later request is unaffected"
            }
        ]
#endif
