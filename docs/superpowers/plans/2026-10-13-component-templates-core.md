# Component templates (core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a component's template be built once per component type and materialised per instance as one clone, one state array, one render effect and one property write per event node.

**Architecture:** A portable `Template.fs` defines a backend-neutral template tree, a typed builder, `Component.define`/`render`/`renderWith`, an expansion into existing `Operations`, and one markup writer used for both the DOM skeleton and server HTML. A browser-only `TemplateDom.fs` compiles a definition once into a parsed skeleton, a walk program and slot tables, and materialises instances. `Backend.Template` has a default that expands; the mount backend overrides it with the compiled path and extends the delegated dispatcher to find template handlers.

**Tech Stack:** F# compiled by Fable 5 (browser) and .NET (portable checks), jsdom and Playwright Chromium tests, the framework benchmark harness against Solid 2.0.0-rc.14.

**Spec:** `docs/superpowers/specs/2026-10-13-component-templates-design.md`

This is plan 2a. Plan 2b adds dynamic structure inside templates (inlined child templates, view holes, `matchWith`, `branch`, `each`). Plan 2c generates the full builder catalogue. Plan 1 (`2026-10-13-fixed-dependency-binding.md`) supplies the core binding that `from` slots will use once it lands.

## Spec clarifications in this plan

- **Builder parameter.** Templates are written against a builder: `Component.define setup (fun t -> t.div [| t.Attrs.title (fun r -> r.Source.Value) |] [| ... |])`. The spec's static `T.*` sketch cannot type its lambdas: F# resolves overloaded static members before it knows the instance type. The builder's type parameter fixes it. Overloads, static/dynamic choice by argument, and the guarantees are as specified.
- **Static properties** are written once per instance during materialisation, not baked into the skeleton: cloning copies attributes, not property values, and server HTML omits properties as it does today.
- **`from` slots** evaluate inside the render effect until plan 1 lands.
- **Queued commit mode** renders templates through the default expansion; the compiled path serves synchronous mounts, which the benchmark uses.
- **Component-ownership check.** The spec's ladder re-run is replaced by the allocation sample of the template adapter, because the ladder measures authored components and has no template rung.

## Global Constraints

- Work on `poc/fable-ranvier-dom` in `C:\Users\shaya\RiderProjects\Ranvier-fable-ranvier-dom`.
- Every commit message is one conventional-commit summary line, with no body and no AI co-author trailer.
- Prefix shell commands with `rtk`.
- Comments follow `.claude/rules/comments.md`; `///` docs follow the `fsharp-xml-docs` skill. Run `dotnet fantomas` on changed F# files before committing.
- Solid comparisons use Solid 2 (`.claude/rules/framework-comparisons.md`).
- `Template.fs` compiles and runs on .NET: no `Emit` or `jsNative` code is reachable from it except behind `#if FABLE_COMPILER`.
- Verify emitted JavaScript for new per-instance closures after Task 3: Fable inserts adapters where curried functions cross boxing or curried parameters.
- Playground commands run from `examples/Fable.Ranvier.Playground`: `npm run test:dom` (Node, jsdom), `npm run test:browser` (Chromium). Portable checks: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release` from the repository root.

## Review Focus

- **One definition mounted in two hosts at once.** Expect independent instances: updating one leaves the other unchanged, and the shared skeleton is never modified. Pinned in Task 3 ("a definition mounted twice keeps instances independent").
- **A template instance inside a keyed row.** Expect its render effect to stop when the row is removed. Pinned in Task 3 ("a template inside a removed keyed row stops updating").
- **A text reader returning null.** Expect empty text, not "null". Pinned in Task 3 ("a null text value renders empty").
- **A template with no dynamic parts.** Expect it to mount and repeat without creating an effect. Pinned in Task 3 ("a static template mounts without effects").
- **A non-bubbling event beside delegated ones on the same node.** Expect the direct listener to run and to be removed on disposal, and delegated events on the node to still dispatch. Pinned in Task 5 ("focus uses a direct listener beside a delegated click").

---

### Task 1: Portable template model, expansion and server HTML

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Representation.fs` (the `Backend` type, the `Html` module)
- Create: `experiments/Ranvier.View.Foundation/Template.fs`
- Modify: `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj`, `experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`, `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (compile `Template.fs` after `Typed.fs`)
- Test: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`

**Interfaces:**
- Produces in `Representation`: `Backend.Template: TemplateDefinition * obj -> unit` (default `definition.Expand (this, props)`); abstract class `TemplateDefinition` with `abstract Expand: Backend * obj -> unit`, `abstract RenderHtml: Setup * obj * bool -> string`, `member Compiled: obj with get, set`; public `Html.escape: bool -> string -> string`.
- Produces in `Template`: `SlotTarget` (`AttributeTarget of string | PropertyTarget of Func<obj, obj, unit> | TextTarget`), `SlotMode` (`RenderMode | IsolatedMode | DirectMode`), `Slot` record (`Target`, `Mode`, `Read: Func<obj, obj>`, `Same: Func<obj, obj, bool>`), `TemplateNode` (`ElementNode of Tag * (string * string) array * (Func<obj, obj, unit> * obj) array * Slot array * (string * Func<obj, obj, unit>) array * TemplateNode array | TextNode of string | TextSlotNode of Slot | FragmentNode of TemplateNode array`), `Part<'native, 'inst>`, `TemplateView<'inst>`, `Source<'inst, 'value>`, `TemplateBuilder<'inst>` with `Attrs`, `Props`, `Events`, `element`, `div`, `section`, `span`, `button`, `input`, `fragment`, `text` (3 overloads), `from` (Signal and Memo overloads), `isolated` (2 overloads), `TemplateComponent` (abstract, `Setup: Func<Setup, obj, obj>`, `Root: TemplateNode`), `Component<'props>`, `Component.define`, `Component.render`, `Component.renderWith`, `primitiveSame: Func<obj, obj, bool>`, `skeletonMarkup: TemplateNode -> string * (TemplateNode * int array) array`, `renderHtml: TemplateComponent -> Setup -> obj -> bool -> string`.

- [ ] **Step 1: Write the failing portable checks**

Append to `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, after its existing checks:

```fsharp
open Ranvier.View.Foundation.Template

type GreetingProps = { Title: string; Name: string }

let greeting =
    Component.define
        (fun _ (props: GreetingProps) -> props)
        (fun t ->
            t.div
                [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |]
                [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |])

let greetingProps = { Title = "T &"; Name = "<Ada>" }

let render policy view =
    Html.render policy (Program.run view)

// Separators keep adjacent texts distinct; values are escaped as the HTML backend escapes them.
equal
    "<div class=\"panel\" title=\"T &amp;\">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div>"
    (render Omit (Component.render greeting greetingProps))

let named =
    Component.define
        (fun _ (props: GreetingProps) -> props)
        (fun t -> t.div [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |] [| t.span [||] [| t.text (fun r -> r.Name) |] |])

let namedTwin (props: GreetingProps) =
    Program.createComponent (fun _ ->
        Program.element
            Div
            [| Attribute ("class", "panel"); ReactiveAttribute ("title", fun () -> Some props.Title) |]
            [| Program.element (HtmlTag "span") [||] [| Program.bind (=) (fun () -> props.Name) |] |])

// A template and its createComponent twin produce identical server HTML.
equal (render Omit (namedTwin greetingProps)) (render Omit (Component.render named greetingProps))

let clicking =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.button [| t.Events.on ("click", fun _ _ -> ()) |] [||])

equal "<button></button>" (render Omit (Component.render clicking greetingProps))

let mutable rejected = false

try
    render Reject (Component.render clicking greetingProps) |> ignore
with :? System.InvalidOperationException ->
    rejected <- true

equal true rejected

// The tree representation renders a template through the default expansion.
equal
    "<div class=\"panel\" title=\"T &amp;\"><span>&lt;Ada&gt;</span></div>"
    (Html.render Omit (Tree.run (Component.renderWith Tree.ops named greetingProps)))

printfn "PASS template portable rendering"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: build failure, `The namespace or module 'Template' is not defined`.

- [ ] **Step 3: Add the backend member and the public escape**

In `experiments/Ranvier.View.Foundation/Representation.fs`, change the `Backend` declaration to a recursive group. Directly after the line `abstract Component: (Setup -> unit) -> unit` add:

```fsharp
    /// <summary>Renders a template component definition with its props.</summary>
    abstract Template: TemplateDefinition * obj -> unit

    default this.Template(definition, props) =
        definition.Expand (this, props)

/// <summary>A component definition whose template is built once and compiled lazily per backend.</summary>
and [<AbstractClass>] TemplateDefinition() =
    /// <summary>Renders the definition through ordinary backend operations.</summary>
    abstract Expand: Backend * obj -> unit

    /// <summary>Renders an instance as HTML markup; a <c>true</c> flag rejects browser-only parts.</summary>
    abstract RenderHtml: Setup * obj * bool -> string

    /// <summary>The DOM backend's compiled form, or null before the first DOM render.</summary>
    member val Compiled: obj = null with get, set
```

In the `Html` module, change `let private escape attribute (value: string) =` to:

```fsharp
    /// <summary>Escapes text for HTML; a <c>true</c> flag also escapes double quotes for attribute values.</summary>
    let escape attribute (value: string) =
```

In the `Html.render` backend object expression, after `member _.Component setup =` and its body, add:

```fsharp
                member this.Template(definition, props) =
                    this.Component (fun context -> append (definition.RenderHtml (context, props, (policy = Reject))))
```

- [ ] **Step 4: Create `Template.fs`**

Create `experiments/Ranvier.View.Foundation/Template.fs`:

```fsharp
module Ranvier.View.Foundation.Template

open System
open System.Text
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Foundation.Representation

/// <summary>Where a dynamic slot writes on its node.</summary>
[<NoEquality; NoComparison>]
type SlotTarget =
    /// <summary>An attribute written from a <c>string option</c>; <c>None</c> removes it.</summary>
    | AttributeTarget of string
    /// <summary>A property written by a shared writer.</summary>
    | PropertyTarget of Func<obj, obj, unit>
    /// <summary>The data of a text node.</summary>
    | TextTarget

/// <summary>How a dynamic slot is re-evaluated.</summary>
type SlotMode =
    /// <summary>Within the instance's render effect.</summary>
    | RenderMode
    /// <summary>In its own effect.</summary>
    | IsolatedMode
    /// <summary>From one fixed source; evaluated in the render effect until core provides fixed-dependency bindings.</summary>
    | DirectMode

/// <summary>A dynamic part of a template: reads a value from the instance and writes it to a node.</summary>
[<NoEquality; NoComparison>]
type Slot =
    {
        Target: SlotTarget
        Mode: SlotMode
        Read: Func<obj, obj>
        Same: Func<obj, obj, bool>
    }

/// <summary>A node of a backend-neutral template tree.</summary>
[<NoEquality; NoComparison>]
type TemplateNode =
    | ElementNode of
        Tag *
        (string * string) array *
        (Func<obj, obj, unit> * obj) array *
        Slot array *
        (string * Func<obj, obj, unit>) array *
        TemplateNode array
    | TextNode of string
    | TextSlotNode of Slot
    | FragmentNode of TemplateNode array

#if FABLE_COMPILER
[<Emit("$0 === $1")>]
let private strictEquals (left: obj) (right: obj) : bool = jsNative
#else
let private strictEquals (left: obj) (right: obj) = Object.Equals (left, right)
#endif

/// <summary>Compares primitive slot values: strings, booleans, numbers and options of them.</summary>
let primitiveSame = Func<obj, obj, bool>(fun left right -> strictEquals left right)

/// <summary>One part of an element in a template for instances of type <c>'inst</c>.</summary>
[<NoEquality; NoComparison>]
type Part<'native, 'inst> =
    internal
    | StaticAttribute of string * string
    | StaticProperty of Func<obj, obj, unit> * obj
    | DynamicPart of Slot
    | EventPart of string * Func<obj, obj, unit>

/// <summary>A template subtree for instances of type <c>'inst</c>.</summary>
[<NoEquality; NoComparison>]
type TemplateView<'inst> = internal TemplateView of TemplateNode

/// <summary>A reader of one fixed signal or memo of an instance.</summary>
[<NoEquality; NoComparison>]
type Source<'inst, 'value> = internal Source of Func<obj, obj>

let private slot target mode (read: 'inst -> 'value) same =
    {
        Target = target
        Mode = mode
        Read = Func<obj, obj>(fun instance -> box (read (unbox instance)))
        Same = same
    }

let private sourceSlot target (Source read: Source<'inst, 'value>) same =
    {
        Target = target
        Mode = DirectMode
        Read = read
        Same = same
    }

let private someOf (Source read: Source<'inst, string>) : Source<'inst, string option> =
    Source (Func<obj, obj>(fun instance -> box (Some (unbox<string> (read.Invoke instance)))))

let private writer (write: 'native -> 'value -> unit) =
    Func<obj, obj, unit>(fun node value -> write (unbox node) (unbox value))

let private nodeOf (TemplateView node) = node

let private elementNode tag (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) =
    let attributes = ResizeArray ()
    let properties = ResizeArray ()
    let slots = ResizeArray ()
    let events = ResizeArray ()

    for part in parts do
        match part with
        | StaticAttribute (name, value) ->
            validateAttribute name
            attributes.Add (name, value)
        | StaticProperty (write, value) -> properties.Add (write, value)
        | DynamicPart slot -> slots.Add slot
        | EventPart (name, handler) -> events.Add (name, handler)

    TemplateView (
        ElementNode (
            tag,
            attributes.ToArray (),
            properties.ToArray (),
            slots.ToArray (),
            events.ToArray (),
            Array.map nodeOf children
        )
    )

/// <summary>Attribute parts: a value is static, a reader or source is dynamic.</summary>
[<Sealed>]
type AttributeBuilder<'inst> internal () =
    member _.attribute(name: string, value: string) : Part<'native, 'inst> = StaticAttribute (name, value)

    member _.attribute(name: string, read: 'inst -> string option) : Part<'native, 'inst> =
        validateAttribute name
        DynamicPart (slot (AttributeTarget name) RenderMode read primitiveSame)

    member _.attribute(name: string, source: Source<'inst, string option>) : Part<'native, 'inst> =
        validateAttribute name
        DynamicPart (sourceSlot (AttributeTarget name) source primitiveSame)

    member this.className(value: string) : Part<'native, 'inst> = this.attribute ("class", value)
    member this.className(read: 'inst -> string) : Part<'native, 'inst> = this.attribute ("class", fun instance -> Some (read instance))
    member this.className(source: Source<'inst, string>) : Part<'native, 'inst> = this.attribute ("class", someOf source)
    member this.id(value: string) : Part<'native, 'inst> = this.attribute ("id", value)
    member this.id(read: 'inst -> string) : Part<'native, 'inst> = this.attribute ("id", fun instance -> Some (read instance))
    member this.id(source: Source<'inst, string>) : Part<'native, 'inst> = this.attribute ("id", someOf source)
    member this.title(value: string) : Part<'native, 'inst> = this.attribute ("title", value)
    member this.title(read: 'inst -> string) : Part<'native, 'inst> = this.attribute ("title", fun instance -> Some (read instance))
    member this.title(source: Source<'inst, string>) : Part<'native, 'inst> = this.attribute ("title", someOf source)

/// <summary>Property parts: a value is written once per instance, a reader or source is dynamic.</summary>
[<Sealed>]
type PropertyBuilder<'inst> internal () =
    member _.propertyStatic(write: 'native -> 'value -> unit, value: 'value) : Part<'native, 'inst> =
        StaticProperty (writer write, box value)

    member _.propertyWith(write: 'native -> 'value -> unit, read: 'inst -> 'value, same: Func<obj, obj, bool>) : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (writer write)) RenderMode read same)

    member _.propertyFrom(write: 'native -> 'value -> unit, source: Source<'inst, 'value>, same: Func<obj, obj, bool>) : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (writer write)) source same)

    member this.value(value: string) : Part<HTMLInputElement, 'inst> =
        this.propertyStatic ((fun (node: HTMLInputElement) next -> node.value <- next), value)

    member this.value(read: 'inst -> string) : Part<HTMLInputElement, 'inst> =
        this.propertyWith ((fun (node: HTMLInputElement) next -> node.value <- next), read, primitiveSame)

    member this.value(source: Source<'inst, string>) : Part<HTMLInputElement, 'inst> =
        this.propertyFrom ((fun (node: HTMLInputElement) next -> node.value <- next), source, primitiveSame)

    member this.disabled(value: bool) : Part<HTMLButtonElement, 'inst> =
        this.propertyStatic ((fun (node: HTMLButtonElement) next -> node.disabled <- next), value)

    member this.disabled(read: 'inst -> bool) : Part<HTMLButtonElement, 'inst> =
        this.propertyWith ((fun (node: HTMLButtonElement) next -> node.disabled <- next), read, primitiveSame)

    member this.disabled(source: Source<'inst, bool>) : Part<HTMLButtonElement, 'inst> =
        this.propertyFrom ((fun (node: HTMLButtonElement) next -> node.disabled <- next), source, primitiveSame)

/// <summary>Event parts: one shared handler per template, given the instance and the event.</summary>
[<Sealed>]
type EventBuilder<'inst> internal () =
    member _.on(eventName: string, handler: 'inst -> Event -> unit) : Part<'native, 'inst> =
        EventPart (eventName, Func<obj, obj, unit>(fun instance event -> handler (unbox instance) (unbox event)))

    member this.onClick(handler: 'inst -> MouseEvent -> unit) : Part<'native, 'inst> =
        this.on ("click", fun instance event -> handler instance (unbox event))

    member this.onInput(handler: 'inst -> InputEvent -> unit) : Part<'native, 'inst> =
        this.on ("input", fun instance event -> handler instance (unbox event))

    member this.onFocus(handler: 'inst -> FocusEvent -> unit) : Part<'native, 'inst> =
        this.on ("focus", fun instance event -> handler instance (unbox event))

/// <summary>Builds a template for instances of type <c>'inst</c>; dynamic parts receive the instance as their argument.</summary>
[<Sealed>]
type TemplateBuilder<'inst> internal () =
    let attrs = AttributeBuilder<'inst> ()
    let props = PropertyBuilder<'inst> ()
    let events = EventBuilder<'inst> ()

    member _.Attrs = attrs
    member _.Props = props
    member _.Events = events

    member _.element (tag: Tag) (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        elementNode tag parts children

    member _.div (parts: Part<HTMLDivElement, 'inst> array) (children: TemplateView<'inst> array) = elementNode Div parts children
    member _.section (parts: Part<HTMLElement, 'inst> array) (children: TemplateView<'inst> array) = elementNode (HtmlTag "section") parts children
    member _.span (parts: Part<HTMLSpanElement, 'inst> array) (children: TemplateView<'inst> array) = elementNode (HtmlTag "span") parts children
    member _.button (parts: Part<HTMLButtonElement, 'inst> array) (children: TemplateView<'inst> array) = elementNode Button parts children
    member _.input(parts: Part<HTMLInputElement, 'inst> array) : TemplateView<'inst> = elementNode Input parts [||]

    member _.fragment(children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateView (FragmentNode (Array.map nodeOf children))

    member _.text(value: string) : TemplateView<'inst> = TemplateView (TextNode value)

    member _.text(read: 'inst -> string) : TemplateView<'inst> =
        TemplateView (TextSlotNode (slot TextTarget RenderMode read primitiveSame))

    member _.text(source: Source<'inst, string>) : TemplateView<'inst> =
        TemplateView (TextSlotNode (sourceSlot TextTarget source primitiveSame))

    member _.from(accessor: 'inst -> Signal<'value>) : Source<'inst, 'value> =
        Source (Func<obj, obj>(fun instance -> box (accessor (unbox instance)).Value))

    member _.from(accessor: 'inst -> Memo<'value>) : Source<'inst, 'value> =
        Source (Func<obj, obj>(fun instance -> box (accessor (unbox instance)).Value))

    member _.isolated(part: Part<'native, 'inst>) : Part<'native, 'inst> =
        match part with
        | DynamicPart slot -> DynamicPart { slot with Mode = IsolatedMode }
        | _ -> invalidArg "part" "Only dynamic parts can be isolated"

    member _.isolated(view: TemplateView<'inst>) : TemplateView<'inst> =
        match view with
        | TemplateView (TextSlotNode slot) -> TemplateView (TextSlotNode { slot with Mode = IsolatedMode })
        | _ -> invalidArg "view" "Only text slots can be isolated"

let rec private flatten (nodes: TemplateNode array) =
    [
        for node in nodes do
            match node with
            | FragmentNode children -> yield! flatten children
            | TextNode "" -> ()
            | other -> other
    ]

let private isText node =
    match node with
    | TextNode _
    | TextSlotNode _ -> true
    | _ -> false

let private rawText tag =
    match name tag with
    | "script"
    | "style"
    | "iframe"
    | "noscript"
    | "xmp"
    | "noembed"
    | "noframes"
    | "plaintext" -> true
    | _ -> false

[<NoEquality; NoComparison>]
type private MarkupMode =
    | Skeleton
    | Server of instance: obj * rejectBrowser: bool

let private hasBrowserParts properties (slots: Slot array) (events: (string * Func<obj, obj, unit>) array) =
    Array.length properties > 0
    || events.Length > 0
    || slots
       |> Array.exists (fun slot ->
           match slot.Target with
           | PropertyTarget _ -> true
           | _ -> false)

let private writeMarkup (output: StringBuilder) mode (targets: ResizeArray<TemplateNode * int array>) (root: TemplateNode) =
    let append (value: string) = output.Append value |> ignore

    let rec writeChildren (children: TemplateNode array) (path: int list) =
        let mutable index = 0
        let mutable previousText = false

        for child in flatten children do
            let text = isText child

            if text && previousText then
                append "<!--!-->"
                index <- index + 1

            writeNode child (index :: path)
            index <- index + 1
            previousText <- text

    and writeNode node path =
        match node with
        | TextNode value -> append (Html.escape false value)
        | TextSlotNode slot ->
            match mode with
            | Skeleton ->
                targets.Add (node, Array.ofList (List.rev path))
                append " "
            | Server (instance, _) ->
                let value = unbox<string> (slot.Read.Invoke instance)
                append (Html.escape false (if isNull value then "" else value))
        | FragmentNode _ -> invalidOp "Fragments are flattened before writing"
        | ElementNode (tag, attributes, properties, slots, events, children) ->
            if rawText tag then
                raise (InvalidOperationException "Raw-text elements require an explicit HTML serialization policy")

            match mode with
            | Skeleton ->
                if properties.Length > 0 || slots.Length > 0 || events.Length > 0 then
                    targets.Add (node, Array.ofList (List.rev path))
            | Server (_, rejectBrowser) ->
                if rejectBrowser && hasBrowserParts properties slots events then
                    invalidOp "Browser capability requires explicit Omit policy"

            append ("<" + name tag)

            for key, value in attributes do
                append (" " + key + "=\"" + Html.escape true value + "\"")

            match mode with
            | Server (instance, _) ->
                for slot in slots do
                    match slot.Target with
                    | AttributeTarget key ->
                        match unbox<string option> (slot.Read.Invoke instance) with
                        | Some value -> append (" " + key + "=\"" + Html.escape true value + "\"")
                        | None -> ()
                    | _ -> ()
            | Skeleton -> ()

            append ">"

            if not (isVoid tag) then
                writeChildren children path
                append ("</" + name tag + ">")

    writeChildren [| root |] []

/// <summary>The skeleton markup of a template and the path of every dynamic node, in document order.</summary>
/// <remarks>A path lists child indexes from the skeleton's root fragment.</remarks>
let skeletonMarkup (root: TemplateNode) =
    let output = StringBuilder ()
    let targets = ResizeArray ()
    writeMarkup output Skeleton targets root
    output.ToString (), targets.ToArray ()

/// <summary>A template component definition without its props type.</summary>
[<AbstractClass>]
type TemplateComponent internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateDefinition ()
    member internal _.Setup = setup
    member internal _.Root = root

/// <summary>Renders an instance as server HTML with the skeleton's markers.</summary>
let renderHtml (definition: TemplateComponent) (setup: Setup) (props: obj) (rejectBrowser: bool) =
    let instance = definition.Setup.Invoke (setup, props)
    let output = StringBuilder ()
    writeMarkup output (Server (instance, rejectBrowser)) (ResizeArray ()) definition.Root
    output.ToString ()

let rec private expandNode (ops: Operations<'view>) (instance: obj) (node: TemplateNode) : 'view =
    match node with
    | TextNode value -> ops.Text value
    | TextSlotNode slot ->
        ops.Bind (fun left right -> slot.Same.Invoke (box left, box right)) (fun () -> unbox<string> (slot.Read.Invoke instance))
    | FragmentNode children -> ops.Fragment (Array.map (expandNode ops instance) children)
    | ElementNode (tag, attributes, properties, slots, events, children) ->
        let modifiers: ModifierAction array =
            [|
                for key, value in attributes -> Attribute (key, value)
                for write, value in properties -> fun (backend: Backend) -> backend.Property (fun target -> write.Invoke (target, value))
                for slot in slots ->
                    match slot.Target with
                    | AttributeTarget key -> ReactiveAttribute (key, fun () -> unbox<string option> (slot.Read.Invoke instance))
                    | PropertyTarget write -> fun (backend: Backend) -> backend.ReactiveProperty ((fun () -> slot.Read.Invoke instance), write)
                    | TextTarget -> invalidOp "A text slot is a child node"
                for eventName, handler in events -> listenModifier eventName (fun event -> handler.Invoke (instance, event))
            |]

        ops.Element tag modifiers (Array.map (expandNode ops instance) children)

/// <summary>Expands a definition into any view representation through its operations.</summary>
let expandWith (ops: Operations<'view>) (definition: TemplateComponent) (props: obj) : 'view =
    ops.Component (fun context -> expandNode ops (definition.Setup.Invoke (context, props)) definition.Root)

/// <summary>A component whose template is built once per definition; setup runs per instance.</summary>
[<Sealed>]
type Component<'props> internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateComponent (setup, root)

    override this.Expand(backend, props) =
        Program.run (expandWith Program.ops (this :> TemplateComponent) props) backend

    override this.RenderHtml(context, props, rejectBrowser) =
        renderHtml (this :> TemplateComponent) context props rejectBrowser

module Component =
    /// <summary>Defines a component from per-instance setup and a template built once.</summary>
    /// <remarks><c>setup</c> runs untracked for each instance and returns the instance that dynamic parts receive.</remarks>
    let define (setup: Setup -> 'props -> 'inst) (build: TemplateBuilder<'inst> -> TemplateView<'inst>) : Component<'props> =
        let (TemplateView root) = build (TemplateBuilder<'inst> ())
        Component<'props> (Func<Setup, obj, obj>(fun context props -> box (setup context (unbox props))), root)

    /// <summary>Renders an instance as a Program view; the DOM backend materialises the compiled template.</summary>
    let render (definition: Component<'props>) (props: 'props) : Program.View =
        Program.View (fun backend -> backend.Template (definition, box props))

    /// <summary>Renders an instance in any representation through the default expansion.</summary>
    let renderWith (ops: Operations<'view>) (definition: Component<'props>) (props: 'props) : 'view =
        expandWith ops (definition :> TemplateComponent) (box props)
```

Add `Template.fs` to the three project files, directly after `Typed.fs`:
- `Ranvier.View.Foundation.fsproj`: `<Compile Include="Template.fs" />`
- `RepresentationChecks.fsproj`: `<Compile Include="Template.fs" />`
- `tests/DomTests.fsproj`: `<Compile Include="../../../experiments/Ranvier.View.Foundation/Template.fs" Link="Template.fs" />`

- [ ] **Step 5: Run the portable checks**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every existing `PASS` line, then `PASS template portable rendering`.

- [ ] **Step 6: Run the playground suites**

Run: `npm run test:dom` then `npm run test:browser` in `examples/Fable.Ranvier.Playground`.
Expected: 211 Node tests and 22 Chromium tests pass; no backend changed behaviour.

- [ ] **Step 7: Commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/Representation.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/tests/DomTests.fsproj
rtk git commit -m "feat(view): add portable component templates"
```

---

### Task 2: DOM compilation

**Files:**
- Create: `experiments/Ranvier.View.Foundation/TemplateDom.fs`
- Modify: `Ranvier.View.Foundation.fsproj` and `tests/DomTests.fsproj` (compile `TemplateDom.fs` directly before `Mounting.fs`)
- Create: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, `examples/Fable.Ranvier.Playground/tests/template.test.mjs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (compile `TemplateChecks.fs` after `DelegationChecks.fs`), `examples/Fable.Ranvier.Playground/package.json` (`test:dom` list)

**Interfaces:**
- Consumes: `skeletonMarkup`, `TemplateComponent`, `Slot`, `SlotMode` (Task 1).
- Produces: `TemplateDom.walkProgram: int array array -> int array`; `TemplateDom.Compiled` record (`Skeleton: DocumentFragment`, `Walk: int array`, `NodeCount: int`, `RenderSlots: (int * Slot) array`, `IsolatedSlots: (int * Slot) array`, `Properties: (int * Func<obj, obj, unit> * obj) array`, `Events: (int * string * Func<obj, obj, unit>) array`, `Dispatch: Func<obj array, Node, obj, unit>`); `TemplateDom.compile: TemplateComponent -> Compiled`; opcode literals `FirstChild = 0`, `NextSibling = 1`, `Parent = 2`, `Collect = 3`; state layout literals `StateCompiled = 0`, `StateInstance = 1`, `StateOwner = 2`, `StateGraph = 3`, `StateNodes = 4`.

- [ ] **Step 1: Write the failing tests**

Create `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`:

```fsharp
module TemplateChecks

open global.Ranvier
open Fable.Core
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
    Component.define
        (fun _ (props: Greeting) -> props)
        (fun t ->
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
    let markup, targets = skeletonMarkup (greeting :> TemplateComponent).Root
    equal "<div class=\"panel\">Hello <!--!--> <!--!--> </div><span> </span>" markup
    equal true (roundTrips markup)
    equal 4 targets.Length
    equal 4 compiled.NodeCount
    equal 4 compiled.RenderSlots.Length
    equal 2. compiled.Skeleton.childNodes.length

/// <summary>Checks template compilation and DOM materialisation.</summary>
let cases =
    [|
        "walk programs reach dynamic nodes in document order", walkPrograms
        "a compiled skeleton round-trips and records every dynamic node", compiledSkeleton
    |]
```

Create `examples/Fable.Ranvier.Playground/tests/template.test.mjs`:

```js
import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests/TemplateChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
globalThis.FocusEvent = dom.window.FocusEvent;
after(() => { dom.window.close(); delete globalThis.window; delete globalThis.document; delete globalThis.FocusEvent; });
for (const [name, run] of cases) test(name, run);
```

In `package.json`, append ` tests/template.test.mjs` to the `test:dom` script's file list, after `tests/delegation.test.mjs`. In `tests/DomTests.fsproj`, add `<Compile Include="TemplateChecks.fs" />` after `<Compile Include="DelegationChecks.fs" />`.

- [ ] **Step 2: Run them to verify they fail**

Run: `npm run test:dom`
Expected: Fable error, `The value, namespace, type or module 'TemplateDom' is not defined`.

- [ ] **Step 3: Implement compilation**

Create `experiments/Ranvier.View.Foundation/TemplateDom.fs`:

```fsharp
module Ranvier.View.Foundation.TemplateDom

open System
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

[<Global "document">]
let private document: Document = jsNative

[<Literal>]
let FirstChild = 0

[<Literal>]
let NextSibling = 1

[<Literal>]
let Parent = 2

[<Literal>]
let Collect = 3

[<Literal>]
let StateCompiled = 0

[<Literal>]
let StateInstance = 1

[<Literal>]
let StateOwner = 2

[<Literal>]
let StateGraph = 3

[<Literal>]
let StateNodes = 4

/// <summary>A definition compiled for the DOM: a skeleton, the walk to its dynamic nodes, and its slot tables.</summary>
[<NoEquality; NoComparison>]
type Compiled =
    {
        Skeleton: DocumentFragment
        Walk: int array
        NodeCount: int
        RenderSlots: (int * Slot) array
        IsolatedSlots: (int * Slot) array
        Properties: (int * Func<obj, obj, unit> * obj) array
        Events: (int * string * Func<obj, obj, unit>) array
        Dispatch: Func<obj array, Node, obj, unit>
    }

/// <summary>Compiles child-index paths, in document order, into walk opcodes ending each path with <c>Collect</c>.</summary>
let walkProgram (paths: int array array) : int array =
    let ops = ResizeArray<int> ()
    let mutable cursor: int array = [||]

    for path in paths do
        let mutable shared = 0

        while shared < cursor.Length
              && shared < path.Length
              && cursor[shared] = path[shared] do
            shared <- shared + 1

        if shared = cursor.Length then
            for level in shared .. path.Length - 1 do
                ops.Add FirstChild

                for _ in 1 .. path[level] do
                    ops.Add NextSibling
        else
            for _ in shared + 1 .. cursor.Length - 1 do
                ops.Add Parent

            for _ in 1 .. path[shared] - cursor[shared] do
                ops.Add NextSibling

            for level in shared + 1 .. path.Length - 1 do
                ops.Add FirstChild

                for _ in 1 .. path[level] do
                    ops.Add NextSibling

        ops.Add Collect
        cursor <- path

    ops.ToArray ()

let private dispatchFor (nodeCount: int) (events: (int * string * Func<obj, obj, unit>) array) =
    Func<obj array, Node, obj, unit>(fun state node event ->
        let mutable index = -1

        for i in 0 .. nodeCount - 1 do
            if obj.ReferenceEquals (state[StateNodes + i], node) then
                index <- i

        if index >= 0 then
            let owner = state[StateOwner] :?> Owner

            if not owner.IsDisposed then
                let graph = state[StateGraph] :?> Graph
                let eventType: string = event?``type``

                for target, eventName, handler in events do
                    if target = index && eventName = eventType then
                        graph.Run (fun () ->
                            Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler.Invoke (state[StateInstance], event)))))

/// <summary>Compiles a definition once: parses its skeleton markup and builds its walk and slot tables.</summary>
let compile (definition: TemplateComponent) : Compiled =
    let markup, targets = skeletonMarkup definition.Root
    let template = document.createElement "template" :?> HTMLTemplateElement
    template.innerHTML <- markup
    let renderSlots = ResizeArray ()
    let isolatedSlots = ResizeArray ()
    let properties = ResizeArray ()
    let events = ResizeArray ()

    let add index (slot: Slot) =
        if slot.Mode = IsolatedMode then
            isolatedSlots.Add (index, slot)
        else
            renderSlots.Add (index, slot)

    targets
    |> Array.iteri (fun index (node, _) ->
        match node with
        | TextSlotNode slot -> add index slot
        | ElementNode (_, _, elementProperties, slots, elementEvents, _) ->
            for write, value in elementProperties do
                properties.Add (index, write, value)

            for slot in slots do
                add index slot

            for eventName, handler in elementEvents do
                events.Add (index, eventName, handler)
        | _ -> ())

    let eventArray = events.ToArray ()

    {
        Skeleton = template.content
        Walk = walkProgram (Array.map snd targets)
        NodeCount = targets.Length
        RenderSlots = renderSlots.ToArray ()
        IsolatedSlots = isolatedSlots.ToArray ()
        Properties = properties.ToArray ()
        Events = eventArray
        Dispatch = dispatchFor targets.Length eventArray
    }
```

Add `<Compile Include="TemplateDom.fs" />` directly before `<Compile Include="Mounting.fs" />` in `Ranvier.View.Foundation.fsproj`, and `<Compile Include="../../../experiments/Ranvier.View.Foundation/TemplateDom.fs" Link="TemplateDom.fs" />` directly before the `Mounting.fs` line in `tests/DomTests.fsproj`.

- [ ] **Step 4: Run the tests**

Run: `npm run test:dom`
Expected: 213 Node tests pass, including both template cases.

- [ ] **Step 5: Commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/TemplateDom.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/tests examples/Fable.Ranvier.Playground/package.json
rtk git commit -m "feat(view): compile templates into skeletons and walk programs"
```

---

### Task 3: Materialisation through the mount backend

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (materialisation)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (services, `Template` override)
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `Compiled`, `compile`, state layout literals (Task 2).
- Produces: `TemplateDom.MountServices` record (`Append: Node -> unit`, `Setup: Setup`, `SettingUp: unit -> bool`, `SetupFailed: exn -> unit`, `RaiseSetupFailure: unit -> unit`, `IsDelegated: string -> bool`, `Delegate: string -> unit`, `ListenDirect: Node -> string -> (obj -> unit) -> unit`); `TemplateDom.materialise: TemplateDefinition -> obj -> MountServices -> unit`; `TemplateDom.attachState: obj -> obj array -> unit`.

- [ ] **Step 1: Write the failing tests**

Add to `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, before `cases`:

```fsharp
module H = Ranvier.View.Foundation.Generated.Html

type RowProps = { Index: int; Source: Signal<string>; Disabled: Memo<bool> }

type Row =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
        Local: Signal<int>
        Log: ResizeArray<string>
    }

let private rowLog = ResizeArray<string> ()

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
                    t.input [| t.Props.value (fun r -> r.Source.Value) |]
                    t.button
                        [|
                            t.Props.disabled (fun r -> r.Disabled.Value)
                            t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)
                        |]
                        [| t.text (fun r -> string r.Local.Value) |]
                |])

let private host () = globalThis.document.createElement "main"

let private mount graph (host: HTMLElement) view =
    Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

let private state (host: HTMLElement) =
    let div = host.querySelector "div" |> unbox<HTMLElement>
    let input = host.querySelector "input" |> unbox<HTMLInputElement>
    let button = host.querySelector "button" |> unbox<HTMLButtonElement>
    div.getAttribute "class", div.getAttribute "id", div.getAttribute "title", input.value, button.disabled, button.textContent

let private twinEquality () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> source.Value = "blocked"))
    let first, second = host (), host ()
    let props = { Index = 1; Source = source; Disabled = disabled }
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
    let mounted = mount graph host (Component.render row { Index = 1; Source = source; Disabled = disabled })
    let div = host.querySelector "div" |> unbox<HTMLElement>
    let mutable titleWrites = 0
    let setAttribute = div?setAttribute
    div?setAttribute <- fun (name: string) (value: string) ->
        if name = "title" then titleWrites <- titleWrites + 1
        setAttribute?call (div, name, value)
    (host.querySelector "button" |> unbox<HTMLButtonElement>).click ()
    equal 0 titleWrites
    graph.Run (fun () -> source.Value <- "b")
    equal 1 titleWrites
    mounted.Dispose ()

let private readerFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")

    let failing =
        Component.define
            (fun _ (source: Signal<string>) -> source)
            (fun t ->
                t.div
                    [| t.Attrs.title (fun s -> if s.Value = "bad" then failwith "reader" else s.Value) |]
                    [| t.text (fun s -> s.Value) |])

    let host = host ()
    let mounted = mount graph host (Component.render failing source)
    graph.Run (fun () -> source.Value <- "bad")
    equal ("a", "bad") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    mounted.Dispose ()

let private setterRollback () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<span>keep</span>"
    let log = ResizeArray<string> ()

    let failing =
        Component.define
            (fun setup () ->
                setup.OnCleanup (fun () -> log.Add "cleanup")
                ())
            (fun t -> t.button [| t.Props.disabled (fun _ -> failwith "setter") |] [||])

    let mutable failed = false

    try
        mount graph host (Component.render failing ()) |> ignore
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
        Program.fragment [| for index in 1..2 -> Component.render row { Index = index; Source = source; Disabled = disabled } |]

    let mounted = mount graph host view
    mounted.Dispose ()
    equal [ "cleanup 2"; "cleanup 1" ] (List.ofSeq rowLog)
    equal 0. host.childNodes.length

let private adjacentTexts () =
    use graph = new Graph ()
    let host = host ()
    let mounted = mount graph host (Component.render greeting { Title = "T"; Name = "Ada" })
    equal "Hello Ada!Ada" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private compiledOnce () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let first, second = host (), host ()
    let a = mount graph first (Component.render row { Index = 1; Source = source; Disabled = disabled })
    let compiled = (row :> TemplateDefinition).Compiled
    let b = mount graph second (Component.render row { Index = 2; Source = source; Disabled = disabled })
    equal true (obj.ReferenceEquals (compiled, (row :> TemplateDefinition).Compiled))
    (first.querySelector "button" |> unbox<HTMLButtonElement>).click ()
    equal ("1", "0") ((first.querySelector "button").textContent, (second.querySelector "button").textContent)
    a.Dispose ()
    b.Dispose ()

let private keyedRows () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host = host ()

    let view =
        Keyed.list Program.ops (=) id (fun () -> rows.Value) (fun current ->
            Component.render row { Index = current (); Source = source; Disabled = disabled })

    let mounted = mount graph host view
    let removed = host.querySelectorAll("div").item 1. |> unbox<HTMLElement>
    graph.Run (fun () -> rows.Value <- [ 1 ])
    graph.Run (fun () -> source.Value <- "b")
    equal "a" (removed.getAttribute "title")
    equal "b" ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title")
    mounted.Dispose ()

let private nullText () =
    use graph = new Graph ()
    let nothing = Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text (fun _ -> (null: string)) |])
    let host = host ()
    let mounted = mount graph host (Component.render nothing ())
    equal "" host.textContent
    mounted.Dispose ()

let private staticTemplate () =
    use graph = new Graph ()
    let fixedView = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.className "fixed" |] [| t.text "static" |])
    let first, second = host (), host ()
    let a = mount graph first (Component.render fixedView ())
    let b = mount graph second (Component.render fixedView ())
    let shape (host: HTMLElement) = (host.querySelector "div.fixed").textContent
    equal ("static", "static") (shape first, shape second)
    equal 0 ((fixedView :> TemplateDefinition).Compiled :?> TemplateDom.Compiled).RenderSlots.Length
    a.Dispose ()
    b.Dispose ()
```

Add these entries to the `cases` array:

```fsharp
        "a template renders the same DOM as its createComponent twin", twinEquality
        "slot writes happen only for changed values", changedSlotsOnly
        "a throwing reader fails only its slot", readerFailure
        "a throwing setter rolls back the mount", setterRollback
        "template cleanups run in use order without an instance owner", cleanupOrder
        "adjacent text slots keep separate nodes", adjacentTexts
        "a definition mounted twice keeps instances independent", compiledOnce
        "a template inside a removed keyed row stops updating", keyedRows
        "a null text value renders empty", nullText
        "a static template mounts without effects", staticTemplate
```

The `changedSlotsOnly` test needs `open Fable.Core.JsInterop` at the top of the file for `?` access.

- [ ] **Step 2: Run them to verify they fail**

Run: `npm run test:dom`
Expected: the new cases fail; templates render through the default expansion, so `compiledOnce` fails with `Expected true; got false` (nothing compiled) and `staticTemplate` fails casting a null `Compiled`.

- [ ] **Step 3: Implement materialisation**

Append to `experiments/Ranvier.View.Foundation/TemplateDom.fs`:

```fsharp
/// <summary>The mount-owned operations a template instance uses.</summary>
[<NoEquality; NoComparison>]
type MountServices =
    {
        Append: Node -> unit
        Setup: Setup
        SettingUp: unit -> bool
        SetupFailed: exn -> unit
        RaiseSetupFailure: unit -> unit
        IsDelegated: string -> bool
        Delegate: string -> unit
        ListenDirect: Node -> string -> (obj -> unit) -> unit
    }

let private unset = obj ()

[<Emit("$0.data = $1 == null ? '' : $1")>]
let private setData (node: obj) (value: obj) : unit = jsNative

[<Emit("$1 == null ? $0.removeAttribute($2) : $0.setAttribute($2, $1)")>]
let private setAttribute (node: obj) (value: obj) (name: string) : unit = jsNative

/// <summary>Marks a node as an event node of a template instance.</summary>
[<Emit("$0.$$ranvier = $1")>]
let attachState (node: obj) (state: obj array) : unit = jsNative

let private write (slot: Slot) (node: obj) (value: obj) =
    match slot.Target with
    | AttributeTarget name -> setAttribute node value name
    | PropertyTarget writer -> writer.Invoke (node, value)
    | TextTarget -> setData node value

let private read (slot: Slot) (instance: obj) =
    try
        slot.Read.Invoke instance
    with _ ->
        unset

let private commit (services: MountServices) (state: obj array) (slot: Slot) (nodeIndex: int) (previousIndex: int) (value: obj) =
    if not (obj.ReferenceEquals (value, unset)) then
        let previous = state[previousIndex]

        if obj.ReferenceEquals (previous, unset) || not (slot.Same.Invoke (previous, value)) then
            try
                write slot state[StateNodes + nodeIndex] value
                state[previousIndex] <- value
            with error when services.SettingUp () ->
                services.SetupFailed error
                reraise ()

let private collect (root: Node) (walk: int array) (state: obj array) =
    let mutable cursor = root
    let mutable count = 0

    for op in walk do
        if op = FirstChild then cursor <- unbox cursor.firstChild
        elif op = NextSibling then cursor <- unbox cursor.nextSibling
        elif op = Parent then cursor <- unbox cursor.parentNode
        else
            state[StateNodes + count] <- box cursor
            count <- count + 1

/// <summary>Materialises one instance: runs setup, clones the skeleton, binds its slots and events, and appends it.</summary>
let materialise (definition: TemplateDefinition) (props: obj) (services: MountServices) =
    let component = definition :?> TemplateComponent

    if isNull definition.Compiled then
        definition.Compiled <- box (compile component)

    let compiled = definition.Compiled :?> Compiled
    let instance = Api.untrack (fun () -> component.Setup.Invoke (services.Setup, props))
    let fragment = compiled.Skeleton.cloneNode true
    let renderCount = compiled.RenderSlots.Length
    let isolatedCount = compiled.IsolatedSlots.Length
    let previousBase = StateNodes + compiled.NodeCount
    let pendingBase = previousBase + renderCount + isolatedCount
    let state: obj array = Array.zeroCreate (pendingBase + renderCount)
    state[StateCompiled] <- box compiled
    state[StateInstance] <- instance
    state[StateOwner] <- box (Api.getOwner ())
    state[StateGraph] <- box Graph.Current

    for index in previousBase .. pendingBase - 1 do
        state[index] <- unset

    collect fragment compiled.Walk state

    for nodeIndex, writer, value in compiled.Properties do
        try
            writer.Invoke (state[StateNodes + nodeIndex], value)
        with error when services.SettingUp () ->
            services.SetupFailed error
            reraise ()

    if renderCount > 0 then
        let commitAll () =
            for k in 0 .. renderCount - 1 do
                let nodeIndex, slot = compiled.RenderSlots[k]
                commit services state slot nodeIndex (previousBase + k) state[pendingBase + k]

        Api.createEffect (fun () ->
            for k in 0 .. renderCount - 1 do
                let _, slot = compiled.RenderSlots[k]
                state[pendingBase + k] <- read slot instance

            Api.untrack commitAll)

        services.RaiseSetupFailure ()

    for k in 0 .. isolatedCount - 1 do
        let nodeIndex, slot = compiled.IsolatedSlots[k]
        let previousIndex = previousBase + renderCount + k

        Api.createEffect (fun () ->
            let value = read slot instance
            Api.untrack (fun () -> commit services state slot nodeIndex previousIndex value))

        services.RaiseSetupFailure ()

    for nodeIndex, eventName, handler in compiled.Events do
        let node = state[StateNodes + nodeIndex]

        if services.IsDelegated eventName then
            attachState node state
            services.Delegate eventName
        else
            let owner = state[StateOwner] :?> Owner
            let graph = state[StateGraph] :?> Graph

            services.ListenDirect (unbox node) eventName (fun event ->
                if not owner.IsDisposed then
                    graph.Run (fun () ->
                        Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler.Invoke (instance, event)))))

    let roots = [| for i in 0 .. int fragment.childNodes.length - 1 -> fragment.childNodes.item (float i) |]

    for root in roots do
        services.Append root
```

- [ ] **Step 4: Wire the mount backend**

In `experiments/Ranvier.View.Foundation/Mounting.fs`:

1. In `mountCore`, extract the delegated-name registration from `Listen` into a local function placed before `let backend =`:

```fsharp
    let registerDelegated (eventName: string) =
        let mutable known = false

        for index in 0 .. delegatedNames.Count - 1 do
            if delegatedNames[index] = eventName then
                known <- true

        if not known then
            delegatedNames.Add eventName
```

and replace the matching loop inside `member _.Listen` with `registerDelegated eventName`.

2. Directly after `registerDelegated`, add the services:

```fsharp
    let mutable templateServices: TemplateDom.MountServices = Unchecked.defaultof<_>

    let getTemplateServices () =
        if isNull (box templateServices) then
            templateServices <-
                {
                    Append = fun node -> append (unbox<ChildNode> node)
                    Setup = { OnCleanup = fun callback -> ownedCleanup (Api.getOwner ()) callback }
                    SettingUp = fun () -> root.Phase = SettingUp
                    SetupFailed = fun error -> bindingSetupFailure <- Some error
                    RaiseSetupFailure = fun () -> bindingSetupFailure |> Option.iter raise
                    IsDelegated = isDelegated delegatedEvents
                    Delegate = registerDelegated
                    ListenDirect =
                        fun node eventName invoke ->
                            let cleanup = Typed.listenDirect (box node) eventName invoke
                            ownedCleanup (Api.getOwner ()) cleanup
                }

        templateServices
```

3. In the backend object expression, after `member _.Component setup =` and its body, add:

```fsharp
            member this.Template(definition, props) =
                ensureSetup ()

                match scheduling with
                | Synchronous -> TemplateDom.materialise definition props (getTemplateServices ())
                | Queued _ -> definition.Expand (this, props)
```

4. In the `delegateFrom` emit string, replace `const handler = node[key]; if (handler) { handler(event); if (event.cancelBubble) break; }` with:

```
const handler = node[key]; if (handler) { handler(event); if (event.cancelBubble) break; } else if (node.$$ranvier !== undefined) { const state = node.$$ranvier; state[0].Dispatch(state, node, event); if (event.cancelBubble) break; }
```

- [ ] **Step 5: Run the tests**

Run: `npm run test:dom`
Expected: 223 Node tests pass.

- [ ] **Step 6: Check the emitted code**

Run: `rtk grep -n "uncurry2\|curry2" examples/Fable.Ranvier.Playground/generated-tests/experiments/Ranvier.View.Foundation/TemplateDom.js`
Expected: no matches inside `materialise`. If any appear, replace the curried call with a `Func` delegate as in `Typed.reactiveProperty`, re-run Step 5, and keep the change.

- [ ] **Step 7: Run the Chromium suite and commit**

Run: `npm run test:browser`
Expected: 22 tests pass.

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/tests
rtk git commit -m "feat(view): materialise templates in the mount backend"
```

---

### Task 4: Isolated and source slots

**Files:**
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `isolated` and `from` builder members (Task 1), `materialise` (Task 3).

- [ ] **Step 1: Write the tests**

Add before `cases`:

```fsharp
type Counted = { First: Signal<int>; Second: Signal<int>; Reads: int ref }

let private isolatedSlots () =
    use graph = new Graph ()
    let first = graph.Run (fun () -> Api.createSignal 1)
    let second = graph.Run (fun () -> Api.createSignal 1)
    let reads = ref 0

    let counted =
        Component.define
            (fun _ (props: Counted) -> props)
            (fun t ->
                t.div
                    [||]
                    [|
                        t.span [||] [| t.text (fun c -> string c.First.Value) |]
                        t.span [||] [| t.isolated (t.text (fun c -> c.Reads.Value <- c.Reads.Value + 1; string c.Second.Value)) |]
                    |])

    let host = host ()
    let mounted = mount graph host (Component.render counted { First = first; Second = second; Reads = reads })
    graph.Run (fun () -> first.Value <- 2)
    equal 1 reads.Value
    graph.Run (fun () -> second.Value <- 2)
    equal (2, "22") (reads.Value, host.textContent)
    mounted.Dispose ()

let private sourceSlots () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let doubled = graph.Run (fun () -> Api.createMemo (fun _ -> source.Value + source.Value))

    let sourced =
        Component.define
            (fun _ () -> source, doubled)
            (fun t ->
                t.div [| t.Attrs.title (t.from (fun (s, _) -> s)) |] [| t.text (t.from (fun (_, d) -> d)) |])

    let host = host ()
    let mounted = mount graph host (Component.render sourced ())
    graph.Run (fun () -> source.Value <- "b")
    equal ("b", "bb") ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title", host.textContent)
    mounted.Dispose ()
```

Add to `cases`:

```fsharp
        "an isolated slot re-runs only on its own source", isolatedSlots
        "source slots follow their signal and memo", sourceSlots
```

- [ ] **Step 2: Run the tests**

Run: `npm run test:dom`
Expected: 225 Node tests pass. Both behaviours are implemented by Tasks 1 and 3; these tests pin them.

- [ ] **Step 3: Commit**

```bash
rtk dotnet fantomas examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "test(view): pin isolated and source template slots"
```

---

### Task 5: Template events

**Files:**
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `Events` builder (Task 1), event wiring and dispatcher extension (Task 3).

- [ ] **Step 1: Write the tests**

Add before `cases`:

```fsharp
[<Emit("$0.dispatchEvent(new FocusEvent('focus'))")>]
let private dispatchFocus (node: HTMLElement) : unit = jsNative

let private clicks () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let host = host ()
    let mounted = mount graph host (Component.render row { Index = 1; Source = source; Disabled = disabled })
    let button = host.querySelector "button" |> unbox<HTMLButtonElement>
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
    let mounted = mount graph first (Component.render row { Index = 1; Source = source; Disabled = disabled })
    let button = first.querySelector "button" |> unbox<HTMLButtonElement>
    mounted.MoveBefore (second, None)
    button.click ()
    equal "1" button.textContent
    mounted.Dispose ()

let private focusBesideClick () =
    use graph = new Graph ()
    let log = ResizeArray<string> ()

    let both =
        Component.define
            (fun _ () -> log)
            (fun t ->
                t.input
                    [|
                        t.Events.onFocus (fun l _ -> l.Add "focus")
                        t.Events.onClick (fun l _ -> l.Add "click")
                    |])

    let host = host ()
    let mounted = mount graph host (Component.render both ())
    let input = host.querySelector "input" |> unbox<HTMLInputElement>
    dispatchFocus input
    input.click ()
    mounted.Dispose ()
    dispatchFocus input
    equal [ "focus"; "click" ] (List.ofSeq log)
```

Add to `cases`:

```fsharp
        "template click handlers receive the instance and stop after disposal", clicks
        "template handlers dispatch after a move", movedClicks
        "focus uses a direct listener beside a delegated click", focusBesideClick
```

- [ ] **Step 2: Run the tests**

Run: `npm run test:dom`
Expected: 228 Node tests pass.

- [ ] **Step 3: Commit**

```bash
rtk dotnet fantomas examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "test(view): pin delegated and direct template events"
```

---

### Task 6: Fallback backends

**Files:**
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `Backend.Template` default (Task 1), `Browser.mount`, `Mounting.mountWith`, `Prepared.prepare`.

- [ ] **Step 1: Write the tests**

Add before `cases`:

```fsharp
let private queuedFallback () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let host = host ()

    let mounted =
        Mounting.mountWith
            (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore))
            graph
            None
            ignore
            host
            None
            (fun context -> Program.run (Component.render row { Index = 1; Source = source; Disabled = disabled }) context.Backend)

    mounted.Flush ()
    equal "a" ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title")
    graph.Run (fun () -> source.Value <- "b")
    mounted.Flush ()
    equal "b" ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "title")
    mounted.Dispose ()

let private browserInterpreter () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let disabled = graph.Run (fun () -> Api.createMemo (fun _ -> false))
    let host = host ()

    let dispose =
        Browser.mount graph host ignore None (Program.run (Component.render row { Index = 1; Source = source; Disabled = disabled }))

    equal "row-1" ((host.querySelector "div" |> unbox<HTMLElement>).getAttribute "id")
    dispose ()

let private preparedRejects () =
    let mutable rejected = false

    try
        let template = Prepared.prepare (Component.render greeting { Title = "T"; Name = "N" })
        use graph = new Graph ()
        mount graph (host ()) (Prepared.view template (fun _ _ -> ())) |> ignore
    with :? System.InvalidOperationException ->
        rejected <- true

    equal true rejected

let private serverMarkup () =
    equal
        "<div class=\"panel\" title=\"T &amp;\">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div><span>&lt;Ada&gt;</span>"
        (Html.render Omit (Program.run (Component.render greeting { Title = "T &"; Name = "<Ada>" })))
```

Add to `cases`:

```fsharp
        "queued mounts render templates through expansion", queuedFallback
        "the Browser interpreter renders templates through expansion", browserInterpreter
        "prepared skeletons reject templates", preparedRejects
        "server markup under Fable matches the portable expectation", serverMarkup
```

The `serverMarkup` expectation is the .NET check from Task 1 extended by the `span`, so the two runtimes are held to the same bytes.

- [ ] **Step 2: Run the tests**

Run: `npm run test:dom`
Expected: 232 Node tests pass.

- [ ] **Step 3: Commit**

```bash
rtk dotnet fantomas examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "test(view): pin template fallbacks and server markup"
```

---

### Task 7: Benchmark adapter, measurement and records

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/tests/FrameworkPerf.fs` (a `mountTemplate` export)
- Modify: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/page.mjs` (adapter), `run.mjs` (`--template-comparison`), `allocations.mjs` (mode list)
- Modify: `experiments/Ranvier.View.Foundation/Prepared.fs` (module doc marks it superseded)
- Create: `docs/research/2026-10-14-component-templates.md`
- Modify: `docs/research/2026-10-11-view-poc-checkpoint.md` (index entry)

**Interfaces:**
- Consumes: `Component.define`, `Component.render` (Task 1), the compiled mount path (Task 3).
- Produces: `FrameworkPerf.mountTemplate: HTMLElement -> int -> (string -> unit) * (unit -> unit)`; benchmark mode `ranvier-template`.

- [ ] **Step 1: Add the template fixture**

In `tests/FrameworkPerf.fs`, after the `module H` line, add:

```fsharp
type private PerfProps = { Index: int; Source: Signal<string>; Disabled: Memo<bool> }

type private PerfRow =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
        Local: Signal<int>
    }

let private perfRow =
    Template.Component.define
        (fun _ (props: PerfProps) ->
            {
                Index = props.Index
                Source = props.Source
                Disabled = props.Disabled
                Local = Api.createSignal 0
            })
        (fun t ->
            t.div
                [|
                    t.Attrs.id (fun r -> "row-" + string r.Index)
                    t.Attrs.className "panel"
                    t.Attrs.title (t.from (fun r -> r.Source))
                |]
                [|
                    t.input [| t.Props.value (t.from (fun r -> r.Source)) |]
                    t.button
                        [|
                            t.Props.disabled (t.from (fun r -> r.Disabled))
                            t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)
                        |]
                        [| t.text (fun r -> string r.Local.Value) |]
                |])
```

and at the end of the file:

```fsharp
/// <summary>Mounts equivalent rows as template component instances.</summary>
let mountTemplate host width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render perfRow { Index = index + 1; Source = source; Disabled = disabled })
            ))
        host
        width
```

- [ ] **Step 2: Wire the harness**

In `benchmarks/frameworks/page.mjs`, change the first import to `import { mount as ranvierMount, mountPrepared, mountTemplate } from "../../generated-tests/FrameworkPerf.js";` and add to `adapters`, after the `"ranvier-prepared"` entry:

```js
  "ranvier-template": adapt(mountTemplate),
```

In `benchmarks/frameworks/run.mjs`:
- after `const preparedComparison = ...` add `const templateComparison = process.argv.includes("--template-comparison");`
- add `templateComparison` to the array in the "Choose one diagnostic adapter set per run" check;
- make `modes` start with `templateComparison ? ["ranvier", "ranvier-template", "solid", "ripple", "react"] : `;
- include `templateComparison` in the `mkdir` condition and map the output name to `investigation/template-framework-results` before the `preparedComparison` branch;
- add the note `templateComparison ? "ranvier-template mounts the row as a compiled template component; the ranvier adapter uses authored components." :` before the `preparedComparison` note;
- add `"../../experiments/Ranvier.View.Foundation/Template.fs"` and `"../../experiments/Ranvier.View.Foundation/TemplateDom.fs"` to the hashed `files`.

In `benchmarks/frameworks/allocations.mjs`, change the mode list to `["ranvier", "ranvier-template", "ranvier-direct", "solid", "ripple", "native-descendants"]`.

- [ ] **Step 3: Verify and measure**

Run: `rtk node benchmarks/frameworks/run.mjs --build --verify-only --template-comparison`
Expected: `Verified 5 adapters` in each process; exit code 0.

Run: `rtk node benchmarks/frameworks/run.mjs --build --template-comparison`
Expected: `docs/research/framework-performance/investigation/template-framework-results.json` written; exit code 0.

Run: `rtk node benchmarks/frameworks/allocations.mjs --out=template-profile`
Expected: `docs/research/framework-performance/investigation/template-profile/allocations.json` written with a `ranvier-template` row.

- [ ] **Step 4: Record the results**

Create `docs/research/2026-10-14-component-templates.md` with:
- a summary of the template model and the spec clarifications listed at the top of this plan;
- the 128-row mount, update and dispose medians and process medians for `ranvier`, `ranvier-template`, Solid 2, Ripple and React from `template-framework-results.json`, and the retained heap per mount;
- allocation per mount for `ranvier`, `ranvier-template`, Solid 2 and Ripple from `template-profile/allocations.json`;
- the spec targets checked explicitly: "mount within 1.3× Solid 2" and "allocation within 1.5× Solid 2", each stated as met or not met with the ratio;
- the acceptance line: Node and Chromium test counts, and the portable checks.

Append an index paragraph to `docs/research/2026-10-11-view-poc-checkpoint.md` linking the note, in the style of the existing entries.

In `experiments/Ranvier.View.Foundation/Prepared.fs`, add to the module's first doc comment: "Superseded by component templates in `Template.fs`; kept until its removal is decided."

- [ ] **Step 5: Run every suite**

Run: `npm run test:dom`, `npm run test:browser`, and `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: 232 Node tests, 22 Chromium tests, and every portable `PASS` line.

- [ ] **Step 6: Commit**

```bash
rtk dotnet fantomas examples/Fable.Ranvier.Playground/tests/FrameworkPerf.fs experiments/Ranvier.View.Foundation/Prepared.fs
rtk git add examples/Fable.Ranvier.Playground experiments/Ranvier.View.Foundation/Prepared.fs docs/research
rtk git commit -m "test(bench): measure component templates against Solid 2"
```

---

### Task 8: Report binding reader failures from every path

This task was added during execution. Today a reader that throws inside an ordinary binding is recorded on the binding's effect (`Status`, `Error`), and nothing the mount owner sees receives it: `createEffect` returns `unit`, and the renderer never reads that state. Templates (Task 3) catch each slot's failure so the other slots keep running. That also hides the failure from the effect, so the template path sends it to the mount's `report` sink instead. This task makes every binding path behave the same way: the effect records the failure, and `report` receives it once.

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (in `mountCore`: a reporting helper, and `startBinding` in both scheduling branches)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`read`, the render effect and the isolated effects)
- Test: `examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs`, `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `MountServices.ReportFailure` (from the Task 3 fix round), `startBinding`, `Binding.startQueued`, and core's `NotReadyException`.
- Produces: `reportReaderFailure: exn -> unit` in `mountCore`. `MountServices.ReportFailure` is bound to it.

**Contract:**
- A reader failure other than `NotReadyException` goes to `report` as `[ error ]`, untracked, and is then rethrown, so the binding's effect records it and tracing shows it. `NotReadyException` is rethrown without a report.
- The same exception object reported twice in a row by one mount is reported once, so a failed memo read by many bindings produces one report rather than one per binding. The mount keeps the last reported exception by reference. A different exception is reported, and so is the same one after a successful run has cleared it.
- A template slot failure keeps the slot's previous value and lets the other slots commit. After the commit, the render effect (or the isolated effect) rethrows the run's first failure, so the effect records it.
- A reporter that throws propagates the way the keyed region's reporter does today: wrapped with the reader failure in an `AggregateException`.

- [ ] **Step 1: Write the failing tests**

In `BindingIntegrationChecks.fs`, add a mount helper that collects reports, and add the following cases. Where the file already has helpers that do the same job, use those names instead.

```fsharp
let private collectingMount graph host (reported: ResizeArray<exn>) view =
    Mounting.mount graph None reported.AddRange host None (fun context -> Program.run view context.Backend)

let private bindingReaderFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let reported = ResizeArray<exn> ()
    let host = globalThis.document.createElement "main"

    let view =
        Program.element
            Div
            [| ReactiveAttribute ("title", fun () -> if source.Value = "bad" then failwith "reader" else Some source.Value) |]
            [| Program.bind (=) (fun () -> source.Value) |]

    let mounted = collectingMount graph host reported view
    equal 0 reported.Count
    graph.Run (fun () -> source.Value <- "bad")
    equal [ "reader" ] [ for error in reported -> error.Message ]
    equal "bad" host.textContent
    graph.Run (fun () -> source.Value <- "c")
    equal 1 reported.Count
    mounted.Dispose ()

let private sharedMemoFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal 1)
    let failing = graph.Run (fun () -> Api.createMemo (fun _ -> if source.Value = 2 then failwith "memo" else string source.Value))
    let reported = ResizeArray<exn> ()
    let host = globalThis.document.createElement "main"
    let view = Program.fragment [| Program.bind (=) (fun () -> failing.Value); Program.bind (=) (fun () -> failing.Value) |]
    let mounted = collectingMount graph host reported view
    graph.Run (fun () -> source.Value <- 2)
    equal [ "memo" ] [ for error in reported -> error.Message ]
    mounted.Dispose ()

let private queuedReaderFailure () =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let reported = ResizeArray<exn> ()
    let host = globalThis.document.createElement "main"

    let mounted =
        Mounting.mountWith
            (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore))
            graph
            None
            reported.AddRange
            host
            None
            (fun context -> Program.run (Program.bind (=) (fun () -> if source.Value = "bad" then failwith "reader" else source.Value)) context.Backend)

    mounted.Flush ()
    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()
    equal [ "reader" ] [ for error in reported -> error.Message ]
    equal "a" host.textContent
    mounted.Dispose ()
```

Register them as `"a throwing binding reader is reported once and other bindings update"`, `"a failed memo read by two bindings is reported once"` and `"a throwing reader is reported under queued commits"`.

In `TemplateChecks.fs`, extend `readerFailure`. After the existing assertions, set the source to `"c"`, then assert that the title is `"c"`, the text is `"c"`, and the report count is still 1.

- [ ] **Step 2: Run them to verify they fail**

Run: `npm run test:dom`
Expected: the three binding cases fail with `Expected ["reader"]; got []` (or `["memo"]`), and the template case passes.

- [ ] **Step 3: Implement**

In `mountCore`, before `startBinding`:

```fsharp
    let mutable lastReported: exn = null

    let reportReaderFailure (error: exn) =
        match error with
        | NotReadyException _ -> ()
        | _ when obj.ReferenceEquals (error, lastReported) -> ()
        | _ ->
            lastReported <- error

            try
                Api.untrack (fun () -> report [ error ])
            with reporterError ->
                raise (AggregateException ("Binding reader and reporter failed", [| error; reporterError |]))
```

In the `Synchronous` branch of `startBinding`, replace `let value = read ()` with:

```fsharp
                let value =
                    try
                        read ()
                    with error ->
                        reportReaderFailure error
                        reraise ()
```

In the `Queued` branch, create a wrapped reader once per binding and pass it to `Binding.startQueued`:

```fsharp
            let reportingRead () =
                try
                    read ()
                with error ->
                    reportReaderFailure error
                    reraise ()
```

Bind `MountServices.ReportFailure` to `reportReaderFailure`.

In `TemplateDom.fs`, change `read` so it hands the failure back to the effect instead of reporting it inline:
- Keep the run's first non-pending failure in a local of the effect.
- Call `services.ReportFailure` for that failure.
- Commit as before.
- Then `raise` the failure, after `Api.untrack commitAll` in the render effect and after the slot's commit in an isolated effect.
- `NotReadyException` still returns `unset`, with no report and no rethrow.

Keep the per-instance closure count from Task 3. Check the emitted `TemplateDom.js` and `Mounting.js` for new per-run closures or curry adapters.

Leave the `LADDER_NAIVE_BINDINGS` branch unchanged; it is a measurement baseline.

- [ ] **Step 4: Run the suites**

Run: `npm run test:dom`, `npm run test:browser`, and the portable checks.
Expected: all pass. Any existing test that mounts with a `failwithf` reporter and provokes a reader failure will now fail. Switch each one to a collecting reporter, assert the report, and list each such test in the report.

- [ ] **Step 5: Commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/TemplateDom.fs examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/tests
rtk git commit -m "fix(view): report binding reader failures to the mount"
```
