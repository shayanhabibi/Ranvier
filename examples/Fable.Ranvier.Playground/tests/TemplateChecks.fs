module TemplateChecks

open global.Ranvier
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

[<Emit("(() => { const shape = n => n.nodeType + (n.nodeName || '') + '(' + Array.from(n.childNodes).map(shape).join(',') + ')'; const t = document.createElement('template'); t.innerHTML = $0; const first = shape(t.content); const again = document.createElement('template'); again.innerHTML = t.innerHTML; return first === shape(again.content); })()")>]
let private roundTrips (markup: string) : bool = jsNative

type Greeting = { Title: string; Name: string }

let private greeting =
    Component.define (fun _ (props: Greeting) -> props) (fun t ->
        t.fragment
            [|
                t.div
                    [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |]
                    [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |]
                t.span [||] [| t.text (fun r -> r.Name) |]
            |])

let private walkPrograms () =
    equal [| 0; 3; 1; 0; 1; 3 |] (TemplateDom.walkProgram [| [| 0 |]; [| 1; 1 |] |])
    equal [| 0; 0; 1; 3; 2; 1; 3 |] (TemplateDom.walkProgram [| [| 0; 1 |]; [| 1 |] |])

let private compiledSkeleton () =
    let compiled = TemplateDom.compile greeting
    let markup, targets, rootCount = skeletonMarkup (greeting :> TemplateComponent).Root
    equal 2 rootCount
    equal "<div class=\"panel\">Hello <!--!--> <!--!--> </div><span> </span>" markup
    equal true (roundTrips markup)
    equal 4 targets.Length
    equal 4 compiled.NodeCount
    equal 4 compiled.RenderSlots.Length
    equal 2. compiled.Skeleton.childNodes.length

module H = Ranvier.View.Foundation.Generated.Html

type RowProps =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
    }

type Row =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
        Local: Signal<int>
        Log: ResizeArray<string>
    }

let private rowLog = ResizeArray<string>()

let private row =
    Component.define
        (fun setup (props: RowProps) ->
            let row =
                {
                    Index = props.Index
                    Source = props.Source
                    Disabled = props.Disabled
                    Local = Api.createSignal 0
                    Log = rowLog
                }

            setup.OnCleanup (fun () -> rowLog.Add ("cleanup " + string row.Index))
            row)
        (fun t ->
            t.div
                [|
                    t.Attrs.className "panel"
                    t.Attrs.id (fun r -> "row-" + string r.Index)
                    t.Attrs.title (fun r -> r.Source.Value)
                |]
                [|
                    t.input [| t.Props.Input.value (fun r -> r.Source.Value) |]
                    t.button
                        [|
                            t.Props.Button.disabled (fun r -> r.Disabled.Value)
                            t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)
                        |]
                        [| t.text (fun r -> string r.Local.Value) |]
                |])

let private host () =
    globalThis.document.createElement "main"

let private mount graph (host: HTMLElement) view =
    Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

let private state (host: HTMLElement) =
    let div = host.querySelector "div" |> unbox<HTMLElement>

    let input =
        host.querySelector "input"
        |> unbox<HTMLInputElement>

    let button =
        host.querySelector "button"
        |> unbox<HTMLButtonElement>

    div.getAttribute "class", div.getAttribute "id", div.getAttribute "title", input.value, button.disabled, button.textContent

let private twinEquality () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let disabled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))

    let first, second = host (), host ()

    let props =
        {
            Index = 1
            Source = source
            Disabled = disabled
        }

    let templateMount = mount graph first (Component.render row props)

    let twin =
        H.Program.div
            [|
                H.Attrs.classStatic "panel"
                H.Attrs.idStatic "row-1"
                H.Attrs.titleReactive (fun () -> Some source.Value)
            |]
            [|
                H.Program.input [| H.Properties.HTMLInputElement.valueReactive (fun () -> source.Value) |]
                H.Program.button [| H.Properties.HTMLButtonElement.disabledReactive (fun () -> disabled.Value) |] [| Program.text "0" |]
            |]

    let twinMount = mount graph second twin
    equal (state second) (state first)
    graph.Run (fun () -> source.Value <- "blocked")
    equal (state second) (state first)
    templateMount.Dispose ()
    twinMount.Dispose ()

let private changedSlotsOnly () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let host = host ()

    let mounted =
        mount
            graph
            host
            (Component.render
                row
                {
                    Index = 1
                    Source = source
                    Disabled = disabled
                })

    let div = host.querySelector "div" |> unbox<HTMLElement>
    let mutable titleWrites = 0
    let setAttribute = div?setAttribute

    div?setAttribute <-
        fun (name: string) (value: string) ->
            if name = "title" then
                titleWrites <- titleWrites + 1

            setAttribute?call (div, name, value)

    (host.querySelector "button"
     |> unbox<HTMLButtonElement>)
        .click()

    equal 0 titleWrites
    graph.Run (fun () -> source.Value <- "b")
    equal 1 titleWrites
    mounted.Dispose ()

let private readerFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let failing =
        Component.define (fun _ (source: Signal<string>) -> source) (fun t ->
            t.div
                [|
                    t.Attrs.title (fun s -> if s.Value = "bad" then failwith "reader" else s.Value)
                |]
                [| t.text (fun s -> s.Value) |])

    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render failing source) context.Backend)

    equal 0 reported.Count
    graph.Run (fun () -> source.Value <- "bad")
    equal (Some "a", "bad") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    equal [ "reader" ] [ for error in reported -> error.Message ]
    graph.Run (fun () -> source.Value <- "c")
    equal (Some "c", "c") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    equal 1 reported.Count
    mounted.Dispose ()

let private recoveredFailureRecurs () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let failure = exn "cached"

    let failing =
        Component.define (fun _ (source: Signal<string>) -> source) (fun t ->
            t.div
                [|
                    t.Attrs.title (fun s -> if s.Value.StartsWith "bad" then raise failure else s.Value)
                |]
                [||])

    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render failing source) context.Backend)

    graph.Run (fun () -> source.Value <- "bad")
    graph.Run (fun () -> source.Value <- "bad!")
    equal 1 reported.Count
    graph.Run (fun () -> source.Value <- "ok")
    graph.Run (fun () -> source.Value <- "bad")
    equal 2 reported.Count
    mounted.Dispose ()

let private throwingReporter () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let failing =
        Component.define (fun _ (source: Signal<string>) -> source) (fun t ->
            t.div
                [|
                    t.Attrs.title (fun s -> if s.Value = "bad" then failwith "title" else s.Value)
                |]
                [|
                    t.text (fun s -> if s.Value = "bad" then failwith "text" else s.Value)
                    t.span [||] [| t.isolated (t.text (fun s -> s.Value + "!")) |]
                |])

    let host = host ()
    let reported = ResizeArray<exn>()

    let reporter (errors: exn list) =
        reported.AddRange errors
        failwith "reporter"

    let mounted =
        Mounting.mount graph None reporter host None (fun context -> Program.run (Component.render failing source) context.Backend)

    let run value =
        try
            graph.Run (fun () -> source.Value <- value)
            None
        with error ->
            Some error

    let surfaced = run "bad"
    equal None surfaced
    equal [ "title" ] [ for error in reported -> error.Message ]
    equal (Some "a", "abad!") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    equal None (run "ok")
    equal 1 reported.Count
    equal (Some "ok", "okok!") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    mounted.Dispose ()

let private initialReaderFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "bad")

    let failing =
        Component.define (fun _ (source: Signal<string>) -> source) (fun t ->
            t.div
                [|
                    t.Attrs.title (fun s -> if s.Value = "bad" then failwith "reader" else s.Value)
                |]
                [| t.text (fun s -> s.Value) |])

    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render failing source) context.Backend)

    equal ("bad", 1) (host.textContent, reported.Count)
    graph.Run (fun () -> source.Value <- "ok")
    equal (Some "ok", "ok") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    mounted.Dispose ()

let private setterRollback () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<span>keep</span>"
    let log = ResizeArray<string>()

    let failing =
        Component.define
            (fun setup () ->
                setup.OnCleanup (fun () -> log.Add "cleanup")
                ())
            (fun t ->
                t.button
                    [|
                        t.Props.propertyWith ((fun (_: HTMLButtonElement) (_: bool) -> failwith "setter"), (fun _ -> true), primitiveSame)
                    |]
                    [||])

    let mutable failed = false

    try
        mount graph host (Component.render failing ())
        |> ignore
    with _ ->
        failed <- true

    equal (true, [ "cleanup" ], "<span>keep</span>") (failed, List.ofSeq log, host.innerHTML)

let private cleanupOrder () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    rowLog.Clear ()
    let host = host ()

    let view =
        Program.fragment
            [|
                for index in 1..2 ->
                    Component.render
                        row
                        {
                            Index = index
                            Source = source
                            Disabled = disabled
                        }
            |]

    let mounted = mount graph host view
    mounted.Dispose ()
    equal [ "cleanup 2"; "cleanup 1" ] (List.ofSeq rowLog)
    equal 0. host.childNodes.length

let private adjacentTexts () =
    use graph = new Graph ()
    let host = host ()

    let mounted =
        mount graph host (Component.render greeting { Title = "T"; Name = "Ada" })

    equal "Hello Ada!Ada" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private compiledOnce () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let first, second = host (), host ()

    let a =
        mount
            graph
            first
            (Component.render
                row
                {
                    Index = 1
                    Source = source
                    Disabled = disabled
                })

    let compiled = (row :> TemplateDefinition).Compiled

    let b =
        mount
            graph
            second
            (Component.render
                row
                {
                    Index = 2
                    Source = source
                    Disabled = disabled
                })

    equal true (obj.ReferenceEquals (compiled, (row :> TemplateDefinition).Compiled))

    (first.querySelector "button"
     |> unbox<HTMLButtonElement>)
        .click()

    equal ("1", "0") ((first.querySelector "button").Value.textContent, (second.querySelector "button").Value.textContent)
    a.Dispose ()
    b.Dispose ()

let private keyedRows () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host = host ()

    let view =
        Keyed.list Program.ops id (fun () -> rows.Value) (fun current ->
            Component.render
                row
                {
                    Index = current ()
                    Source = source
                    Disabled = disabled
                })

    let mounted = mount graph host view

    let removed =
        host.querySelectorAll("div").item 1.
        |> unbox<HTMLElement>

    graph.Run (fun () -> rows.Value <- [ 1 ])
    graph.Run (fun () -> source.Value <- "b")
    equal (Some "a") (removed.getAttribute "title")
    equal (Some "b") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title")
    mounted.Dispose ()

let private nullText () =
    use graph = new Graph ()

    let nothing =
        Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text (fun _ -> (null: string)) |])

    let host = host ()
    let mounted = mount graph host (Component.render nothing ())
    equal "" host.textContent
    mounted.Dispose ()

let private staticTemplate () =
    use graph = new Graph ()

    let fixedView =
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.className "fixed" |] [| t.text "static" |])

    let first, second = host (), host ()
    let a = mount graph first (Component.render fixedView ())
    let b = mount graph second (Component.render fixedView ())

    let shape (host: HTMLElement) =
        (host.querySelector "div.fixed").Value.textContent

    equal ("static", "static") (shape first, shape second)
    equal 0 ((fixedView :> TemplateDefinition).Compiled :?> TemplateDom.Compiled).RenderSlots.Length
    a.Dispose ()
    b.Dispose ()

type Counted =
    {
        First: Signal<int>
        Second: Signal<int>
        Reads: int ref
    }

let private isolatedSlots () =
    use graph = new Graph ()
    let first = graph.Run (fun () -> Api.createSignal 1)
    let second = graph.Run (fun () -> Api.createSignal 1)
    let reads = ref 0

    let counted =
        Component.define (fun _ (props: Counted) -> props) (fun t ->
            t.div
                [||]
                [|
                    t.span [||] [| t.text (fun c -> string c.First.Value) |]
                    t.span
                        [||]
                        [|
                            t.isolated (
                                t.text (fun c ->
                                    c.Reads.Value <- c.Reads.Value + 1
                                    string c.Second.Value)
                            )
                        |]
                |])

    let host = host ()

    let mounted =
        mount
            graph
            host
            (Component.render
                counted
                {
                    First = first
                    Second = second
                    Reads = reads
                })

    graph.Run (fun () -> first.Value <- 2)
    equal 1 reads.Value
    graph.Run (fun () -> second.Value <- 2)
    equal (2, "22") (reads.Value, host.textContent)
    mounted.Dispose ()

let private sourceSlots () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let doubled =
        graph.Run (fun () -> Api.createMemo (fun _ -> source.Value + source.Value))

    let sourced =
        Component.define (fun _ () -> source, doubled) (fun t ->
            t.div [| t.Attrs.title (t.from (fun (s, _) -> s)) |] [| t.text (t.from (fun (_, d) -> d)) |])

    let host = host ()
    let mounted = mount graph host (Component.render sourced ())
    graph.Run (fun () -> source.Value <- "b")
    equal (Some "b", "bb") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    mounted.Dispose ()

[<Emit("$0.dispatchEvent(new FocusEvent('focus'))")>]
let private dispatchFocus (node: HTMLElement) : unit = jsNative

let private clicks () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let host = host ()

    let mounted =
        mount
            graph
            host
            (Component.render
                row
                {
                    Index = 1
                    Source = source
                    Disabled = disabled
                })

    let button =
        host.querySelector "button"
        |> unbox<HTMLButtonElement>

    button.click ()
    button.click ()
    equal "2" button.textContent
    mounted.Dispose ()
    button.click ()
    equal "2" button.textContent

let private movedClicks () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let first, second = host (), host ()

    let mounted =
        mount
            graph
            first
            (Component.render
                row
                {
                    Index = 1
                    Source = source
                    Disabled = disabled
                })

    let button =
        first.querySelector "button"
        |> unbox<HTMLButtonElement>

    mounted.MoveBefore (second, None)
    button.click ()
    equal "1" button.textContent
    mounted.Dispose ()

let private focusBesideClick () =
    use graph = new Graph ()
    let log = ResizeArray<string>()

    let both =
        Component.define (fun _ () -> log) (fun t ->
            t.input
                [|
                    t.Events.onFocus (fun l _ -> l.Add "focus")
                    t.Events.onClick (fun l _ -> l.Add "click")
                |])

    let host = host ()
    let mounted = mount graph host (Component.render both ())

    let input =
        host.querySelector "input"
        |> unbox<HTMLInputElement>

    dispatchFocus input
    input.click ()
    mounted.Dispose ()
    dispatchFocus input
    equal [ "focus"; "click" ] (List.ofSeq log)

let private queuedFallback () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let host = host ()

    let mounted =
        Mounting.mountWith (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)) graph None ignore host None (fun context ->
            Program.run
                (Component.render
                    row
                    {
                        Index = 1
                        Source = source
                        Disabled = disabled
                    })
                context.Backend)

    mounted.Flush ()
    equal (Some "a") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title")
    graph.Run (fun () -> source.Value <- "b")
    mounted.Flush ()
    equal (Some "b") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title")
    mounted.Dispose ()

let private browserInterpreter () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let plain =
        Component.define (fun _ (source: Signal<string>) -> source) (fun t -> t.div [| t.Attrs.id "row-1" |] [| t.text (fun s -> s.Value) |])

    let host = host ()

    let dispose =
        Browser.mount graph host ignore None (Program.run (Component.render plain source))

    equal (Some "row-1", "a") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "id", host.textContent)
    graph.Run (fun () -> source.Value <- "b")
    equal "b" host.textContent
    dispose ()
    equal 0. host.childNodes.length

let private preparedRejects () =
    let mutable rejected = false

    try
        let template =
            Prepared.prepare (Component.render greeting { Title = "T"; Name = "N" })

        use graph = new Graph ()

        mount graph (host ()) (Prepared.view template (fun _ _ -> ()))
        |> ignore
    with :? System.InvalidOperationException ->
        rejected <- true

    equal true rejected

let private serverMarkup () =
    equal
        "<div class=\"panel\" title=\"T &amp;\">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div><span>&lt;Ada&gt;</span>"
        (Html.render Omit (Program.run (Component.render greeting { Title = "T &"; Name = "<Ada>" })))

let private mountFailure (view: Program.View) =
    use graph = new Graph ()

    try
        (mount graph (host ()) view).Dispose()
        []
    with error ->
        [ error.Message ]

let private restructuredMarkup () =
    let tableCell =
        Component.define (fun _ () -> ()) (fun t ->
            t.element
                (HtmlTag "table")
                [||]
                [|
                    t.element (HtmlTag "tr") [||] [| t.element (HtmlTag "td") [||] [| t.text (fun _ -> "x") |] |]
                |])

    let paragraph =
        Component.define (fun _ () -> ()) (fun t -> t.element (HtmlTag "p") [||] [| t.div [||] [| t.text "x" |] |])

    let cellFailure = mountFailure (Component.render tableCell ())
    equal (true, true) (cellFailure.Length > 0, cellFailure |> List.exists _.Contains("td"))
    equal true (isNull (tableCell :> TemplateDefinition).Compiled)
    let paragraphFailure = mountFailure (Component.render paragraph ())
    equal true (paragraphFailure |> List.exists _.Contains("root"))

let private unsetText () =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createAsyncSource<string>())

    let viewOf (read: unit -> string) =
        Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text (fun _ -> read ()) |])

    let pendingHost, failedHost = host (), host ()

    let pending =
        mount graph pendingHost (Component.render (viewOf (fun () -> flight.Value)) ())

    let failed =
        Mounting.mount graph None ignore failedHost None (fun context ->
            Program.run (Component.render (viewOf (fun () -> failwith "reader")) ()) context.Backend)

    equal ("", "") (pendingHost.textContent, failedHost.textContent)
    graph.Run (fun () -> flight.Settle "ready")
    equal "ready" pendingHost.textContent
    pending.Dispose ()
    failed.Dispose ()

let private nullAttributes () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal (null: string))
    let other = graph.Run (fun () -> Api.createSignal 0)

    let nulls =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [|
                    t.Attrs.title (fun _ ->
                        other.Value |> ignore
                        (null: string))
                    t.Attrs.id (t.from (fun _ -> source))
                    t.Attrs.className (fun _ -> source.Value)
                |]
                [| t.text (fun _ -> string other.Value) |])

    let host = host ()
    let mounted = mount graph host (Component.render nulls ())
    let div = host.querySelector "div" |> unbox<HTMLElement>
    let mutable writes = 0
    let setAttribute = div?setAttribute

    div?setAttribute <-
        fun (name: string) (value: string) ->
            writes <- writes + 1
            setAttribute?call (div, name, value)

    equal (None, None, None) (div.getAttribute "title", div.getAttribute "id", div.getAttribute "class")
    graph.Run (fun () -> other.Value <- 1)
    equal (0, None, "1") (writes, div.getAttribute "title", host.textContent)
    graph.Run (fun () -> source.Value <- "set")
    equal (Some "set", Some "set") (div.getAttribute "id", div.getAttribute "class")
    mounted.Dispose ()

    let server =
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.title (fun _ -> (null: string)) |] [| t.text (fun _ -> "0") |])

    equal "<div>0</div>" (Html.render Omit (Program.run (Component.render server ())))

type Setter = { Current: Signal<string> }

let private laterSetterFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let written = ResizeArray<string>()

    let failing =
        Component.define (fun _ () -> { Current = source }) (fun t ->
            t.button
                [|
                    t.Props.propertyWith (
                        (fun (_: HTMLButtonElement) (value: string) ->
                            if value = "bad" then
                                failwith "setter"

                            written.Add value),
                        (fun s -> s.Current.Value),
                        primitiveSame
                    )
                    t.Attrs.title (fun s -> s.Current.Value)
                |]
                [| t.text (fun s -> s.Current.Value) |])

    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render failing ()) context.Backend)

    graph.Run (fun () -> source.Value <- "bad")
    let button = host.querySelector "button" |> unbox<HTMLElement>
    equal ([ "setter" ], Some "bad", "bad") ([ for error in reported -> error.Message ], button.getAttribute "title", host.textContent)
    graph.Run (fun () -> source.Value <- "c")
    equal ([ "a"; "c" ], "c") (List.ofSeq written, host.textContent)
    mounted.Dispose ()

let private textRoot () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let only = Component.define (fun _ () -> source) (fun t -> t.text (t.from id))

    let host = host ()
    let mounted = mount graph host (Component.render only ())
    equal "a" host.textContent
    graph.Run (fun () -> source.Value <- "b")
    equal "b" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private fragmentRoot () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let wrapped =
        Component.define (fun _ () -> source) (fun t -> t.fragment [| t.div [||] [| t.text (t.from id) |] |])

    let host = host ()
    let mounted = mount graph host (Component.render wrapped ())
    equal 1. host.children.length
    equal "a" host.textContent
    graph.Run (fun () -> source.Value <- "b")
    equal "b" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private movedRoots () =
    use graph = new Graph ()
    let first, second = host (), host ()

    let mounted =
        mount graph first (Component.render greeting { Title = "T"; Name = "Ada" })

    equal 2. first.children.length
    mounted.MoveBefore (second, None)
    equal 0. first.children.length
    equal 2. second.children.length
    equal "Hello Ada!Ada" second.textContent
    mounted.Dispose ()
    equal 0. second.childNodes.length

let private walkProgramEmptyPath () =
    equal [| 3; 0; 1; 3 |] (TemplateDom.walkProgram [| [||]; [| 1 |] |])

let private singleRootCompiled () =
    let single =
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.title (fun _ -> "x") |] [||])

    let compiled = TemplateDom.compile single
    equal 1 compiled.RootCount
    equal 1. compiled.Skeleton.nodeType

    equal
        true
        (compiled.Walk.Length = 0
         || compiled.Walk[0] <> TemplateDom.FirstChild)

type Holed = { Caption: string; Body: Program.View }

let private holed =
    Component.define (fun _ (props: Holed) -> props) (fun t ->
        t.div [| t.Attrs.className "card" |] [| t.text (fun r -> r.Caption); t.view (fun r -> r.Body) |])

let private holeMarkup () =
    let body = Program.element (HtmlTag "em") [||] [| Program.text "<b>" |]

    equal
        "<div class=\"card\">A<!--[--><em>&lt;b&gt;</em><!--]--></div>"
        (Html.render Omit (Program.run (Component.render holed { Caption = "A"; Body = body })))

let private holeSlotPaths () =
    let between =
        Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.text "x"; t.view (fun _ -> Program.fragment [||]); t.text (fun _ -> "y") |])

    let markup, targets, rootCount = skeletonMarkup (between :> TemplateComponent).Root
    equal ("x<!--[--><!--]--> ", 4, [| 3 |]) (markup, rootCount, snd targets[1])

    let leading =
        Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.view (fun _ -> Program.fragment [||]); t.text (fun _ -> "y") |])

    let markup, _, _ = skeletonMarkup (leading :> TemplateComponent).Root
    equal "<!--[--><!--]--> " markup

[<Emit("$0.innerHTML.replace(/<!--.*?-->/g, '')")>]
let private withoutComments (node: HTMLElement) : string = jsNative

[<Emit("Array.from($0.childNodes).filter(n => n.nodeType === 8 && (n.data === '[' || n.data === ']')).length")>]
let private regionMarkers (node: Node) : int = jsNative

let private rootHole =
    Component.define (fun _ (body: Program.View) -> body) (fun t -> t.fragment [| t.view id |])

let private viewHole () =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "x")
    let mutable cleaned = 0

    let body =
        Program.createComponent (fun context ->
            context.OnCleanup (fun () -> cleaned <- cleaned + 1)
            Program.element (HtmlTag "em") [||] [| Program.bind (=) (fun () -> label.Value) |])

    let host = host ()

    let mounted =
        mount graph host (Component.render holed { Caption = "A"; Body = body })

    let div = host.querySelector "div" |> unbox<HTMLElement>
    equal ("<div class=\"card\">A<em>x</em></div>", 2) (withoutComments host, regionMarkers div)
    graph.Run (fun () -> label.Value <- "y")
    equal "Ay" host.textContent
    mounted.Dispose ()
    equal (1, 0.) (cleaned, host.childNodes.length)

let private holeTwin () =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "x")

    let body () =
        Program.element (HtmlTag "em") [||] [| Program.bind (=) (fun () -> label.Value) |]

    let first, second = host (), host ()

    let templateMount =
        mount graph first (Component.render holed { Caption = "A"; Body = body () })

    let twinMount =
        mount graph second (Component.renderWith Program.ops holed { Caption = "A"; Body = body () })

    equal (withoutComments second) (withoutComments first)
    graph.Run (fun () -> label.Value <- "y")
    equal (withoutComments second, "<div class=\"card\">A<em>y</em></div>") (withoutComments first, withoutComments second)
    templateMount.Dispose ()
    twinMount.Dispose ()

let private emptyHole () =
    use graph = new Graph ()
    let host = host ()

    let mounted =
        mount
            graph
            host
            (Component.render
                holed
                {
                    Caption = "E"
                    Body = Program.fragment [||]
                })

    let div = host.querySelector "div" |> unbox<HTMLElement>
    equal ("<div class=\"card\">E</div>", 2) (withoutComments host, regionMarkers div)
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private viewReadOnce () =
    use graph = new Graph ()
    let choice = graph.Run (fun () -> Api.createSignal "first")
    let mutable reads = 0

    let once =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [||]
                [|
                    t.view (fun _ ->
                        reads <- reads + 1
                        Program.text choice.Value)
                |])

    let host = host ()
    let mounted = mount graph host (Component.render once ())
    graph.Run (fun () -> choice.Value <- "second")
    equal ("first", 1) (host.textContent, reads)
    mounted.Dispose ()

let private failingHole () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<span>keep</span>"
    let mutable failed = false

    try
        mount
            graph
            host
            (Component.render
                holed
                {
                    Caption = "F"
                    Body = Program.createComponent (fun _ -> failwith "view")
                })
        |> ignore
    with error ->
        failed <- error.Message = "view"

    equal (true, "<span>keep</span>") (failed, host.innerHTML)

let private rootHoleClicks () =
    use graph = new Graph ()
    let clicks = ResizeArray<string>()

    let body =
        H.Program.button [| H.Events.onClick (fun _ -> clicks.Add "click") |] [||]

    let first, second = host (), host ()
    let mounted = mount graph first (Component.render rootHole body)

    let button =
        first.querySelector "button"
        |> unbox<HTMLButtonElement>

    button.click ()
    mounted.MoveBefore (second, None)
    button.click ()
    equal (2, 0., 1.) (clicks.Count, first.childNodes.length, second.children.length)
    mounted.Dispose ()
    button.click ()
    equal (2, 0.) (clicks.Count, second.childNodes.length)

let private holeFallbacks () =
    use graph = new Graph ()
    let queuedHost, browserHost = host (), host ()

    let queued =
        Mounting.mountWith (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)) graph None ignore queuedHost None (fun context ->
            Program.run
                (Component.render
                    holed
                    {
                        Caption = "Q"
                        Body = Program.text "v"
                    })
                context.Backend)

    queued.Flush ()

    let dispose =
        Browser.mount
            graph
            browserHost
            ignore
            None
            (Program.run (
                Component.render
                    holed
                    {
                        Caption = "B"
                        Body = Program.text "v"
                    }
            ))

    equal ("Qv", "Bv") (queuedHost.textContent, browserHost.textContent)
    queued.Dispose ()
    dispose ()
    equal (0., 0.) (queuedHost.childNodes.length, browserHost.childNodes.length)

type Stage =
    | Waiting
    | Working of int
    | Complete of string

let private stageLog = ResizeArray<string>()
let private stageRenders = ResizeArray<string>()

let private waitingArm =
    Component.define
        (fun setup (_: unit -> unit) ->
            stageLog.Add "setup waiting"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup waiting"))
        (fun t -> t.span [||] [| t.text "waiting" |])

let private workingArm =
    Component.define
        (fun setup (progress: unit -> int) ->
            stageLog.Add "setup working"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup working")
            progress)
        (fun t ->
            t.span
                [||]
                [|
                    t.text (fun progress ->
                        let text = "working " + string (progress ())
                        stageRenders.Add text
                        text)
                |])

let private completeArm =
    Component.define
        (fun setup (result: unit -> string) ->
            stageLog.Add "setup complete"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup complete")
            result)
        (fun t -> t.span [||] [| t.text (fun result -> "complete " + result ()) |])

/// <summary>Selects the arm of a stage; <c>complete</c> renders <c>Complete</c>.</summary>
let private stageArmsWith (complete: Component<unit -> string>) (t: TemplateBuilder<'inst>) (stage: Stage) =
    match stage with
    | Waiting -> t.arm waitingArm ()
    | Working progress -> t.arm workingArm progress
    | Complete result -> t.arm complete result

let private stageArms (t: TemplateBuilder<'inst>) (stage: Stage) =
    stageArmsWith completeArm t stage

let private staged =
    Component.define (fun _ (stage: Signal<Stage>) -> stage) (fun t -> t.div [||] [| t.matchWith (fun stage -> stage.Value) (stageArms t) |])

let private matchSwitching () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal Waiting)
    let host = host ()
    let mounted = mount graph host (Component.render staged stage)
    equal "waiting" host.textContent
    graph.Run (fun () -> stage.Value <- Working 1)
    graph.Run (fun () -> stage.Value <- Working 2)
    equal "working 2" host.textContent
    graph.Run (fun () -> stage.Value <- Complete "ok")
    equal "complete ok" host.textContent

    equal
        [
            "setup waiting"
            "cleanup waiting"
            "setup working"
            "cleanup working"
            "setup complete"
        ]
        (List.ofSeq stageLog)

    mounted.Dispose ()
    equal ("cleanup complete", 0.) (stageLog[stageLog.Count - 1], host.childNodes.length)

let private matchSameFlush () =
    use graph = new Graph ()
    stageLog.Clear ()
    stageRenders.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal (Working 1))
    let host = host ()
    let mounted = mount graph host (Component.render staged stage)

    graph.Run (fun () ->
        Api.batch (fun () ->
            stage.Value <- Complete "x"
            stage.Value <- Working 2))

    equal ("working 2", [ "setup working" ]) (host.textContent, List.ofSeq stageLog)

    graph.Run (fun () ->
        Api.batch (fun () ->
            stage.Value <- Working 3
            stage.Value <- Complete "y"))

    equal ("complete y", [ "setup working"; "cleanup working"; "setup complete" ]) (host.textContent, List.ofSeq stageLog)
    equal [ "working 1"; "working 2" ] (List.ofSeq stageRenders)
    mounted.Dispose ()

let private matchTwin () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal (Working 1))
    let first, second = host (), host ()
    let templateMount = mount graph first (Component.render staged stage)
    let twinMount = mount graph second (Component.renderWith Program.ops staged stage)
    equal (withoutComments second, "<div><span>working 1</span></div>") (withoutComments first, withoutComments second)
    graph.Run (fun () -> stage.Value <- Working 2)
    equal (withoutComments second, "<div><span>working 2</span></div>") (withoutComments first, withoutComments second)
    graph.Run (fun () -> stage.Value <- Complete "ok")
    equal (withoutComments second, "<div><span>complete ok</span></div>") (withoutComments first, withoutComments second)
    templateMount.Dispose ()
    twinMount.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

let mutable private haltDuringSetup: unit -> unit = ignore

let private haltingArm =
    Component.define
        (fun setup (_: unit -> string) ->
            stageLog.Add "setup halting"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup halting")
            haltDuringSetup ())
        (fun t -> t.span [||] [| t.text "halting" |])

let private halting =
    Component.define (fun _ (stage: Signal<Stage>) -> stage) (fun t ->
        t.div [||] [| t.matchWith (fun stage -> stage.Value) (stageArmsWith haltingArm t) |])

let private disposedMidSwitch () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal (Working 1))
    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render halting stage) context.Backend)

    haltDuringSetup <- fun () -> mounted.Dispose ()

    try
        graph.Run (fun () -> stage.Value <- Complete "x")
    finally
        haltDuringSetup <- ignore

    equal (0., 0) (host.childNodes.length, reported.Count)

    equal [ "cleanup halting"; "cleanup working"; "setup halting"; "setup working" ] (List.sort (List.ofSeq stageLog))

let private stagedServer =
    Component.define (fun _ (stage: Stage) -> stage) (fun t -> t.div [||] [| t.matchWith id (stageArms t) |])

let private matchMarkup () =
    equal "<div><!--[--><span>working 3</span><!--]--></div>" (Html.render Omit (Program.run (Component.render stagedServer (Working 3))))

type Phase =
    | Loading of int
    | Saving of int
    | Idle

let private phased =
    Component.define (fun _ (phase: Signal<Phase>) -> phase) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun phase -> phase.Value) (function
                    | Loading progress
                    | Saving progress -> t.arm workingArm progress
                    | Idle -> t.arm waitingArm ())
            |])

let private sharedArm () =
    use graph = new Graph ()
    stageLog.Clear ()
    let phase = graph.Run (fun () -> Api.createSignal (Loading 1))
    let host = host ()
    let mounted = mount graph host (Component.render phased phase)
    let span = host.querySelector "span" |> unbox<HTMLElement>
    equal ("working 1", [ "setup working" ]) (host.textContent, List.ofSeq stageLog)
    graph.Run (fun () -> phase.Value <- Saving 2)

    equal ("working 2", [ "setup working" ], true) (host.textContent, List.ofSeq stageLog, obj.ReferenceEquals (span, host.querySelector "span"))

    graph.Run (fun () -> phase.Value <- Loading 3)
    equal ("working 3", [ "setup working" ]) (host.textContent, List.ofSeq stageLog)
    graph.Run (fun () -> phase.Value <- Idle)
    equal ("waiting", [ "setup working"; "cleanup working"; "setup waiting" ]) (host.textContent, List.ofSeq stageLog)
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private decadeLog = ResizeArray<string>()

let private decadeArm =
    Component.define
        (fun setup (decade: unit -> int) ->
            let initial = decade ()
            decadeLog.Add ("setup " + string initial)
            setup.OnCleanup (fun () -> decadeLog.Add ("cleanup " + string initial))
            decade)
        (fun t -> t.span [||] [| t.text (fun decade -> string (decade ())) |])

let private decades =
    Component.define (fun _ (value: Signal<int>) -> value) (fun t ->
        t.div
            [||]
            [|
                t.branch (fun value -> value.Value / 10) (fun decade -> t.arm decadeArm decade)
            |])

let private branchSwitching () =
    use graph = new Graph ()
    decadeLog.Clear ()
    let value = graph.Run (fun () -> Api.createSignal 11)
    let host = host ()
    let mounted = mount graph host (Component.render decades value)
    graph.Run (fun () -> value.Value <- 12)
    equal ("1", [ "setup 1" ]) (host.textContent, List.ofSeq decadeLog)
    graph.Run (fun () -> value.Value <- 21)
    equal ("2", [ "setup 1"; "cleanup 1"; "setup 2" ]) (host.textContent, List.ofSeq decadeLog)
    mounted.Dispose ()
    equal ([ "setup 1"; "cleanup 1"; "setup 2"; "cleanup 2" ], 0.) (List.ofSeq decadeLog, host.childNodes.length)

let private branchTwin () =
    use graph = new Graph ()
    decadeLog.Clear ()
    let value = graph.Run (fun () -> Api.createSignal 11)
    let first, second = host (), host ()
    let templateMount = mount graph first (Component.render decades value)
    let twinMount = mount graph second (Component.renderWith Program.ops decades value)
    equal (withoutComments second, "<div><span>1</span></div>") (withoutComments first, withoutComments second)
    graph.Run (fun () -> value.Value <- 12)
    equal (withoutComments second, "<div><span>1</span></div>") (withoutComments first, withoutComments second)
    graph.Run (fun () -> value.Value <- 21)
    equal (withoutComments second, "<div><span>2</span></div>") (withoutComments first, withoutComments second)
    templateMount.Dispose ()
    twinMount.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

let private branchMarkup () =
    use graph = new Graph ()
    let value = graph.Run (fun () -> Api.createSignal 42)

    equal "<div><!--[--><span>4</span><!--]--></div>" (graph.Run (fun () -> Html.render Omit (Program.run (Component.render decades value))))

let private roleLog = ResizeArray<string>()

let private adminArm =
    Component.define
        (fun setup (tab: unit -> string) ->
            roleLog.Add "setup admin"
            setup.OnCleanup (fun () -> roleLog.Add "cleanup admin")
            tab)
        (fun t -> t.span [||] [| t.text (fun tab -> "admin " + tab ()) |])

let private userArm =
    Component.define
        (fun setup (length: unit -> int) ->
            roleLog.Add "setup user"
            setup.OnCleanup (fun () -> roleLog.Add "cleanup user")
            length)
        (fun t -> t.span [||] [| t.text (fun length -> "user " + string (length ())) |])

/// <summary>Mounts a <c>branch</c> whose arms read an admin flag, through the template or its default expansion, and returns the DOM after each step and the setup/cleanup log.</summary>
let private roleSteps (expanded: bool) =
    use graph = new Graph ()
    roleLog.Clear ()
    let tab = graph.Run (fun () -> Api.createSignal "home")
    let admin = graph.Run (fun () -> Api.createSignal false)

    let roled =
        Component.define (fun _ (tab: Signal<string>) -> tab) (fun t ->
            t.div
                [||]
                [|
                    t.branch (fun tab -> tab.Value) (fun tab ->
                        if admin.Value then
                            t.arm adminArm tab
                        else
                            t.arm userArm tab.Length)
                |])

    let host = host ()

    let mounted =
        mount
            graph
            host
            (if expanded then
                 Component.renderWith Program.ops roled tab
             else
                 Component.render roled tab)

    let steps = ResizeArray<string>()
    steps.Add (withoutComments host)
    graph.Run (fun () -> admin.Value <- true)
    steps.Add (withoutComments host)
    graph.Run (fun () -> tab.Value <- "away")
    steps.Add (withoutComments host)
    graph.Run (fun () -> admin.Value <- false)
    steps.Add (withoutComments host)
    mounted.Dispose ()
    steps.Add (string host.childNodes.length)
    List.ofSeq steps, List.ofSeq roleLog

let private branchDefinitionChange () =
    let expected =
        [
            "<div><span>user 4</span></div>"
            "<div><span>admin home</span></div>"
            "<div><span>admin away</span></div>"
            "<div><span>user 4</span></div>"
            "0"
        ],
        [
            "setup user"
            "cleanup user"
            "setup admin"
            "cleanup admin"
            "setup admin"
            "cleanup admin"
            "setup user"
            "cleanup user"
        ]

    equal expected (roleSteps false)
    equal expected (roleSteps true)

let mutable private ambient = "original"

/// <summary>An arm that saves <c>ambient</c> on setup, sets it to its name, and restores the saved value on cleanup.</summary>
let private ambientArm (name: string) =
    Component.define
        (fun setup (_: unit -> unit) ->
            let saved = ambient
            ambient <- name
            setup.OnCleanup (fun () -> ambient <- saved))
        (fun t -> t.span [||] [| t.text name |])

let private ambientFirst = ambientArm "a"
let private ambientSecond = ambientArm "b"

let private ambientSwitch =
    Component.define (fun _ (second: Signal<bool>) -> second) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun second -> second.Value) (fun second ->
                    if second then
                        t.arm ambientSecond ()
                    else
                        t.arm ambientFirst ())
            |])

let private armSaveRestore () =
    for expanded in [ false; true ] do
        use graph = new Graph ()
        ambient <- "original"
        let second = graph.Run (fun () -> Api.createSignal false)
        let host = host ()

        let mounted =
            mount
                graph
                host
                (if expanded then
                     Component.renderWith Program.ops ambientSwitch second
                 else
                     Component.render ambientSwitch second)

        equal "a" ambient
        graph.Run (fun () -> second.Value <- true)
        equal ("b", "b") (ambient, host.textContent)
        mounted.Dispose ()
        equal "original" ambient

let private failingArm =
    Component.define
        (fun setup (_: unit -> string) ->
            stageLog.Add "setup failing"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup failing")
            (failwith "arm setup failed": unit))
        (fun t -> t.span [||] [| t.text "failing" |])

let private failingStaged =
    Component.define (fun _ (stage: Signal<Stage>) -> stage) (fun t ->
        t.div [||] [| t.matchWith (fun stage -> stage.Value) (stageArmsWith failingArm t) |])

let private armSetupFailure () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal (Working 1))
    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render failingStaged stage) context.Backend)

    let div = host.querySelector "div" |> unbox<HTMLElement>
    graph.Run (fun () -> stage.Value <- Complete "x")

    equal
        ([ "setup working"; "cleanup working"; "setup failing"; "cleanup failing" ], 2., "", [ "arm setup failed" ])
        (List.ofSeq stageLog, div.childNodes.length, div.textContent, reported |> Seq.map _.Message |> List.ofSeq)

    graph.Run (fun () -> stage.Value <- Waiting)
    equal ("waiting", "setup waiting", 1) (host.textContent, stageLog[stageLog.Count - 1], reported.Count)
    mounted.Dispose ()
    equal ("cleanup waiting", 0.) (stageLog[stageLog.Count - 1], host.childNodes.length)

let private throwingCleanupArm =
    Component.define
        (fun setup (_: unit -> unit) ->
            stageLog.Add "setup throwing"
            setup.OnCleanup (fun () -> failwith "arm cleanup failed"))
        (fun t -> t.span [||] [| t.text "throwing" |])

let private throwingCleanupStaged =
    Component.define (fun _ (stage: Signal<Stage>) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun stage -> stage.Value) (fun stage ->
                    match stage with
                    | Waiting -> t.arm throwingCleanupArm ()
                    | other -> stageArms t other)
            |])

let private armCleanupFailure () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal Waiting)
    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context ->
            Program.run (Component.render throwingCleanupStaged stage) context.Backend)

    graph.Run (fun () -> stage.Value <- Working 1)

    equal
        ("working 1", [ "setup throwing"; "setup working" ], [ "arm cleanup failed" ])
        (host.textContent, List.ofSeq stageLog, reported |> Seq.map _.Message |> List.ofSeq)

    mounted.Dispose ()
    equal ("cleanup working", 0.) (stageLog[stageLog.Count - 1], host.childNodes.length)

let private armPlacementFailure () =
    use graph = new Graph ()
    let host = host ()
    let log = ResizeArray<string>()
    let ticks = graph.Run (fun () -> Api.createSignal 0)

    let detachingArm =
        Component.define
            (fun setup (_: unit -> unit) ->
                setup.OnCleanup (fun () ->
                    let div = host.querySelector "div" |> unbox<HTMLElement>
                    div.removeChild div.firstChild.Value |> ignore))
            (fun t -> t.span [||] [| t.text "detaching" |])

    let tickingArm =
        Component.define
            (fun setup (_: unit -> unit) ->
                Api.createEffect (fun () -> log.Add ("tick " + string ticks.Value))
                setup.OnCleanup (fun () -> log.Add "cleanup ticking"))
            (fun t -> t.span [||] [| t.text "ticking" |])

    let switching =
        Component.define (fun _ (detached: Signal<bool>) -> detached) (fun t ->
            t.div
                [||]
                [|
                    t.matchWith (fun detached -> detached.Value) (fun detached ->
                        if detached then
                            t.arm tickingArm ()
                        else
                            t.arm detachingArm ())
                |])

    let detached = graph.Run (fun () -> Api.createSignal false)
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run (Component.render switching detached) context.Backend)

    graph.Run (fun () -> detached.Value <- true)
    let afterSwitch = List.ofSeq log
    graph.Run (fun () -> ticks.Value <- 1)

    equal
        (true, afterSwitch, [ [ "Keyed-list boundaries have no parent" ] ])
        (List.contains "cleanup ticking" afterSwitch,
         List.ofSeq log,
         reports
         |> Seq.map (List.map _.Message)
         |> List.ofSeq)

    mounted.Dispose ()

type Listed = { Id: int; Text: string }

let private listedLog = ResizeArray<string>()

let private listedRow =
    Component.define
        (fun setup (item: unit -> Listed) ->
            let id = (item ()).Id
            listedLog.Add ("setup " + string id)
            setup.OnCleanup (fun () -> listedLog.Add ("cleanup " + string id))
            item)
        (fun t ->
            t.element
                (HtmlTag "li")
                [|
                    t.Events.onClick (fun item _ -> listedLog.Add ("click " + string (item ()).Id))
                |]
                [| t.text (fun item -> (item ()).Text) |])

let private listed =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) listedRow
            |])

let private rootListed =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.fragment
            [|
                t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) listedRow
            |])

let private eachRows () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let first = host.querySelector "li" |> unbox<HTMLElement>
    equal "ab" host.textContent
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "B" }; { Id = 1; Text = "a" } |])
    equal ("Ba", true) (host.textContent, obj.ReferenceEquals (first, host.querySelectorAll("li").item 1.))
    first.click ()
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "a" } |])
    equal "a" host.textContent
    mounted.Dispose ()

    equal ([ "setup 1"; "setup 2"; "click 1"; "cleanup 2"; "cleanup 1" ], 0.) (List.ofSeq listedLog, host.childNodes.length)

let private eachReplacement () =
    for expanded in [ false; true ] do
        use graph = new Graph ()
        listedLog.Clear ()
        let items = graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" } |])
        let host = host ()

        let mounted =
            mount
                graph
                host
                (if expanded then
                     Component.renderWith Program.ops listed items
                 else
                     Component.render listed items)

        graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "b" } |])
        equal ("b", [ "setup 1"; "setup 2"; "cleanup 1" ]) (host.textContent, List.ofSeq listedLog)
        mounted.Dispose ()

let private eachDuplicates () =
    use graph = new Graph ()
    let first, second = host (), host ()
    first.innerHTML <- "<span>keep</span>"

    let duplicated =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 1; Text = "b" } |])

    let mutable rejected = false

    try
        mount graph first (Component.render listed duplicated)
        |> ignore
    with :? System.InvalidOperationException ->
        rejected <- true

    equal (true, "<span>keep</span>") (rejected, first.innerHTML)

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange second None (fun context -> Program.run (Component.render listed items) context.Backend)

    graph.Run (fun () -> items.Value <- [| { Id = 3; Text = "c" }; { Id = 3; Text = "d" } |])
    equal ("ab", 1, true) (second.textContent, reported.Count, reported[0] :? System.InvalidOperationException)
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "b" } |])
    equal ("b", 1) (second.textContent, reported.Count)
    mounted.Dispose ()

[<Emit("(() => { let count = 0; const old = $0.insertBefore; $0.insertBefore = function(n, b) { count++; return old.call(this, n, b); }; return () => { $0.insertBefore = old; return count; }; })()")>]
let private countInserts (node: Node) : unit -> int = jsNative

[<Emit("Array.from($0.childNodes).filter(n => n.nodeType === 8 && n.data === 'ranvier:row')")>]
let private rowMarkers (node: Node) : obj array = jsNative

[<Emit("document.activeElement === $0")>]
let private isFocused (node: obj) : bool = jsNative

[<Emit("$0.selectionStart + ',' + $0.selectionEnd")>]
let private selectionOf (node: obj) : string = jsNative

let private reported graph host view =
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run view context.Backend)

    mounted, reports

let private eachSwapMovesTwo () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal (Array.init 1000 (fun index -> { Id = index; Text = "" })))

    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let next = Array.copy items.Value
    let first = next[1]
    next[1] <- next[998]
    next[998] <- first
    let inserts = countInserts list
    graph.Run (fun () -> items.Value <- next)
    equal 2 (inserts ())
    mounted.Dispose ()

let private stableRemove (view: (Listed -> int) -> Signal<Listed array> -> Program.View) =
    use graph = new Graph ()
    listedLog.Clear ()
    let keyCalls = ref 0

    let key (item: Listed) =
        keyCalls.Value <- keyCalls.Value + 1
        item.Id

    let items =
        graph.Run (fun () ->
            Api.createSignal (
                Array.init 200 (fun index ->
                    {
                        Id = index
                        Text = string index + ","
                    })
            ))

    let host = host ()
    let mounted = mount graph host (view key items)
    listedLog.Clear ()
    keyCalls.Value <- 0
    graph.Run (fun () -> items.Value <- Array.removeAt 100 items.Value)
    equal (199., [ "cleanup 100" ]) ((host.querySelectorAll "li").length, List.ofSeq listedLog)
    equal 0 keyCalls.Value
    mounted.Dispose ()

let private eachStableRemove () =
    stableRemove (fun key items ->
        let counted =
            Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
                t.element (HtmlTag "ul") [||] [| t.each key (fun items -> items.Value) listedRow |])

        Component.render counted items)

let private keyedStableRemove () =
    stableRemove (fun key items -> Keyed.array Program.ops key (fun () -> items.Value) (fun current -> Component.render listedRow current))

let private listedView (keyed: bool) (items: Signal<Listed array>) =
    if keyed then
        Keyed.array Program.ops (fun (item: Listed) -> item.Id) (fun () -> items.Value) (fun current -> Component.render listedRow current)
    else
        Component.render listed items

let private listedItems count =
    Array.init count (fun id -> { Id = id; Text = string id + "," })

let private rowNodes (host: HTMLElement) =
    let rendered = host.querySelectorAll "li"
    Array.init (int rendered.length) (fun index -> box (rendered.item (float index)))

let private sameNodes (expected: obj array) (actual: obj array) =
    expected.Length = actual.Length
    && Array.forall2 (fun a b -> obj.ReferenceEquals (a, b)) expected actual

let private swapEnds (keyed: bool) (count: int) (left: int) (right: int) =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal (listedItems count))
    let host = host ()
    let mounted = mount graph host (listedView keyed items)
    let before = rowNodes host
    let next = Array.copy items.Value
    let held = next[left]
    next[left] <- next[right]
    next[right] <- held
    let list = (before[0] :?> Node).parentNode.Value :> Node
    let inserts = countInserts list
    graph.Run (fun () -> items.Value <- next)
    let moved = inserts ()
    let expectedNodes = Array.copy before
    expectedNodes[left] <- before[right]
    expectedNodes[right] <- before[left]
    equal (next |> Array.map _.Text |> String.concat "", true) (host.textContent, sameNodes expectedNodes (rowNodes host))
    mounted.Dispose ()
    moved

/// <summary>Node moves for a swap of two non-adjacent rows; a Keyed.array row holds three nodes.</summary>
let private rowMoves (keyed: bool) =
    if keyed then 6 else 2

let private swapShapes () =
    for keyed in [ false; true ] do
        equal (rowMoves keyed) (swapEnds keyed 1000 1 998)
        equal (rowMoves keyed) (swapEnds keyed 1000 0 999)
        equal (rowMoves keyed / 2) (swapEnds keyed 6 2 3)
        equal (rowMoves keyed / 2) (swapEnds keyed 2 0 1)
        equal true (swapEnds keyed 3 0 2 <= rowMoves keyed)

[<Emit("(() => { const w = document.defaultView; if (!w.customElements.get('x-probe')) w.customElements.define('x-probe', class extends w.HTMLElement { disconnectedCallback() { const hook = w.probeHook; w.probeHook = null; if (hook) hook(); } }); })()")>]
let private defineProbe () : unit = jsNative

[<Emit("document.defaultView.probeHook = $0")>]
let private setProbeHook (hook: unit -> unit) : unit = jsNative

let private probeRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.element (HtmlTag "li") [||] [| t.element (HtmlTag "x-probe") [||] [||] |])

let private probeList =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) probeRow |])

let private swapStopsWhenDisposed () =
    defineProbe ()
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal (listedItems 6))
    let host = host ()

    globalThis.document.body.appendChild host
    |> ignore

    let mounted = mount graph host (Component.render probeList items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let inserts = countInserts list
    let next = Array.copy items.Value
    let held = next[1]
    next[1] <- next[4]
    next[4] <- held
    setProbeHook (fun () -> mounted.Dispose ())
    graph.Run (fun () -> items.Value <- next)
    equal 1 (inserts ())
    host.remove ()

#if RANVIER_VIEW_COUNTERS
let private swapSkipsRun () =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal (listedItems 1000))
    let host = host ()
    let mounted = mount graph host (listedView false items)
    let next = Array.copy items.Value
    let held = next[1]
    next[1] <- next[998]
    next[998] <- held
    Mounting.ListCounters.increasingRuns.Value <- 0
    graph.Run (fun () -> items.Value <- next)
    equal 0 Mounting.ListCounters.increasingRuns.Value

    let adjacent = Array.copy next
    let neighbour = adjacent[3]
    adjacent[3] <- adjacent[4]
    adjacent[4] <- neighbour
    graph.Run (fun () -> items.Value <- adjacent)
    equal 0 Mounting.ListCounters.increasingRuns.Value

    let rotated = Array.copy adjacent
    let first = rotated[2]
    rotated[2] <- rotated[3]
    rotated[3] <- rotated[4]
    rotated[4] <- first
    graph.Run (fun () -> items.Value <- rotated)
    equal 1 Mounting.ListCounters.increasingRuns.Value
    mounted.Dispose ()
#endif

#if RANVIER_VIEW_COUNTERS
let private windowedRemove (keyed: bool) =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal (listedItems 200))
    let host = host ()
    let mounted = mount graph host (listedView keyed items)
    Mounting.ListCounters.validatedRows.Value <- 0
    Mounting.ListCounters.lookups.Value <- 0
    graph.Run (fun () -> items.Value <- Array.removeAt 100 items.Value)

    let validated, lookups =
        Mounting.ListCounters.validatedRows.Value, Mounting.ListCounters.lookups.Value

    equal 199. (host.querySelectorAll "li").length

    if validated > 4 || lookups > 1 then
        failwithf "Removing one of 200 rows validated %d rows and looked up %d" validated lookups

    mounted.Dispose ()

let private eachWindowedRemove () =
    windowedRemove false

let private keyedWindowedRemove () =
    windowedRemove true
#endif

let private inPlaceView (keyed: bool) (tick: Signal<int>) (read: unit -> Listed array) =
    let key (item: Listed) = item.Id

    let current () =
        tick.Value |> ignore
        read ()

    if keyed then
        Keyed.array Program.ops key current (fun item -> Component.render listedRow item)
    else
        let view =
            Component.define (fun _ (tick: Signal<int>) -> tick) (fun t ->
                t.element (HtmlTag "ul") [||] [| t.each key (fun _ -> current ()) listedRow |])

        Component.render view tick

let private inPlaceMutation (keyed: bool) =
    use graph = new Graph ()
    let shared = listedItems 6
    let mutable fresh = Array.copy shared
    let tick = graph.Run (fun () -> Api.createSignal 0)
    let sharedHost, freshHost = host (), host ()
    let sharedMount = mount graph sharedHost (inPlaceView keyed tick (fun () -> shared))
    let freshMount = mount graph freshHost (inPlaceView keyed tick (fun () -> fresh))
    let sharedNodes = System.Collections.Generic.Dictionary<int, obj>()
    let freshNodes = System.Collections.Generic.Dictionary<int, obj>()

    let kept (known: System.Collections.Generic.Dictionary<int, obj>) (host: HTMLElement) =
        let nodes = rowNodes host

        shared
        |> Array.mapi (fun index item ->
            match known.TryGetValue item.Id with
            | true, node -> obj.ReferenceEquals (node, nodes[index])
            | _ ->
                known[item.Id] <- nodes[index]
                true)

    let check () =
        let expected = shared |> Array.map _.Text |> String.concat ""
        equal (expected, expected) (sharedHost.textContent, freshHost.textContent)
        equal (kept freshNodes freshHost) (kept sharedNodes sharedHost)

    let edit (change: unit -> unit) =
        change ()
        fresh <- Array.copy shared
        graph.Run (fun () -> tick.Value <- tick.Value + 1)
        check ()

    check ()
    edit (fun () -> shared[3] <- { Id = 50; Text = "50," })
    edit (fun () -> shared[1] <- { Id = 1; Text = "X," })

    edit (fun () ->
        let first = shared[0]
        shared[0] <- shared[5]
        shared[5] <- first)

    edit (fun () -> shared[3] <- { Id = 3; Text = "3," })
    sharedMount.Dispose ()
    freshMount.Dispose ()

let private eachInPlaceMutation () =
    inPlaceMutation false

let private keyedInPlaceMutation () =
    inPlaceMutation true

let private failedPassThenValid (keyed: bool) =
    use graph = new Graph ()
    let original = listedItems 10
    let items = graph.Run (fun () -> Api.createSignal original)
    let host = host ()
    let mounted, reports = reported graph host (listedView keyed items)
    let before = rowNodes host
    graph.Run (fun () -> items.Value <- Array.append original [| original[2] |])
    equal 1 reports.Count
    let valid = Array.removeAt 5 original
    graph.Run (fun () -> items.Value <- valid)
    let expectedNodes = Array.removeAt 5 before

    equal (valid |> Array.map _.Text |> String.concat "", true, 1) (host.textContent, sameNodes expectedNodes (rowNodes host), reports.Count)

    mounted.Dispose ()

let private eachFailedPassThenValid () =
    failedPassThenValid false

let private keyedFailedPassThenValid () =
    failedPassThenValid true

// Removes row `removed` of 10 with a foreign row inserted before row `position`, or before the region's end marker at 10.
let private foreignNearWindow (keyed: bool) (position: int) (removed: int) (eachMessage: string) =
    use graph = new Graph ()
    let original = listedItems 10
    let items = graph.Run (fun () -> Api.createSignal original)
    let host = host ()
    let mounted, reports = reported graph host (listedView keyed items)
    let before = rowNodes host

    let anchor =
        if position < before.Length then
            before[position] :?> Node
        else
            (before[position - 1] :?> Node).nextSibling.Value

    let foreign = globalThis.document.createElement "li"

    anchor.parentNode.Value.insertBefore (foreign, anchor)
    |> ignore

    let expectedNodes = Array.insertAt position (box foreign) before

    // An authored row's own mount reports the foreign node first.
    let message =
        if keyed then
            "Range boundaries or owned roots were externally mutated"
        else
            eachMessage

    graph.Run (fun () -> items.Value <- Array.removeAt removed original)
    equal ([ message ], true) (reports[0] |> List.map _.Message, sameNodes expectedNodes (rowNodes host))
    graph.Run (fun () -> items.Value <- Array.removeAt 0 original)
    equal (2, [ message ], true) (reports.Count, reports[1] |> List.map _.Message, sameNodes expectedNodes (rowNodes host))
    mounted.Dispose ()

let private rowsMutated = "Keyed-list rows or contents were externally mutated"

let private eachForeignBeforeNeighbour () =
    foreignNearWindow false 4 5 rowsMutated

let private keyedForeignBeforeNeighbour () =
    foreignNearWindow true 4 5 rowsMutated

let private eachForeignBeforeWindow () =
    foreignNearWindow false 5 5 rowsMutated

let private keyedForeignBeforeWindow () =
    foreignNearWindow true 5 5 rowsMutated

let private eachForeignAfterWindow () =
    foreignNearWindow false 6 5 rowsMutated

let private keyedForeignAfterWindow () =
    foreignNearWindow true 6 5 rowsMutated

let private eachForeignBeforeEnd () =
    foreignNearWindow false 10 9 "Keyed-list boundaries were externally reordered"

let private keyedForeignBeforeEnd () =
    foreignNearWindow true 10 9 "Keyed-list boundaries were externally reordered"

let private eachMatchesReferenceOrder () =
    use graph = new Graph ()
    listedLog.Clear ()
    let mutable state = 7.0

    let next bound =
        state <- (state * 48271.0) % 2147483647.0
        int (state % float bound)

    let items = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let nodes = System.Collections.Generic.Dictionary<int, obj>()
    let mutable nextId = 1

    for _ in 1..200 do
        let current = items.Value |> Array.map _.Id |> ResizeArray

        for _ in 1 .. next 3 do
            if current.Count > 0 then
                current.RemoveAt (next current.Count)

        for _ in 1 .. next 3 do
            current.Insert (next (current.Count + 1), nextId)
            nextId <- nextId + 1

        for _ in 1 .. next 3 do
            if current.Count > 1 then
                let i, j = next current.Count, next current.Count
                let swapped = current[i]
                current[i] <- current[j]
                current[j] <- swapped

        let order = current.ToArray ()

        graph.Run (fun () ->
            items.Value <-
                order
                |> Array.map (fun id -> { Id = id; Text = string id + "," }))

        equal
            (order
             |> Array.map (fun id -> string id + ",")
             |> String.concat "")
            host.textContent

        let rendered = host.querySelectorAll "li"

        for index in 0 .. order.Length - 1 do
            let node = box (rendered.item (float index))

            match nodes.TryGetValue order[index] with
            | true, known -> equal true (obj.ReferenceEquals (known, node))
            | _ -> nodes[order[index]] <- node

    mounted.Dispose ()
    equal 0. host.childNodes.length

let private stableView (keyed: bool) (items: Signal<Listed array>) =
    if keyed then
        Keyed.array Program.ops (fun (item: Listed) -> item.Id) (fun () -> items.Value) (fun current -> Component.render listedRow current)
    else
        Component.render listed items

let private stableItemsMatchReferenceOrder (keyed: bool) =
    use graph = new Graph ()
    listedLog.Clear ()
    let mutable state = 20261015.0

    let next bound =
        state <- (state * 48271.0) % 2147483647.0
        int (state % float bound)

    let items = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let host = host ()
    let mounted = mount graph host (stableView keyed items)
    let nodes = System.Collections.Generic.Dictionary<int, obj>()
    let records = System.Collections.Generic.Dictionary<int, Listed>()
    let mutable nextId = 1

    let record id =
        match records.TryGetValue id with
        | true, known -> known
        | _ ->
            let created = { Id = id; Text = string id + "," }
            records[id] <- created
            created

    let apply (order: int array) =
        graph.Run (fun () -> items.Value <- Array.map record order)

        equal
            (order
             |> Array.map (fun id -> string id + ",")
             |> String.concat "")
            host.textContent

        let rendered = host.querySelectorAll "li"
        equal (float order.Length) rendered.length

        for index in 0 .. order.Length - 1 do
            let node = box (rendered.item (float index))

            match nodes.TryGetValue order[index] with
            | true, known -> equal true (obj.ReferenceEquals (known, node))
            | _ -> nodes[order[index]] <- node

    let initial = Array.init 30 id
    nextId <- 30
    apply initial

    for _ in 1..300 do
        let current = items.Value |> Array.map _.Id |> ResizeArray

        for _ in 1 .. 1 + next 3 do
            if current.Count > 0 then
                current.RemoveAt (next current.Count)

        for _ in 1 .. 1 + next 3 do
            current.Insert (next (current.Count + 1), nextId)
            nextId <- nextId + 1

        for _ in 1 .. 1 + next 3 do
            if current.Count > 1 then
                let i, j = next current.Count, next current.Count
                let swapped = current[i]
                current[i] <- current[j]
                current[j] <- swapped

        apply (current.ToArray ())

    mounted.Dispose ()
    equal 0. host.childNodes.length

let private eachStableItemsMatchReferenceOrder () =
    stableItemsMatchReferenceOrder false

let private keyedStableItemsMatchReferenceOrder () =
    stableItemsMatchReferenceOrder true

let private recreatedView (keyed: bool) (items: Signal<Listed array>) =
    let read () =
        items.Value
        |> Array.map (fun row -> { row with Text = row.Text })

    let key (item: Listed) = item.Id

    if keyed then
        Keyed.array Program.ops key read (fun current -> Component.render listedRow current)
    else
        let recreated =
            Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
                t.element (HtmlTag "ul") [||] [| t.each key (fun _ -> read ()) listedRow |])

        Component.render recreated items

let private recreatedRows (keyed: bool) =
    use graph = new Graph ()
    listedLog.Clear ()

    let row id =
        { Id = id; Text = string id + "," }

    let items = graph.Run (fun () -> Api.createSignal (Array.init 5 row))
    let host = host ()
    let mounted = mount graph host (recreatedView keyed items)
    let nodes = System.Collections.Generic.Dictionary<int, obj>()

    let remember () =
        let rendered = host.querySelectorAll "li"

        for index in 0 .. int rendered.length - 1 do
            let node = rendered.item (float index)
            let text = node.textContent |> Option.defaultValue ""
            let id = int (text.TrimEnd ',')

            match nodes.TryGetValue id with
            | true, known -> equal true (obj.ReferenceEquals (known, box node))
            | _ -> nodes[id] <- box node

    let set (order: int array) =
        graph.Run (fun () -> items.Value <- Array.map row order)

        equal
            (order
             |> Array.map (fun id -> string id + ",")
             |> String.concat "")
            host.textContent

        remember ()

    remember ()
    set [| 0; 1; 99; 2; 3; 4 |]
    set [| 0; 1; 99; 3; 4 |]
    set [| 4; 1; 99; 3; 0 |]
    set [| 0; 3; 99; 1; 4 |]
    set [| 4; 1; 99; 3; 0 |]
    set [| 0; 3; 99; 1; 4 |]
    set [| 3; 99; 1; 4 |]
    set [| 3; 99; 1 |]
    set [||]
    equal 0. (host.querySelectorAll "li").length
    mounted.Dispose ()

let private eachRecreatedRows () =
    recreatedRows false

let private keyedRecreatedRows () =
    recreatedRows true

let private recreatedDuplicates (keyed: bool) =
    use graph = new Graph ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (recreatedView keyed items) context.Backend)

    let before = host.innerHTML
    graph.Run (fun () -> items.Value <- [| { Id = 3; Text = "c" }; { Id = 3; Text = "d" } |])
    equal (before, 1, true) (host.innerHTML, reported.Count, reported[0] :? System.InvalidOperationException)
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "B" } |])
    equal ("B", 1) (host.textContent, reported.Count)
    mounted.Dispose ()

let private eachRecreatedDuplicates () =
    recreatedDuplicates false

let private keyedRecreatedDuplicates () =
    recreatedDuplicates true

let private recreatedEqualKeyUpdate (keyed: bool) =
    use graph = new Graph ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let host = host ()
    let mounted = mount graph host (recreatedView keyed items)
    let first = host.querySelector "li"
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "A" }; { Id = 2; Text = "b" } |])
    equal ("Ab", true) (host.textContent, obj.ReferenceEquals (first, host.querySelector "li"))
    mounted.Dispose ()

let private stableEqualKeyUpdate (keyed: bool) =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal (Array.init 5 (fun id -> { Id = id; Text = string id + "," })))

    let read () =
        items.Value

    let key (item: Listed) = item.Id

    let view =
        if keyed then
            Keyed.array Program.ops key read (fun current -> Component.render listedRow current)
        else
            let stable =
                Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
                    t.element (HtmlTag "ul") [||] [| t.each key (fun _ -> read ()) listedRow |])

            Component.render stable items

    let host = host ()
    let mounted = mount graph host view

    let before =
        Array.init 5 (fun index -> box ((host.querySelectorAll "li").item(float index)))

    let setups = listedLog.Count

    graph.Run (fun () ->
        items.Value <-
            items.Value
            |> Array.mapi (fun index it -> if index = 3 then { it with Text = "X" } else it))

    let after =
        Array.init 5 (fun index -> box ((host.querySelectorAll "li").item(float index)))

    equal "0,1,2,X4," host.textContent
    equal (true, setups) (Array.forall2 (fun a b -> obj.ReferenceEquals (a, b)) before after, listedLog.Count)
    mounted.Dispose ()

let private eachStableEqualKeyUpdate () =
    stableEqualKeyUpdate false

let private keyedStableEqualKeyUpdate () =
    stableEqualKeyUpdate true

let private eachRecreatedEqualKeyUpdate () =
    recreatedEqualKeyUpdate false

let private keyedRecreatedEqualKeyUpdate () =
    recreatedEqualKeyUpdate true

let private emptyRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.fragment [||])

let private emptyListed =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) emptyRow |])

let private zeroNodeRows () =
    use graph = new Graph ()

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = "" } |])

    let host = host ()
    let mounted = mount graph host (Component.render emptyListed items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let markers = rowMarkers list
    equal 3 markers.Length
    graph.Run (fun () -> items.Value <- [| for id in [ 3; 1; 2 ] -> { Id = id; Text = "" } |])
    let moved = rowMarkers list

    equal
        true
        (obj.ReferenceEquals (moved[0], markers[2])
         && obj.ReferenceEquals (moved[1], markers[0])
         && obj.ReferenceEquals (moved[2], markers[1]))

    graph.Run (fun () -> items.Value <- [||])
    equal 0 (rowMarkers list).Length
    mounted.Dispose ()

let private disposalCleansBeforeRelease (rootLevel: bool) =
    use graph = new Graph ()
    let host = host ()
    let counts = ResizeArray<float>()

    let watching =
        Component.define
            (fun setup (item: unit -> Listed) ->
                setup.OnCleanup (fun () -> counts.Add (host.querySelectorAll "li").length)
                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let watched =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            let rows =
                t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) watching

            if rootLevel then
                t.fragment [| rows |]
            else
                t.element (HtmlTag "ul") [||] [| rows |])

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = string id } |])

    let mounted = mount graph host (Component.render watched items)
    equal 3. (host.querySelectorAll "li").length
    mounted.Dispose ()
    equal ([ 3.; 3.; 3. ], 0.) (List.ofSeq counts, host.childNodes.length)

let private armCleansBeforeRelease () =
    use graph = new Graph ()
    let host = host ()
    let counts = ResizeArray<float>()

    let watchingArm =
        Component.define (fun setup (_: unit -> unit) -> setup.OnCleanup (fun () -> counts.Add (host.querySelectorAll "span").length)) (fun t ->
            t.span [||] [| t.text "on" |])

    let watched =
        Component.define (fun _ (on: Signal<bool>) -> on) (fun t ->
            t.div
                [||]
                [|
                    t.matchWith (fun on -> on.Value) (fun on -> if on then t.arm watchingArm () else t.arm waitingArm ())
                |])

    let on = graph.Run (fun () -> Api.createSignal true)
    let mounted = mount graph host (Component.render watched on)
    graph.Run (fun () -> on.Value <- false)
    equal "waiting" host.textContent
    graph.Run (fun () -> on.Value <- true)
    mounted.Dispose ()
    equal ([ 1.; 1. ], 0.) (List.ofSeq counts, host.childNodes.length)

let private clicksFollowMovedRows () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = string id } |])

    let other = host ()
    let host = host ()

    globalThis.document.body.appendChild host
    |> ignore

    globalThis.document.body.appendChild other
    |> ignore

    let mounted = mount graph host (Component.render listed items)
    let one = host.querySelector "li" |> unbox<HTMLElement>
    graph.Run (fun () -> items.Value <- [| for id in [ 3; 1; 2 ] -> { Id = id; Text = string id } |])
    one.click ()
    mounted.MoveBefore (other, None)
    one.click ()
    graph.Run (fun () -> items.Value <- [| for id in [ 3; 2 ] -> { Id = id; Text = string id } |])
    let removed = one.parentNode.IsNone
    // Back under the delegating host, the removed row's node reaches the listener.
    other.appendChild one |> ignore
    one.click ()
    mounted.Dispose ()
    host.remove ()
    other.remove ()

    equal
        (true, 2)
        (removed,
         listedLog
         |> Seq.filter (fun line -> line = "click 1")
         |> Seq.length)

let private throwingRowCleanup () =
    use graph = new Graph ()
    let host = host ()
    let seen = ResizeArray<float>()

    let throwing =
        Component.define
            (fun setup (item: unit -> Listed) ->
                setup.OnCleanup (fun () ->
                    seen.Add (host.querySelectorAll "li").length
                    failwith "row cleanup")

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) throwing |])

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..2 -> { Id = id; Text = string id } |])

    let mutable atReport = -1.
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount
            graph
            None
            (fun errors ->
                atReport <- (host.querySelectorAll "li").length
                reports.Add errors)
            host
            None
            (fun context -> Program.run (Component.render list items) context.Backend)

    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "2" } |])
    equal ([ 2. ], 1, [ "row cleanup" ], 1.) (List.ofSeq seen, reports.Count, reports[0] |> List.map _.Message, atReport)
    mounted.Dispose ()

let private disposalFromRowSetup () =
    use graph = new Graph ()
    let host = host ()
    let mutable handle: Mounting.Mounted = Unchecked.defaultof<_>

    let disposing =
        Component.define
            (fun _ (item: unit -> Listed) ->
                if (item ()).Id = 3 then
                    handle.Dispose ()

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element
                (HtmlTag "ul")
                [||]
                [|
                    t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) disposing
                |])

    let items = graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" } |])
    let mounted, reports = reported graph host (Component.render list items)
    handle <- mounted
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "a" }; { Id = 3; Text = "c" } |])
    equal (Disposed, 0., 0) (handle.Phase, host.childNodes.length, reports.Count)

type Grouped = { Group: int; Members: Listed array }

let private groupRow =
    Component.define (fun _ (item: unit -> Grouped) -> item) (fun t ->
        t.element
            (HtmlTag "li")
            [||]
            [|
                t.text (fun item -> string (item ()).Group + ":")
                t.each (fun (member': Listed) -> member'.Id) (fun item -> (item ()).Members) listedRow
            |])

let private grouped =
    Component.define (fun _ (items: Signal<Grouped array>) -> items) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.each (fun (group: Grouped) -> group.Group) (fun items -> items.Value) groupRow
            |])

let private nestedListsMoveWhole () =
    use graph = new Graph ()
    listedLog.Clear ()

    let listedOf texts =
        texts
        |> Array.map (fun (id, text) -> { Id = id; Text = text })

    let items =
        graph.Run (fun () ->
            Api.createSignal
                [|
                    {
                        Group = 1
                        Members = listedOf [| 10, "a"; 11, "b" |]
                    }
                    {
                        Group = 2
                        Members = listedOf [| 20, "c" |]
                    }
                |])

    let host = host ()
    let mounted = mount graph host (Component.render grouped items)
    equal "1:ab2:c" host.textContent

    graph.Run (fun () ->
        items.Value <-
            [|
                {
                    Group = 2
                    Members = listedOf [| 20, "c"; 21, "d" |]
                }
                {
                    Group = 1
                    Members = listedOf [| 11, "b" |]
                }
            |])

    equal "2:cd1:b" host.textContent

    graph.Run (fun () ->
        items.Value <-
            [|
                {
                    Group = 1
                    Members = listedOf [| 11, "b" |]
                }
            |])

    equal "1:b" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private boxedRow =
    Component.define (fun _ (item: unit -> obj * string) -> item) (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> snd (item ())) |])

let private boxedListed =
    Component.define (fun _ (items: Signal<(obj * string) array>) -> items) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.each (fun (item: obj * string) -> fst item) (fun items -> items.Value) boxedRow
            |])

let private boxedKeys () =
    use graph = new Graph ()

    let items =
        graph.Run (fun () -> Api.createSignal [| box 1, "int"; box 1L, "long"; box "1", "string" |])

    let host = host ()
    let mounted = mount graph host (Component.render boxedListed items)
    let rendered = host.querySelectorAll "li"
    let long = box (rendered.item 1.)
    graph.Run (fun () -> items.Value <- [| box 1L, "LONG"; box "1", "string" |])
    equal "LONGstring" host.textContent
    equal true (obj.ReferenceEquals (long, box ((host.querySelectorAll "li").item 0.)))
    mounted.Dispose ()

let private disposalAfterCleanupFailure placement =
    use graph = new Graph ()
    let host, other = host (), host ()
    let mutable handle: Mounting.Mounted = Unchecked.defaultof<_>
    let mutable cleanups = 0

    let ending =
        Component.define
            (fun setup (item: unit -> Listed) ->
                setup.OnCleanup (fun () ->
                    cleanups <- cleanups + 1

                    if cleanups = 1 then
                        failwith "first cleanup"
                    else
                        handle.Dispose ())

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            let rows = t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) ending

            if placement = "root" then
                t.fragment [| rows |]
            else
                t.element (HtmlTag "ul") [||] [| rows |])

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = string id } |])

    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context ->
            let backend = context.Backend

            if placement = "adopted" then
                backend.Element (
                    HtmlTag "div",
                    [||],
                    fun () -> backend.Element (HtmlTag "section", [||], (fun () -> Program.run (Component.render list items) backend))
                )
            else
                Program.run (Component.render list items) backend)

    let adopted =
        if placement = "adopted" then
            let section = host.querySelector "section" |> unbox<HTMLElement>
            other.appendChild section |> ignore
            Some (Mounting.mount graph None ignore other None (fun context -> context.Native (section, Mounting.Owned)))
        else
            None

    handle <- mounted
    graph.Run (fun () -> items.Value <- [| { Id = 3; Text = "3" } |])

    let rows =
        (host.querySelectorAll "li").length
        + (other.querySelectorAll "li").length

    let hostNodes = host.childNodes.length

    adopted
    |> Option.iter (fun (mount: Mounting.Mounted) -> mount.Dispose ())

    equal
        (Disposed, 0., 0., [ [ "first cleanup" ] ])
        (handle.Phase,
         rows,
         hostNodes,
         reports
         |> Seq.map (List.map _.Message)
         |> List.ofSeq)

let private adoptedElementLosesRows () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..2 -> { Id = id; Text = string id } |])

    let host, other = host (), host ()

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            let backend = context.Backend

            backend.Element (
                HtmlTag "div",
                [||],
                fun () -> backend.Element (HtmlTag "section", [||], (fun () -> Program.run (Component.render listed items) backend))
            ))

    let section = host.querySelector "section" |> unbox<HTMLElement>
    other.appendChild section |> ignore

    let adopted =
        Mounting.mount graph None ignore other None (fun context -> context.Native (section, Mounting.Owned))

    mounted.Dispose ()
    let rows = (section.querySelectorAll "li").length
    adopted.Dispose ()
    equal (0., 0., 0.) (rows, host.childNodes.length, other.childNodes.length)

let private focusSurvivesOtherMoves () =
    use graph = new Graph ()

    let field =
        Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
            t.element (HtmlTag "li") [||] [| t.input [| t.Props.Input.value (fun item -> (item ()).Text) |] |])

    let fields =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) field |])

    let items =
        graph.Run (fun () -> Api.createSignal [| for id in 1..4 -> { Id = id; Text = "value" + string id } |])

    let host = host ()

    globalThis.document.body.appendChild host
    |> ignore

    let mounted = mount graph host (Component.render fields items)

    let third =
        (host.querySelectorAll "input").item 2.
        |> unbox<HTMLInputElement>

    third.focus ()
    third.setSelectionRange (1., 3.)
    graph.Run (fun () -> items.Value <- [| for id in [ 2; 3; 4; 1 ] -> { Id = id; Text = "value" + string id } |])
    let focused = isFocused third
    let selection = selectionOf third
    mounted.Dispose ()
    host.remove ()
    equal (true, "1,3") (focused, selection)

[<Emit("(() => { const w = document.defaultView; const protos = [w.Element.prototype, w.CharacterData.prototype]; const olds = protos.map(p => p.remove); let count = 0; protos.forEach((p, i) => { p.remove = function () { count++; return olds[i].call(this); }; }); return () => { protos.forEach((p, i) => { p.remove = olds[i]; }); return count; }; })()")>]
let private countRemoves () : unit -> int = jsNative

[<Emit("Array.from($0.childNodes)")>]
let private childrenOf (node: Node) : obj array = jsNative

let private clearsWithoutRemoves () =
    use graph = new Graph ()
    listedLog.Clear ()
    let items = graph.Run (fun () -> Api.createSignal (listedItems 1000))
    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let before = childrenOf list
    let removes = countRemoves ()
    graph.Run (fun () -> items.Value <- [||])
    let removed = removes ()
    let after = childrenOf list

    equal
        (0, 2, true, 1000)
        (removed,
         after.Length,
         obj.ReferenceEquals (after[0], before[0])
         && obj.ReferenceEquals (after[1], before[before.Length - 1]),
         listedLog
         |> Seq.filter (fun line -> line.StartsWith "cleanup")
         |> Seq.length)

    graph.Run (fun () -> items.Value <- listedItems 3)
    equal "0,1,2," host.textContent
    mounted.Dispose ()

let private bordered =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.element (HtmlTag "p") [||] [| t.text "before" |]
                t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) listedRow
                t.element (HtmlTag "p") [||] [| t.text "after" |]
            |])

let private clearsBetweenSiblings () =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal (listedItems 50))
    let rooted = graph.Run (fun () -> Api.createSignal (listedItems 50))
    let host, other = host (), host ()
    other.innerHTML <- "<p>outside</p>"
    let a = mount graph host (Component.render bordered items)
    let b = mount graph other (Component.render rootListed rooted)
    let removes = countRemoves ()

    graph.Run (fun () ->
        items.Value <- [||]
        rooted.Value <- [||])

    let removed = removes ()
    let list = host.querySelector "ul" |> unbox<HTMLElement>

    equal
        (0, 4., "beforeafter", 5., "outside", 0.)
        (removed, list.childNodes.length, list.textContent, other.childNodes.length, other.textContent, (other.querySelectorAll "li").length)

    graph.Run (fun () ->
        items.Value <- listedItems 2
        rooted.Value <- listedItems 2)

    equal ("before0,1,after", "outside0,1,") (host.textContent, other.textContent)
    a.Dispose ()
    b.Dispose ()
    equal (0., "<p>outside</p>") (host.childNodes.length, other.innerHTML)

let private loggedGroupRow =
    Component.define
        (fun setup (item: unit -> Grouped) ->
            let group = (item ()).Group
            setup.OnCleanup (fun () -> listedLog.Add ("group cleanup " + string group))
            item)
        (fun t ->
            t.element
                (HtmlTag "li")
                [||]
                [|
                    t.text (fun item -> string (item ()).Group + ":")
                    t.each (fun (member': Listed) -> member'.Id) (fun item -> (item ()).Members) listedRow
                |])

let private keyedClearKeepsOrder () =
    use graph = new Graph ()
    listedLog.Clear ()

    let groups =
        [|
            for group in 1..3 ->
                {
                    Group = group
                    Members = [| for id in 1..2 -> { Id = group * 10 + id; Text = "" } |]
                }
        |]

    let items = graph.Run (fun () -> Api.createSignal groups)
    let host = host ()

    let mounted =
        mount graph host (Keyed.array Program.ops (fun (group: Grouped) -> group.Group) (fun () -> items.Value) (Component.render loggedGroupRow))

    listedLog.Clear ()
    graph.Run (fun () -> items.Value <- [||])

    equal
        ([
            "cleanup 12"
            "cleanup 11"
            "group cleanup 1"
            "cleanup 22"
            "cleanup 21"
            "group cleanup 2"
            "cleanup 32"
            "cleanup 31"
            "group cleanup 3"
         ],
         0.,
         4.)
        (List.ofSeq listedLog, (host.querySelectorAll "li").length, host.childNodes.length)

    graph.Run (fun () -> items.Value <- groups[0..0])
    equal "1:" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private cleanupsSeeConnectedRows () =
    use graph = new Graph ()
    let host = host ()
    let nodes = System.Collections.Generic.Dictionary<int, HTMLElement>()
    let seen = ResizeArray<string>()

    let watching =
        Component.define
            (fun setup (item: unit -> Listed) ->
                let id = (item ()).Id
                setup.OnCleanup (fun () -> seen.Add (string id + ":" + string nodes[id].isConnected))
                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> string (item ()).Id) |])

    let watched =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) watching |])

    let record () =
        let rendered = host.querySelectorAll "li"

        for index in 0 .. int rendered.length - 1 do
            let node = rendered.item (float index) |> unbox<HTMLElement>
            nodes[int node.textContent] <- node

    globalThis.document.body.appendChild host
    |> ignore

    let items = graph.Run (fun () -> Api.createSignal (listedItems 4))
    let mounted = mount graph host (Component.render watched items)
    record ()
    graph.Run (fun () -> items.Value <- Array.removeAt 1 items.Value)
    graph.Run (fun () -> items.Value <- [||])
    graph.Run (fun () -> items.Value <- listedItems 2)
    record ()
    mounted.Dispose ()
    host.remove ()

    equal
        (6, true)
        (seen.Count,
         seen
         |> Seq.forall (fun line -> line.EndsWith ":true"))

let private failingClearLeavesNoRows () =
    use graph = new Graph ()
    let host = host ()

    let throwing =
        Component.define
            (fun setup (item: unit -> Listed) ->
                let id = (item ()).Id

                setup.OnCleanup (fun () ->
                    if id = 1 then
                        failwith "row cleanup")

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) throwing |])

    globalThis.document.body.appendChild host
    |> ignore

    let items = graph.Run (fun () -> Api.createSignal (listedItems 3))
    let mounted, reports = reported graph host (Component.render list items)
    graph.Run (fun () -> items.Value <- [||])
    let rows = (globalThis.document.querySelectorAll "li").length
    let list = host.querySelector "ul" |> unbox<HTMLElement>

    equal
        (0., 2., [ [ "row cleanup" ] ])
        (rows,
         list.childNodes.length,
         reports
         |> Seq.map (List.map _.Message)
         |> List.ofSeq)

    mounted.Dispose ()
    host.remove ()

let private failedInsertionLeavesStagingEmpty () =
    use graph = new Graph ()

    let failing =
        Component.define
            (fun _ (item: unit -> Listed) ->
                if (item ()).Id = 3 then
                    failwith "row setup"

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) failing |])

    let items = graph.Run (fun () -> Api.createSignal (listedItems 2))
    let host = host ()
    let mounted, reports = reported graph host (Component.render list items)
    graph.Run (fun () -> items.Value <- listedItems 4)
    let removes = countRemoves ()
    graph.Run (fun () -> items.Value <- [||])
    let removed = removes ()
    let ul = host.querySelector "ul" |> unbox<HTMLElement>
    let inserts = countInserts ul
    graph.Run (fun () -> items.Value <- [| { Id = 4; Text = "4," }; { Id = 5; Text = "5," } |])
    equal (0, 1, "4,5,", 1) (removed, inserts (), host.textContent, reports.Count)
    mounted.Dispose ()

let private retiredRowLeavesWhole () =
    use graph = new Graph ()

    let items =
        graph.Run (fun () ->
            Api.createSignal
                [|
                    for group in 1..2 ->
                        {
                            Group = group
                            Members = [| for id in 1..3 -> { Id = group * 10 + id; Text = "" } |]
                        }
                |])

    let host = host ()
    let mounted = mount graph host (Component.render grouped items)
    let first = host.querySelector "li" |> unbox<HTMLElement>
    let removes = countRemoves ()
    graph.Run (fun () -> items.Value <- items.Value[1..])
    let removed = removes ()

    equal (1, true, 3., 3.) (removed, first.parentNode.IsNone, (first.querySelectorAll "li").length, (host.querySelectorAll "li li").length)

    mounted.Dispose ()
    equal 0. host.childNodes.length

let private disposalLeavesDetachedHeldRows () =
    use graph = new Graph ()
    let host = host ()
    let mutable handle: Mounting.Mounted = Unchecked.defaultof<_>
    let mutable cleanups = 0

    let pair =
        Component.define
            (fun setup (item: unit -> Listed) ->
                setup.OnCleanup (fun () ->
                    cleanups <- cleanups + 1

                    if cleanups = 2 then
                        handle.Dispose ())

                item)
            (fun t ->
                t.fragment
                    [|
                        t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |]
                        t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |]
                    |])

    let group =
        Component.define (fun _ (item: unit -> Grouped) -> item) (fun t ->
            t.fragment
                [|
                    t.each (fun (member': Listed) -> member'.Id) (fun item -> (item ()).Members) pair
                |])

    let groups =
        Component.define (fun _ (items: Signal<Grouped array>) -> items) (fun t ->
            t.element
                (HtmlTag "ul")
                [||]
                [|
                    t.each (fun (group: Grouped) -> group.Group) (fun items -> items.Value) group
                |])

    let items =
        graph.Run (fun () ->
            Api.createSignal
                [|
                    {
                        Group = 1
                        Members = [| for id in 1..3 -> { Id = id; Text = string id } |]
                    }
                |])

    let mounted, reports = reported graph host (Component.render groups items)
    handle <- mounted
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    graph.Run (fun () -> items.Value <- [| { items.Value[0] with Members = [||] } |])

    equal
        (Disposed, 0., 0., [])
        (mounted.Phase,
         host.childNodes.length,
         (list.querySelectorAll "li").length,
         reports
         |> Seq.map (List.map _.Message)
         |> List.ofSeq)

let private loggedGrouped =
    Component.define (fun _ (items: Signal<Grouped array>) -> items) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.each (fun (group: Grouped) -> group.Group) (fun items -> items.Value) loggedGroupRow
            |])

let private nestedClearKeepsOrder () =
    use graph = new Graph ()
    listedLog.Clear ()

    let groups =
        [|
            for group in 1..3 ->
                {
                    Group = group
                    Members = [| for id in 1..2 -> { Id = group * 10 + id; Text = "" } |]
                }
        |]

    let items = graph.Run (fun () -> Api.createSignal groups)
    let host = host ()
    let mounted = mount graph host (Component.render loggedGrouped items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let before = childrenOf list
    listedLog.Clear ()
    graph.Run (fun () -> items.Value <- [||])
    let after = childrenOf list

    equal
        ([
            "cleanup 12"
            "cleanup 11"
            "group cleanup 1"
            "cleanup 22"
            "cleanup 21"
            "group cleanup 2"
            "cleanup 32"
            "cleanup 31"
            "group cleanup 3"
         ],
         0.,
         2,
         true)
        (List.ofSeq listedLog,
         (list.querySelectorAll "li").length,
         after.Length,
         obj.ReferenceEquals (after[0], before[0])
         && obj.ReferenceEquals (after[1], before[before.Length - 1]))

    graph.Run (fun () -> items.Value <- groups[0..0])
    equal "1:" host.textContent
    mounted.Dispose ()

let browserCases: (string * (unit -> unit)) array =
    [|
        "focus and selection inside a row survive moving another row", focusSurvivesOtherMoves
        "clearing a list between foreign siblings removes only the list's nodes", clearsBetweenSiblings
    |]

let private eachEmpty () =
    use graph = new Graph ()
    listedLog.Clear ()
    let nested = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let rooted = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let first, second = host (), host ()
    let a = mount graph first (Component.render listed nested)
    let b = mount graph second (Component.render rootListed rooted)
    let list = first.querySelector "ul" |> unbox<HTMLElement>
    equal (2., 2, "") (list.childNodes.length, regionMarkers second, second.textContent)

    graph.Run (fun () ->
        nested.Value <- [| { Id = 1; Text = "a" } |]
        rooted.Value <- [| { Id = 2; Text = "b" } |])

    equal ("a", "b") (first.textContent, second.textContent)
    (second.querySelector "li" |> unbox<HTMLElement>).click()
    equal true (listedLog.Contains "click 2")

    graph.Run (fun () ->
        nested.Value <- [||]
        rooted.Value <- [||])

    equal (2., 2, "", "") (list.childNodes.length, regionMarkers second, first.textContent, second.textContent)
    a.Dispose ()
    b.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

let private eachTwin () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let first, second = host (), host ()
    let templateMount = mount graph first (Component.render listed items)
    let twinMount = mount graph second (Component.renderWith Program.ops listed items)

    let matches expected =
        equal (withoutComments second, expected) (withoutComments first, withoutComments second)

    matches "<ul><li>a</li><li>b</li></ul>"
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "a" }; { Id = 3; Text = "c" }; { Id = 2; Text = "b" } |])
    matches "<ul><li>a</li><li>c</li><li>b</li></ul>"
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])
    matches "<ul><li>a</li><li>b</li></ul>"
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "b" }; { Id = 1; Text = "a" } |])
    matches "<ul><li>b</li><li>a</li></ul>"
    let retained = first.querySelector "li" |> unbox<HTMLElement>
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "B" }; { Id = 1; Text = "a" } |])
    matches "<ul><li>B</li><li>a</li></ul>"
    equal true (obj.ReferenceEquals (retained, first.querySelector "li"))
    templateMount.Dispose ()
    twinMount.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

type Shelf =
    {
        Label: Signal<string>
        Left: Signal<Listed array>
        Right: Signal<Listed array>
    }

let private shelved =
    Component.define (fun _ (shelf: Shelf) -> shelf) (fun t ->
        t.fragment
            [|
                t.text "<"
                t.each (fun (item: Listed) -> item.Id) (fun shelf -> shelf.Left.Value) listedRow
                t.text (fun shelf -> shelf.Label.Value)
                t.each (fun (item: Listed) -> item.Id) (fun shelf -> shelf.Right.Value) listedRow
            |])

let private rootRegions () =
    use graph = new Graph ()
    listedLog.Clear ()

    let shelf =
        graph.Run (fun () ->
            {
                Label = Api.createSignal "|"
                Left = Api.createSignal [| { Id = 1; Text = "a" } |]
                Right = Api.createSignal [| { Id = 2; Text = "b" }; { Id = 3; Text = "c" } |]
            })

    let first, second = host (), host ()
    let mounted = mount graph first (Component.render shelved shelf)
    equal ("<a|bc", 4) (first.textContent, regionMarkers first)

    let moved =
        first.querySelectorAll("li").item 2.
        |> unbox<HTMLElement>

    graph.Run (fun () ->
        shelf.Left.Value <- [| { Id = 4; Text = "d" }; { Id = 1; Text = "A" } |]
        shelf.Right.Value <- [| { Id = 3; Text = "c" }; { Id = 2; Text = "b" } |]
        shelf.Label.Value <- "/")

    equal ("<dA/cb", true) (first.textContent, obj.ReferenceEquals (moved, first.querySelectorAll("li").item 2.))
    mounted.MoveBefore (second, None)
    equal (0., "<dA/cb", 4) (first.childNodes.length, second.textContent, regionMarkers second)
    moved.click ()

    graph.Run (fun () ->
        shelf.Left.Value <- [||]
        shelf.Right.Value <- [| { Id = 2; Text = "b" }; { Id = 5; Text = "e" } |])

    equal ("</be", 4) (second.textContent, regionMarkers second)
    mounted.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

    equal
        (List.sort
            [
                "setup 1"
                "setup 2"
                "setup 3"
                "setup 4"
                "setup 5"
                "click 3"
                "cleanup 1"
                "cleanup 2"
                "cleanup 3"
                "cleanup 4"
                "cleanup 5"
            ])
        (List.sort (List.ofSeq listedLog))

type Job = { JobId: int; Stage: Stage }

let private jobRow =
    Component.define (fun _ (job: unit -> Job) -> job) (fun t ->
        t.element (HtmlTag "li") [||] [| t.matchWith (fun job -> (job ()).Stage) (stageArms t) |])

let private jobs =
    Component.define (fun _ (items: Signal<Job array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (job: Job) -> job.JobId) (fun items -> items.Value) jobRow |])

let private nestedRegions () =
    use graph = new Graph ()
    stageLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { JobId = 1; Stage = Waiting }; { JobId = 2; Stage = Working 1 } |])

    let host = host ()
    let mounted = mount graph host (Component.render jobs items)

    let second =
        host.querySelectorAll("li").item 1.
        |> unbox<HTMLElement>

    equal "waitingworking 1" host.textContent

    graph.Run (fun () -> items.Value <- [| { JobId = 2; Stage = Working 2 }; { JobId = 1; Stage = Complete "ok" } |])

    equal ("working 2complete ok", true) (host.textContent, obj.ReferenceEquals (second, host.querySelector "li" |> unbox<HTMLElement>))
    graph.Run (fun () -> items.Value <- [| { JobId = 1; Stage = Complete "ok" } |])
    equal "complete ok" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

    equal
        [
            "setup waiting"
            "setup working"
            "cleanup waiting"
            "setup complete"
            "cleanup working"
            "cleanup complete"
        ]
        (List.ofSeq stageLog)

let private jobItems count =
    Array.init count (fun id ->
        {
            JobId = id
            Stage = if id % 2 = 0 then Waiting else Working id
        })

let private clearsArmRowsWithoutRemoves () =
    use graph = new Graph ()
    stageLog.Clear ()
    let items = graph.Run (fun () -> Api.createSignal (jobItems 100))
    let host = host ()
    let mounted = mount graph host (Component.render jobs items)
    let removes = countRemoves ()
    graph.Run (fun () -> items.Value <- [||])
    let removed = removes ()

    equal
        (0, 0., 100)
        (removed,
         (host.querySelectorAll "li").length,
         stageLog
         |> Seq.filter (fun line -> line.StartsWith "cleanup")
         |> Seq.length)

    graph.Run (fun () -> items.Value <- jobItems 2)
    equal "waitingworking 1" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private removesArmRowLikePlainRow () =
    use graph = new Graph ()
    let armed = graph.Run (fun () -> Api.createSignal (jobItems 3))
    let plain = graph.Run (fun () -> Api.createSignal (listedItems 3))
    let armedHost, plainHost = host (), host ()
    let a = mount graph armedHost (Component.render jobs armed)
    let b = mount graph plainHost (Component.render listed plain)
    let removes = countRemoves ()
    graph.Run (fun () -> armed.Value <- [| armed.Value[0]; armed.Value[2] |])
    let armedRemoved = removes ()
    let removes = countRemoves ()
    graph.Run (fun () -> plain.Value <- [| plain.Value[0]; plain.Value[2] |])
    let plainRemoved = removes ()
    equal (plainRemoved, "waitingwaiting") (armedRemoved, armedHost.textContent)
    a.Dispose ()
    b.Dispose ()

let private eachMarkup () =
    use graph = new Graph ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "<b>" } |])

    equal
        "<ul><!--[--><li>a</li><li>&lt;b&gt;</li><!--]--></ul>"
        (graph.Run (fun () -> Html.render Omit (Program.run (Component.render listed items))))

type BadgeProps =
    {
        Count: Signal<int>
        BadgeReads: int ref
        BadgeLog: ResizeArray<string>
    }

type CardProps =
    {
        Heading: Signal<string>
        Tally: Signal<int>
        CardReads: int ref
        BadgeCounter: int ref
        CardLog: ResizeArray<string>
    }

let private badge =
    Component.define
        (fun setup (props: BadgeProps) ->
            setup.OnCleanup (fun () -> props.BadgeLog.Add "cleanup badge")
            props)
        (fun t ->
            t.button
                [|
                    t.Events.onClick (fun badge _ -> badge.BadgeLog.Add ("badge " + string badge.Count.Value))
                |]
                [|
                    t.text (fun badge ->
                        badge.BadgeReads.Value <- badge.BadgeReads.Value + 1
                        string badge.Count.Value)
                |])

let private card =
    Component.define
        (fun setup (props: CardProps) ->
            setup.OnCleanup (fun () -> props.CardLog.Add "cleanup card")
            props)
        (fun t ->
            t.div
                [| t.Events.onClick (fun card _ -> card.CardLog.Add "card") |]
                [|
                    t.text (fun card ->
                        card.CardReads.Value <- card.CardReads.Value + 1
                        card.Heading.Value)
                    t.render badge (fun card ->
                        {
                            Count = card.Tally
                            BadgeReads = card.BadgeCounter
                            BadgeLog = card.CardLog
                        })
                |])

let private cardProps (graph: Graph) =
    {
        Heading = graph.Run (fun () -> Api.createSignal "h")
        Tally = graph.Run (fun () -> Api.createSignal 1)
        CardReads = ref 0
        BadgeCounter = ref 0
        CardLog = ResizeArray<string>()
    }

let private inlinedChild () =
    use graph = new Graph ()
    let props = cardProps graph
    let host = host ()
    let mounted = mount graph host (Component.render card props)
    equal ("h1", 1, 1) (host.textContent, props.CardReads.Value, props.BadgeCounter.Value)
    graph.Run (fun () -> props.Tally.Value <- 2)
    equal ("h2", 1, 2) (host.textContent, props.CardReads.Value, props.BadgeCounter.Value)
    graph.Run (fun () -> props.Heading.Value <- "H")
    equal ("H2", 2, 2) (host.textContent, props.CardReads.Value, props.BadgeCounter.Value)
    equal true (isNull (badge :> TemplateDefinition).Compiled)
    equal 1 ((card :> TemplateDefinition).Compiled :?> TemplateDom.Compiled).Children.Length

    (host.querySelector "button"
     |> unbox<HTMLButtonElement>)
        .click()

    mounted.Dispose ()

    equal ([ "badge 2"; "card"; "cleanup badge"; "cleanup card" ], 0.) (List.ofSeq props.CardLog, host.childNodes.length)

let private framed =
    Component.define (fun _ (props: CardProps) -> props) (fun t -> t.section [||] [| t.render card id |])

let private inlinedGrandchild () =
    use graph = new Graph ()
    let props = cardProps graph
    let host = host ()
    let mounted = mount graph host (Component.render framed props)
    graph.Run (fun () -> props.Tally.Value <- 5)

    (host.querySelector "button"
     |> unbox<HTMLButtonElement>)
        .click()

    equal ("h5", [ "badge 5"; "card" ]) (host.textContent, List.ofSeq props.CardLog)
    equal 2 ((framed :> TemplateDefinition).Compiled :?> TemplateDom.Compiled).Children.Length
    mounted.Dispose ()
    equal 0. host.childNodes.length

type ShelfProps = { Shelf: Signal<Listed array> }

let private shelf =
    Component.define (fun _ (props: ShelfProps) -> props) (fun t ->
        t.element
            (HtmlTag "ul")
            [||]
            [|
                t.each (fun (item: Listed) -> item.Id) (fun props -> props.Shelf.Value) listedRow
            |])

let private shelfSection =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.section [||] [| t.text "shelf:"; t.render shelf (fun items -> { Shelf = items }) |])

let private inlinedRegion () =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" } |])
    let host = host ()
    let mounted = mount graph host (Component.render shelfSection items)
    equal "shelf:a" host.textContent
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "b" }; { Id = 1; Text = "a" } |])
    equal ("shelf:ba", 2) (host.textContent, regionMarkers (host.querySelector "ul" |> unbox<HTMLElement>))
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private textual =
    Component.define (fun _ (value: Signal<string>) -> value) (fun t -> t.text (fun value -> value.Value))

let private besideText =
    Component.define (fun _ (value: Signal<string>) -> value) (fun t -> t.span [||] [| t.text "a"; t.render textual id |])

let private inlinedText () =
    use graph = new Graph ()
    let value = graph.Run (fun () -> Api.createSignal "b")
    let host = host ()
    let mounted = mount graph host (Component.render besideText value)
    graph.Run (fun () -> value.Value <- "c")
    let span = host.querySelector "span" |> unbox<HTMLElement>
    equal ("ac", 3.) (span.textContent, span.childNodes.length)
    mounted.Dispose ()

let private inlinedFailure () =
    use graph = new Graph ()

    let failingChild =
        Component.define (fun _ () -> failwith "child") (fun t -> t.span [||] [||])

    let parent =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render failingChild id |])

    let host = host ()
    host.innerHTML <- "<span>keep</span>"
    let mutable failed = false

    try
        mount graph host (Component.render parent ())
        |> ignore
    with error ->
        failed <- error.Message = "child"

    equal (true, "<span>keep</span>") (failed, host.innerHTML)

type Composite =
    {
        Caption: Signal<string>
        Card: CardProps
        Items: Signal<Listed array>
    }

let private composite =
    Component.define (fun _ (props: Composite) -> props) (fun t ->
        t.div
            [| t.Attrs.title (fun r -> r.Caption.Value) |]
            [|
                t.text (fun r -> r.Caption.Value)
                t.render card (fun r -> r.Card)
                t.render shelf (fun r -> { Shelf = r.Items })
            |])

let private inlinedTwin () =
    use graph = new Graph ()
    let card = cardProps graph

    let props =
        graph.Run (fun () ->
            {
                Caption = Api.createSignal "c"
                Card = card
                Items = Api.createSignal [| { Id = 1; Text = "a" } |]
            })

    let first, second = host (), host ()
    let templateMount = mount graph first (Component.render composite props)

    let twinMount =
        mount graph second (Component.renderWith Program.ops composite props)

    let matches expected =
        equal (withoutComments second, expected) (withoutComments first, withoutComments second)

    matches "<div title=\"c\">c<div>h<button>1</button></div><ul><li>a</li></ul></div>"
    graph.Run (fun () -> props.Caption.Value <- "C")
    matches "<div title=\"C\">C<div>h<button>1</button></div><ul><li>a</li></ul></div>"
    graph.Run (fun () -> card.Tally.Value <- 2)
    matches "<div title=\"C\">C<div>h<button>2</button></div><ul><li>a</li></ul></div>"
    graph.Run (fun () -> card.Heading.Value <- "H")
    matches "<div title=\"C\">C<div>H<button>2</button></div><ul><li>a</li></ul></div>"
    graph.Run (fun () -> props.Items.Value <- [| { Id = 2; Text = "b" }; { Id = 1; Text = "a" } |])
    matches "<div title=\"C\">C<div>H<button>2</button></div><ul><li>b</li><li>a</li></ul></div>"
    templateMount.Dispose ()
    twinMount.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

let private structureRoundTrips () =
    let definitions: TemplateComponent list =
        [
            holed :> TemplateComponent
            staged :> TemplateComponent
            listed :> TemplateComponent
            rootListed :> TemplateComponent
            shelved :> TemplateComponent
            card :> TemplateComponent
            framed :> TemplateComponent
            shelfSection :> TemplateComponent
            besideText :> TemplateComponent
            composite :> TemplateComponent
        ]

    for definition in definitions do
        let markup, _, _ = skeletonMarkup definition.Root
        equal (markup, true) (markup, roundTrips markup)

let private inlinedMarkup () =
    use graph = new Graph ()
    let props = cardProps graph

    equal "<div>h<button>1</button></div>" (graph.Run (fun () -> Html.render Omit (Program.run (Component.render card props))))

type Ledger =
    {
        LedgerLabel: Signal<string>
        LedgerItems: Signal<Listed array>
    }

type LedgerFrame = { Inner: Ledger }

let private ledgerBody =
    Component.define (fun _ (ledger: Ledger) -> ledger) (fun t ->
        t.fragment
            [|
                t.text (fun ledger -> ledger.LedgerLabel.Value)
                t.each (fun (item: Listed) -> item.Id) (fun ledger -> ledger.LedgerItems.Value) listedRow
            |])

let private ledgerFrame =
    Component.define (fun _ (ledger: Ledger) -> { Inner = ledger }) (fun t ->
        t.fragment [| t.text "<"; t.render ledgerBody (fun frame -> frame.Inner); t.text ">" |])

/// <summary>Mounts a multi-root template whose inlined child owns a root-level region beside text, at the mount's top level or inside an authored element.</summary>
let private inlinedRootRegion (wrapped: bool) =
    use graph = new Graph ()
    listedLog.Clear ()

    let ledger =
        graph.Run (fun () ->
            {
                LedgerLabel = Api.createSignal "L"
                LedgerItems = Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |]
            })

    let first, second = host (), host ()

    let view =
        if wrapped then
            Program.element Div [||] [| Component.render ledgerFrame ledger |]
        else
            Component.render ledgerFrame ledger

    let mounted = mount graph first view

    let markers (host: HTMLElement) =
        if wrapped then
            regionMarkers (host.querySelector "div" |> unbox<HTMLElement>)
        else
            regionMarkers host

    equal ("<Lab>", 2) (first.textContent, markers first)
    let retained = first.querySelector "li" |> unbox<HTMLElement>

    graph.Run (fun () ->
        ledger.LedgerLabel.Value <- "M"
        ledger.LedgerItems.Value <- [| { Id = 2; Text = "B" }; { Id = 3; Text = "c" }; { Id = 1; Text = "a" } |])

    equal ("<MBca>", true) (first.textContent, obj.ReferenceEquals (retained, first.querySelectorAll("li").item 2.))
    mounted.MoveBefore (second, None)
    equal (0., "<MBca>", 2) (first.childNodes.length, second.textContent, markers second)
    retained.click ()
    graph.Run (fun () -> ledger.LedgerItems.Value <- [| { Id = 1; Text = "A" } |])
    equal ("<MA>", 2) (second.textContent, markers second)
    mounted.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

    equal
        (List.sort
            [
                "setup 1"
                "setup 2"
                "setup 3"
                "click 1"
                "cleanup 1"
                "cleanup 2"
                "cleanup 3"
            ])
        (List.sort (List.ofSeq listedLog))

/// <summary>Checks template compilation and DOM materialisation.</summary>
let cases =
    [|
        "walk programs reach dynamic nodes in document order", walkPrograms
        "a compiled skeleton round-trips and records every dynamic node", compiledSkeleton
        "a template renders the same DOM as its createComponent twin", twinEquality
        "slot writes happen only for changed values", changedSlotsOnly
        "a throwing reader fails only its slot", readerFailure
        "a slot failing on first render is reported without failing the mount", initialReaderFailure
        "a throwing reporter leaves no failure for the next successful run", throwingReporter
        "a recurring slot failure object is reported again after the slot recovers", recoveredFailureRecurs
        "a throwing setter rolls back the mount", setterRollback
        "template cleanups run in use order without an instance owner", cleanupOrder
        "adjacent text slots keep separate nodes", adjacentTexts
        "a definition mounted twice keeps instances independent", compiledOnce
        "a template inside a removed keyed row stops updating", keyedRows
        "a null text value renders empty", nullText
        "a static template mounts without effects", staticTemplate
        "an isolated slot re-runs only on its own source", isolatedSlots
        "source slots follow their signal and memo", sourceSlots
        "template click handlers receive the instance and stop after disposal", clicks
        "template handlers dispatch after a move", movedClicks
        "focus uses a direct listener beside a delegated click", focusBesideClick
        "queued mounts render templates through expansion", queuedFallback
        "the Browser interpreter renders templates through expansion", browserInterpreter
        "prepared skeletons reject templates", preparedRejects
        "server markup under Fable matches the portable expectation", serverMarkup
        "markup restructured by the HTML parser fails compilation", restructuredMarkup
        "a pending or failed text slot renders empty", unsetText
        "null attribute values remove the attribute", nullAttributes
        "a later setter failure is reported and keeps the other slots", laterSetterFailure
        "a template whose single root is a text slot mounts, updates and disposes", textRoot
        "a single root reached through a fragment mounts, updates and disposes", fragmentRoot
        "a multi-root template moves together and disposes empty", movedRoots
        "walk programs begin with a collect for the empty path", walkProgramEmptyPath
        "a single-root definition compiles to an element skeleton", singleRootCompiled
        "view hole server markup under Fable matches .NET", holeMarkup
        "a slot after a view hole is indexed past both markers without a separator", holeSlotPaths
        "a view hole mounts its view between region markers and disposes it", viewHole
        "a view hole renders the same DOM as its default expansion", holeTwin
        "an empty view hole keeps its markers", emptyHole
        "a view hole reads its view once per instance", viewReadOnce
        "a failing view hole rolls back the mount", failingHole
        "a root-level view hole dispatches clicks before and after a move", rootHoleClicks
        "queued mounts and the Browser interpreter render view holes through expansion", holeFallbacks
        "matchWith switches arms by case and updates payloads in place", matchSwitching
        "a match changed twice in one flush settles on its final case", matchSameFlush
        "matchWith renders the same DOM as its default expansion across a switch and a payload change", matchTwin
        "disposing the mount during an arm's setup ends the switch cleanly", disposedMidSwitch
        "matchWith server markup under Fable matches .NET", matchMarkup
        "two cases selecting the same matchWith definition keep the arm and update its payload", sharedArm
        "branch remounts on a key change and keeps the arm for an equal key", branchSwitching
        "branch renders the same DOM as its default expansion across a key change", branchTwin
        "branch server markup under Fable matches .NET", branchMarkup
        "branch remounts when its arms select another definition for an equal key", branchDefinitionChange
        "a switched arm sets up after the previous arm restores its saved state", armSaveRestore
        "a switched arm whose setup throws leaves the region empty and a later switch mounts", armSetupFailure
        "a previous arm whose cleanup throws is reported and the switched arm mounts", armCleanupFailure
        "a switched arm whose placement fails is disposed and reported once", armPlacementFailure
        "each renders template rows, retains them by key and dispatches their clicks", eachRows
        "each sets up a replacing row before the replaced row's cleanup", eachReplacement
        "duplicate each keys fail the mount, are reported on update and keep the rows", eachDuplicates
        "a two-row swap of 1,000 rows moves two rows", eachSwapMovesTwo
        "swapping the ends of a changed window moves two rows and keeps node identity", swapShapes
        "an each end swap stops placing rows once the mount is disposed", swapStopsWhenDisposed
#if RANVIER_VIEW_COUNTERS
        "a keyed end swap computes no longest run and a rotation computes one", swapSkipsRun
#endif
        "removing a kept item from each sets up no row and keys no item", eachStableRemove
        "removing a kept item from Keyed.array sets up no row and keys no item", keyedStableRemove
#if RANVIER_VIEW_COUNTERS
        "removing one of 200 each rows validates at most 4 rows and looks up at most 1", eachWindowedRemove
        "removing one of 200 Keyed.array rows validates at most 4 rows and looks up at most 1", keyedWindowedRemove
#endif
        "each over an array mutated in place matches a fresh array", eachInPlaceMutation
        "Keyed.array over an array mutated in place matches a fresh array", keyedInPlaceMutation
        "each after a duplicate-key pass renders the next valid items and keeps their nodes", eachFailedPassThenValid
        "Keyed.array after a duplicate-key pass renders the next valid items and keeps their nodes", keyedFailedPassThenValid
        "a foreign row before the row preceding each's changed window is reported on that pass and the next", eachForeignBeforeNeighbour
        "a foreign row before the row preceding Keyed.array's changed window is reported on that pass and the next", keyedForeignBeforeNeighbour
        "a foreign row before each's changed window is reported on that pass and the next", eachForeignBeforeWindow
        "a foreign row before Keyed.array's changed window is reported on that pass and the next", keyedForeignBeforeWindow
        "a foreign row after each's changed window is reported on that pass and the next", eachForeignAfterWindow
        "a foreign row after Keyed.array's changed window is reported on that pass and the next", keyedForeignAfterWindow
        "a foreign row before each's end marker is reported when the last row is removed and on the next pass", eachForeignBeforeEnd
        "a foreign row before Keyed.array's end marker is reported when the last row is removed and on the next pass", keyedForeignBeforeEnd
        "list updates match a reference order and keep node identities", eachMatchesReferenceOrder
        "each over stable items matches a reference order through mixed edits and keeps node identities", eachStableItemsMatchReferenceOrder
        "Keyed.array over stable items matches a reference order through mixed edits and keeps node identities", keyedStableItemsMatchReferenceOrder
        "each over recreated items keeps order and node identities", eachRecreatedRows
        "Keyed.array over recreated items keeps order and node identities", keyedRecreatedRows
        "each over recreated items reports a duplicate key and keeps the rows", eachRecreatedDuplicates
        "Keyed.array over recreated items reports a duplicate key and keeps the rows", keyedRecreatedDuplicates
        "each over recreated items updates the text of an equal-key row", eachRecreatedEqualKeyUpdate
        "Keyed.array over recreated items updates the text of an equal-key row", keyedRecreatedEqualKeyUpdate
        "each over stable items updates the text of an equal-key row and keeps every node", eachStableEqualKeyUpdate
        "Keyed.array over stable items updates the text of an equal-key row and keeps every node", keyedStableEqualKeyUpdate
        "zero-node rows keep their place through their marker", zeroNodeRows
        "disposing a mount runs every row cleanup before a node leaves", fun () -> disposalCleansBeforeRelease false
        "disposing a mount runs every root-level row cleanup before a node leaves", fun () -> disposalCleansBeforeRelease true
        "an arm switch and a mount disposal run the arm's cleanups before its nodes leave", armCleansBeforeRelease
        "delegated clicks reach a moved row and stop after its removal", clicksFollowMovedRows
        "a throwing row cleanup is reported once with its update after the row leaves", throwingRowCleanup
        "disposing the mount from a row's setup ends the update silently", disposalFromRowSetup
        "rows holding nested lists move and leave whole", nestedListsMoveWhole
        "boxed keys of different types keep separate rows", boxedKeys
        "a cleanup failure before a row cleanup disposes the mount is reported once", fun () -> disposalAfterCleanupFailure "element"
        "a row cleanup disposing the mount leaves no row nodes at the mount's top level", fun () -> disposalAfterCleanupFailure "root"
        "a row cleanup disposing the mount leaves no row nodes in an adopted element", fun () -> disposalAfterCleanupFailure "adopted"
        "an adopted element holding a list keeps no row nodes after its mount is disposed", adoptedElementLosesRows
        "clearing 1,000 each rows removes no row node by itself and keeps the region's markers", clearsWithoutRemoves
        "clearing a list between foreign siblings removes only the list's nodes", clearsBetweenSiblings
        "clearing Keyed.array rows holding nested lists keeps their cleanup order", keyedClearKeepsOrder
        "row cleanups see their row connected on remove, clear and mount disposal", cleanupsSeeConnectedRows
        "a clear whose row cleanup throws reports it and leaves no row nodes", failingClearLeavesNoRows
        "a clear and an insertion after a failed row setup each take one operation", failedInsertionLeavesStagingEmpty
        "a retired row holding a nested list leaves whole in one remove", retiredRowLeavesWhole
        "a nested row cleanup disposing the mount leaves no nodes and reports nothing", disposalLeavesDetachedHeldRows
        "clearing template rows holding nested lists keeps their cleanup order and markers", nestedClearKeepsOrder
        "an empty each region keeps its markers, fills and empties", eachEmpty
        "each renders the same DOM as its default expansion across insert, remove, reorder and an item change", eachTwin
        "root-level each regions beside other roots update, reorder, move and dispose", rootRegions
        "a matchWith inside each rows follows, moves and leaves with its row", nestedRegions
        "clearing each rows holding matchWith arms removes no arm node by itself", clearsArmRowsWithoutRemoves
        "removing an each row holding a matchWith arm takes the removes of a plain row", removesArmRowLikePlainRow
        "each server markup under Fable matches .NET", eachMarkup
        "an inlined child shares the clone and keeps its own instance, effect and handlers", inlinedChild
        "a grandchild inlined through a child is hoisted and updates", inlinedGrandchild
        "an inlined child's region reads the child instance", inlinedRegion
        "text beside an inlined text root keeps separate nodes", inlinedText
        "a failing inlined child setup rolls back the mount", inlinedFailure
        "inlined children render the same DOM as their default expansion across parent and child updates", inlinedTwin
        "region and child skeletons round-trip", structureRoundTrips
        "inlined child server markup under Fable matches .NET", inlinedMarkup
        "an inlined child's root-level region beside text updates, reorders, moves and disposes at a mount's top level",
        fun () -> inlinedRootRegion false
        "an inlined child's root-level region beside text updates, reorders, moves and disposes inside an authored element",
        fun () -> inlinedRootRegion true
    |]
