module MountingInvestigationChecks

open System
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Authoring
open Ranvier.View.Foundation.Representation

module H = Ranvier.View.Foundation.Generated.Html

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>Checks wide component cleanup while a sibling mount and their shared graph remain live.</summary>
let wideCleanup width =
    use graph = new Graph ()
    let host = globalThis.document.createElement "main"
    let otherHost = globalThis.document.createElement "main"
    let source = graph.Run (fun () -> Api.createSignal "a")
    let cleaned = ResizeArray<int>()
    let inputs = ResizeArray<HTMLInputElement>()
    let buttons = ResizeArray<HTMLButtonElement>()
    let mutable reads = 0
    let mutable clicks = 0

    let rows =
        [|
            for index in 1..width ->
                createComponent
                    (fun context () ->
                        let local = Api.createSignal 0

                        context.OnCleanup (fun () ->
                            equal true (host.childNodes.length > 0.)
                            cleaned.Add index)

                        H.Program.div
                            [||]
                            [|
                                H.Program.input
                                    [|
                                        H.Properties.HTMLInputElement.valueReactive (fun () ->
                                            reads <- reads + 1
                                            source.Value)
                                        Typed.browser (fun (node: HTMLInputElement) ->
                                            inputs.Add node
                                            ignore)
                                    |]
                                H.Program.button
                                    [|
                                        Typed.browser (fun (node: HTMLButtonElement) ->
                                            buttons.Add node
                                            ignore)
                                        H.Events.onClick (fun _ ->
                                            clicks <- clicks + 1
                                            local.Value <- local.Value + 1)
                                    |]
                                    [| reactiveText (fun () -> string local.Value) |]
                            |])
                    ()
        |]

    let view = H.Program.section [||] rows

    let mount host view =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    let mounted = mount host view

    let other =
        mount otherHost (H.Program.span [||] [| reactiveText (fun () -> "still:" + source.Value) |])

    try
        graph.Run (fun () -> source.Value <- "blocked")
        equal (2 * width) reads
        mounted.Dispose ()
        mounted.Dispose ()
        equal [| width .. -1 .. 1 |] (cleaned.ToArray ())
        equal 0. host.childNodes.length

        for button in buttons do
            button.click ()

        equal 0 clicks
        graph.Run (fun () -> source.Value <- "disposed")
        equal (2 * width) reads
        equal true (otherHost.innerHTML.Contains "still:disposed")

        for input in inputs do
            equal "blocked" input.value
    finally
        mounted.Dispose ()
        other.Dispose ()

/// <summary>Checks that a moved generated descendant can be adopted by another mount and outlives its original mount.</summary>
let movedDescendant () =
    use graph = new Graph ()
    let host = globalThis.document.createElement "main"
    let externalHost = globalThis.document.createElement "main"
    let source = graph.Run (fun () -> Api.createSignal "a")

    let view =
        H.Program.div
            [||]
            [|
                H.Program.input [| H.Properties.HTMLInputElement.valueReactive (fun () -> source.Value) |]
            |]

    let mounted =
        Mounting.mount graph None ignore host None (fun context -> Program.run view context.Backend)

    let input =
        host.querySelector "input"
        |> unbox<HTMLInputElement>

    externalHost.appendChild input |> ignore

    let adopted =
        Mounting.mount graph None ignore externalHost None (fun context -> context.Native (input, Mounting.Owned))

    try
        mounted.Dispose ()
        equal 0. host.childNodes.length

        equal
            true
            (input.parentNode
             |> Option.exists (fun parent -> obj.ReferenceEquals (parent, externalHost)))

        graph.Run (fun () -> source.Value <- "b")
        equal "a" input.value
        adopted.Dispose ()
        equal 0. externalHost.childNodes.length
    finally
        adopted.Dispose ()
        mounted.Dispose ()

let private setterDisposal () =
    use graph = new Graph ()
    let host = globalThis.document.createElement "main"
    host.innerHTML <- "<span>keep</span>"
    let mutable writes = 0

    let view =
        createComponent
            (fun _ () ->
                H.Program.div
                    [|
                        Typed.staticProperty
                            (fun (_: HTMLDivElement) () ->
                                writes <- writes + 1
                                (Api.getOwner ()).Dispose())
                            ()
                    |]
                    [||])
            ()

    let mutable rejected = false

    try
        let mounted =
            Mounting.mount graph None ignore host None (fun context -> Program.run view context.Backend)

        mounted.Dispose ()
    with :? InvalidOperationException ->
        rejected <- true

    equal 1 writes
    equal true rejected
    equal "<span>keep</span>" host.innerHTML

/// <summary>Runs wide cleanup and external-ownership regressions for the mounting investigation.</summary>
let cases =
    [|
        "128 components clean up without stopping a sibling mount", fun () -> wideCleanup 128
        "512 components clean up without stopping a sibling mount", fun () -> wideCleanup 512
        "moved generated descendants can be adopted and outlive their mount", movedDescendant
        "static property setter disposal cannot attach a dead component", setterDisposal
    |]
