module ComponentsExample

open global.Ranvier
open Ranvier.View.Foundation.Authoring
open Ranvier.View.Foundation.Components
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Generated.Html.Authoring
open Fable.Core.TS.Dom

module Html = Ranvier.View.Foundation.Generated.Html.Program
module Events = Ranvier.View.Foundation.Generated.Html.Events

/// <summary>Inputs to a counter instance, including its reactive label and projected child views.</summary>
type CounterProps =
    {
        Id: string
        Label: Prop<string>
        InitialCount: int
        Changed: int -> unit
        Children: Program.View array
    }

/// <summary>Creates a counter with state owned by its mounted instance.</summary>
let counter props =
    createComponent
        (fun _ props ->
            let count = Api.createSignal props.InitialCount

            Html.article
                [|
                    Attrs.id props.Id
                    Attrs.className "panel"
                    Attrs.title (fun () -> Prop.read props.Label)
                |]
                [|
                    Html.h2 [||] [| propText props.Label |]
                    Html.output [| Attrs.id (props.Id + "-count"); Attrs.className "count" |] [| reactiveText (fun () -> string count.Value) |]
                    Html.button
                        [|
                            Attrs.id (props.Id + "-increment")
                            Attrs.className "primary"
                            Attrs.Button.``type`` "button"
                            Events.onClick (fun _ ->
                                count.Value <- count.Value + 1
                                props.Changed count.Value)
                        |]
                        [| text "Increment" |]
                    fragment props.Children
                |])
        props

/// <summary>Creates two counters sharing a label and reporting changes to their parent.</summary>
let view () =
    createComponent
        (fun _ () ->
            let label = Api.createSignal "Shared label"
            let changed = Api.createSignal "No changes yet"
            let renamed = Api.createMemo (fun _ -> label.Value = "Renamed")
            let selected = Api.createSignal false

            Html.section
                [| Attrs.id "component-example" |]
                [|
                    Html.h2 [||] [| text "Reusable components" |]
                    Html.input
                        [|
                            Attrs.id "component-label"
                            Attrs.Input.placeholder "Shared label"
                            Props.Input.value label
                            Events.onInput (fun event -> label.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                        |]
                    Html.button
                        [|
                            Attrs.id "component-rename"
                            Attrs.Button.``type`` "button"
                            Props.Button.disabled renamed
                            Events.onClick (fun _ -> label.Value <- "Renamed")
                        |]
                        [| text "Rename both counters" |]
                    Html.p [| Attrs.id "component-changed" |] [| reactiveText (fun () -> changed.Value) |]
                    Html.input
                        [|
                            Attrs.id "component-checkbox"
                            Attrs.Input.``type`` "checkbox"
                            Props.Input.``checked`` selected
                            Events.onChange (fun event -> selected.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).``checked``)
                        |]
                    Html.output
                        [| Attrs.id "component-checkbox-state" |]
                        [| reactiveText (fun () -> if selected.Value then "Checked" else "Unchecked") |]
                    Html.button
                        [|
                            Attrs.id "component-toggle-checkbox"
                            Attrs.Button.``type`` "button"
                            Events.onClick (fun _ -> selected.Value <- not selected.Value)
                        |]
                        [| text "Toggle checkbox" |]
                    Html.div
                        [| Attrs.className "demo-grid" |]
                        [|
                            counter
                                {
                                    Id = "component-first"
                                    Label = Prop.ofSignal label
                                    InitialCount = 0
                                    Changed = fun value -> changed.Value <- "First changed to " + string value
                                    Children = [| Html.p [||] [| text "First counter child content" |] |]
                                }
                            counter
                                {
                                    Id = "component-second"
                                    Label = Prop.ofSignal label
                                    InitialCount = 10
                                    Changed = fun value -> changed.Value <- "Second changed to " + string value
                                    Children = [| Html.p [||] [| reactiveText (fun () -> "Child sees " + label.Value) |] |]
                                }
                        |]
                |])
        ()
