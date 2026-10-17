module TemplateCatalogueChecks

open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

module G = Ranvier.View.Foundation.Generated.Html

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

[<Emit("$0[$1]")>]
let private read (node: obj) (name: string) : 'value = jsNative

[<Emit("(() => { const event = new ($0.ownerDocument.defaultView.Event)($1, { bubbles: true }); $0.dispatchEvent(event); return event; })()")>]
let private fire (target: HTMLElement) (name: string) : obj = jsNative

let private host () =
    globalThis.document.createElement "main"

let private mount graph (host: HTMLElement) view =
    Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

type Card =
    {
        Index: int
        Name: string
        Busy: Signal<bool>
        Value: Signal<string>
    }

/// <summary>The most recent record with an <c>Index</c> label in scope for the <c>card</c> template.</summary>
type Decoy = { Index: string }

let private card =
    Component.define (fun _ (props: Card) -> props) (fun t ->
        t.div
            [| t.Attrs.className "card"; t.Attrs.id (fun r -> "card-" + string r.Index) |]
            [|
                t.input [| t.Props.Input.value (t.from (fun r -> r.Value)) |]
                t.button
                    [|
                        t.Props.Button.disabled (fun r -> r.Busy.Value)
                        t.Events.onClick (fun r _ -> r.Busy.Value <- true)
                    |]
                    [| t.span [||] [| t.text (fun r -> r.Name) |] |]
            |])

let private labelInference () =
    use graph = new Graph ()
    let host = host ()
    let busy = graph.Run (fun () -> Api.createSignal false)
    let value = graph.Run (fun () -> Api.createSignal "v")

    let mounted =
        mount
            graph
            host
            (Component.render
                card
                {
                    Index = 7
                    Name = "Ada"
                    Busy = busy
                    Value = value
                })

    let div = host.querySelector "div" |> unbox<HTMLElement>

    let button =
        host.querySelector "button"
        |> unbox<HTMLButtonElement>

    equal (Some "card") (div.getAttribute "class")
    equal (Some "card-7") (div.getAttribute "id")

    equal
        "v"
        (host.querySelector "input"
         |> unbox<HTMLInputElement>)
            .value

    equal false button.disabled
    button.click ()
    equal true button.disabled

    mounted.Dispose ()

type Flags =
    {
        Name: string
        Nickname: string option
        Busy: bool
        Expanded: bool option
        State: string
    }

let private flagsTemplate =
    Component.define (fun _ (props: Flags) -> props) (fun t ->
        t.button
            [|
                t.Attrs.title (fun r -> r.Name)
                t.Attrs.id (fun r -> r.Nickname)
                t.Attrs.Button.disabled (fun r -> r.Busy)
                t.Attrs.ariaExpanded (fun r -> r.Expanded)
                t.Attrs.ariaBusy (fun r -> r.Busy)
                t.Attrs.data ("state", fun r -> Some r.State)
            |]
            [| t.text "go" |])

let private flagsProgram (flags: Flags) =
    G.Program.button
        [|
            G.Authoring.Attrs.title (fun () -> flags.Name)
            G.Authoring.Attrs.id (fun () -> flags.Nickname)
            G.Attrs.Button.disabledReactive (fun () -> flags.Busy)
            G.Attrs.ariaExpandedReactive (fun () -> flags.Expanded)
            G.Attrs.ariaBusyReactive (fun () -> Some flags.Busy)
            G.Attrs.dataReactive "state" (fun () -> Some flags.State)
        |]
        [| Program.text "go" |]

let private markup view =
    Html.render Omit (Program.run view)

let private flagCases =
    [
        {
            Name = "a"
            Nickname = None
            Busy = false
            Expanded = None
            State = "x"
        }
        {
            Name = "b"
            Nickname = Some "n"
            Busy = true
            Expanded = Some false
            State = "y"
        }
        {
            Name = "c"
            Nickname = Some "m"
            Busy = false
            Expanded = Some true
            State = "z"
        }
        {
            Name = null
            Nickname = None
            Busy = false
            Expanded = None
            State = "n"
        }
    ]

let private flagMarkup =
    [
        "<button title=\"a\" aria-busy=\"false\" data-state=\"x\">go</button>"
        "<button title=\"b\" id=\"n\" disabled=\"\" aria-expanded=\"false\" aria-busy=\"true\" data-state=\"y\">go</button>"
        "<button title=\"c\" id=\"m\" aria-expanded=\"true\" aria-busy=\"false\" data-state=\"z\">go</button>"
        "<button aria-busy=\"false\" data-state=\"n\">go</button>"
    ]

let private readerParity () =
    for flags in flagCases do
        equal (markup (flagsProgram flags)) (markup (Component.render flagsTemplate flags))

    equal flagMarkup [ for flags in flagCases -> markup (Component.render flagsTemplate flags) ]

let private staticTemplate =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.button
                    [|
                        t.Attrs.Button.disabled false
                        t.Attrs.ariaExpanded false
                        t.Attrs.id (None: string option)
                        t.Attrs.title "t"
                    |]
                    [||]
                t.button
                    [|
                        t.Attrs.Button.disabled true
                        t.Attrs.ariaExpanded true
                        t.Attrs.id (Some "i")
                        t.Attrs.data ("k", "v")
                    |]
                    [||]
            |])

let private staticProgram =
    G.Program.div
        [||]
        [|
            G.Program.button
                [|
                    G.Attrs.Button.disabledStatic false
                    G.Attrs.ariaExpandedStatic false
                    G.Authoring.Attrs.id (None: string option)
                    G.Authoring.Attrs.title "t"
                |]
                [||]
            G.Program.button
                [|
                    G.Attrs.Button.disabledStatic true
                    G.Attrs.ariaExpandedStatic true
                    G.Authoring.Attrs.id (Some "i")
                    G.Attrs.dataStatic "k" "v"
                |]
                [||]
        |]

/// <summary>The server markup of <c>staticTemplate</c>, shared with RepresentationChecks.fsx.</summary>
let private staticMarkup =
    "<div><button aria-expanded=\"false\" title=\"t\"></button><button disabled=\"\" aria-expanded=\"true\" id=\"i\" data-k=\"v\"></button></div>"

let private staticParity () =
    equal (markup staticProgram) (markup (Component.render staticTemplate ()))
    equal staticMarkup (markup (Component.render staticTemplate ()))

let private optionalReader =
    Component.define (fun _ (props: Flags) -> props) (fun t -> t.div [| t.Attrs.title (fun r -> r.Nickname) |] [||])

let private optionReaderRemoval () =
    equal "<div></div>" (markup (Component.render optionalReader (List.item 0 flagCases)))
    equal "<div title=\"n\"></div>" (markup (Component.render optionalReader (List.item 1 flagCases)))

let private ariaTemplate =
    Component.define (fun _ (props: Flags) -> props) (fun t ->
        t.div
            [||]
            [|
                t.div
                    [|
                        t.Attrs.aria ("label", "x")
                        t.Attrs.aria ("note", (fun r -> r.Nickname))
                        t.Attrs.ariaExpanded (Some false)
                    |]
                    [||]
                t.div [| t.Attrs.ariaExpanded (None: bool option) |] [||]
            |])

let private ariaProgram (flags: Flags) =
    G.Program.div
        [||]
        [|
            G.Program.div
                [|
                    G.Attrs.ariaStatic "label" "x"
                    G.Attrs.ariaExpandedReactive (fun () -> Some false)
                    G.Attrs.ariaReactive "note" (fun () -> flags.Nickname)
                |]
                [||]
            G.Program.div [| G.Attrs.ariaExpandedReactive (fun () -> None) |] [||]
        |]

let private ariaParity () =
    for flags in flagCases do
        equal (markup (ariaProgram flags)) (markup (Component.render ariaTemplate flags))

type Sourced =
    {
        Text: Signal<string>
        Optional: Signal<string option>
        On: Signal<bool>
        Aria: Signal<bool>
        AriaOption: Signal<bool option>
        Label: Signal<string option>
        Data: Signal<string option>
    }

let private sourced =
    Component.define (fun _ (props: Sourced) -> props) (fun t ->
        t.button
            [|
                t.Attrs.title (t.from (fun r -> r.Text))
                t.Attrs.id (t.from (fun r -> r.Optional))
                t.Attrs.Button.disabled (t.from (fun r -> r.On))
                t.Attrs.ariaDisabled (t.from (fun r -> r.Aria))
                t.Attrs.ariaExpanded (t.from (fun r -> r.AriaOption))
                t.Attrs.aria ("label", t.from (fun r -> r.Label))
                t.Attrs.data ("k", t.from (fun r -> r.Data))
            |]
            [||])

let private sourceAttributes () =
    use graph = new Graph ()
    let host = host ()
    let text = graph.Run (fun () -> Api.createSignal "a")
    let optional = graph.Run (fun () -> Api.createSignal (Some "o"))
    let on = graph.Run (fun () -> Api.createSignal true)
    let aria = graph.Run (fun () -> Api.createSignal true)
    let ariaOption = graph.Run (fun () -> Api.createSignal (Some true))
    let label = graph.Run (fun () -> Api.createSignal (Some "l"))
    let data = graph.Run (fun () -> Api.createSignal (Some "d"))

    let mounted =
        mount
            graph
            host
            (Component.render
                sourced
                {
                    Text = text
                    Optional = optional
                    On = on
                    Aria = aria
                    AriaOption = ariaOption
                    Label = label
                    Data = data
                })

    let button = host.querySelector "button" |> unbox<HTMLElement>

    let snapshot () =
        [
            for name in
                [
                    "title"
                    "id"
                    "disabled"
                    "aria-disabled"
                    "aria-expanded"
                    "aria-label"
                    "data-k"
                ] -> button.getAttribute name
        ]

    equal [ Some "a"; Some "o"; Some ""; Some "true"; Some "true"; Some "l"; Some "d" ] (snapshot ())

    graph.Run (fun () ->
        text.Value <- "b"
        optional.Value <- None
        on.Value <- false
        aria.Value <- false
        ariaOption.Value <- Some false
        label.Value <- None
        data.Value <- None)

    equal [ Some "b"; None; None; Some "false"; Some "false"; None; None ] (snapshot ())

    graph.Run (fun () ->
        optional.Value <- Some "p"
        on.Value <- true
        ariaOption.Value <- None
        label.Value <- Some "m"
        data.Value <- Some "e")

    equal [ Some "b"; Some "p"; Some ""; Some "false"; None; Some "m"; Some "e" ] (snapshot ())
    graph.Run (fun () -> text.Value <- null)
    equal [ None; Some "p"; Some ""; Some "false"; None; Some "m"; Some "e" ] (snapshot ())
    mounted.Dispose ()

let private rawTextElement () =
    use graph = new Graph ()
    let host = host ()

    let framed =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.iframe [| t.Attrs.title "map" |] [||] |])

    let mounted = mount graph host (Component.render framed ())
    let frames = host.querySelectorAll "iframe"
    equal 1. frames.length
    equal (Some "map") ((frames.[0] |> unbox<HTMLElement>).getAttribute "title")
    mounted.Dispose ()

let private rawTextChildrenRejected () =
    let filled =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.iframe [||] [| t.text "x" |] |])

    let empty =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.iframe [||] [||] |])

    let raises action =
        try
            action ()
            false
        with :? System.InvalidOperationException ->
            true

    equal true (raises (fun () -> TemplateDom.compile filled |> ignore))
    equal true (raises (fun () -> markup (Component.render empty ()) |> ignore))

type Live =
    {
        Text: Signal<string>
        On: Signal<bool>
        Level: Signal<float>
        Popover: Signal<string option>
    }

let private live =
    Component.define (fun _ (props: Live) -> props) (fun t ->
        t.div
            [||]
            [|
                t.input [| t.Props.Input.value "static"; t.Props.Input.``checked`` true |]
                t.input
                    [|
                        t.Props.Input.value (fun r -> r.Text.Value)
                        t.Props.Input.``checked`` (fun r -> r.On.Value)
                    |]
                t.input
                    [|
                        t.Props.Input.value (t.from (fun r -> r.Text))
                        t.Props.Input.``checked`` (t.from (fun r -> r.On))
                    |]
                t.meter [| t.Props.Meter.high 0.25 |] [||]
                t.meter [| t.Props.Meter.high (fun r -> r.Level.Value) |] [||]
                t.meter [| t.Props.Meter.high (t.from (fun r -> r.Level)) |] [||]
                t.span [| t.Props.popover (Some "manual") |] [||]
                t.span [| t.Props.popover (fun r -> r.Popover.Value) |] [||]
                t.span [| t.Props.popover (t.from (fun r -> r.Popover)) |] [||]
            |])

let private propertyTypes () =
    use graph = new Graph ()
    let host = host ()
    let text = graph.Run (fun () -> Api.createSignal "a")
    let on = graph.Run (fun () -> Api.createSignal false)
    let level = graph.Run (fun () -> Api.createSignal 0.5)
    let popover = graph.Run (fun () -> Api.createSignal (Some "auto"))

    let mounted =
        mount
            graph
            host
            (Component.render
                live
                {
                    Text = text
                    On = on
                    Level = level
                    Popover = popover
                })

    let inputs =
        [|
            for i in 0..2 ->
                host.querySelectorAll("input").[i]
                |> unbox<HTMLInputElement>
        |]

    let meters = [| for i in 0..2 -> host.querySelectorAll("meter").[i] :> obj |]

    let spans = [| for i in 0..2 -> host.querySelectorAll("span").[i] :> obj |]

    let snapshot () =
        [| for i in inputs -> i.value |],
        [| for i in inputs -> i.``checked`` |],
        [| for m in meters -> (read m "high": float) |],
        [| for s in spans -> (read s "popover": string option) |]

    equal ([| "static"; "a"; "a" |], [| true; false; false |], [| 0.25; 0.5; 0.5 |], [| Some "manual"; Some "auto"; Some "auto" |]) (snapshot ())

    graph.Run (fun () ->
        text.Value <- "b"
        on.Value <- true
        level.Value <- 0.75
        popover.Value <- None)

    equal ([| "static"; "b"; "b" |], [| true; true; true |], [| 0.25; 0.75; 0.75 |], [| Some "manual"; None; None |]) (snapshot ())

    mounted.Dispose ()

type Hits =
    {
        Label: string
        Log: ResizeArray<string * obj>
    }

let private eventTemplate =
    Component.define (fun _ (props: Hits) -> props) (fun t ->
        t.div
            [||]
            [|
                t.input [| t.Events.onInput (fun r e -> r.Log.Add ("input " + r.Label, box e)) |]
                t.video
                    [|
                        t.Events.Video.onWaitingforkey (fun r e -> r.Log.Add ("video " + r.Label, box e))
                    |]
                    [||]
            |])

let private events () =
    use graph = new Graph ()
    let host = host ()
    let log = ResizeArray ()

    let mounted =
        mount graph host (Component.render eventTemplate { Label = "L"; Log = log })

    let inputEvent = fire (host.querySelector "input" |> unbox<HTMLElement>) "input"

    let videoEvent =
        fire (host.querySelector "video" |> unbox<HTMLElement>) "waitingforkey"

    fire (host.querySelector "video" |> unbox<HTMLElement>) "input"
    |> ignore

    equal [ "input L"; "video L" ] [ for name, _ in log -> name ]

    equal
        [ true; true ]
        [
            for (_, received), sent in Seq.zip log [ inputEvent; videoEvent ] -> obj.ReferenceEquals (received, sent)
        ]

    mounted.Dispose ()

type Counts =
    {
        First: Signal<string>
        Second: Signal<string>
        Reads: int ref
    }

let private isolation () =
    use graph = new Graph ()
    let first = graph.Run (fun () -> Api.createSignal "a")
    let second = graph.Run (fun () -> Api.createSignal "x")
    let reads = ref 0

    let counted =
        Component.define (fun _ (props: Counts) -> props) (fun t ->
            t.div
                [|
                    t.Attrs.id (fun c -> c.First.Value)
                    t.isolated (
                        t.Attrs.title (fun c ->
                            c.Reads.Value <- c.Reads.Value + 1
                            c.Second.Value)
                    )
                |]
                [||])

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

    let div = host.querySelector "div" |> unbox<HTMLElement>
    graph.Run (fun () -> first.Value <- "b")
    equal (1, Some "b", Some "x") (reads.Value, div.getAttribute "id", div.getAttribute "title")
    graph.Run (fun () -> second.Value <- "y")
    equal (2, Some "y") (reads.Value, div.getAttribute "title")
    mounted.Dispose ()

let private staticIsolationRejected () =
    let mutable rejected = false

    try
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.isolated (t.Attrs.title "static") |] [||])
        |> ignore
    with error when error.Message.Contains "Only dynamic parts can be isolated" ->
        rejected <- true

    equal true rejected

let private invalidAttributeRejectedAtDefinition () =
    let mutable rejected = false

    try
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.attribute ("bad name", "x") |] [||])
        |> ignore
    with :? System.ArgumentException ->
        rejected <- true

    equal true rejected

let cases =
    [|
        "a template lambda types against the instance when another record shares the field name", labelInference
        "reader attributes render the same markup as Html.Program, including null and absent values", readerParity
        "static attributes render the same markup as Html.Program", staticParity
        "an option reader on a string attribute removes the attribute on None", optionReaderRemoval
        "static bool option ARIA and aria static and reader forms render the same markup as Html.Attrs", ariaParity
        "string, bool, ARIA and data source attributes write and remove through signals, null removing a string attribute", sourceAttributes
        "a raw-text element without children mounts with its attributes", rawTextElement
        "a raw-text element with children fails to compile, and server markup rejects every raw-text element", rawTextChildrenRejected
        "string, bool, float and option properties write in static, reader and source forms", propertyTypes
        "flat and grouped events receive the instance and fire only for their event name", events
        "an isolated generated attribute re-runs only on its own source", isolation
        "isolating a static generated attribute fails at definition", staticIsolationRejected
        "an invalid attribute name raises ArgumentException when the template is defined", invalidAttributeRejectedAtDefinition
    |]
