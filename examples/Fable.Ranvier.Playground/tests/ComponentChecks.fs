module ComponentChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Components

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private at (node: Node) (index: int) : ChildNode =
    node.childNodes[index]

let private listen (node: HTMLElement) (action: unit -> unit) : unit -> unit =
    let fn: Event -> obj = fun _ -> action ()
    node.addEventListener ("click", fn)
    fun () -> node.removeEventListener ("click", fn)

let private button (node: HTMLElement) : HTMLElement =
    node.querySelector ("button") |> unbox

let private mountView graph host run view =
    Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun ctx -> run view ctx.Backend)

let private onceUntracked (ops: Operations<'view>) run =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let source, trigger = graph.Run (fun () -> Api.createSignal 1, Api.createSignal 0)
    let mutable setups, cleanups, runs = 0, 0, 0

    let view =
        create
            ops
            (fun context prop ->
                setups <- setups + 1
                context.OnCleanup (fun () -> cleanups <- cleanups + 1)
                ops.Text (string (Prop.read prop)))
            (Prop.ofSignal source)

    equal 0 setups
    let mutable handle = None

    graph.Run (fun () ->
        Api.createEffect (fun () ->
            trigger.Value |> ignore
            runs <- runs + 1
            let mounted = mountView graph host run view
            handle <- Some mounted
            Api.onCleanup mounted.Dispose))

    equal "1" host.textContent
    graph.Run (fun () -> source.Value <- 2)
    equal 1 runs
    equal 1 setups
    equal "1" host.textContent
    graph.Run (fun () -> trigger.Value <- 1)
    equal "2" host.textContent
    equal 2 setups
    equal 1 cleanups
    handle.Value.Dispose ()
    equal 2 cleanups
    graph.Dispose ()
    equal 2 cleanups

type private StateProps =
    {
        Shared: Prop<string>
        Created: Signal<int> -> unit
    }

let private independentState (ops: Operations<'view>) run =
    use graph = new Graph ()
    let a, b = Dom.element "section" [] [], Dom.element "section" [] []
    let shared = graph.Run (fun () -> Api.createSignal "s")
    let locals = ResizeArray<Signal<int>>()

    let props =
        {
            Shared = Prop.ofSignal shared
            Created = locals.Add
        }

    let view =
        create
            ops
            (fun _ props ->
                let local = Api.createSignal 0
                props.Created local

                ops.Fragment
                    [|
                        ops.Bind (=) (fun () -> string local.Value)
                        Prop.text ops (=) props.Shared
                    |])
            props

    equal 0 locals.Count
    let first, second = mountView graph a run view, mountView graph b run view
    equal 2 locals.Count
    equal false (obj.ReferenceEquals (locals[0], locals[1]))

    graph.Run (fun () ->
        locals[0].Value <- 1
        shared.Value <- "t")

    equal "1t" a.textContent
    equal "0t" b.textContent
    first.Dispose ()

    graph.Run (fun () ->
        locals[0].Value <- 2
        locals[1].Value <- 3
        shared.Value <- "u")

    equal "" a.textContent
    equal "3u" b.textContent
    second.Dispose ()

let private adapters (ops: Operations<'view>) run =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let mutable computes = 0

    let memo =
        graph.Run (fun () ->
            Api.createMemo (fun _ ->
                computes <- computes + 1
                source.Value + source.Value))

    let signalProp, memoProp = Prop.ofSignal source, Prop.ofMemo memo
    equal 0 computes
    let mutable reads = 0

    let reader =
        Reactive (fun () ->
            reads <- reads + 1
            source.Value)

    let host = Dom.element "section" [] []

    let view =
        create
            ops
            (fun _ props ->
                let snapshot = Prop.read props

                ops.Fragment
                    [|
                        ops.Text snapshot
                        Prop.text ops (=) props
                        Prop.text ops (=) memoProp
                        Prop.text ops (=) reader
                    |])
            signalProp

    equal 0 reads
    let mounted = mountView graph host run view
    equal "aaaaa" host.textContent
    let retained = at host 2
    graph.Run (fun () -> source.Value <- "b")
    equal "abbbb" host.textContent
    equal true (obj.ReferenceEquals (retained, at host 2))
    equal 2 reads
    equal "bb" (graph.Run (fun () -> Prop.read memoProp))
    equal "bb" (graph.Run (fun () -> Prop.read memoProp))
    equal 2 computes
    mounted.Dispose ()
    graph.Run (fun () -> source.Value <- "c")
    equal 2 reads
    equal "c" (graph.Run (fun () -> Prop.read signalProp))

type private ChildProps<'view> =
    {
        Label: string
        OnClick: unit -> unit
        Children: 'view array
    }

let private typedChildren (ops: Operations<'view>) run =
    use graph = new Graph ()
    let mutable calls, childSetups = 0, 0

    let child =
        create
            ops
            (fun _ label ->
                childSetups <- childSetups + 1
                ops.Text label)
            "child"

    let props =
        {
            Label = "go:"
            OnClick = fun () -> calls <- calls + 1
            Children = [| child |]
        }

    let view =
        create
            ops
            (fun _ props ->
                Typed.button ops [| Typed.browser (fun node -> listen node props.OnClick) |] [| ops.Text props.Label; ops.Fragment props.Children |])
            props

    let a, b = Dom.element "section" [] [], Dom.element "section" [] []
    let first, second = mountView graph a run view, mountView graph b run view
    equal 2 childSetups
    equal "go:child" a.textContent
    equal "go:child" b.textContent
    let retained = button a
    retained.click ()
    equal 1 calls
    first.Dispose ()
    retained.click ()
    equal 1 calls
    (button b).click()
    equal 2 calls
    second.Dispose ()

let private setupFailure (ops: Operations<'view>) run =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let host = Dom.element "section" [] []
    let sibling = Dom.element "aside" [] []
    host.appendChild sibling |> ignore
    let mutable childClean, parentClean, reads, calls = 0, 0, 0, 0
    let mutable retained: HTMLElement option = None
    let original = InvalidOperationException "props setup"

    let child =
        create
            ops
            (fun context _ ->
                context.OnCleanup (fun () -> childClean <- childClean + 1)

                ops.Fragment
                    [|
                        Typed.button
                            ops
                            [|
                                Typed.browser (fun node ->
                                    retained <- Some node
                                    listen node (fun () -> calls <- calls + 1))
                            |]
                            [||]
                        ops.Bind (=) (fun () ->
                            reads <- reads + 1
                            source.Value)
                    |])
            ()

    let parent =
        create
            ops
            (fun context _ ->
                context.OnCleanup (fun () -> parentClean <- parentClean + 1)
                ops.Fragment [| child; create ops (fun _ _ -> raise original) () |])
            ()

    let mutable caught = false

    try
        mountView graph host run parent |> ignore
    with error ->
        equal true (obj.ReferenceEquals (original, error))
        caught <- true

    equal true caught
    equal 1 childClean
    equal 1 parentClean
    equal 1. host.childNodes.length
    retained.Value.click ()
    equal 0 calls
    graph.Run (fun () -> source.Value <- "b")
    equal 1 reads

let private disposedCapability (ops: Operations<'view>) run =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let mutable context: Setup option = None

    let mounted =
        mountView
            graph
            host
            run
            (create
                ops
                (fun ctx _ ->
                    context <- Some ctx
                    ops.Empty)
                ())

    mounted.Dispose ()
    let mutable caught = false

    try
        context.Value.OnCleanup ignore
    with :? InvalidOperationException ->
        caught <- true

    equal true caught

let private resources (ops: Operations<'view>) run =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "live")

    for prop, effects in [ Constant "fixed", 0; Prop.ofSignal source, 1 ] do
        let host = Dom.element "section" [] []
        let counts = Browser.metrics ()
        let view = create ops (fun _ value -> Prop.text ops (=) value) prop
        let dispose = Browser.mount graph host ignore (Some counts) (run view)
        equal 2 counts.Owners
        equal 1 counts.Setups
        equal effects counts.Effects
        equal effects counts.Writes
        dispose ()

let private portable (ops: Operations<'view>) run =
    let mutable setups, cleanups, reads = 0, 0, 0

    let view =
        create
            ops
            (fun ctx props ->
                setups <- setups + 1
                ctx.OnCleanup (fun () -> cleanups <- cleanups + 1)
                ops.Element Div [||] [| Prop.text ops (=) props |])
            (Reactive (fun () ->
                reads <- reads + 1
                "<&"))

    equal 0 reads
    equal "<div>&lt;&amp;</div>" (Html.render Reject (run view))
    equal "<div>&lt;&amp;</div>" (Html.render Reject (run view))
    equal 2 setups
    equal 2 cleanups
    equal 2 reads

let private withOps name ops run =
    [|
        name
        + " components set up once and do not subscribe their calling effect",
        fun () -> onceUntracked ops run
        name
        + " component local state is independent and captured state stays shared",
        fun () -> independentState ops run
        name
        + " static snapshots and explicit signal memo reader props stay distinct",
        fun () -> adapters ops run
        name
        + " typed callbacks and reusable children have independent lifetimes",
        fun () -> typedChildren ops run
        name
        + " failed component output unwinds nested resources",
        fun () -> setupFailure ops run
        name
        + " disposed component cleanup capability rejects registration",
        fun () -> disposedCapability ops run
        name
        + " constant props add no effects and reactive props add one binding",
        fun () -> resources ops run
        name
        + " typed components remain portable and reusable",
        fun () -> portable ops run
    |]

let cases =
    Array.concat
        [|
            withOps "tree" Tree.ops Tree.run
            withOps "program" Program.ops Program.run
            ComponentPortableChecks.cases
        |]
