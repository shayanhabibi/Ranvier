namespace Ranvier.Docs.Maps

open System
open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Browser
open Browser.Types
open Partas.Solid
open Ranvier
open Ranvier.Docs.Maps

module private Anime =
    [<Import("animate", "animejs")>]
    let animate (targets: obj) (parameters: obj) : obj = jsNative

module private Dom =
    let svgNs = "http://www.w3.org/2000/svg"

    let el (tag: string) (cls: string) : HTMLElement =
        let e = document.createElement tag
        e.className <- cls
        e

    let svg (tag: string) (cls: string) : Element =
        let e = document.createElementNS (svgNs, tag)
        e.setAttribute ("class", cls)
        e

    let attrs (e: Element) (pairs: (string * string) list) =
        for name, value in pairs do
            e.setAttribute (name, value)

    let toggle (e: Element) (cls: string) (on: bool) =
        if on then e.classList.add cls else e.classList.remove cls

    let button (label: string) (cls: string) (onClick: unit -> unit) =
        let b = el "button" cls
        b.setAttribute ("type", "button")
        b.textContent <- label
        b.addEventListener ("click", fun _ -> onClick ())
        b

    /// <summary>A class held for <c>ms</c> milliseconds, restarting its animation.</summary>
    let flash (e: Element) (cls: string) (ms: int) =
        e.classList.remove cls
        e?getBBox () |> ignore
        e.classList.add cls

        window.setTimeout ((fun () -> e.classList.remove cls), ms)
        |> ignore

/// <summary>A node's drawing on the stage.</summary>
type private NodeView =
    {
        Group: Element
        Shape: Element
        Name: Element
        Value: Element
        mutable At: float * float
    }

module private Look =
    let column = 150.0
    let row = 78.0
    let margin = 52.0

    let position (layer: int, row': int) =
        margin + float layer * column, margin + float row' * row

    let kindName (kind: TraceNodeKind) =
        match kind with
        | TraceNodeKind.Signal -> "signal"
        | TraceNodeKind.Memo -> "memo"
        | TraceNodeKind.Effect -> "effect"
        | TraceNodeKind.AsyncMemo -> "async memo"
        | TraceNodeKind.AsyncSource -> "async source"
        | TraceNodeKind.Boundary -> "boundary"
        | TraceNodeKind.Projection -> "projection"
        | _ -> "node"

    let shapeOf (kind: TraceNodeKind) =
        match kind with
        | TraceNodeKind.Signal -> "signal"
        | TraceNodeKind.Effect -> "effect"
        | TraceNodeKind.AsyncMemo
        | TraceNodeKind.AsyncSource -> "async"
        | _ -> "memo"

    let statusName (status: TraceNodeStatus) =
        match status with
        | TraceNodeStatus.Fresh -> "fresh"
        | TraceNodeStatus.Running -> "running"
        | TraceNodeStatus.Disposed -> "disposed"
        | TraceNodeStatus.Ended RunStatus.Ok -> "ok"
        | TraceNodeStatus.Ended RunStatus.Pending -> "pending"
        | TraceNodeStatus.Ended RunStatus.Error -> "error"
        | TraceNodeStatus.Ended _ -> "abandoned"

    let badge (text: string) =
        if text.Length > 16 then
            text.Substring (0, 15) + "…"
        else
            text

    /// <summary>A cubic edge from the right of a source to the left of an observer.</summary>
    let edgePath (x1: float, y1: float) (x2: float, y2: float) =
        let x1 = x1 + 22.0
        let x2 = x2 - 22.0
        let mid = (x1 + x2) / 2.0
        $"M{x1} {y1} C{mid} {y1} {mid} {y2} {x2} {y2}"

[<AutoOpen>]
module SignalMapComponent =

    /// <summary>
    /// A live map of a traced graph: the stage, the example's controls, a log and, when <c>timeline</c> is true, a
    /// scrubber over every frame.
    /// </summary>
    /// <remarks>
    /// Placed in a <c>partas-solid-card</c>, it highlights the lines of the binding whose node runs. A binding is
    /// (label, first line, last line), with lines counted from 1 in the card's code block.
    /// </remarks>
    let SignalMap (source: MapSource) (bindings: (string * int * int)[]) (timeline: bool) : HtmlElement =
        let reduced: bool = window?matchMedia("(prefers-reduced-motion: reduce)")?matches

        let timeline =
            timeline
            || (match source with
                | Recorded _ -> true
                | Live _ -> false)

        let root = Dom.el "div" "rv-map"
        let stageBox = Dom.el "div" "rv-map__stage"
        let stage = Dom.svg "svg" "rv-map__svg"
        Dom.attrs stage [ "role", "img"; "aria-label", "Signal map" ]
        let edgeLayer = Dom.svg "g" "rv-map__edges"
        let dotLayer = Dom.svg "g" "rv-map__dots"
        let nodeLayer = Dom.svg "g" "rv-map__nodes"
        stage.appendChild edgeLayer |> ignore
        stage.appendChild dotLayer |> ignore
        stage.appendChild nodeLayer |> ignore
        stageBox.appendChild stage |> ignore
        let inspect = Dom.el "div" "rv-map__inspect"
        let hint = "Hover a node for its state; click it for why it last ran."
        inspect.textContent <- hint
        stageBox.appendChild inspect |> ignore
        let bar = Dom.el "div" "rv-map__bar"
        let controlRow = Dom.el "div" "rv-map__controls"
        bar.appendChild controlRow |> ignore
        let log = Dom.el "ol" "rv-map__log"
        log.setAttribute ("aria-live", "polite")
        let why = Dom.el "pre" "rv-map__why"
        let error = Dom.el "div" "rv-map__error"
        error.setAttribute ("role", "alert")

        for e in [ stageBox; bar; log; why; error ] do
            root.appendChild e |> ignore

        let nodes = Dictionary<int, NodeView>()
        let edges = Dictionary<string, Element>()
        let history = ResizeArray<Frame>()
        let lit = Dictionary<int, Element list>()
        let mutable cursor = -1
        let mutable playing = not timeline
        let mutable scheduled = false
        let mutable timer = 0.0
        let mutable layoutKey = ""
        let mutable shown = MapModel.start
        let mutable tail = MapModel.start
        let mutable read = 0
        let mutable graph: Graph option = None
        let mutable disposed = false

        let fail (text: string) =
            error.textContent <- text
            root.classList.add "rv-map--failed"
            playing <- false

        let codeLines () =
            match root.closest ".partas-solid-card" with
            | Some card -> card.querySelectorAll ".nacara-code__line"
            | None -> document.createDocumentFragment().querySelectorAll "x"

        let unlight (node: int) =
            match lit.TryGetValue node with
            | true, lines ->
                for l in lines do
                    l.classList.remove "rv-map-line--run"

                lit.Remove node |> ignore
            | _ -> ()

        let unlightAll () =
            for node in List.ofSeq lit.Keys do
                unlight node

        let light (node: int) =
            let name = MapModel.name shown.Snapshot node

            match
                bindings
                |> Array.tryFind (fun (b, _, _) -> b = name)
            with
            | Some (_, first, last) ->
                let all = codeLines ()

                let lines =
                    [
                        for i in first - 1 .. last - 1 do
                            if i >= 0 && i < all.length then
                                all.item i
                    ]

                for l in lines do
                    l.classList.add "rv-map-line--run"

                lit[node] <- lines
            | None -> ()

        let say (text: string) (cls: string) =
            why.textContent <- ""
            let item = Dom.el "li" cls
            item.textContent <- text
            log.appendChild item |> ignore

            while log.children.length > 8 do
                log.removeChild log.firstChild |> ignore

            log.scrollTop <- log.scrollHeight

        let describe (n: TraceSnapshotNode) =
            let path = TraceModel.pathOf shown.Snapshot n.Id

            let parts =
                [
                    path
                    Look.kindName n.Kind
                    Look.statusName n.Status
                    match n.Value with
                    | Some v -> "= " + v
                    | None -> ()
                    $"runs {n.Runs}"
                    match shown.Waiting.TryFind n.Id with
                    | Some s -> "waiting on " + TraceModel.pathOf shown.Snapshot s
                    | None -> ()
                    match shown.Errors.TryFind n.Id with
                    | Some e -> "failed: " + e
                    | None -> ()
                ]

            String.Join (" · ", parts)

        let explain (id: int) =
            let events =
                [|
                    for i in 0..cursor do
                        history[i].Event
                |]

            why.textContent <-
                try
                    TraceModel.renderWhy shown.Snapshot (TraceModel.why events null id 0)
                with _ ->
                    MapModel.name shown.Snapshot id + " has not run."

        let viewOf (n: TraceSnapshotNode) =
            let shape = Look.shapeOf n.Kind
            let group = Dom.svg "g" ("rv-map-node rv-map-node--" + shape)
            group.setAttribute ("tabindex", "0")
            let ring = Dom.svg "circle" "rv-map-node__flight"
            Dom.attrs ring [ "r", "25" ]

            let body =
                match shape with
                | "signal" ->
                    let c = Dom.svg "circle" "rv-map-node__shape"
                    Dom.attrs c [ "r", "16" ]
                    c
                | "async" ->
                    let c = Dom.svg "circle" "rv-map-node__shape"
                    Dom.attrs c [ "r", "17" ]
                    c
                | "effect" ->
                    let p = Dom.svg "path" "rv-map-node__shape"
                    Dom.attrs p [ "d", "M0 -19 L19 0 L0 19 L-19 0 Z" ]
                    p
                | _ ->
                    let r = Dom.svg "rect" "rv-map-node__shape"
                    Dom.attrs r [ "x", "-23"; "y", "-15"; "width", "46"; "height", "30"; "rx", "9" ]
                    r

            let name = Dom.svg "text" "rv-map-node__name"
            Dom.attrs name [ "y", "36"; "text-anchor", "middle" ]
            let value = Dom.svg "text" "rv-map-node__value"
            Dom.attrs value [ "y", "-26"; "text-anchor", "middle" ]

            for e in [ ring; body; name; value ] do
                group.appendChild e |> ignore

            let id = n.Id

            group.addEventListener (
                "mouseenter",
                fun _ ->
                    match shown.Snapshot.Nodes.TryFind id with
                    | Some n -> inspect.textContent <- describe n
                    | None -> ()
            )

            group.addEventListener ("mouseleave", fun _ -> inspect.textContent <- hint)
            group.addEventListener ("click", fun _ -> explain id)

            group.addEventListener (
                "keydown",
                fun e ->
                    if (e :?> KeyboardEvent).key = "Enter" then
                        explain id
            )

            nodeLayer.appendChild group |> ignore

            {
                Group = group
                Shape = body
                Name = name
                Value = value
                At = 0.0, 0.0
            }

        let place (view: NodeView) (x: float, y: float) (animate: bool) =
            let fromX, fromY = view.At
            view.At <- x, y

            if
                animate
                && not reduced
                && (fromX, fromY) <> (0.0, 0.0)
            then
                let at = createObj [ "x" ==> fromX; "y" ==> fromY ]

                Anime.animate
                    at
                    (createObj
                        [
                            "x" ==> x
                            "y" ==> y
                            "duration" ==> 360
                            "ease" ==> "outQuart"
                            "onUpdate"
                            ==> fun () -> view.Group.setAttribute ("transform", $"translate({at?x} {at?y})")
                        ])
                |> ignore
            else
                view.Group.setAttribute ("transform", $"translate({x} {y})")

        /// <summary>Draws <c>scene</c>: nodes, edges, positions, values and state classes.</summary>
        let sync (scene: Scene) =
            shown <- scene
            let snapshot = scene.Snapshot

            let live =
                snapshot.Nodes.Values
                |> Seq.filter (fun n ->
                    MapModel.visible n
                    && n.Status <> TraceNodeStatus.Disposed)
                |> Seq.map _.Id
                |> Set.ofSeq

            let links =
                MapModel.edges snapshot
                |> List.filter (fun (s, o) -> live.Contains s && live.Contains o)

            for id in List.ofSeq nodes.Keys do
                if not (live.Contains id) then
                    nodes[id].Group.remove()
                    nodes.Remove id |> ignore
                    unlight id

            for id in live do
                if not (nodes.ContainsKey id) then
                    nodes[id] <- viewOf snapshot.Nodes[id]

            let key =
                String.Join (";", live)
                + "|"
                + String.Join (";", links)

            if key <> layoutKey then
                layoutKey <- key

                let sources =
                    links
                    |> List.groupBy snd
                    |> List.map (fun (o, pairs) -> o, List.map fst pairs)
                    |> Map.ofList

                let placed = Layout.place (Set.toList live) sources
                let layers = placed.Values |> Seq.map fst |> Seq.fold max 0
                let rows = placed.Values |> Seq.map snd |> Seq.fold max 0
                let width = Look.margin * 2.0 + float layers * Look.column
                let height = Look.margin * 2.0 + float rows * Look.row
                stage.setAttribute ("viewBox", $"0 0 {width} {height}")

                for KeyValue (id, at) in placed do
                    place nodes[id] (Look.position at) true

                for e in edges.Values do
                    e.remove ()

                edges.Clear ()

                for s, o in links do
                    let path = Dom.svg "path" "rv-map-edge"
                    path.setAttribute ("d", Look.edgePath (Look.position placed[s]) (Look.position placed[o]))
                    edgeLayer.appendChild path |> ignore
                    edges[$"{s}>{o}"] <- path

            for KeyValue (id, view) in nodes do
                let n = snapshot.Nodes[id]
                view.Name.textContent <- MapModel.name snapshot id

                view.Value.textContent <-
                    n.Value
                    |> Option.map Look.badge
                    |> Option.defaultValue ""

                Dom.toggle view.Group "is-running" (n.Status = TraceNodeStatus.Running)
                Dom.toggle view.Group "is-pending" (n.Status = TraceNodeStatus.Ended RunStatus.Pending)
                Dom.toggle view.Group "is-fresh" (n.Status = TraceNodeStatus.Fresh)
                Dom.toggle view.Group "is-flight" (scene.Flights.ContainsKey id)
                Dom.toggle view.Group "is-waiting" (scene.Waiting.ContainsKey id)
                Dom.toggle view.Group "is-failed" (scene.Errors.ContainsKey id)
                view.Group.setAttribute ("aria-label", describe n)

        let travel (s: int) (t: int) (bright: bool) =
            match edges.TryGetValue $"{s}>{t}" with
            | true, path ->
                let length: float = path?getTotalLength ()

                let dot =
                    Dom.svg
                        "circle"
                        (if bright then
                             "rv-map-dot rv-map-dot--bright"
                         else
                             "rv-map-dot")

                dot.setAttribute ("r", (if bright then "4.5" else "3.5"))
                dotLayer.appendChild dot |> ignore
                let at = createObj [ "t" ==> 0.0 ]

                let move () =
                    let p = path?getPointAtLength (at?t * length)
                    Dom.attrs dot [ "cx", string p?x; "cy", string p?y ]

                move ()

                Anime.animate
                    at
                    (createObj
                        [
                            "t" ==> 1.0
                            "duration" ==> (if bright then 460 else 380)
                            "ease" ==> "inOutSine"
                            "onUpdate" ==> move
                            "onComplete" ==> fun () -> dot.remove ()
                        ])
                |> ignore
            | _ -> ()

        let play (cue: Cue) =
            let shape id =
                match nodes.TryGetValue id with
                | true, v -> Some v
                | _ -> None

            match cue with
            | Flash id ->
                shape id
                |> Option.iter (fun v ->
                    Anime.animate
                        v.Shape
                        (createObj
                            [
                                "scale" ==> [| box 1.0; box 1.35; box 1.0 |]
                                "duration" ==> 420
                                "ease" ==> "outQuad"
                            ])
                    |> ignore

                    Dom.flash v.Group "is-flash" 700)
            | Pulse (s, t) -> travel s t false
            | Surge (s, targets) ->
                for t in targets do
                    travel s t true

                shape s
                |> Option.iter (fun v -> Dom.flash v.Value "is-swap" 600)
            | Drop id ->
                shape id
                |> Option.iter (fun v -> Dom.flash v.Group "is-dropped" 700)
            | Settled id ->
                shape id
                |> Option.iter (fun v -> Dom.flash v.Group "is-settled" 800)
            | Failed id ->
                shape id
                |> Option.iter (fun v -> Dom.flash v.Group "is-failing" 800)
            | _ -> ()

        let ticks = Dom.el "div" "rv-map__ticks"
        let scrub = Dom.el "input" "rv-map__scrub" :?> HTMLInputElement
        let playButton = Dom.button "Play" "rv-map__button" ignore

        let refresh () =
            if timeline then
                scrub.max <- string history.Count
                scrub.value <- string (cursor + 1)
                playButton.textContent <- (if playing then "Pause" else "Play")

        let show (frame: Frame) =
            sync frame.After

            match frame.Cue with
            | Ring id -> light id
            | Rest id -> unlight id
            | _ -> ()

            if not reduced then
                play frame.Cue

            match frame.Cue with
            | Quiet -> ()
            | Failed _ -> say frame.Log "is-error"
            | _ -> say frame.Log ""

        let rec tick () =
            scheduled <- false

            try
                if playing && not disposed then
                    let mutable go = true

                    while go && cursor + 1 < history.Count do
                        cursor <- cursor + 1
                        let frame = history[cursor]
                        show frame

                        if frame.Cue <> Quiet && not reduced then
                            go <- false

                    if cursor + 1 >= history.Count && timeline then
                        playing <- false

                    refresh ()

                    if cursor + 1 < history.Count then
                        schedule ()
            with ex ->
                fail ("The map stopped: " + ex.Message)

        and schedule () =
            if not scheduled && not disposed then
                scheduled <- true
                let backlog = history.Count - cursor - 1
                let gap = if reduced then 0 else max 40 (180 - 10 * backlog)
                timer <- window.setTimeout (tick, gap)

        let redrawTicks () =
            if timeline then
                ticks.innerHTML <- ""

                for i in 0 .. history.Count - 1 do
                    if history[i].Cue <> Quiet then
                        let t = Dom.el "span" "rv-map__tick"
                        t.setAttribute ("style", $"left: {float (i + 1) / float history.Count * 100.0}%%")
                        ticks.appendChild t |> ignore

        let append (frames: Frame[]) =
            if frames.Length > 0 then
                history.AddRange frames
                tail <- (Array.last frames).After
                redrawTicks ()
                refresh ()

                if playing then
                    schedule ()

        let jump (index: int) =
            playing <- false
            unlightAll ()
            cursor <- max -1 (min index (history.Count - 1))
            sync (MapModel.stateAt MapModel.start (history.ToArray ()) cursor)
            refresh ()

        let clear () =
            window.clearTimeout timer
            scheduled <- false
            history.Clear ()
            cursor <- -1
            tail <- MapModel.start
            read <- 0
            layoutKey <- ""
            unlightAll ()

            for v in nodes.Values do
                v.Group.remove ()

            nodes.Clear ()

            for e in edges.Values do
                e.remove ()

            edges.Clear ()
            log.innerHTML <- ""
            why.textContent <- ""
            error.textContent <- ""
            root.classList.remove "rv-map--failed"
            sync MapModel.start

        let rec start () =
            clear ()

            match source with
            | Recorded events ->
                controlRow.innerHTML <- ""
                append (MapModel.frames MapModel.start events)
            | Live scenario ->
                graph
                |> Option.iter (fun g -> (g :> IDisposable).Dispose())

                let g = new Graph ()
                graph <- Some g
                playing <- true
                controlRow.innerHTML <- ""

                try
                    for control in scenario g do
                        controlRow.appendChild (
                            Dom.button control.Label "rv-map__button" (fun () ->
                                playing <- true

                                try
                                    use _ = g.Activate ()
                                    control.Run ()
                                with ex ->
                                    say $"{control.Label} threw: {ex.Message}" "is-error")
                        )
                        |> ignore

                    controlRow.appendChild (Dom.button "Reset" "rv-map__button rv-map__button--reset" start)
                    |> ignore
                with ex ->
                    fail ("The example threw: " + ex.Message)

        let rec poll () =
            if not disposed then
                try
                    match graph with
                    | Some g ->
                        let events = Trace.events g

                        if events.Length > read then
                            let fresh = events[read..]
                            read <- events.Length
                            append (MapModel.frames tail fresh)
                    | None -> ()

                    window.requestAnimationFrame (fun _ -> poll ())
                    |> ignore
                with ex ->
                    fail ("The map stopped: " + ex.Message)

        if timeline then
            let row = Dom.el "div" "rv-map__timeline"

            playButton.addEventListener (
                "click",
                fun _ ->
                    if playing then
                        playing <- false
                    else
                        if cursor + 1 >= history.Count then
                            jump -1

                        playing <- true
                        schedule ()

                    refresh ()
            )

            let step =
                Dom.button "Step" "rv-map__button" (fun () ->
                    playing <- false
                    let mutable go = true

                    while go && cursor + 1 < history.Count do
                        cursor <- cursor + 1
                        show history[cursor]

                        if history[cursor].Cue <> Quiet then
                            go <- false

                    refresh ())

            Dom.attrs scrub [ "type", "range"; "min", "0"; "step", "1"; "aria-label", "Event" ]
            scrub.addEventListener ("input", fun _ -> jump (int scrub.value - 1))
            let track = Dom.el "div" "rv-map__track"
            track.appendChild ticks |> ignore
            track.appendChild scrub |> ignore

            for e in [ playButton; step; track ] do
                row.appendChild e |> ignore

            bar.appendChild row |> ignore

            match source with
            | Recorded _ ->
                bar.appendChild (Dom.button "Reset" "rv-map__button rv-map__button--reset" (fun () -> jump -1))
                |> ignore
            | Live _ -> ()

        try
            start ()
            poll ()
        with ex ->
            fail ("The map stopped: " + ex.Message)

        Partas.Solid.Bindings.onCleanup (fun () ->
            disposed <- true
            window.clearTimeout timer

            graph
            |> Option.iter (fun g -> (g :> IDisposable).Dispose()))

        unbox<HtmlElement> root
