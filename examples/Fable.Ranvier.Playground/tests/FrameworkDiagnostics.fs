module FrameworkDiagnostics

open global.Ranvier
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

/// <summary>Measures graph, state, definition, DOM mount and disposal stages for one fixture instance.</summary>
let parts (host: HTMLElement) width =
    let times = ResizeArray<string * float>()

    let measure label action =
        let start = globalThis.performance.now ()
        let result = action ()
        times.Add (label, globalThis.performance.now () - start)
        result

    let graph = measure "graph" (fun () -> new Graph ())

    let source, disabled =
        measure "state" (fun () ->
            graph.Run (fun () ->
                let source = Api.createSignal "a"
                source, Api.createMemo (fun _ -> source.Value = "blocked")))

    let view =
        measure "definition" (fun () ->
            Generated.Html.Program.section [||] [| AuthoringPerfFixtures.create "overloaded" width source disabled ignore |])

    let mounted =
        measure "dom-mount" (fun () ->
            Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend))

    measure "renderer-dispose" mounted.Dispose
    measure "graph-dispose" graph.Dispose
    times.ToArray ()

/// <summary>Measures flat mount-lifetime setup and teardown with empty callbacks and releases, excluding DOM work.</summary>
let scopes width =
    use graph = new Graph ()
    let start = globalThis.performance.now ()
    let root = MountLifetime.Root (graph, ignore, ignore)

    graph.Run (fun () ->
        for _ in 1..width do
            let child = root.Child ignore
            child.OnCleanup ignore)

    let built = globalThis.performance.now ()
    root.Dispose ()
    let disposed = globalThis.performance.now ()
    [| "scope-setup", built - start; "scope-dispose", disposed - built |]

/// <summary>Measures component/effect graph setup and teardown without DOM operations or renderer scopes.</summary>
let core width =
    let graph = new Graph ()
    let start = globalThis.performance.now ()

    graph.Run (fun () ->
        let source = Api.createSignal "a"
        let disabled = Api.createMemo (fun _ -> source.Value = "blocked")

        Api.createRoot (fun _ ->
            for _ in 1..width do
                Api.createRoot (fun _ ->
                    let local = Api.createSignal 0
                    Api.createEffect (fun () -> source.Value |> ignore)
                    Api.createEffect (fun () -> source.Value |> ignore)
                    Api.createEffect (fun () -> disabled.Value |> ignore)
                    Api.createEffect (fun () -> local.Value |> ignore))))

    let built = globalThis.performance.now ()
    graph.Dispose ()
    let disposed = globalThis.performance.now ()
    [| "core-setup", built - start; "core-dispose", disposed - built |]

/// <summary>Measures independent component-owner disposal, optionally under one graph batch, followed by graph disposal.</summary>
let coreOwners width batched =
    let graph = new Graph ()
    let owners = ResizeArray<Owner>()

    graph.Run (fun () ->
        let source = Api.createSignal "a"
        let disabled = Api.createMemo (fun _ -> source.Value = "blocked")

        Api.createRoot (fun _ ->
            for _ in 1..width do
                Api.createRoot (fun owner ->
                    owners.Add owner
                    let local = Api.createSignal 0
                    Api.createEffect (fun () -> source.Value |> ignore)
                    Api.createEffect (fun () -> source.Value |> ignore)
                    Api.createEffect (fun () -> disabled.Value |> ignore)
                    Api.createEffect (fun () -> local.Value |> ignore))))

    let start = globalThis.performance.now ()

    let dispose () =
        for owner in owners do
            owner.Dispose ()

    if batched then graph.Run dispose else dispose ()
    graph.Dispose ()
    globalThis.performance.now () - start

/// <summary>Mounts the comparison fixture with optional batching of renderer disposal for diagnostic timing.</summary>
let mountVariant (host: HTMLElement) width batched =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let rows = AuthoringPerfFixtures.create "overloaded" width source disabled ignore
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

            if batched then
                graph.Run mounted.Dispose
            else
                mounted.Dispose ()

            graph.Dispose ()

    update, dispose

let private modifierTokens: Operations<ModifierAction array> =
    {
        Empty = [||]
        Text = fun _ -> invalidOp "Token extraction requires an element"
        Fragment = fun _ -> invalidOp "Token extraction requires an element"
        Element = fun _ tokens _ -> tokens
        Bind = fun _ _ -> invalidOp "Token extraction requires an element"
        Keyed = fun _ _ -> invalidOp "Token extraction requires an element"
        Switch = fun _ _ -> invalidOp "Token extraction requires an element"
        Component = fun _ -> invalidOp "Token extraction requires an element"
        Embed = fun _ -> invalidOp "Token extraction requires an element"
    }

/// <summary>Mounts equivalent typed modifiers directly through the existing owned backend, bypassing per-element Program views.</summary>
let mountDirect (host: HTMLElement) width =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            let backend = context.Backend

            backend.Element (
                HtmlTag "section",
                [||],
                fun () ->
                    for index in 1..width do
                        let container =
                            Generated.Html.div
                                modifierTokens
                                [|
                                    Generated.Html.Attrs.idStatic ("row-" + string index)
                                    Generated.Html.Attrs.classStatic "panel"
                                    Generated.Html.Attrs.titleReactive (fun () -> Some source.Value)
                                |]
                                [||]

                        let input =
                            Generated.Html.input
                                modifierTokens
                                [|
                                    Generated.Html.Properties.HTMLInputElement.valueReactive (fun () -> source.Value)
                                |]

                        let button =
                            [|
                                Generated.Html.Properties.HTMLButtonElement.disabledReactive (fun () -> disabled.Value)
                            |]

                        backend.Component (fun _ ->
                            let local = Api.createSignal 0

                            let button =
                                Generated.Html.button
                                    modifierTokens
                                    (Array.append button [| Generated.Html.Events.onClick (fun _ -> local.Value <- local.Value + 1) |])
                                    [||]

                            backend.Element (
                                Div,
                                container,
                                fun () ->
                                    backend.Element (Input, input, ignore)
                                    backend.Element (Button, button, fun () -> backend.Bind ((=), fun () -> string local.Value))
                            ))
            ))

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

[<Global "document">]
let private document: Document = jsNative

/// <summary>Mounts the comparison fixture through direct DOM construction and one bare effect per binding.</summary>
/// <remarks>Excludes renderer scopes, range ownership, backend dispatch and authoring; all reactive state shares one root.</remarks>
let mountReactiveNative (host: HTMLElement) width =
    let graph = new Graph ()
    let section = document.createElement "section"

    let source =
        graph.Run (fun () ->
            let source = Api.createSignal "a"
            let disabled = Api.createMemo (fun _ -> source.Value = "blocked")

            Api.createRoot (fun _ ->
                for index in 1..width do
                    let local = Api.createSignal 0
                    let row = document.createElement "div"
                    let input = document.createElement "input" :?> HTMLInputElement
                    let button = document.createElement "button" :?> HTMLButtonElement
                    let text = document.createTextNode ""
                    row.setAttribute ("id", "row-" + string index)
                    row.setAttribute ("class", "panel")
                    Api.createEffect (fun () -> row.setAttribute ("title", source.Value))
                    Api.createEffect (fun () -> input.value <- source.Value)
                    Api.createEffect (fun () -> button.disabled <- disabled.Value)
                    Api.createEffect (fun () -> text.textContent <- string local.Value)

                    let clicked: EventListener =
                        fun _ -> graph.Run (fun () -> local.Value <- local.Value + 1)

                    let callback: EventListenerOrEventListenerObject = !^clicked
                    (button :> EventTarget).addEventListener("click", callback)
                    button.appendChild text |> ignore
                    row.appendChild input |> ignore
                    row.appendChild button |> ignore
                    section.appendChild row |> ignore)

            source)

    host.appendChild section |> ignore
    let mutable live = true

    let update value =
        if live then
            graph.Run (fun () -> source.Value <- value)

    let dispose () =
        if live then
            live <- false
            section.remove ()
            graph.Dispose ()

    update, dispose
