module AuthoringChecks

open Ranvier
open Fable.Core.TS.Dom
open Fable.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Components
open Ranvier.View.Foundation.Authoring
open Ranvier.View.Foundation.Generated.Html.Authoring

module Html = Ranvier.View.Foundation.Generated.Html.Program
module Attr = Ranvier.View.Foundation.Generated.Html.Attrs
module Events = Ranvier.View.Foundation.Generated.Html.Events

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

type private PanelProps =
    {
        Label: Prop<string>
        Children: Program.View array
        Changed: int -> unit
    }

let private mountedComponents () =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "First")
    let mutable setups, cleanups, changes = 0, 0, 0

    let panel props =
        createComponent
            (fun context props ->
                setups <- setups + 1
                context.OnCleanup (fun () -> cleanups <- cleanups + 1)
                let count = Api.createSignal 0

                Html.section
                    [||]
                    [|
                        propText props.Label
                        Html.button
                            [|
                                Events.onClick (fun _ ->
                                    count.Value <- count.Value + 1
                                    props.Changed count.Value)
                            |]
                            [| reactiveText (fun () -> string count.Value) |]
                        fragment props.Children
                    |])
            props

    let view =
        panel
            {
                Label = Prop.ofSignal label
                Children = [| text " child" |]
                Changed = fun value -> changes <- value
            }

    let first, second = Dom.element "section" [] [], Dom.element "section" [] []

    let mount host =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    equal 0 setups
    let a, b = mount first, mount second
    equal 2 setups
    equal "First0 child" first.textContent

    let button =
        first.querySelector ("button")
        |> unbox<HTMLButtonElement>

    button.click ()
    equal 1 changes
    equal "First1 child" first.textContent
    equal "First0 child" second.textContent
    graph.Run (fun () -> label.Value <- "Second")
    equal 2 setups
    equal "Second1 child" first.textContent
    equal "Second0 child" second.textContent
    a.Dispose ()
    button.click ()
    equal 1 changes
    equal 1 cleanups
    graph.Run (fun () -> label.Value <- "Third")
    equal "" first.textContent
    equal "Third0 child" second.textContent
    b.Dispose ()
    equal 2 cleanups

let private portableAuthoring () =
    let view =
        Html.article
            [| Attr.classStatic "panel" |]
            [|
                propText (Constant "<&")
                Html.br [||]
                keyedArray fst (fun () -> [| 1, "one"; 2, "two" |]) (fun current -> Html.p [||] [| reactiveText (fun () -> snd (current ())) |])
                branch id (fun () -> "selected") (fun current -> text (current ()))
                empty
            |]

    equal "<article class=\"panel\">&lt;&amp;<br><p>one</p><p>two</p>selected</article>" (Representation.Html.render Reject (Program.run view))

let private overloadedModifiers () =
    use graph = new Graph ()

    let source, optional, busy =
        graph.Run (fun () -> Api.createSignal "a", Api.createSignal (Some "default"), Api.createSignal (Some false))

    let memo = graph.Run (fun () -> Api.createMemo (fun _ -> source.Value + "!"))
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "b"))
    let mutable reads = 0

    let read () =
        reads <- reads + 1
        source.Value

    let values =
        [|
            Props.Input.value "fixed"
            Props.Input.value read
            Props.Input.value source
            Props.Input.value memo
        |]

    let titles: Typed.Modifier<HTMLInputElement> array =
        [|
            Attrs.title "fixed"
            Attrs.title read
            Attrs.title source
            Attrs.title memo
        |]

    let view =
        fragment
            [|
                yield! Array.map2 (fun value title -> Html.input [| value; title |]) values titles
                Html.input [| Props.Input.value source; Attrs.Input.value optional; Attrs.ariaBusy busy |]
                Html.button [| Props.Button.disabled disabled |] [||]
            |]

    equal 0 reads
    let host = Dom.element "section" [] []

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    let inputs =
        [
            for i in 0..4 ->
                host.querySelectorAll("input")[i]
                |> unbox<HTMLInputElement>
        ]

    equal [ "fixed"; "a"; "a"; "a!"; "a" ] (inputs |> List.map _.value)

    equal
        [ Some "fixed"; Some "a"; Some "a"; Some "a!" ]
        (inputs
         |> List.take 4
         |> List.map (fun input -> input.getAttribute "title"))

    equal (Some "default") (inputs[4].getAttribute "value")
    equal (Some "false") (inputs[4].getAttribute "aria-busy")
    equal 2 reads

    graph.Run (fun () ->
        source.Value <- "b"
        optional.Value <- None
        busy.Value <- None)

    equal [ "fixed"; "b"; "b"; "b!"; "b" ] (inputs |> List.map _.value)

    equal
        [ Some "fixed"; Some "b"; Some "b"; Some "b!" ]
        (inputs
         |> List.take 4
         |> List.map (fun input -> input.getAttribute "title"))

    equal None (inputs[4].getAttribute "value")
    equal None (inputs[4].getAttribute "aria-busy")

    equal
        true
        ((host.querySelector ("button")
          |> unbox<HTMLButtonElement>)
            .disabled)

    equal 4 reads
    mounted.Dispose ()
    graph.Run (fun () -> source.Value <- "c")
    equal 4 reads
    equal "b" inputs[2].value

let private portableOverloads () =
    let mutable current = Some "<&"

    let view =
        Html.button
            [|
                Attrs.title (fun () -> current)
                Attrs.Button.disabled false
                Attrs.ariaBusy false
            |]
            [| text "safe" |]

    equal "<button title=\"&lt;&amp;\" aria-busy=\"false\">safe</button>" (Representation.Html.render Reject (Program.run view))
    current <- None
    equal "<button aria-busy=\"false\">safe</button>" (Representation.Html.render Reject (Program.run view))

    for make in
        [
            (fun () -> Attrs.title (Unchecked.defaultof<Signal<string>>))
            (fun () -> Attrs.title (Unchecked.defaultof<Memo<string>>))
        ] do
        let mutable rejected = false

        try
            make () |> ignore
        with :? System.ArgumentException ->
            rejected <- true

        equal true rejected

let cases =
    [|
        "authoring components retain independent state, reactive props and cleanup", mountedComponents
        "authoring elements, arrays and branches share the portable Program representation", portableAuthoring
        "overloaded authoring preserves static values, tracked sources, removal and lifetime", overloadedModifiers
        "overloaded attributes preserve escaped snapshots, boolean semantics and null-source rejection", portableOverloads
    |]
