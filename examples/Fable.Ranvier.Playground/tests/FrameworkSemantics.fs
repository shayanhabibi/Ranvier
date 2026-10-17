module FrameworkSemantics

open global.Ranvier
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Authoring
open Ranvier.View.Foundation.Representation

module H = Ranvier.View.Foundation.Generated.Html

/// <summary>Mounts an input whose value is bound to a signal written by its own input handler, and returns its disposal.</summary>
let caret (host: HTMLElement) =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "")

    let view =
        H.Program.input
            [|
                H.Properties.HTMLInputElement.valueReactive (fun () -> source.Value)
                H.Events.onInput (fun event -> source.Value <- (unbox<HTMLInputElement> event.target).value)
            |]

    let mounted =
        Mounting.mount graph None ignore host None (fun context -> Program.run view context.Backend)

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()

/// <summary>Mounts four rows whose third row fails during mount, by its property setter or by its title reader.</summary>
/// <remarks>
/// The <c>setter</c> kind writes <c>"boom"</c> to the <c>explode</c> property, which the probe page defines to throw.
/// <c>update</c> writes the shared source and returns the number of title reads it caused.
/// </remarks>
let failure (host: HTMLElement) (kind: string) =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let mutable cleaned = 0
    let mutable reads = 0

    let row index =
        createComponent
            (fun context () ->
                context.OnCleanup (fun () -> cleaned <- cleaned + 1)

                let title =
                    H.Attrs.titleReactive (fun () ->
                        reads <- reads + 1

                        if kind = "reader" && index = 3 then
                            failwith "reader"

                        Some source.Value)

                let modifiers =
                    if kind = "setter" && index = 3 then
                        [|
                            title
                            Typed.staticProperty (fun (node: HTMLDivElement) (value: string) -> node?explode <- value) "boom"
                        |]
                    else
                        [| title |]

                H.Program.div modifiers [||])
            ()

    let view = H.Program.section [||] [| for index in 1..4 -> row index |]
    let mutable mounted = None
    let mutable threw = false
    let mutable message = ""

    try
        mounted <- Some (Mounting.mount graph None ignore host None (fun context -> Program.run view context.Backend))
    with error ->
        threw <- true
        message <- error.Message

    {|
        threw = threw
        message = message
        update =
            fun () ->
                let before = reads
                graph.Run (fun () -> source.Value <- "b")
                reads - before
        dispose =
            fun () ->
                mounted |> Option.iter _.Dispose()
                graph.Dispose ()
        cleaned = fun () -> cleaned
    |}
