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
    /// <summary>From one fixed source, evaluated in the render effect.</summary>
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
    /// <summary>Dynamic structure mounted between a pair of region markers.</summary>
    | RegionNode of Region
    /// <summary>A child template inlined into this template's skeleton, with the reader of its props from the inlining instance.</summary>
    | ChildNode of TemplateComponent * Func<obj, obj>

/// <summary>Dynamic structure of a template: keyed items read from an instance, rendered one row per item.</summary>
and [<NoEquality; NoComparison>] Region =
    {
        /// <summary>Reads the items from an instance: an <c>ItemRow</c> region's row values, otherwise one boxed <c>(key, value)</c> pair.</summary>
        Items: Func<obj, obj array>
        Key: Func<obj, obj>
        Row: RegionRow
    }

/// <summary>What a region's item is and how its row renders it.</summary>
and [<NoEquality; NoComparison>] RegionRow =
    /// <summary>The item is a <c>Program.View</c>, read once per instance.</summary>
    | ViewRow
    /// <summary>The item is an <c>Arm</c>; the arm's definition receives a reader of the current arm's payload.</summary>
    | ArmRow
    /// <summary>The item is a row value; the conversion turns a reader of the current item into the definition's props.</summary>
    | ItemRow of TemplateComponent * Func<(unit -> obj), obj>

/// <summary>A template component definition without its props type.</summary>
and [<AbstractClass>] TemplateComponent internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateDefinition()
    member internal _.Setup = setup
    member internal _.Root = root

    /// <summary>The arm reader shared by every arm of the definition; <c>null</c> until the definition first appears in an arm.</summary>
    member val internal ArmReader: Func<(unit -> obj), obj> = null with get, set

/// <summary>A component definition selected by a <c>matchWith</c> or <c>branch</c> arm, with the payload read by its props.</summary>
[<NoEquality; NoComparison>]
type Arm =
    internal
        {
            Definition: TemplateComponent
            Payload: obj
            /// <summary>Turns a reader of the current arm into the definition's props, a reader of the current payload.</summary>
            Reader: Func<(unit -> obj), obj>
        }

#if FABLE_COMPILER
[<Emit("$0 === $1")>]
let private strictEquals (left: obj) (right: obj) : bool = jsNative
#else
let private strictEquals (left: obj) (right: obj) =
    Object.Equals (left, right)
#endif

/// <summary>Compares primitive slot values: strings, booleans, numbers and options of them.</summary>
let primitiveSame = Func<obj, obj, bool>(fun left right -> strictEquals left right)

[<NoEquality; NoComparison>]
type private Flat =
    | Item of TemplateNode
    | Enter of TemplateComponent * Func<obj, obj>
    | Leave

let rec private flatten (nodes: TemplateNode array) : Flat list =
    [
        for node in nodes do
            match node with
            | FragmentNode children -> yield! flatten children
            | TextNode "" -> ()
            | ChildNode (definition, props) ->
                yield Enter (definition, props)
                yield! flatten [| definition.Root |]
                yield Leave
            | other -> yield Item other
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
    | Server of setup: Setup * rejectBrowser: bool * backend: Backend

let private hasBrowserParts properties (slots: Slot array) (events: (string * Func<obj, obj, unit>) array) =
    Array.length properties > 0
    || events.Length > 0
    || slots
       |> Array.exists (fun slot ->
           match slot.Target with
           | PropertyTarget _ -> true
           | _ -> false)

/// <summary>The number of skeleton sibling nodes occupied by a flattened node.</summary>
let private width node =
    match node with
    | RegionNode _ -> 2
    | _ -> 1

/// <summary>Renders a region row through a backend; the reader returns the row's current item.</summary>
let internal rowRenderer (row: RegionRow) (current: unit -> obj) (backend: Backend) : unit =
    match row with
    | ViewRow -> (unbox<Program.View>(current ())).Invoke backend
    | ArmRow ->
        let arm = unbox<Arm>(current ())
        backend.Template (arm.Definition, arm.Reader.Invoke current)
    | ItemRow (definition, props) -> backend.Template (definition, props.Invoke current)

/// <summary>The value a region item's row reads: the item of an <c>ItemRow</c>, otherwise the value of its pair.</summary>
let private rowValue (row: RegionRow) (item: obj) =
    match row with
    | ItemRow _ -> item
    | ViewRow
    | ArmRow -> KeyedItems.value item

let private serverRow (backend: Backend) (row: RegionRow) (item: obj) =
    let value = rowValue row item
    rowRenderer row (fun () -> value) backend

[<NoEquality; NoComparison>]
type InlinedChild =
    {
        Definition: TemplateComponent
        Props: Func<obj, obj>
        /// <summary>The part whose instance the props reader receives: 0 for the template, k for the k-th inlined child in document order.</summary>
        Owner: int
        /// <summary>The index of the child's first dynamic node.</summary>
        First: int
        /// <summary>The number of dynamic nodes of the child and its inlined descendants.</summary>
        Count: int
    }

/// <summary>The markup of a template's skeleton with its dynamic nodes and inlined children.</summary>
[<NoEquality; NoComparison>]
type SkeletonLayout =
    {
        Markup: string
        /// <summary>Each dynamic node in document order, with its child-index path from the skeleton's root fragment.</summary>
        Targets: (TemplateNode * int array) array
        /// <summary>The part owning each dynamic node: 0 for the template, k for the k-th inlined child in document order.</summary>
        Owners: int array
        RootCount: int
        /// <summary>The inlined children, at every depth, in document order.</summary>
        Children: InlinedChild array
    }

type private WriterState(instance: obj) =
    member val Instance = instance with get, set
    member val Instances = ResizeArray<obj>()
    member val Targets = ResizeArray<TemplateNode * int array>()
    member val Owners = ResizeArray<int>()
    member val Children = ResizeArray<InlinedChild>()
    member val Parts = ResizeArray<int>()
    member val Part = 0 with get, set

let private writeMarkup (append: string -> unit) (mode: MarkupMode) (state: WriterState) (root: TemplateNode) =
    let target node (path: int list) =
        state.Targets.Add (node, Array.ofList (List.rev path))
        state.Owners.Add state.Part

    let enter (definition: TemplateComponent) (props: Func<obj, obj>) =
        match mode with
        | Skeleton ->
            state.Children.Add
                {
                    InlinedChild.Definition = definition
                    Props = props
                    Owner = state.Part
                    First = state.Targets.Count
                    Count = 0
                }

            state.Parts.Add state.Part
            state.Part <- state.Children.Count
        | Server (setup, _, _) ->
            state.Instances.Add state.Instance
            state.Instance <- definition.Setup.Invoke (setup, props.Invoke state.Instance)

    let leave () =
        match mode with
        | Skeleton ->
            let index = state.Part - 1
            let child = state.Children[index]

            state.Children[index] <-
                { child with
                    Count = state.Targets.Count - child.First
                }

            state.Part <- state.Parts[state.Parts.Count - 1]
            state.Parts.RemoveAt (state.Parts.Count - 1)
        | Server _ ->
            state.Instance <- state.Instances[state.Instances.Count - 1]
            state.Instances.RemoveAt (state.Instances.Count - 1)

    let rec writeChildren (children: TemplateNode array) (path: int list) =
        let mutable index = 0
        let mutable previousText = false

        for flat in flatten children do
            match flat with
            | Enter (definition, props) -> enter definition props
            | Leave -> leave ()
            | Item child ->
                let text = isText child

                if text && previousText then
                    append "<!--!-->"
                    index <- index + 1

                writeNode child (index :: path)
                index <- index + width child
                previousText <- text

        index

    and writeNode node path =
        match node with
        | TextNode value -> append (Html.escape false value)
        | TextSlotNode slot ->
            match mode with
            | Skeleton ->
                target node path
                append " "
            | Server _ ->
                let value = unbox<string>(slot.Read.Invoke state.Instance)
                append (Html.escape false (if isNull value then "" else value))
        | FragmentNode _
        | ChildNode _ -> invalidOp "Fragments and inlined children are flattened before writing"
        | ElementNode (tag, attributes, properties, slots, events, children) ->
            if rawText tag then
                match mode with
                | Skeleton when children.Length = 0 && name tag <> "plaintext" -> ()
                | _ -> raise (InvalidOperationException "Raw-text elements require an explicit HTML serialization policy")

            match mode with
            | Skeleton ->
                if
                    properties.Length > 0
                    || slots.Length > 0
                    || events.Length > 0
                then
                    target node path
            | Server (_, rejectBrowser, _) ->
                if
                    rejectBrowser
                    && hasBrowserParts properties slots events
                then
                    invalidOp "Browser capability requires explicit Omit policy"

            append ("<" + name tag)

            for key, value in attributes do
                append (" " + key + "=\"" + Html.escape true value + "\"")

            match mode with
            | Server _ ->
                for slot in slots do
                    match slot.Target with
                    | AttributeTarget key ->
                        match unbox<string option>(slot.Read.Invoke state.Instance) with
                        | Some value -> append (" " + key + "=\"" + Html.escape true value + "\"")
                        | None -> ()
                    | _ -> ()
            | Skeleton -> ()

            append ">"

            if not (isVoid tag) then
                writeChildren children path |> ignore
                append ("</" + name tag + ">")
        | RegionNode region ->
            match mode with
            | Skeleton ->
                target node path
                append "<!--[--><!--]-->"
            | Server (_, _, backend) ->
                let items = Array.copy (region.Items.Invoke state.Instance)
                Projection.ensureUniqueKeys region.Key.Invoke items
                append "<!--[-->"

                for item in items do
                    serverRow backend region.Row item

                append "<!--]-->"

    writeChildren [| root |] []

/// <summary>The skeleton of a template: its markup, its dynamic nodes in document order with their owning parts, its root count and its inlined children.</summary>
/// <remarks>A path lists child indexes from the skeleton's root fragment. A region's path leads to its start marker.</remarks>
let skeletonLayout (root: TemplateNode) : SkeletonLayout =
    let output = StringBuilder ()
    let state = WriterState (null: obj)

    let rootCount =
        writeMarkup (fun value -> output.Append value |> ignore) Skeleton state root

    {
        Markup = output.ToString ()
        Targets = state.Targets.ToArray ()
        Owners = state.Owners.ToArray ()
        RootCount = rootCount
        Children = state.Children.ToArray ()
    }

/// <summary>The skeleton markup of a template, the path of every dynamic node in document order, and the number of root nodes.</summary>
/// <remarks>A path lists child indexes from the skeleton's root fragment. A region's path leads to its start marker.</remarks>
let skeletonMarkup (root: TemplateNode) =
    let layout = skeletonLayout root
    layout.Markup, layout.Targets, layout.RootCount

/// <summary>Writes an instance as server HTML with the skeleton's markers; region rows render through <c>backend</c>.</summary>
/// <remarks>Inlined children's setups receive <c>setup</c>; their cleanups run with the enclosing component's.</remarks>
let renderHtml (definition: TemplateComponent) (backend: Backend) (setup: Setup) (props: obj) (rejectBrowser: bool) (append: string -> unit) =
    let instance = definition.Setup.Invoke (setup, props)

    writeMarkup append (Server (setup, rejectBrowser, backend)) (WriterState instance) definition.Root
    |> ignore

let rec private expandNode (ops: Operations<'view>) (instance: obj) (node: TemplateNode) : 'view =
    match node with
    | TextNode value -> ops.Text value
    | TextSlotNode slot -> ops.Bind (fun left right -> slot.Same.Invoke (box left, box right)) (fun () -> unbox<string>(slot.Read.Invoke instance))
    | FragmentNode children -> ops.Fragment (Array.map (expandNode ops instance) children)
    | ElementNode (tag, attributes, properties, slots, events, children) ->
        let modifiers: ModifierAction array =
            [|
                for key, value in attributes -> Attribute (key, value)
                for write, value in properties -> fun (backend: Backend) -> backend.Property (fun target -> write.Invoke (target, value))
                for slot in slots ->
                    match slot.Target with
                    | AttributeTarget key -> ReactiveAttribute (key, fun () -> unbox<string option>(slot.Read.Invoke instance))
                    | PropertyTarget write -> fun (backend: Backend) -> backend.ReactiveProperty ((fun () -> slot.Read.Invoke instance), write)
                    | TextTarget -> invalidOp "A text slot is a child node"
                for eventName, handler in events -> listenModifier eventName (fun event -> handler.Invoke (instance, event))
            |]

        ops.Element tag modifiers (Array.map (expandNode ops instance) children)
    | RegionNode region ->
        match region.Row with
        | ViewRow ->
            let item = KeyedItems.value ((region.Items.Invoke instance)[0])
            expandRow ops region.Row (fun () -> item)
        | ArmRow -> ops.Switch (fun () -> Array.map unbox<obj * obj> (region.Items.Invoke instance)) (expandRow ops region.Row)
        | ItemRow _ ->
            ops.Keyed
                {
                    Read = fun () -> region.Items.Invoke instance
                    Key = region.Key
                }
                (expandRow ops region.Row)
    | ChildNode (definition, props) -> expandWith ops definition (props.Invoke instance)

/// <summary>Expands a region row through the operations; the reader returns the row's current item.</summary>
and private expandRow (ops: Operations<'view>) (row: RegionRow) (current: unit -> obj) : 'view =
    match row with
    | ViewRow -> ops.Embed (rowRenderer row current)
    | ArmRow ->
        let arm = unbox<Arm>(current ())
        expandWith ops arm.Definition (arm.Reader.Invoke current)
    | ItemRow (definition, props) -> expandWith ops definition (props.Invoke current)

/// <summary>Expands a definition into any view representation through its operations.</summary>
and expandWith (ops: Operations<'view>) (definition: TemplateComponent) (props: obj) : 'view =
    ops.Component (fun context -> expandNode ops (definition.Setup.Invoke (context, props)) definition.Root)

/// <summary>A component whose template is built once per definition; setup runs per instance.</summary>
[<Sealed>]
type Component<'props> internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateComponent(setup, root)

    override this.Expand(backend, props) =
        Program.run (expandWith Program.ops (this :> TemplateComponent) props) backend

    override this.RenderHtml(backend, context, props, rejectBrowser, append) =
        renderHtml (this :> TemplateComponent) backend context props rejectBrowser append

/// <summary>One part of an element in a template for instances of type <c>'inst</c>.</summary>
[<NoEquality; NoComparison>]
type Part<'native, 'inst> =
    internal
    | StaticAttribute of string * string
    | StaticProperty of Func<obj, obj, unit> * obj
    | DynamicPart of Slot
    | EventPart of string * Func<obj, obj, unit>
    | NoPart

/// <summary>A template subtree for instances of type <c>'inst</c>.</summary>
[<NoEquality; NoComparison>]
type TemplateView<'inst> = internal TemplateView of TemplateNode

/// <summary>A reader of one fixed signal or memo of an instance.</summary>
[<NoEquality; NoComparison>]
type Source<'inst, 'value> = internal Source of Func<obj, obj>

let private slot target mode (read: 'inst -> 'value) same =
    {
        Slot.Target = target
        Mode = mode
        Read = Func<obj, obj>(fun instance -> box (read (unbox instance)))
        Same = same
    }

let private sourceSlot target (Source read: Source<'inst, 'value>) same =
    {
        Slot.Target = target
        Mode = DirectMode
        Read = read
        Same = same
    }

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
        | NoPart -> ()

    TemplateView (ElementNode (tag, attributes.ToArray (), properties.ToArray (), slots.ToArray (), events.ToArray (), Array.map nodeOf children))

/// <summary>Part constructors used by the generated template catalogue.</summary>
module internal TemplateParts =
    let element tag (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        elementNode tag parts children

    let attribute name (value: string) : Part<'native, 'inst> =
        validateAttribute name
        StaticAttribute (name, value)

    let optionalAttribute name (value: string option) : Part<'native, 'inst> =
        validateAttribute name

        match value with
        | Some value -> StaticAttribute (name, value)
        | None -> NoPart

    let readAttribute name (read: 'inst -> string option) : Part<'native, 'inst> =
        validateAttribute name
        DynamicPart (slot (AttributeTarget name) RenderMode read primitiveSame)

    let sourceAttribute name (source: Source<'inst, string option>) : Part<'native, 'inst> =
        validateAttribute name
        DynamicPart (sourceSlot (AttributeTarget name) source primitiveSame)

    let mapSource (map: 'value -> 'next) (Source read: Source<'inst, 'value>) : Source<'inst, 'next> =
        Source (Func<obj, obj>(fun instance -> box (map (unbox<'value>(read.Invoke instance)))))

    /// <summary>Wraps a string as an attribute value; <c>null</c> gives <c>None</c>.</summary>
    let present (value: string) =
        if isNull value then None else Some value

    let presence enabled =
        if enabled then Some "" else None

    let ariaText enabled =
        if enabled then "true" else "false"

    let ariaOption (value: bool option) =
        Option.map ariaText value

    let property (write: 'native -> 'value -> unit) (value: 'value) : Part<'native, 'inst> =
        StaticProperty (writer write, box value)

    let readProperty (write: 'native -> 'value -> unit) (read: 'inst -> 'value) : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (writer write)) RenderMode read primitiveSame)

    let sourceProperty (write: 'native -> 'value -> unit) (source: Source<'inst, 'value>) : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (writer write)) source primitiveSame)

    let event name (handler: 'inst -> 'event -> unit) : Part<'native, 'inst> =
        EventPart (name, Func<obj, obj, unit>(fun instance event -> handler (unbox instance) (unbox event)))

/// <summary>Attribute parts: a value is static, a reader or source is dynamic.</summary>
[<Sealed>]
type AttributeBuilder<'inst> internal () =
    member _.attribute(name: string, value: string) : Part<'native, 'inst> =
        TemplateParts.attribute name value

    member _.attribute(name: string, read: 'inst -> string option) : Part<'native, 'inst> =
        TemplateParts.readAttribute name read

    member _.attribute(name: string, source: Source<'inst, string option>) : Part<'native, 'inst> =
        TemplateParts.sourceAttribute name source

/// <summary>Property parts: a value is written once per instance, a reader or source is dynamic.</summary>
/// <remarks>A writer sets properties and leaves the element's child nodes intact: nodes moved into a template clone by a writer bypass the mount's ownership checks.</remarks>
[<Sealed>]
type PropertyBuilder<'inst> internal () =
    member _.propertyStatic(write: 'native -> 'value -> unit, value: 'value) : Part<'native, 'inst> =
        TemplateParts.property write value

    member _.propertyWith(write: 'native -> 'value -> unit, read: 'inst -> 'value, same: Func<obj, obj, bool>) : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (writer write)) RenderMode read same)

    member _.propertyFrom(write: 'native -> 'value -> unit, source: Source<'inst, 'value>, same: Func<obj, obj, bool>) : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (writer write)) source same)

/// <summary>Event parts: one shared handler per template, given the instance and the event.</summary>
[<Sealed>]
type EventBuilder<'inst> internal () =
    member _.on(eventName: string, handler: 'inst -> Event -> unit) : Part<'native, 'inst> =
        TemplateParts.event eventName handler

/// <summary>Builds a template for instances of type <c>'inst</c>; dynamic parts receive the instance as their argument.</summary>
[<Sealed>]
type TemplateBuilder<'inst> internal () =
    let attrs = AttributeBuilder<'inst>()
    let props = PropertyBuilder<'inst>()
    let events = EventBuilder<'inst>()

    member _.Attrs = attrs
    member _.Props = props
    member _.Events = events

    member _.element (tag: Tag) (parts: Part<'native, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        elementNode tag parts children

    member _.fragment(children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateView (FragmentNode (Array.map nodeOf children))

    member _.text(value: string) : TemplateView<'inst> =
        TemplateView (TextNode value)

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

    /// <summary>A view hole: renders the view read from the instance as it is created, between region markers.</summary>
    /// <remarks>The reader runs once per instance, untracked.</remarks>
    member _.view(read: 'inst -> Program.View) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items = Func<obj, obj array>(fun instance -> [| box ((null: obj), box (read (unbox instance))) |])
                    Key = KeyedItems.pairKey
                    Row = ViewRow
                }
        )

    /// <summary>An arm that renders <c>definition</c>; its props read the payload of the arm currently selected.</summary>
    member _.arm (definition: Component<unit -> 'payload>) (payload: 'payload) : Arm =
        let template = definition :> TemplateComponent

        if isNull template.ArmReader then
            template.ArmReader <- Func<(unit -> obj), obj>(fun current -> box (fun () -> unbox<'payload> (unbox<Arm>(current ())).Payload))

        {
            Arm.Definition = template
            Payload = box payload
            Reader = template.ArmReader
        }

    /// <summary>Renders the arm that <c>arms</c> selects for the instance's union value, between region markers.</summary>
    /// <remarks>Arms are compared by key under <c>=</c>; a <c>matchWith</c> key is the arm's definition. Selecting another definition remounts, and selecting the same definition updates the payload read by its props. Writing <c>arms</c> as a <c>function</c> match lets the compiler check that every case has an arm. A remount runs the previous arm's cleanups and removes its nodes before the new arm is set up.</remarks>
    member _.matchWith (read: 'inst -> 'union) (arms: 'union -> Arm) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items =
                        Func<obj, obj array>(fun instance ->
                            let arm = arms (read (unbox instance))
                            [| box (box arm.Definition, box arm) |])
                    Key = KeyedItems.pairKey
                    Row = ArmRow
                }
        )

    /// <summary>Renders the arm selected for the instance's key, between region markers; a key change under <c>=</c> or a change of the selected definition remounts the arm.</summary>
    /// <remarks>An equal key with the same definition keeps the arm and updates the payload read by its props. A remount runs the previous arm's cleanups and removes its nodes before the new arm is set up.</remarks>
    member _.branch<'key when 'key: equality> (key: 'inst -> 'key) (arms: 'key -> Arm) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items =
                        Func<obj, obj array>(fun instance ->
                            let current = key (unbox instance)
                            let arm = arms current
                            [| box (box (box current, arm.Definition), box arm) |])
                    Key = KeyedItems.pairKey
                    Row = ArmRow
                }
        )

    /// <summary>Renders one <c>row</c> instance per item, between region markers; items with equal keys under <c>=</c> keep their row, whose props read the current item.</summary>
    /// <remarks>Keys must be pure. An item keeps its key while it is in the list; replace the item to change its key.
    /// Duplicate keys fail before the rows change.</remarks>
    member _.each<'item, 'key when 'key: equality>
        (key: 'item -> 'key)
        (items: 'inst -> 'item array)
        (row: Component<unit -> 'item>)
        : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Items = Func<obj, obj array>(fun instance -> KeyedItems.boxAll (items (unbox instance)))
                    Key = Func<obj, obj>(fun item -> box (key (unbox<'item> item)))
                    Row = ItemRow (row :> TemplateComponent, Func<(unit -> obj), obj>(fun current -> box (fun () -> unbox<'item>(current ()))))
                }
        )

    /// <summary>Inlines a child template: its markup joins this template's skeleton, and its setup runs once per instance with props read untracked from the instance.</summary>
    member _.render (definition: Component<'props>) (props: 'inst -> 'props) : TemplateView<'inst> =
        TemplateView (ChildNode (definition :> TemplateComponent, Func<obj, obj>(fun instance -> box (props (unbox instance)))))

module Component =
    /// <summary>Defines a component from per-instance setup and a template built once.</summary>
    /// <remarks><c>setup</c> runs untracked for each instance and returns the instance that dynamic parts receive.</remarks>
    let define (setup: Setup -> 'props -> 'inst) (build: TemplateBuilder<'inst> -> TemplateView<'inst>) : Component<'props> =
        let (TemplateView root) = build (TemplateBuilder<'inst>())
        Component<'props>(Func<Setup, obj, obj>(fun context props -> box (setup context (unbox props))), root)

    /// <summary>Renders an instance as a Program view; the DOM backend materialises the compiled template.</summary>
    let render (definition: Component<'props>) (props: 'props) : Program.View =
        Program.View (fun backend -> backend.Template (definition, box props))

    /// <summary>Renders an instance in any representation through the default expansion.</summary>
    let renderWith (ops: Operations<'view>) (definition: Component<'props>) (props: 'props) : 'view =
        expandWith ops (definition :> TemplateComponent) (box props)
