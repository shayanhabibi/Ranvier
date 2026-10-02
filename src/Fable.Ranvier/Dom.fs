namespace Fable.Ranvier

open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier

module Dom =
    type Modifier = HTMLElement -> unit

    [<Global "window">]
    let window: Window = jsNative

    [<Global "document">]
    let document: Document = jsNative

    let createElement (name: string) : HTMLElement = document.createElement name

    let text (value: string) : Node = document.createTextNode value

    let reactiveText (read: unit -> string) : Node =
        let node = document.createTextNode ""
        DomContext.bind read (fun value -> node.data <- value)
        node

    let attribute (name: string) (value: string) : Modifier =
        fun element -> element.setAttribute(name, value)

    let bindAttribute (name: string) (read: unit -> string option) : Modifier =
        fun element ->
            DomContext.bind read (function
                | Some value -> element.setAttribute(name, value)
                | None -> element.removeAttribute name)

    let property (write: HTMLElement -> 'T -> unit) (value: 'T) : Modifier =
        fun element -> write element value

    let bindProperty (read: unit -> 'T) (write: HTMLElement -> 'T -> unit) : Modifier =
        fun element -> DomContext.bind read (write element)

    let on (name: string) (handler: Event -> unit) : Modifier =
        fun element ->
            let graph = Graph.Current
            let owner = getOwner ()
            let context = DomContext.current
            let target = element :> EventTarget
            let listener: EventListener =
                fun event ->
                    graph.Run(fun () ->
                        runWithOwner owner (fun () ->
                            DomContext.run context (fun () ->
                                let invoke () = untrack (fun () -> handler event)
                                match context with
                                | Some current when current.Options.BatchEvents -> batch invoke
                                | _ -> invoke ())))
            let callback: EventListenerOrEventListenerObject = U2.Case1 listener
            target.addEventListener(name, callback)
            onCleanup (fun () -> target.removeEventListener(name, callback))

    let element (name: string) (modifiers: Modifier list) (children: Node list) : HTMLElement =
        let node = createElement name
        for modifier in modifiers do modifier node
        for child in children do node.appendChild child |> ignore
        node
