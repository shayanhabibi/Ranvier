module FrameworkPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Generated.Html.Authoring

module H = Ranvier.View.Foundation.Generated.Html

type private PerfProps =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
    }

type private PerfRow =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
        Local: Signal<int>
    }

let private perfRow =
    Template.Component.define
        (fun _ (props: PerfProps) ->
            {
                Index = props.Index
                Source = props.Source
                Disabled = props.Disabled
                Local = Api.createSignal 0
            })
        (fun t ->
            t.div
                [|
                    t.Attrs.id (fun r -> "row-" + string r.Index)
                    t.Attrs.className "panel"
                    t.Attrs.title (t.from (fun r -> r.Source))
                |]
                [|
                    t.input [| t.Props.Input.value (t.from (fun r -> r.Source)) |]
                    t.button
                        [|
                            t.Props.Button.disabled (t.from (fun r -> r.Disabled))
                            t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)
                        |]
                        [| t.text (fun r -> string r.Local.Value) |]
                |])

let private rowTemplate =
    Prepared.prepare (H.Program.div [| H.Attrs.classStatic "panel" |] [| H.Program.input [||]; H.Program.button [||] [| Program.text "0" |] |])

let private preparedRows width (source: Signal<string>) (disabled: Memo<bool>) =
    let title = Attrs.title source
    let inputModifiers = [| Props.Input.value source |]
    let disabledModifier = Props.Button.disabled disabled

    let setup _ index (root: Fable.Core.TS.Dom.HTMLElement) backend =
        let local = Api.createSignal 0
        let input = root.firstChild.Value :?> Fable.Core.TS.Dom.HTMLInputElement
        let button = input.nextSibling.Value :?> Fable.Core.TS.Dom.HTMLButtonElement
        Prepared.apply root [| Attrs.id ("row-" + string index); title |] backend
        Prepared.apply input inputModifiers backend

        Prepared.apply
            button
            [|
                disabledModifier
                H.Events.onClick (fun _ -> local.Value <- local.Value + 1)
            |]
            backend

        Prepared.text button.firstChild.Value (fun () -> string local.Value) backend

    Program.fragment (Array.init width (fun index -> Prepared.createComponent rowTemplate setup (index + 1)))

let private mountCore rows (host: HTMLElement) width =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let rows = rows width source disabled
    let view = Generated.Html.Program.section [||] [| rows |]

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    let mutable live = true

    let update value =
        if live then
            graph.Run (fun () -> source.Value <- value)

    let dispose () =
        if live then
            live <- false
            mounted.Dispose ()
            graph.Dispose ()

    update, dispose

/// <summary>Mounts the overloaded fixture with its own graph and returns update and disposal callbacks.</summary>
let mount host width =
    mountCore (fun width source disabled -> AuthoringPerfFixtures.create "overloaded" width source disabled ignore) host width

/// <summary>Mounts equivalent components through a shared static skeleton and instance bindings.</summary>
let mountPrepared host width =
    mountCore preparedRows host width

/// <summary>Mounts equivalent rows as template component instances.</summary>
let mountTemplate host width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        perfRow
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width
