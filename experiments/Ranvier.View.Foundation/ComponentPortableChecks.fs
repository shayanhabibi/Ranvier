module ComponentPortableChecks

open System
open global.Ranvier
open global.Ranvier.View.Foundation.Representation
open global.Ranvier.View.Foundation.Components

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

type PanelProps<'view> =
    {
        Title: Prop<string>
        Children: 'view array
        OnSelect: int -> unit
    }

let panel (ops: Operations<'view>) (_: Setup) (props: PanelProps<'view>) =
    ops.Element Div [||] [| Prop.text ops (=) props.Title; ops.Fragment props.Children |]

let private typedExample () =
    let mutable selected = 0

    let props =
        {
            Title = Constant "Title:"
            Children = [| Tree.ops.Text "child" |]
            OnSelect = fun value -> selected <- value
        }

    let view = create Tree.ops (panel Tree.ops) props
    equal "<div>Title:child</div>" (Html.render Reject (Tree.run view))
    props.OnSelect 2
    equal 2 selected

let private uncachedReader () =
    let mutable reads = 0

    let prop =
        Reactive (fun () ->
            reads <- reads + 1
            reads)

    equal 0 reads
    equal 1 (Prop.read prop)
    equal 2 (Prop.read prop)

let private lazyAdapters () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal 1)
    let mutable computes = 0

    let memo =
        graph.Run (fun () ->
            Api.createMemo (fun _ ->
                computes <- computes + 1
                source.Value * 2))

    let before = computes
    let a, b = Prop.ofSignal source, Prop.ofMemo memo
    equal before computes

    graph.Run (fun () ->
        equal 1 (Prop.read a)
        equal 2 (Prop.read b)
        let cached = computes
        equal 2 (Prop.read b)
        equal cached computes
        source.Value <- 3
        equal 3 (Prop.read a)
        equal 6 (Prop.read b))

let private invalidAdapters () =
    let rejected action =
        let mutable caught = false

        try
            action ()
        with error ->
            equal true (error.Message.Contains "cannot be null")
            caught <- true

        equal true caught

    rejected (fun () ->
        Prop.ofSignal (Unchecked.defaultof<Signal<int>>)
        |> ignore)

    rejected (fun () ->
        Prop.ofMemo (Unchecked.defaultof<Memo<int>>)
        |> ignore)

    let prop = Reactive (Unchecked.defaultof<unit -> string>)
    rejected (fun () -> Prop.read prop |> ignore)
    rejected (fun () -> Prop.text Tree.ops (=) prop |> ignore)

type private MutableValue = { mutable Value: int }

let private capturedReference () =
    let state = { Value = 1 }
    let prop = Constant state
    (Prop.read prop).Value <- 2
    equal 2 state.Value
    equal true (obj.ReferenceEquals (state, Prop.read prop))

let private serverCapability () =
    let mutable context: Setup option = None

    let view =
        create
            Tree.ops
            (fun ctx () ->
                context <- Some ctx
                Tree.ops.Text "x")
            ()

    equal "x" (Html.render Reject (Tree.run view))
    let mutable rejected = false

    try
        context.Value.OnCleanup ignore
    with :? InvalidOperationException ->
        rejected <- true

    equal true rejected

let cases =
    [|
        "typed prop records carry explicit callbacks and reusable children", typedExample
        "reader props do not introduce caching", uncachedReader
        "signal and memo adapters defer reads and preserve explicit memo caching", lazyAdapters
        "null source and reader adapters fail before use", invalidAdapters
        "constant reference props preserve ordinary mutability", capturedReference
        "server component capability rejects cleanup after completion", serverCapability
    |]
