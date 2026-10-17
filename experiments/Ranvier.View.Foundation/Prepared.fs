/// <summary>Clones cached static skeletons and applies instance modifiers to them. Superseded by component templates in <c>Template.fs</c>; kept until its removal is decided.</summary>
module Ranvier.View.Foundation.Prepared

open System
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation.Representation

/// <summary>Supplies a cached static HTML skeleton for browser cloning.</summary>
type Template = delegate of unit -> HTMLElement

/// <summary>Interprets cloned skeletons and their instance bindings.</summary>
[<AbstractClass>]
type internal Backend() =
    inherit Representation.Backend()
    abstract Clone: HTMLElement * (HTMLElement -> unit) -> unit
    abstract Apply: HTMLElement * ModifierAction array -> unit
    abstract BindText: Node * (unit -> string) -> unit

[<Global "document">]
let private document: Document = jsNative

let private unsupported () =
    raise (InvalidOperationException "Prepared skeletons accept static elements, text and attributes")

let private build (view: Program.View) =
    let staging = document.createDocumentFragment ()
    let mutable parent: Node = staging
    let mutable target: HTMLElement = Unchecked.defaultof<_>

    let backend =
        { new Representation.Backend() with
            member _.Text value =
                parent.appendChild (document.createTextNode value)
                |> ignore

            member this.Element(tag, modifiers, children) =
                let node = document.createElement (name tag)
                parent.appendChild node |> ignore
                let previousTarget = target
                target <- node

                try
                    for apply in modifiers do
                        apply this
                finally
                    target <- previousTarget

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

            member _.Attribute(key, value) =
                validateAttribute key
                target.setAttribute (key, value)

            member _.OptionalAttribute(key, value) =
                validateAttribute key

                match value with
                | Some value -> target.setAttribute (key, value)
                | None -> target.removeAttribute key

            member _.InputValue _ =
                unsupported ()

            member _.ReactiveAttribute(_, _) =
                unsupported ()

            member _.Browser _ =
                unsupported ()

            member _.Listen(_, _) =
                unsupported ()

            member _.Property _ =
                unsupported ()

            member _.ReactiveProperty(_, _) =
                unsupported ()

            member _.Bind(_, _) =
                unsupported ()

            member _.Keyed(_, _) =
                unsupported ()

            member _.Component _ =
                unsupported ()
        }

    Program.run view backend

    if staging.childNodes.length <> 1. then
        raise (ArgumentException ("A prepared skeleton must contain exactly one HTML root element", "view"))

    match staging.firstChild with
    | Some node when node.nodeType = 1. -> node :?> HTMLElement
    | _ -> raise (ArgumentException ("A prepared skeleton must have an HTML root element", "view"))

/// <summary>Caches a static skeleton on its first browser mount.</summary>
/// <remarks>The view must produce one HTML root using static elements, text and attributes.
/// Reactive instructions, browser hooks and components are rejected before their callbacks run.</remarks>
let prepare view =
    let mutable cached: HTMLElement = Unchecked.defaultof<_>

    Template (fun () ->
        if isNull (box cached) then
            cached <- build view

        cached)

let private target (backend: Representation.Backend) =
    match backend with
    | :? Backend as prepared -> prepared
    | _ -> raise (InvalidOperationException "Prepared views require the owned browser renderer")

/// <summary>Clones a skeleton and runs setup for the new instance.</summary>
/// <remarks>Setup applies bindings to clone nodes through this module. Server serialization rejects prepared views.</remarks>
let view (template: Template) setup : Program.View =
    Program.View (fun backend ->
        let prepared = target backend
        prepared.Clone (template.Invoke (), fun root -> setup root backend))

/// <summary>Clones a skeleton inside an owned component and runs instance setup.</summary>
/// <remarks>Setup receives the component cleanup context and props; its signal reads are untracked.</remarks>
let createComponent (template: Template) setup props : Program.View =
    Program.View (fun backend ->
        let prepared = target backend
        backend.Component (fun context -> prepared.Clone (template.Invoke (), fun root -> setup context props root backend)))

/// <summary>Applies typed modifiers to an element in the current clone during setup.</summary>
let apply (node: 'native) (modifiers: Typed.Modifier<'native> array) backend =
    (target backend).Apply((node :> HTMLElement), Typed.modifierActions modifiers)

/// <summary>Binds an existing text node in the current clone to a tracked reader.</summary>
let text (node: Node) read backend =
    (target backend).BindText(node, read)
