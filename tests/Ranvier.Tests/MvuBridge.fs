module Ranvier.Tests.MvuBridge

open Expecto
open Ranvier
open Ranvier.Elmish

type private Address = { City: string; Zip: string }

type private Model =
    {
        Count: int
        Name: string
        Address: Address
    }

type private Msg =
    | Increment
    | Rename of string
    | Move of string
    | Nothing
    | IncrementLater

let private init =
    {
        Count = 0
        Name = "Ada"
        Address = { City = "Bergen"; Zip = "5003" }
    }

let private update msg model =
    match msg with
    | Increment
    | IncrementLater -> { model with Count = model.Count + 1 }
    | Rename name -> { model with Name = name }
    | Move city ->
        { model with
            Address = { model.Address with City = city }
        }
    | Nothing -> model

/// <summary>Counts the runs of an effect that reads <c>read</c>.</summary>
let private countRuns (read: unit -> 'a) =
    let runs = ref 0

    createEffect (fun () ->
        runs.Value <- runs.Value + 1
        read () |> ignore)

    runs

[<Tests>]
let tests =
    testList
        "MvuBridge"
        [
            test "Dispatch applies update to the model" {
                use g = new Graph ()
                use _ = g.Activate ()
                let app = Mvu.create init update

                app.Dispatch Increment
                app.Dispatch (Rename "Grace")

                Expect.equal app.Model.Count 1 "the count"
                Expect.equal app.Model.Name "Grace" "the name"
            }

            test "an update that returns its argument wakes nothing" {
                use g = new Graph ()
                use _ = g.Activate ()
                let app = Mvu.create init update
                let runs = countRuns (fun () -> app.Model)

                app.Dispatch Nothing

                Expect.equal runs.Value 1 "the model reader stays asleep"
            }

            test "a selector wakes its readers only when its part changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let app = Mvu.create init update
                let count = app.Select _.Count
                let name = app.Select _.Name
                let countRuns' = countRuns (fun () -> count.Value)
                let nameRuns = countRuns (fun () -> name.Value)

                app.Dispatch Increment
                app.Dispatch Increment

                Expect.equal count.Value 2 "the selected count"
                Expect.equal countRuns'.Value 3 "the count reader runs per change"
                Expect.equal nameRuns.Value 1 "the name reader stays asleep"
            }

            test "a nested selector re-runs only when its parent memo changes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let app = Mvu.create init update
                let address = app.Select _.Address
                let cityRuns = ref 0

                let city =
                    createMemo (fun _ ->
                        cityRuns.Value <- cityRuns.Value + 1
                        address.Value.City)

                countRuns (fun () -> city.Value) |> ignore

                app.Dispatch Increment
                app.Dispatch (Rename "Grace")
                Expect.equal cityRuns.Value 1 "an unrelated write leaves the nested selector alone"

                app.Dispatch (Move "Oslo")
                Expect.equal city.Value "Oslo" "the city"
                Expect.equal cityRuns.Value 2 "a change to the parent re-runs it"
            }

            test "commands run after the write, with Dispatch" {
                use g = new Graph ()
                use _ = g.Activate ()
                let seen = ResizeArray<int>()

                let app =
                    Mvu.withCmd (init, []) (fun msg model ->
                        let next = update msg model

                        match msg with
                        | IncrementLater -> next, [ (fun dispatch -> seen.Add next.Count); (fun dispatch -> dispatch Increment) ]
                        | _ -> next, [])

                app.Dispatch IncrementLater

                Expect.sequenceEqual seen [ 1 ] "the command sees the written model"
                Expect.equal app.Model.Count 2 "the dispatched message is applied"
            }

            test "initial commands run before withCmd returns" {
                use g = new Graph ()
                use _ = g.Activate ()

                let app =
                    Mvu.withCmd (init, [ fun dispatch -> dispatch (Rename "Grace") ]) (fun msg model -> update msg model, [])

                Expect.equal app.Model.Name "Grace" "the initial command's message is applied"
            }

            test "a dispatch from an effect leaves the effect unsubscribed from the model" {
                use g = new Graph ()
                use _ = g.Activate ()
                let app = Mvu.create init update
                let trigger = createSignal 0
                let runs = ref 0

                createEffect (fun () ->
                    runs.Value <- runs.Value + 1

                    if trigger.Value > 0 then
                        app.Dispatch Increment)

                trigger.Value <- 1
                app.Dispatch (Rename "Grace")

                Expect.equal app.Model.Count 1 "the effect's dispatch is applied"
                Expect.equal runs.Value 2 "the effect runs for the trigger only"
            }

#if !FABLE_COMPILER
            // .NET only: JavaScript has one thread.
            test "a dispatch from another thread is queued until the graph pumps" {
                use g =
                    new Graph (
                        { GraphOptions.Default with
                            Dispatcher = Some (ManualDispatcher ())
                        }
                    )

                use _ = g.Activate ()
                let app = Mvu.create init update

                let thread = System.Threading.Thread (fun () -> app.Dispatch Increment)
                thread.Start ()
                thread.Join ()

                Expect.equal app.Model.Count 0 "queued"
                g.Pump () |> ignore
                Expect.equal app.Model.Count 1 "applied by the pump"
            }
#endif
        ]
