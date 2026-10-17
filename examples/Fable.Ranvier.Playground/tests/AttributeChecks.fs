module AttributeChecks

open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Mounting

module H = Ranvier.View.Foundation.Generated.Html

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private query (host: HTMLElement) (selector: string) : HTMLElement =
    host.querySelector (selector) |> Option.get

let private exercise (ops: Operations<'view>) run queued =
    use graph = new Graph ()
    let text = graph.Run (fun () -> Api.createSignal (Some "first<&\""))
    let flag = graph.Run (fun () -> Api.createSignal true)
    let aria = graph.Run (fun () -> Api.createSignal (Some false))
    let mutable reads = 0

    let read () =
        reads <- reads + 1
        text.Value

    let view =
        H.button
            ops
            [|
                H.Attrs.classStatic "panel"
                H.Attrs.titleReactive read
                H.Attrs.dataReactive "item" read
                H.Attrs.ariaExpandedReactive (fun () -> aria.Value)
                H.Attrs.Button.disabledReactive (fun () -> flag.Value)
            |]
            [||]

    let host, parsed = Dom.element "section" [] [], Dom.element "section" [] []

    let mode =
        if queued then
            Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)
        else
            Scheduling.SynchronousCommits

    let mounted =
        mountWith mode graph None ignore host None (fun ctx -> run view ctx.Backend)

    let button = query host "button" :?> HTMLButtonElement

    try
        equal (Some "panel") (button.getAttribute "class")
        equal (Some "false") (button.getAttribute "aria-expanded")
        equal true button.disabled
        equal 2 reads
        parsed.innerHTML <- Html.render Reject (run view)
        let snapshot = query parsed "button" :?> HTMLButtonElement
        equal (button.getAttribute "title") (snapshot.getAttribute "title")
        equal (button.getAttribute "data-item") (snapshot.getAttribute "data-item")
        equal true snapshot.disabled

        graph.Run (fun () ->
            text.Value <- None
            flag.Value <- false
            aria.Value <- None)

        if queued then
            equal true button.disabled

        mounted.Flush ()
        equal None (button.getAttribute "title")
        equal None (button.getAttribute "data-item")
        equal None (button.getAttribute "aria-expanded")
        equal false button.disabled

        graph.Run (fun () ->
            text.Value <- Some "again"
            aria.Value <- Some false)

        mounted.Flush ()
        equal (Some "again") (button.getAttribute "title")
        equal (Some "false") (button.getAttribute "aria-expanded")
        let before = reads
        graph.Run (fun () -> text.Value <- Some "cancelled")
        let retained = button.getAttribute "title"
        mounted.Dispose ()
        mounted.Flush ()
        equal retained (button.getAttribute "title")
        graph.Run (fun () -> text.Value <- Some "stopped")
        equal (before + 2) reads
    finally
        mounted.Dispose ()

    equal 0. host.childNodes.length

let private duplicateSnapshot (ops: Operations<'view>) run =
    let view =
        H.div
            ops
            [|
                H.Attrs.idStatic "first"
                Typed.optionalAttribute "id" None
                H.Attrs.titleStatic "title"
                H.Attrs.idStatic "last"
                H.Attrs.hiddenStatic false
            |]
            [||]

    equal "<div title=\"title\" id=\"last\"></div>" (Html.render Reject (run view))

let private reflectedInput () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal (Some "initial"))
    let host = Dom.element "section" [] []

    let view =
        H.input Tree.ops [| H.Attrs.Input.valueReactive (fun () -> source.Value) |]

    let mounted =
        mount graph None ignore host None (fun ctx -> Tree.run view ctx.Backend)

    let input = query host "input" :?> HTMLInputElement

    try
        equal "initial" input.value
        input.value <- "edited"
        graph.Run (fun () -> source.Value <- Some "new default")
        equal "new default" input.defaultValue
        equal "edited" input.value
    finally
        mounted.Dispose ()

let private names () =
    for name in [ ""; "bad name"; "onclick"; "srcdoc"; "bad\"name" ] do
        let mutable rejected = false

        try
            H.div Tree.ops [| Typed.attribute name "x" |] [||]
            |> ignore
        with :? System.ArgumentException ->
            rejected <- true

        equal true rejected

    for suffix in [ ""; "bad name" ] do
        let mutable rejected = false

        try
            H.div Tree.ops [| H.Attrs.dataStatic suffix "x" |] [||]
            |> ignore
        with :? System.ArgumentException ->
            rejected <- true

        equal true rejected

let cases =
    [|
        for queued in [ false; true ] do
            yield
                sprintf "tree %s generated attribute removal and lifetime" (if queued then "queued" else "sync"),
                fun () -> exercise Tree.ops Tree.run queued

            yield
                sprintf "program %s generated attribute removal and lifetime" (if queued then "queued" else "sync"),
                fun () -> exercise Program.ops Program.run queued
        yield "tree duplicate attribute snapshots serialize once", fun () -> duplicateSnapshot Tree.ops Tree.run
        yield "program duplicate attribute snapshots serialize once", fun () -> duplicateSnapshot Program.ops Program.run
        yield "input value attributes preserve dirty live values", reflectedInput
        yield "attribute names and prefixed suffixes reject invalid input", names
    |]
