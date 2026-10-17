module AuthoringPerfFixtures

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Authoring
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Generated.Html.Authoring

module H = Ranvier.View.Foundation.Generated.Html

/// <summary>Creates equivalent reusable components through explicit or overloaded modifiers.</summary>
let create mode width (source: Signal<string>) (disabled: Memo<bool>) clicked =
    let row index =
        let container, input, button =
            match mode with
            | "explicit" ->
                [|
                    H.Attrs.idStatic ("row-" + string index)
                    H.Attrs.classStatic "panel"
                    H.Attrs.titleReactive (fun () -> Some source.Value)
                |],
                [| H.Properties.HTMLInputElement.valueReactive (fun () -> source.Value) |],
                [| H.Properties.HTMLButtonElement.disabledReactive (fun () -> disabled.Value) |]
            | "overloaded" ->
                [|
                    Attrs.id ("row-" + string index)
                    Attrs.className "panel"
                    Attrs.title source
                |],
                [| Props.Input.value source |],
                [| Props.Button.disabled disabled |]
            | _ -> invalidArg "mode" mode

        createComponent
            (fun _ () ->
                let local = Api.createSignal 0

                H.Program.div
                    container
                    [|
                        H.Program.input input
                        H.Program.button
                            (Array.append
                                button
                                [|
                                    H.Events.onClick (fun _ ->
                                        local.Value <- local.Value + 1
                                        clicked ())
                                |])
                            [| reactiveText (fun () -> string local.Value) |]
                    |])
            ()

    fragment [| for index in 1..width -> row index |]
