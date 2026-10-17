module GeneratedChecks

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

let private query (host: Element) (selector: string) : HTMLElement =
    host.querySelector (selector) |> Option.get

[<Emit("$0.dispatchEvent(new ($0.ownerDocument.defaultView.InputEvent)('input', { bubbles: true, data: 'x', inputType: 'insertText' }))")>]
let private dispatchInput (node: Node) : unit = jsNative

let private parseHtml (host: HTMLElement) (html: string) : unit =
    host.innerHTML <- html

let private lifecycle (ops: Operations<'view>) run queued generated =
    use graph = new Graph ()
    let value = graph.Run (fun () -> Api.createSignal "first")
    let checkedValue = graph.Run (fun () -> Api.createSignal false)
    let mutable events = 0
    let mutable reads = 0

    let read () =
        reads <- reads + 1
        value.Value

    let onInput (event: InputEvent) =
        equal "insertText" event.inputType
        events <- events + 1
        value.Value <- "from event"

    let view =
        if generated then
            H.input
                ops
                [|
                    H.Input.titleStatic "static"
                    H.Input.valueReactive read
                    H.Input.checkedReactive (fun () -> checkedValue.Value)
                    H.Input.onInput onInput
                |]
        else
            Typed.input
                ops
                [|
                    Typed.staticProperty (fun (node: HTMLInputElement) value -> node.title <- value) "static"
                    Typed.reactiveProperty (fun (node: HTMLInputElement) value -> node.value <- value) read
                    Typed.reactiveProperty (fun (node: HTMLInputElement) value -> node.``checked`` <- value) (fun () -> checkedValue.Value)
                    Typed.listener "input" (fun event -> onInput (unbox event))
                |]

    let hosts = [| Dom.element "section" [] []; Dom.element "section" [] [] |]

    let mode =
        if queued then
            Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)
        else
            Scheduling.SynchronousCommits

    let mounts =
        hosts
        |> Array.map (fun host -> mountWith mode graph None ignore host None (fun ctx -> run view ctx.Backend))

    let inputs =
        hosts
        |> Array.map (fun host -> query host "input" :?> HTMLInputElement)

    try
        equal false (obj.ReferenceEquals (inputs[0], inputs[1]))
        equal 2 reads

        for input in inputs do
            equal "first" input.value
            equal "static" input.title
            equal false input.``checked``
            equal None (H.Input.files input)
            equal None (H.Input.form input)

        graph.Run (fun () ->
            value.Value <- "second"
            checkedValue.Value <- true)

        if queued then
            equal "first" inputs[0].value

        for mount in mounts do
            mount.Flush ()

        for input in inputs do
            equal "second" input.value
            equal true input.``checked``
            equal None (input.getAttribute "checked")

        graph.Run (fun () -> checkedValue.Value <- false)

        for mount in mounts do
            mount.Flush ()

        equal false inputs[0].``checked``
        dispatchInput inputs[0]

        for mount in mounts do
            mount.Flush ()

        equal 1 events
        equal "from event" inputs[1].value
        let beforeReads = reads
        graph.Run (fun () -> value.Value <- "cancelled")
        let retained = inputs[0].value
        mounts[0].Dispose()
        mounts[0].Flush()
        equal retained inputs[0].value
        dispatchInput inputs[0]
        equal 1 events
        mounts[1].Flush()
        equal "cancelled" inputs[1].value
        equal (beforeReads + 2) reads
    finally
        for mount in mounts do
            mount.Dispose ()

    for host in hosts do
        equal 0. host.childNodes.length

let private tagsAndSelect () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let view =
        H.div
            Tree.ops
            [||]
            [|
                H.button Tree.ops [||] [| Tree.ops.Text "go" |]
                H.select
                    Tree.ops
                    [| H.Select.valueStatic "b" |]
                    [|
                        H.option Tree.ops [| H.Option.valueStatic "a" |] [| Tree.ops.Text "A" |]
                        H.option Tree.ops [| H.Option.valueStatic "b" |] [| Tree.ops.Text "B" |]
                    |]
            |]

    let mounted =
        mount graph None ignore host None (fun ctx -> Tree.run view ctx.Backend)

    try
        equal "b" ((query host "select" :?> HTMLSelectElement).value)
        equal "goAB" host.textContent
    finally
        mounted.Dispose ()

let private portable () =
    let view =
        H.div Tree.ops [| Typed.id "generated" |] [| H.option Tree.ops [||] [| Tree.ops.Text "<&" |] |]

    equal "<div id=\"generated\"><option>&lt;&amp;</option></div>" (Html.render Reject (Tree.run view))

let private expandedElements () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []

    let view =
        H.article
            Tree.ops
            [| H.Properties.HTMLElement.titleStatic "expanded" |]
            [|
                H.br Tree.ops [||]
                H.img Tree.ops [| H.Img.altStatic "sample" |]
                H.textarea Tree.ops [| H.Textarea.rowsStatic 3. |] [||]
                H.template Tree.ops [||] [| H.span Tree.ops [||] [| Tree.ops.Text "inert" |] |]
            |]

    let mounted =
        mount graph None ignore host None (fun ctx -> Tree.run view ctx.Backend)

    try
        equal "expanded" ((query host "article").title)
        equal "sample" ((query host "img" :?> HTMLImageElement).alt)
        equal 3. ((query host "textarea" :?> HTMLTextAreaElement).rows)
        let template = query host "template" :?> HTMLTemplateElement
        equal "inert" template.content.textContent
        equal 0. template.childNodes.length
    finally
        mounted.Dispose ()

    equal 0. host.childNodes.length

let private expandedPortable () =
    let view =
        H.article
            Tree.ops
            [||]
            [|
                H.br Tree.ops [||]
                H.img Tree.ops [||]
                H.textarea Tree.ops [||] [| Tree.ops.Text "<&" |]
            |]

    equal "<article><br><img><textarea>&lt;&amp;</textarea></article>" (Html.render Reject (Tree.run view))

    for tag in
        [
            "script"
            "style"
            "iframe"
            "noscript"
            "xmp"
            "noembed"
            "noframes"
            "plaintext"
        ] do
        let mutable rejected = false

        try
            Html.render Reject (Tree.run (Tree.ops.Element (HtmlTag tag) [||] [| Tree.ops.Text "<&" |]))
            |> ignore
        with :? System.InvalidOperationException ->
            rejected <- true

        equal true rejected

let private leadingNewlineRoundtrip (ops: Operations<'view>) run =
    use graph = new Graph ()

    for tag in [ "pre"; "textarea"; "listing" ] do
        for text in [ "\nhello"; "\r\nhello" ] do
            let view =
                ops.Element (HtmlTag tag) [||] [| ops.Text ""; ops.Bind (=) (fun () -> text) |]

            let parsed, direct = Dom.element "section" [] [], Dom.element "section" [] []
            let mounted = mount graph None ignore direct None (fun ctx -> run view ctx.Backend)

            try
                parseHtml parsed (Html.render Reject (run view))
                equal direct.textContent parsed.textContent
                equal text parsed.textContent
            finally
                mounted.Dispose ()

let cases =
    [|
        for queued in [ false; true ] do
            for generated in [ false; true ] do
                yield
                    sprintf
                        "tree %s %s properties and listener ownership"
                        (if queued then "queued" else "sync")
                        (if generated then "generated" else "handwritten"),
                    fun () -> lifecycle Tree.ops Tree.run queued generated

                yield
                    sprintf
                        "program %s %s properties and listener ownership"
                        (if queued then "queued" else "sync")
                        (if generated then "generated" else "handwritten"),
                    fun () -> lifecycle Program.ops Program.run queued generated
        yield "generated five-tag subset and select initialization", tagsAndSelect
        yield "generated tags render portable escaped HTML", portable
        yield "expanded tags mount void elements and template content", expandedElements
        yield "expanded HTML snapshots preserve void and raw-text policies", expandedPortable
        yield "tree initial line feeds roundtrip through the HTML parser", fun () -> leadingNewlineRoundtrip Tree.ops Tree.run
        yield "program initial line feeds roundtrip through the HTML parser", fun () -> leadingNewlineRoundtrip Program.ops Program.run
    |]
