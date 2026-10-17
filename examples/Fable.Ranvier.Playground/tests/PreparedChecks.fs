module PreparedChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

module H = Ranvier.View.Foundation.Generated.Html

[<Global "document">]
let private document: Document = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private rejected action =
    let mutable failed = false

    try
        action ()
    with :? InvalidOperationException ->
        failed <- true

    equal true failed

let private mount graph mode host view =
    Mounting.mountWith mode graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

let private reuse mode =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let a, b = document.createElement "main", document.createElement "main"
    let mutable builds, setups, cleaned, clicks = 0, 0, 0, 0

    let staticView =
        H.Program.div [| Typed.attribute "class" "panel" |] [| H.Program.input [||]; H.Program.button [||] [| Program.text "0" |] |]

    let template =
        Prepared.prepare (
            Program.View (fun backend ->
                builds <- builds + 1
                Program.run staticView backend)
        )

    let inputModifiers =
        [| H.Properties.HTMLInputElement.valueReactive (fun () -> source.Value) |]

    let view =
        Prepared.createComponent
            template
            (fun context () root backend ->
                setups <- setups + 1
                let local = Api.createSignal 0
                context.OnCleanup (fun () -> cleaned <- cleaned + 1)
                let input = root.firstChild.Value :?> HTMLInputElement
                let button = input.nextSibling.Value :?> HTMLButtonElement
                Prepared.apply input inputModifiers backend

                Prepared.apply
                    button
                    [|
                        H.Events.onClick (fun _ ->
                            clicks <- clicks + 1
                            local.Value <- local.Value + 1)
                    |]
                    backend

                Prepared.text button.firstChild.Value (fun () -> string local.Value) backend)
            ()

    let first, second = mount graph mode a view, mount graph mode b view

    try
        equal (1, 2) (builds, setups)
        let inputA = a.querySelector "input" |> unbox<HTMLInputElement>
        let inputB = b.querySelector "input" |> unbox<HTMLInputElement>

        let buttonA =
            a.querySelector "button"
            |> unbox<HTMLButtonElement>

        let buttonB =
            b.querySelector "button"
            |> unbox<HTMLButtonElement>

        equal false (obj.ReferenceEquals (inputA, inputB))
        buttonA.click ()
        first.Flush ()
        second.Flush ()
        equal ("1", "0") (buttonA.textContent, buttonB.textContent)
        graph.Run (fun () -> source.Value <- "b")
        first.Flush ()
        second.Flush ()
        equal ("b", "b") (inputA.value, inputB.value)
        first.Dispose ()
        buttonA.click ()
        graph.Run (fun () -> source.Value <- "c")
        second.Flush ()
        equal (1, 1, "b", "c") (cleaned, clicks, inputA.value, inputB.value)
        equal 0. a.childNodes.length
    finally
        first.Dispose ()
        second.Dispose ()

    equal 2 cleaned

let private moved () =
    use graph = new Graph ()
    let host, outside = document.createElement "main", document.createElement "main"
    let template = Prepared.prepare (H.Program.div [||] [| H.Program.input [||] |])
    let view = Prepared.view template (fun _ _ -> ())
    let mounted = mount graph Scheduling.SynchronousCommits host view

    let input =
        host.querySelector "input"
        |> unbox<HTMLInputElement>

    outside.appendChild input |> ignore

    let adopted =
        Mounting.mount graph None ignore outside None (fun context -> context.Native (input, Mounting.Owned))

    mounted.Dispose ()
    equal 0. host.childNodes.length

    equal
        true
        (input.parentNode
         |> Option.exists (fun parent -> obj.ReferenceEquals (parent, outside)))

    adopted.Dispose ()
    equal 0. outside.childNodes.length

let private rollback dispose =
    use graph = new Graph ()
    let host = document.createElement "main"
    host.innerHTML <- "<span>keep</span>"
    let mutable cleaned = 0
    let template = Prepared.prepare (H.Program.div [||] [| H.Program.input [||] |])

    let view =
        Prepared.createComponent
            template
            (fun context () root backend ->
                context.OnCleanup (fun () -> cleaned <- cleaned + 1)

                Prepared.apply
                    root
                    [|
                        Typed.staticProperty
                            (fun (_: HTMLElement) () ->
                                if dispose then
                                    (Api.getOwner ()).Dispose()
                                else
                                    failwith "setter")
                            ()
                    |]
                    backend)
            ()

    let mutable failed = false

    try
        mount graph Scheduling.SynchronousCommits host view
        |> ignore
    with _ ->
        failed <- true

    equal (true, 1, "<span>keep</span>") (failed, cleaned, host.innerHTML)

let private invalidTargets () =
    use graph = new Graph ()
    let host, other = document.createElement "main", document.createElement "div"
    let template = Prepared.prepare (H.Program.div [||] [||])

    rejected (fun () ->
        mount graph Scheduling.SynchronousCommits host (Prepared.view template (fun _ backend -> Prepared.apply other [||] backend))
        |> ignore)

    equal 0. host.childNodes.length
    let mutable failed = false

    try
        mount graph Scheduling.SynchronousCommits host (Prepared.view template (fun root backend -> Prepared.text root (fun () -> "bad") backend))
        |> ignore
    with :? ArgumentException ->
        failed <- true

    equal true failed
    equal 0. host.childNodes.length

let private staticOnly () =
    use graph = new Graph ()
    let host = document.createElement "main"
    let mutable reads = 0

    let template =
        Prepared.prepare (
            H.Program.div
                [|
                    Typed.reactiveAttribute "title" (fun () ->
                        reads <- reads + 1
                        Some "bad")
                |]
                [||]
        )

    for _ in 1..2 do
        rejected (fun () ->
            mount graph Scheduling.SynchronousCommits host (Prepared.view template (fun _ _ -> ()))
            |> ignore)

    equal (0, 0.) (reads, host.childNodes.length)

    rejected (fun () ->
        Html.render Reject (Program.run (Prepared.view template (fun _ _ -> ())))
        |> ignore)

    equal 0 reads

let private templateContent () =
    use graph = new Graph ()
    let host = document.createElement "main"
    let template = Prepared.prepare (H.Program.template [||] [| H.Program.input [||] |])

    let view =
        Prepared.view template (fun root backend ->
            let input =
                (root :?> HTMLTemplateElement).content.firstChild.Value :?> HTMLInputElement

            Prepared.apply input [| H.Properties.HTMLInputElement.valueStatic "a" |] backend)

    let mounted = mount graph Scheduling.SynchronousCommits host view

    let root =
        host.querySelector "template"
        |> unbox<HTMLTemplateElement>

    let input = root.content.firstChild.Value :?> HTMLInputElement
    equal "a" input.value
    mounted.Dispose ()
    equal 0. host.childNodes.length
    equal true (obj.ReferenceEquals (input, root.content.firstChild.Value))

/// <summary>Checks clone reuse, ownership, rollback and static preparation restrictions.</summary>
let cases =
    [|
        "prepared components reuse skeletons with independent synchronous bindings", fun () -> reuse Scheduling.SynchronousCommits
        "prepared components reuse skeletons with independent queued bindings",
        fun () -> reuse (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore))
        "moved prepared descendants can be adopted and outlive their mount", moved
        "prepared setter failure rolls back and cleans up", fun () -> rollback false
        "prepared setter disposal cannot attach a dead component", fun () -> rollback true
        "prepared operations reject foreign nodes and element text targets", invalidTargets
        "preparation rejects reactive callbacks and server serialization", staticOnly
        "prepared template content is bound and leaves with its root", templateContent
    |]
