module Ranvier.View.Foundation.Browser

open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

type Metrics =
    {
        mutable Owners: int
        mutable Effects: int
        mutable Setups: int
        mutable Writes: int
    }

let metrics () =
    {
        Owners = 0
        Effects = 0
        Setups = 0
        Writes = 0
    }

[<Global>]
let globalThis: TS.Es.GlobalThis = jsNative

let private element (tag: string) : HTMLElement =
    globalThis.document.createElement tag

let private text (value: string) : Node =
    globalThis.document.createTextNode value

let private remove (node: #ChildNode) : unit =
    node.remove ()

let private inputValue (node: HTMLInputElement) (value: string) : unit =
    node.value <- value

let mount (graph: Graph) (host: HTMLElement) (report: exn list -> unit) (counts: Metrics option) (build: Backend -> unit) : unit -> unit =
    let roots = ResizeArray<Node>()

    let release () =
        for node in roots do
            remove (node :?> ChildNode)

        roots.Clear ()

    let mutable parent: Node = host
    let mutable root: MountLifetime = null
    let mutable scope: MountLifetime = null
    let mutable modifierNode: HTMLElement = Unchecked.defaultof<_>
    let mutable modifierTag = Div

    let currentElement () =
        if isNull (box modifierNode) then
            invalidOp "Element modifiers require an active element"

        modifierNode

    let applyModifiers (backend: Backend) tag node modifiers =
        let previousNode = modifierNode
        let previousTag = modifierTag
        modifierNode <- node
        modifierTag <- tag

        try
            for apply in modifiers do
                apply backend
        finally
            modifierNode <- previousNode
            modifierTag <- previousTag

    let count action =
        counts |> Option.iter action

    let append node =
        if obj.ReferenceEquals (parent, host) then
            roots.Add node

        parent.appendChild node |> ignore

    let backend =
        { new Backend() with
            member _.Text value =
                append (text value)

            member _.Attribute(key, value) =
                validateAttribute key
                (currentElement ()).setAttribute(key, value)

            member _.OptionalAttribute(key, value) =
                validateAttribute key
                let node = currentElement ()

                match value with
                | Some value -> node.setAttribute (key, value)
                | None -> node.removeAttribute key

            member _.ReactiveAttribute(_, _) =
                invalidOp "Reactive attributes require the owned-range backend"

            member _.InputValue value =
                let node = currentElement ()

                if modifierTag <> Input then
                    invalidArg "modifiers" "Input value requires input"

                inputValue (node :?> HTMLInputElement) value

            member _.Browser hook =
                scope.OnCleanup (hook (box (currentElement ())))

            member _.Listen(eventName, handler) =
                scope.OnCleanup (Typed.listenDirect (box (currentElement ())) eventName (Typed.ownedHandler handler))

            member _.Property write =
                write (box (currentElement ()))

            member _.ReactiveProperty(_, _) =
                invalidOp "Reactive properties require the owned-range backend"

            member this.Element(tag, modifiers, children) =
                let node = element (name tag)
                append node

                applyModifiers this tag node modifiers

                if not (isVoid tag) then
                    let previous = parent

                    parent <-
                        if name tag = "template" then
                            (node :?> HTMLTemplateElement).content :> Node
                        else
                            node

                    try
                        children ()
                    finally
                        parent <- previous

            member _.Bind(equals, read) =
                let node = text ""
                append node
                let mutable committed: string option = None
                count (fun c -> c.Effects <- c.Effects + 1)

                Api.createEffect (fun () ->
                    let value = read ()

                    let changed =
                        match committed with
                        | None -> true
                        | Some old -> not (equals old value)

                    if changed then
                        Api.untrack (fun () -> node.textContent <- Some value)
                        committed <- Some value
                        count (fun c -> c.Writes <- c.Writes + 1))

            member _.Keyed(_, _) =
                invalidOp "The baseline browser interpreter does not support keyed dynamic lists"

            member _.Component setup =
                let previous = scope

                try
                    scope.Child (
                        ignore,
                        fun child ->
                            scope <- child

                            count (fun c ->
                                c.Owners <- c.Owners + 1
                                c.Setups <- c.Setups + 1)

                            Api.untrack (fun () -> setup { OnCleanup = child.OnCleanup })
                    )
                    |> ignore
                finally
                    scope <- previous
        }

    try
        graph.Run (fun () ->
            Api.createRoot (fun owner ->
                root <- MountLifetime.Create (graph, owner, report, release, null)
                scope <- root
                count (fun c -> c.Owners <- c.Owners + 1)
                Api.untrack (fun () -> build backend)))
    with _ ->
        if not (isNull root) then
            root.Abandon ()

        reraise ()

    fun () -> root.Dispose ()
