# Component templates (dynamic structure) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let component templates contain dynamic structure (view holes, `matchWith`, `branch`, `each` and inlined child templates) that renders through the compiled DOM path, the portable markup writer and the default expansion with the same semantics.

**Architecture:** The portable template tree gains a `RegionNode` (keyed items read from the instance, rendered one row per item between `<!--[-->` and `<!--]-->` markers) and a `ChildNode` (a child definition whose markup is inlined into the parent's skeleton). The DOM compiler records each region's start marker as a dynamic node and each inlined child's node range; materialisation binds every inlined child to its own state array over the parent's cloned nodes, appends the clone, then mounts each region through the mount backend's existing keyed path, generalised to take existing markers. Server HTML writes markers and renders region rows through the HTML backend itself; the default expansion maps regions to `Keyed` and view holes to a new `Operations.Embed`.

**Tech Stack:** F# compiled by Fable 5 (browser) and .NET (portable checks), jsdom and Playwright Chromium tests, the framework benchmark harness against Solid 2.0.0-rc.14.

**Spec:** `docs/superpowers/specs/2026-10-13-component-templates-design.md`

This is plan 2b. Plan 2a (`docs/superpowers/plans/2026-10-13-component-templates-core.md`) built the core; its clarifications still apply: builder-parameter authoring (`fun t -> t.div ...`), static properties written at materialisation, and queued commit mode rendering templates through the default expansion. `from` slots stay in the render effect (`docs/research/2026-10-14-direct-binding-spike.md`); this plan adds no direct bindings.

## Spec clarifications in this plan

- **Builder members.** The spec's `T.render`, `T.view`, `T.matchWith`, `T.branch` and `T.each` are members of the template builder: `t.render`, `t.view`, `t.matchWith`, `t.branch`, `t.each`, plus `t.arm` to build an arm.
- **Arm and row props are readers.** A `matchWith` or `branch` arm is a `Component<unit -> 'payload>` and an `each` row a `Component<unit -> 'item>`. The reader returns the current payload or item, so a payload change within the same arm, or an item change within a retained row, reaches the mounted instance without remounting it.
- **`matchWith` identifies an arm by its definition, not by the union case tag.** Fable erases `option` (`None` is `undefined`), so no portable tag reader exists. A change that selects a different definition remounts; a change that selects the same definition, whether the same case or another case mapped to that definition, updates the arm's payload. The compiler checks exhaustiveness of the `function` expression that maps cases to arms (warning FS0025 for a missing case).
- **`branch` arms** are `'key -> Arm`; the region remounts its arm whenever the key changes under `=`, including when both keys select the same definition.
- **View holes** read their view once per instance, untracked, when the instance is created. Reactive switching is `branch` or `matchWith`.
- **`each` items are arrays**: `t.each key (fun r -> items) row` with `items: 'inst -> 'item array` and keys compared with `=`.
- **Regions mount after the clone is appended** (the spec lists region mounting before the append). A region whose markers are roots of a multi-root skeleton must have its markers registered as one span of the mount's range before rows are inserted between them.
- **No `<!--!-->` separator beside a region.** Region markers are comment nodes, which already keep adjacent text nodes apart; separators stay between adjacent text and text slots, including across an inlined child's boundary.
- **Inlined children.** A child's props are read once per instance, untracked, from the instance of the template that inlines it. Every inlined descendant's regions and children are hoisted into the root template's compiled form, in document order, each recording the part (template or inlined child) whose instance it reads. Server HTML runs a child's setup with the enclosing setup context, as the DOM path registers child cleanups on the enclosing owner.
- **Server HTML and expansion parity.** `Html.render` of `Component.render` writes the markup writer's output, with markers, and renders region rows through the HTML backend. The default expansion (`Component.renderWith`, `Tree.ops`, queued DOM mounts) has no comment operation; its HTML equals the template's once comments are removed. This expansion is the `createComponent` twin of every structure feature.
- **`Operations` gains `Embed: (Backend -> unit) -> 'view`**, so the default expansion can place a view hole's `Program.View` in any representation.
- **Browser interpreter.** `Browser.fs` renders view holes and inlined children through the expansion; its existing `Keyed` member rejects keyed lists, so `matchWith`, `branch` and `each` raise its `InvalidOperationException` there.
- **One mount path for every region kind.** View holes, `matchWith`, `branch` and `each` all mount through the mount backend's keyed path. An arm switch sets up the new arm before the previous arm's cleanups run, the keyed path's create-then-retire order; Solid 2 disposes the previous arm first.
- **Per-instance cost of structure.** `materialise` iterates the compiled form's children and regions arrays for every instance, including templates whose arrays are empty.

## Global Constraints

- Work on `poc/fable-ranvier-dom` in `C:\Users\shaya\RiderProjects\Ranvier-fable-ranvier-dom`; every commit message is one conventional-commit summary line, no body, no AI co-author trailer; prefix shell commands with `rtk`.
- Comments follow `.claude/rules/comments.md` (main repo `C:\Users\shaya\RiderProjects\Ranvier\.claude\rules\comments.md`); `///` docs follow the `fsharp-xml-docs` skill; run `dotnet fantomas` on changed F# files before committing.
- `Template.fs` compiles and runs on .NET: `Emit`/`jsNative` only behind `#if FABLE_COMPILER`.
- Performance: the template hot path must not gain per-instance or per-run closures beyond those a feature needs; verify emitted JS (`examples/Fable.Ranvier.Playground/generated-tests/experiments/Ranvier.View.Foundation/TemplateDom.js` and `Mounting.js`) after tasks touching `TemplateDom`/`Mounting`; templates without dynamic structure must cost exactly what they cost now (re-run `node benchmarks/frameworks/run.mjs --build --template-comparison` at the end and compare with `docs/research/2026-10-14-component-templates.md`).
- Solid comparisons are against Solid 2 (`.claude/rules/framework-comparisons.md`).
- Commands: from `examples/Fable.Ranvier.Playground` `npm run test:dom`, `npm run test:browser`; from the repository root `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`.
- Server HTML: every new structure renders through the portable markup writer (`Html.render`) with region markers, and the default expansion (`Backend.Template` default) renders it on non-DOM backends (`Tree.ops`, `Html`) with identical semantics.

## Review Focus

- **A `matchWith` whose union changes case and payload within one flush.** Expect only the final value to count: switching away and back inside one batch keeps the mounted arm and shows the latest payload; a payload change followed by a switch disposes the old arm once and never renders the intermediate payload. Pinned in Task 3 ("a match changed twice in one flush settles on its final case").
- **`each` with duplicate keys.** Expect the mount to fail with `ArgumentException` and leave the host untouched; an update with duplicate keys to be reported once, keep the rows, and let a later valid update recover; server HTML to raise `ArgumentException`. Pinned in Task 5 ("duplicate each keys fail the mount, are reported on update and keep the rows") and its portable check.
- **A region with no content.** Expect an empty `each` (inside an element or at the template's root) to keep exactly its two markers, rows to fill in between them later, emptying to restore the markers alone, and disposal to leave the host empty; an empty view hole renders no elements or text between its markers. Pinned in Task 5 ("an empty each region keeps its markers, fills and empties") and Task 2 ("an empty view hole keeps its markers").
- **Regions nested inside `each` rows.** Expect a `matchWith` inside a row to follow its row's item, keep its arm when only the payload changes, move with the row on reorder, and be disposed with the row. Pinned in Task 5 ("a matchWith inside each rows follows, moves and leaves with its row").
- **Disposal of the instance while a region's content is mid-switch.** Expect disposing the mount from inside the incoming arm's setup to end the write without an exception, run every arm cleanup exactly once, report nothing and leave the host empty. Pinned in Task 3 ("disposing the mount during an arm's setup ends the switch cleanly").

---

### Task 1: Portable view holes and region markers

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Representation.fs` (`TemplateDefinition.RenderHtml`, `Operations`, `Tree`, `Program.ops`, `Html.render`'s `Template` member)
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (type group, file order, markup writer, expansion, `Component<'props>.RenderHtml`, builder `view`)
- Modify: `examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs:146-155` (`modifierTokens` gains `Embed`)
- Test: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: plan 2a's `Template` and `Representation` surface.
- Produces in `Representation`: `Operations<'view>.Embed: (Backend -> unit) -> 'view`; `Tree.ops.Embed`; `Program.ops.Embed`; `TemplateDefinition.RenderHtml: Backend * Setup * obj * bool * (string -> unit) -> unit` (writes through the sink; region rows render through the backend).
- Produces in `Template`: `TemplateNode.RegionNode of Region`; record `Region = { Same: Func<obj, obj, bool>; Items: Func<obj, (obj * obj) array>; Row: RegionRow }`; union `RegionRow = ViewRow` (Tasks 3 and 5 add cases); `TemplateComponent` now declared in the same recursive group as `TemplateNode`; `renderHtml: TemplateComponent -> Backend -> Setup -> obj -> bool -> (string -> unit) -> unit`; `TemplateBuilder<'inst>.view: ('inst -> Program.View) -> TemplateView<'inst>`. File order after this task: slot types, the `TemplateNode`/`Region`/`RegionRow`/`TemplateComponent` group, `primitiveSame`, markup writer, `renderHtml`, expansion, `Component<'props>`, builder block, `module Component`. Later tasks rely on the builder block following `Component<'props>`.

- [ ] **Step 1: Write the failing portable checks**

Append to `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, after `printfn "PASS template portable rendering"`:

```fsharp
let stripMarkers (html: string) =
    System.Text.RegularExpressions.Regex.Replace (html, "<!--.*?-->", "")

type HoleProps = { Caption: string; Body: Program.View }

let holed =
    Component.define (fun _ (props: HoleProps) -> props) (fun t ->
        t.div [| t.Attrs.className "card" |] [| t.text (fun r -> r.Caption); t.view (fun r -> r.Body) |])

let holeProps =
    {
        Caption = "A"
        Body = Program.element (HtmlTag "em") [||] [| Program.text "<b>" |]
    }

// A view hole renders between region markers; text beside a marker has no separator.
equal "<div class=\"card\">A<!--[--><em>&lt;b&gt;</em><!--]--></div>" (render Omit (Component.render holed holeProps))

// The default expansion renders the same HTML without markers.
equal
    (stripMarkers (render Omit (Component.render holed holeProps)))
    (Html.render Omit (Tree.run (Component.renderWith Tree.ops holed holeProps)))

// An empty view hole keeps its markers.
equal
    "<div class=\"card\">B<!--[--><!--]--></div>"
    (render Omit (Component.render holed { Caption = "B"; Body = Program.fragment [||] }))

let mutable holeRejected = false

try
    render
        Reject
        (Component.render
            holed
            {
                Caption = "C"
                Body = Program.element Button [| listenModifier "click" ignore |] [||]
            })
    |> ignore
with :? System.InvalidOperationException ->
    holeRejected <- true

// A browser-only part inside a view hole follows the Reject policy.
equal true holeRejected

printfn "PASS template view holes"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: build failure, `The type 'TemplateBuilder<_>' does not define the field, constructor or member 'view'`.

- [ ] **Step 3: Extend the representation**

In `experiments/Ranvier.View.Foundation/Representation.fs`, replace

```fsharp
    /// <summary>Renders an instance as HTML markup; a <c>true</c> flag rejects browser-only parts.</summary>
    abstract RenderHtml: Setup * obj * bool -> string
```

with

```fsharp
    /// <summary>Writes an instance's HTML through the sink; region rows render through the backend, and a <c>true</c> flag rejects browser-only parts.</summary>
    abstract RenderHtml: Backend * Setup * obj * bool * (string -> unit) -> unit
```

In `Operations<'view>`, after `Component: (Setup -> 'view) -> 'view`, add:

```fsharp
        /// <summary>Runs backend operations at this position; a template view hole embeds its view through it.</summary>
        Embed: (Backend -> unit) -> 'view
```

In `module Tree`, add the case `| Embed of (Backend -> unit)` after `| Component of (Setup -> View)`; in `run`, after the `Component` arm, add `| Embed build -> build backend`; in `Tree.ops`, after `Component = Component`, add `Embed = Embed`.

In `Program.ops`, after `Component = createComponent`, add:

```fsharp
            Embed = fun build -> View (fun backend -> build backend)
```

In `Html.render`, replace

```fsharp
                member this.Template(definition, props) =
                    this.Component (fun context -> append (definition.RenderHtml (context, props, (policy = Reject))))
```

with

```fsharp
                member this.Template(definition, props) =
                    this.Component (fun context -> definition.RenderHtml (this, context, props, (policy = Reject), append))
```

In `examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs`, in `modifierTokens`, after the `Component` field add:

```fsharp
        Embed = fun _ -> invalidOp "Token extraction requires an element"
```

- [ ] **Step 4: Regroup the template types and reorder `Template.fs`**

In `experiments/Ranvier.View.Foundation/Template.fs`, replace the `TemplateNode` declaration (from `/// <summary>A node of a backend-neutral template tree.</summary>` through `| FragmentNode of TemplateNode array`) with:

```fsharp
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

/// <summary>Dynamic structure of a template: keyed items read from an instance, rendered one row per item.</summary>
and [<NoEquality; NoComparison>] Region =
    {
        /// <summary>Compares two item keys.</summary>
        Same: Func<obj, obj, bool>
        /// <summary>Reads the keyed items from an instance.</summary>
        Items: Func<obj, (obj * obj) array>
        Row: RegionRow
    }

/// <summary>What a region's item is and how its row renders it.</summary>
and [<NoEquality; NoComparison>] RegionRow =
    /// <summary>The item is a <c>Program.View</c>, read once per instance.</summary>
    | ViewRow

/// <summary>A template component definition without its props type.</summary>
and [<AbstractClass>] TemplateComponent internal (setup: Func<Setup, obj, obj>, root: TemplateNode) =
    inherit TemplateDefinition()
    member internal _.Setup = setup
    member internal _.Root = root
```

Delete the old `TemplateComponent` declaration (from `/// <summary>A template component definition without its props type.</summary>` through `member internal _.Root = root`, directly above `renderHtml`).

`Region` also has a `Same` label, so qualify the first label of the two `Slot` record expressions: in `let private slot` and `let private sourceSlot`, change `Target = target` to `Slot.Target = target`.

Cut the block that starts at `/// <summary>One part of an element in a template for instances of type <c>'inst</c>.</summary>` and ends with the last member of `TemplateBuilder<'inst>` (`member _.isolated(view: TemplateView<'inst>)` and its two match arms), and paste it unchanged directly above `module Component =`. The block holds `Part`, `TemplateView`, `Source`, the helpers `slot`, `sourceSlot`, `present`, `someOf`, `writer`, `nodeOf`, `elementNode`, and the four builder types; none of the writer or expansion code uses them.

- [ ] **Step 5: Write regions through the markup writer**

Replace everything from `[<NoEquality; NoComparison>]` above `type private MarkupMode =` through the end of `renderHtml` with:

```fsharp
[<NoEquality; NoComparison>]
type private MarkupMode =
    | Skeleton
    | Server of instance: obj * rejectBrowser: bool * backend: Backend

let private hasBrowserParts properties (slots: Slot array) (events: (string * Func<obj, obj, unit>) array) =
    Array.length properties > 0
    || events.Length > 0
    || slots
       |> Array.exists (fun slot ->
           match slot.Target with
           | PropertyTarget _ -> true
           | _ -> false)

/// <summary>The number of skeleton sibling nodes a flattened node occupies.</summary>
let private width node =
    match node with
    | RegionNode _ -> 2
    | _ -> 1

let private serverRow (backend: Backend) (row: RegionRow) (item: obj) =
    match row with
    | ViewRow -> (unbox<Program.View> item).Invoke backend

let private writeMarkup (append: string -> unit) mode (targets: ResizeArray<TemplateNode * int array>) (root: TemplateNode) =
    let rec writeChildren (children: TemplateNode array) (path: int list) =
        let mutable index = 0
        let mutable previousText = false

        for child in flatten children do
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
                targets.Add (node, Array.ofList (List.rev path))
                append " "
            | Server (instance, _, _) ->
                let value = unbox<string>(slot.Read.Invoke instance)
                append (Html.escape false (if isNull value then "" else value))
        | FragmentNode _ -> invalidOp "Fragments are flattened before writing"
        | ElementNode (tag, attributes, properties, slots, events, children) ->
            if rawText tag then
                raise (InvalidOperationException "Raw-text elements require an explicit HTML serialization policy")

            match mode with
            | Skeleton ->
                if
                    properties.Length > 0
                    || slots.Length > 0
                    || events.Length > 0
                then
                    targets.Add (node, Array.ofList (List.rev path))
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
            | Server (instance, _, _) ->
                for slot in slots do
                    match slot.Target with
                    | AttributeTarget key ->
                        match unbox<string option>(slot.Read.Invoke instance) with
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
                targets.Add (node, Array.ofList (List.rev path))
                append "<!--[--><!--]-->"
            | Server (instance, _, backend) ->
                let items = Array.copy (region.Items.Invoke instance)
                Keyed.validateUniqueKeys (fun left right -> region.Same.Invoke (left, right)) items
                append "<!--[-->"

                for _, item in items do
                    serverRow backend region.Row item

                append "<!--]-->"

    writeChildren [| root |] []

/// <summary>The skeleton markup of a template, the path of every dynamic node in document order, and the number of root nodes.</summary>
/// <remarks>A path lists child indexes from the skeleton's root fragment. A region's path leads to its start marker.</remarks>
let skeletonMarkup (root: TemplateNode) =
    let output = StringBuilder ()
    let targets = ResizeArray ()

    let rootCount =
        writeMarkup (fun value -> output.Append value |> ignore) Skeleton targets root

    output.ToString (), targets.ToArray (), rootCount

/// <summary>Writes an instance as server HTML with the skeleton's markers; region rows render through <c>backend</c>.</summary>
let renderHtml (definition: TemplateComponent) (backend: Backend) (setup: Setup) (props: obj) (rejectBrowser: bool) (append: string -> unit) =
    let instance = definition.Setup.Invoke (setup, props)

    writeMarkup append (Server (instance, rejectBrowser, backend)) (ResizeArray ()) definition.Root
    |> ignore
```

- [ ] **Step 6: Expand view holes, update the HTML override, add the builder member**

Replace `let rec private expandNode` through the end of `expandWith` with:

```fsharp
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
        let _, view = (region.Items.Invoke instance)[0]
        ops.Embed (fun backend -> (unbox<Program.View> view).Invoke backend)

/// <summary>Expands a definition into any view representation through its operations.</summary>
let expandWith (ops: Operations<'view>) (definition: TemplateComponent) (props: obj) : 'view =
    ops.Component (fun context -> expandNode ops (definition.Setup.Invoke (context, props)) definition.Root)
```

In `Component<'props>`, replace the `RenderHtml` override with:

```fsharp
    override this.RenderHtml(backend, context, props, rejectBrowser, append) =
        renderHtml (this :> TemplateComponent) backend context props rejectBrowser append
```

In the moved builder block, directly above `/// <summary>Attribute parts: a value is static, a reader or source is dynamic.</summary>`, add:

```fsharp
let private anySame = Func<obj, obj, bool>(fun _ _ -> true)
```

At the end of `TemplateBuilder<'inst>`, after the second `isolated` member, add:

```fsharp
    /// <summary>A view hole: renders the view read from the instance as it is created, between region markers.</summary>
    /// <remarks>The reader runs once per instance, untracked.</remarks>
    member _.view(read: 'inst -> Program.View) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Region.Same = anySame
                    Items = Func<obj, (obj * obj) array>(fun instance -> [| (null: obj), box (read (unbox instance)) |])
                    Row = ViewRow
                }
        )
```

- [ ] **Step 7: Run the portable checks to verify they pass**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every earlier `PASS` line, then `PASS template portable rendering` and `PASS template view holes`.

- [ ] **Step 8: Add the Fable server-markup check**

In `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, directly above `/// <summary>Checks template compilation and DOM materialisation.</summary>`, add:

```fsharp
type Holed = { Caption: string; Body: Program.View }

let private holed =
    Component.define (fun _ (props: Holed) -> props) (fun t ->
        t.div [| t.Attrs.className "card" |] [| t.text (fun r -> r.Caption); t.view (fun r -> r.Body) |])

let private holeMarkup () =
    let body = Program.element (HtmlTag "em") [||] [| Program.text "<b>" |]

    equal
        "<div class=\"card\">A<!--[--><em>&lt;b&gt;</em><!--]--></div>"
        (Html.render Omit (Program.run (Component.render holed { Caption = "A"; Body = body })))
```

and append to `cases`:

```fsharp
        "view hole server markup under Fable matches .NET", holeMarkup
```

- [ ] **Step 9: Run the DOM suite**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass, including `view hole server markup under Fable matches .NET`.

- [ ] **Step 10: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Representation.fs experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation/Representation.fs experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "feat(view): render template view holes between region markers on every backend"
```

---

### Task 2: DOM regions at skeleton markers

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`CompiledRegion`, `Compiled.Regions`, `isRegionStart`, `validateParsed`, `readsOnce`, `renderRow`, `compile`, `MountServices.Region`, `materialise`)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`RegionPlacement`, `OwnedRange.SpanRegion`, `OwnedRange.Placement`, `OwnedRange.Attach`, `MountState.Relocate`, `MountState.Keyed`/`KeyedRegion`, the `MountServices` implementation)
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `RegionNode`, `Region`, `RegionRow.ViewRow` (Task 1).
- Produces in `TemplateDom`: record `CompiledRegion = { Marker: int; RootLevel: bool; Same: obj -> obj -> bool; Items: Func<obj, (obj * obj) array>; Once: bool; Render: (unit -> obj) -> Backend -> unit }`; `Compiled.Regions: CompiledRegion array`; private `readsOnce: RegionRow -> bool` and `renderRow: RegionRow -> ((unit -> obj) -> Backend -> unit)`, extended by Tasks 3 and 5; `MountServices.Region: Node * Node * bool * (obj -> obj -> bool) * (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit` (start marker, end marker, markers are clone roots, key comparer, items reader, row renderer).
- Produces in `Mounting`: `OwnedRange.SpanRegion(start, finish, validate, relocate)`, `OwnedRange.Placement: Node`; `MountState.KeyedRegion` (private), the body of the former `Keyed` override over given markers.

How a region anchor becomes an insertion point: the start marker is a collected dynamic node of the clone; its `nextSibling` in the fresh clone is the end marker. After the clone is appended, `MountServices.Region` hands both markers to `KeyedRegion`, which mounts each item as a nested `MountState` row (its own scope, a child of the mount's root scope, and its own owner) and moves the rows before the end marker, as the keyed path already does. Placement differs only in how the markers join the mount's range: authored lists at the range's top level still call `AddRegion`; markers that are roots of a multi-root clone appended at the range's top level were added as two root entries by `Append`, and `SpanRegion` merges them into one entry spanning the rows; markers inside an element need no range entry. The template instance has no owner: each region owns its rows through its region scope and owner, created under the enclosing owner, and is disposed with the enclosing mount, branch arm or keyed row.

Rows of a region at the range's top level first delegate events at the range's staging fragment. `Attach` now relocates its entries after inserting a staged range, and `MountState.Relocate` moves a row's delegation to its new parent, so their delegated events dispatch once the mount is attached. The same change fixes authored top-level keyed lists.

- [ ] **Step 1: Write the failing DOM tests**

In `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, add directly after `holeMarkup`:

```fsharp
[<Emit("$0.innerHTML.replace(/<!--.*?-->/g, '')")>]
let private withoutComments (node: HTMLElement) : string = jsNative

[<Emit("Array.from($0.childNodes).filter(n => n.nodeType === 8 && (n.data === '[' || n.data === ']')).length")>]
let private regionMarkers (node: Node) : int = jsNative

let private rootHole =
    Component.define (fun _ (body: Program.View) -> body) (fun t -> t.fragment [| t.view id |])

let private viewHole () =
    use graph = new Graph ()
    let label = graph.Run (fun () -> Api.createSignal "x")
    let mutable cleaned = 0

    let body =
        Program.createComponent (fun context ->
            context.OnCleanup (fun () -> cleaned <- cleaned + 1)
            Program.element (HtmlTag "em") [||] [| Program.bind (=) (fun () -> label.Value) |])

    let host = host ()
    let mounted = mount graph host (Component.render holed { Caption = "A"; Body = body })
    let div = host.querySelector "div" |> unbox<HTMLElement>
    equal ("<div class=\"card\">A<em>x</em></div>", 2) (withoutComments host, regionMarkers div)
    graph.Run (fun () -> label.Value <- "y")
    equal "Ay" host.textContent
    mounted.Dispose ()
    equal (1, 0.) (cleaned, host.childNodes.length)

let private emptyHole () =
    use graph = new Graph ()
    let host = host ()

    let mounted =
        mount graph host (Component.render holed { Caption = "E"; Body = Program.fragment [||] })

    let div = host.querySelector "div" |> unbox<HTMLElement>
    equal ("<div class=\"card\">E</div>", 2) (withoutComments host, regionMarkers div)
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private viewReadOnce () =
    use graph = new Graph ()
    let choice = graph.Run (fun () -> Api.createSignal "first")
    let mutable reads = 0

    let once =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [||]
                [|
                    t.view (fun _ ->
                        reads <- reads + 1
                        Program.text choice.Value)
                |])

    let host = host ()
    let mounted = mount graph host (Component.render once ())
    graph.Run (fun () -> choice.Value <- "second")
    equal ("first", 1) (host.textContent, reads)
    mounted.Dispose ()

let private failingHole () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<span>keep</span>"
    let mutable failed = false

    try
        mount graph host (Component.render holed { Caption = "F"; Body = Program.createComponent (fun _ -> failwith "view") })
        |> ignore
    with error ->
        failed <- error.Message = "view"

    equal (true, "<span>keep</span>") (failed, host.innerHTML)

let private rootHoleClicks () =
    use graph = new Graph ()
    let clicks = ResizeArray<string>()

    let body =
        H.Program.button [| H.Events.onClick (fun _ -> clicks.Add "click") |] [||]

    let first, second = host (), host ()
    let mounted = mount graph first (Component.render rootHole body)
    let button = first.querySelector "button" |> unbox<HTMLButtonElement>
    button.click ()
    mounted.MoveBefore (second, None)
    button.click ()
    equal (2, 0., 1.) (clicks.Count, first.childNodes.length, second.children.length)
    mounted.Dispose ()
    button.click ()
    equal (2, 0.) (clicks.Count, second.childNodes.length)

let private holeFallbacks () =
    use graph = new Graph ()
    let queuedHost, browserHost = host (), host ()

    let queued =
        Mounting.mountWith (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)) graph None ignore queuedHost None (fun context ->
            Program.run (Component.render holed { Caption = "Q"; Body = Program.text "v" }) context.Backend)

    queued.Flush ()

    let dispose =
        Browser.mount graph browserHost ignore None (Program.run (Component.render holed { Caption = "B"; Body = Program.text "v" }))

    equal ("Qv", "Bv") (queuedHost.textContent, browserHost.textContent)
    queued.Dispose ()
    dispose ()
    equal (0., 0.) (queuedHost.childNodes.length, browserHost.childNodes.length)
```

Append to `cases`:

```fsharp
        "a view hole mounts its view between region markers and disposes it", viewHole
        "an empty view hole keeps its markers", emptyHole
        "a view hole reads its view once per instance", viewReadOnce
        "a failing view hole rolls back the mount", failingHole
        "a root-level view hole dispatches clicks before and after a move", rootHoleClicks
        "queued mounts and the Browser interpreter render view holes through expansion", holeFallbacks
```

- [ ] **Step 2: Run them to verify they fail**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: the five synchronous view-hole tests FAIL with `The HTML parser restructured the template markup: the dynamic node at path ... should be a text node but is #comment` (the compiler treats the start marker as a text slot); `queued mounts and the Browser interpreter render view holes through expansion` passes.

- [ ] **Step 3: Compile regions**

In `experiments/Ranvier.View.Foundation/TemplateDom.fs`, directly above `/// <summary>A definition compiled for the DOM`, add:

```fsharp
/// <summary>A region of a compiled template.</summary>
[<NoEquality; NoComparison>]
type CompiledRegion =
    {
        /// <summary>The node index of the region's start marker.</summary>
        Marker: int
        /// <summary>True when the markers are roots of a multi-root skeleton.</summary>
        RootLevel: bool
        Same: obj -> obj -> bool
        Items: Func<obj, (obj * obj) array>
        /// <summary>True when the items are read once, as the instance is created.</summary>
        Once: bool
        Render: (unit -> obj) -> Backend -> unit
    }
```

In `Compiled`, after `Dispatch: Func<obj array, Node, obj, unit>`, add `Regions: CompiledRegion array`.

Directly above `let private validateParsed`, add:

```fsharp
[<Emit("$0 != null && $0.nodeType === 8 && $0.data === '[' && $0.nextSibling != null && $0.nextSibling.nodeType === 8 && $0.nextSibling.data === ']'")>]
let private isRegionStart (node: Node) : bool = jsNative
```

In `validateParsed`, in the `match target with`, directly above `| _ -> "a text node", ...`, add:

```fsharp
                | RegionNode _ -> "a region marker pair", isRegionStart cursor
```

Directly above `/// <summary>Compiles a definition once`, add:

```fsharp
let private readsOnce (row: RegionRow) =
    match row with
    | ViewRow -> true

let private renderRow (row: RegionRow) : (unit -> obj) -> Backend -> unit =
    match row with
    | ViewRow -> fun current backend -> (unbox<Program.View> (current ())).Invoke backend
```

In `compile`, directly above the record expression that begins `{ Skeleton =`, add:

```fsharp
    let regions =
        [|
            for index in 0 .. targets.Length - 1 do
                match targets[index] with
                | RegionNode region, path ->
                    yield
                        {
                            CompiledRegion.Marker = index
                            RootLevel = rootCount > 1 && path.Length = 1
                            Same = fun left right -> region.Same.Invoke (left, right)
                            Items = region.Items
                            Once = readsOnce region.Row
                            Render = renderRow region.Row
                        }
                | _ -> ()
        |]
```

and add `Regions = regions` as the last field of that record.

- [ ] **Step 4: Mount regions after the append**

In `MountServices`, after `abstract ClearFailure: unit -> unit`, add:

```fsharp
    /// <summary>Mounts keyed rows between a region's start and end markers; <c>true</c> marks markers that are roots of the appended clone.</summary>
    abstract Region: Node * Node * bool * (obj -> obj -> bool) * (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit
```

At the end of `materialise`, after the `if compiled.RootCount = 1 then ... else ...` append block, add:

```fsharp
    let regions = compiled.Regions

    for i in 0 .. regions.Length - 1 do
        let region = at regions i
        let start: Node = unbox (at state (StateNodes + region.Marker))
        let finish: Node = unbox start.nextSibling
        let items = region.Items

        let read =
            if region.Once then
                let snapshot = items.Invoke instance
                fun () -> snapshot
            else
                fun () -> items.Invoke instance

        services.Region (start, finish, region.RootLevel, region.Same, read, region.Render)
```

Change the doc comment of `materialise` to:

```fsharp
/// <summary>Materialises one instance: runs setup, clones the skeleton, binds its slots and events, appends it and mounts its regions.</summary>
```

- [ ] **Step 5: Generalise the keyed path in `Mounting.fs`**

Directly above `exception private KeyedDisposed`, add:

```fsharp
/// <summary>How a keyed region's markers join the mount's range.</summary>
type private RegionPlacement =
    /// <summary>Markers created for an authored list at the range's top level.</summary>
    | NewRegion
    /// <summary>Markers already added as adjacent roots of the range.</summary>
    | RootRegion
    /// <summary>Markers inside an element.</summary>
    | NestedRegion
```

In `OwnedRange`, directly after the `AddRegion` member, add:

```fsharp
    /// <summary>Joins a region's start and end markers, adjacent roots of the range, into one entry spanning the region's rows.</summary>
    member _.SpanRegion(start: Node, finish: Node, validate: unit -> unit, relocate: unit -> unit) =
        let mutable index = entries.Count - 2

        while index > 1
              && not (same (at entries index).Node finish) do
            index <- index - 1

        if
            index < 2
            || not (same (at entries (index - 1)).Node start)
        then
            invalid "Region markers must be adjacent roots of the range"

        entries[index - 1] <-
            {
                Node = start
                EndNode = finish
                Validate = validate
                Relocate = relocate
                Restore = None
            }

        entries.RemoveAt index

    /// <summary>The parent of the range's entries.</summary>
    member _.Placement: Node = placement
```

In `OwnedRange.Attach`, replace

```fsharp
        if staged then
            insert parent staging before
        else
```

with

```fsharp
        if staged then
            insert parent staging before

            for index in 0 .. entries.Count - 1 do
                (at entries index).Relocate ()
        else
```

In `MountState`, replace

```fsharp
    member _.Relocate() : unit =
        range.Relocate ()
```

with

```fsharp
    member _.Relocate() : unit =
        range.Relocate ()

        if
            not (isNull delegatedNames)
            && not (same range.Placement delegatedHost)
        then
            releaseDelegation ()
            delegateAt range.Placement
```

Replace the head of the `Keyed` override:

```fsharp
    override _.Keyed(equals, read, render) =
        ensureSetup ()

        let regionStart, regionEnd =
            comment "ranvier:list:start", comment "ranvier:list:end"

        if not (isNull (box parent)) then
            range.AppendGenerated (parent, regionStart)
            range.AppendGenerated (parent, regionEnd)

        let regionScope = root.Child (report, ignore)
```

with:

```fsharp
    override this.Keyed(equals, read, render) =
        ensureSetup ()

        let regionStart, regionEnd =
            comment "ranvier:list:start", comment "ranvier:list:end"

        if isNull (box parent) then
            this.KeyedRegion (regionStart, regionEnd, NewRegion, equals, read, render)
        else
            range.AppendGenerated (parent, regionStart)
            range.AppendGenerated (parent, regionEnd)
            this.KeyedRegion (regionStart, regionEnd, NestedRegion, equals, read, render)

    /// <summary>Mounts one nested mount per keyed item between two boundary nodes and reconciles them as the items change.</summary>
    member private _.KeyedRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            equals: obj -> obj -> bool,
            read: unit -> (obj * obj) array,
            render: (unit -> obj) -> Backend -> unit
        ) =
        let regionScope = root.Child (report, ignore)
```

In the same body, replace

```fsharp
        if isNull (box parent) then
            range.AddRegion (
                regionStart,
                regionEnd,
                (fun () -> validateRegion (getRegionParent ())),
                (fun () ->
                    for entry in entries do
                        entry.Mount.Relocate ())
            )
```

with:

```fsharp
        match placement with
        | NewRegion ->
            range.AddRegion (
                regionStart,
                regionEnd,
                (fun () -> validateRegion (getRegionParent ())),
                (fun () ->
                    for entry in entries do
                        entry.Mount.Relocate ())
            )
        | RootRegion ->
            range.SpanRegion (
                regionStart,
                regionEnd,
                (fun () -> validateRegion (getRegionParent ())),
                (fun () ->
                    for entry in entries do
                        entry.Mount.Relocate ())
            )
        | NestedRegion -> ()
```

The rest of the former `Keyed` body is unchanged and now forms the end of `KeyedRegion`.

In `interface TemplateDom.MountServices with`, after the `ClearFailure` member, add:

```fsharp
        member this.Region(start, finish, rootLevel, equals, read, render) =
            ensureSetup ()

            let placement =
                if rootLevel && isNull (box parent) then
                    RootRegion
                else
                    NestedRegion

            this.KeyedRegion (start, finish, placement, equals, read, render)
```

- [ ] **Step 6: Run the DOM suite to verify it passes**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass, including the six view-hole tests and the existing `DelegationChecks` and `RangeChecks` suites.

- [ ] **Step 7: Run the browser suite**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:browser`
Expected: all Chromium tests pass.

- [ ] **Step 8: Verify the emitted JavaScript**

Run: `rtk grep -n "export function materialise" -A 120 examples/Fable.Ranvier.Playground/generated-tests/experiments/Ranvier.View.Foundation/TemplateDom.js`
Expected: the body before the regions loop matches the body at `19c8a8f` line for line; the regions loop holds the only new arrow functions (the `read` arrow, and the `snapshot` arrow for a view hole); no `curry`, `uncurry` or `partialApply` call appears in `materialise`, `renderRow` or `compile`.

Run: `rtk grep -n "Keyed(\|KeyedRegion\|SpanRegion" examples/Fable.Ranvier.Playground/generated-tests/experiments/Ranvier.View.Foundation/Mounting.js`
Expected: `Keyed` calls `KeyedRegion` directly; `KeyedRegion` creates its `validate`/`relocate` arrows only inside the `NewRegion` and `RootRegion` branches.

- [ ] **Step 9: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "feat(dom): mount template regions at their skeleton markers through the keyed path"
```

---

### Task 3: `matchWith` arms

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (`Arm`, `RegionRow.ArmRow`, `serverRow`, expansion, builder `arm` and `matchWith`)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`readsOnce`, `renderRow`)
- Test: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `Region`, `RegionRow`, `serverRow`, `expandNode`/`expandWith`, `TemplateDom.readsOnce`/`renderRow` (Tasks 1 and 2).
- Produces in `Template`: record `Arm = internal { Definition: TemplateComponent; Payload: obj; Reader: Func<(unit -> obj), obj> }` (the reader turns a row's item reader into the arm's props: a `unit -> 'payload` reading the current arm's payload); case `RegionRow.ArmRow`; private `expandRow: Operations<'view> -> RegionRow -> (unit -> obj) -> 'view` in a recursive group with `expandNode` and `expandWith`; `TemplateBuilder<'inst>.arm: Component<unit -> 'payload> -> 'payload -> Arm`; `TemplateBuilder<'inst>.matchWith: ('inst -> 'union) -> ('union -> Arm) -> TemplateView<'inst>`.

- [ ] **Step 1: Write the failing portable checks**

Append to `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`:

```fsharp
type Stage =
    | Waiting
    | Working of int
    | Complete of string

let waitingArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "waiting" |])

let workingArm =
    Component.define (fun _ (progress: unit -> int) -> progress) (fun t ->
        t.span [||] [| t.text (fun progress -> "working " + string (progress ())) |])

let completeArm =
    Component.define (fun _ (result: unit -> string) -> result) (fun t -> t.span [||] [| t.text (fun result -> "complete " + result ()) |])

let staged =
    Component.define (fun _ (stage: Stage) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith id (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

equal "<div><!--[--><span>working 3</span><!--]--></div>" (render Omit (Component.render staged (Working 3)))
equal "<div><!--[--><span>complete &lt;ok&gt;</span><!--]--></div>" (render Omit (Component.render staged (Complete "<ok>")))

equal
    (stripMarkers (render Omit (Component.render staged Waiting)))
    (Html.render Omit (Tree.run (Component.renderWith Tree.ops staged Waiting)))

printfn "PASS template matchWith"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: build failure, `The type 'TemplateBuilder<_>' does not define the field, constructor or member 'matchWith'`.

- [ ] **Step 3: Add arms to the portable model**

In `experiments/Ranvier.View.Foundation/Template.fs`, in `RegionRow`, after `| ViewRow`, add:

```fsharp
    /// <summary>The item is an <c>Arm</c>; the arm's definition receives a reader of the current arm's payload.</summary>
    | ArmRow
```

Directly after the `TemplateComponent` declaration (the end of the recursive type group), add:

```fsharp
/// <summary>A component definition selected by a <c>matchWith</c> or <c>branch</c> arm, with the payload its props read.</summary>
[<NoEquality; NoComparison>]
type Arm =
    internal
        {
            Definition: TemplateComponent
            Payload: obj
            /// <summary>Turns a reader of the current arm into the definition's props, a reader of the current payload.</summary>
            Reader: Func<(unit -> obj), obj>
        }
```

Replace `serverRow` with:

```fsharp
let private serverRow (backend: Backend) (row: RegionRow) (item: obj) =
    match row with
    | ViewRow -> (unbox<Program.View> item).Invoke backend
    | ArmRow ->
        let arm = unbox<Arm> item
        backend.Template (arm.Definition, arm.Reader.Invoke (fun () -> item))
```

Replace `let rec private expandNode` through the end of `expandWith` with:

```fsharp
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
            let _, view = (region.Items.Invoke instance)[0]
            ops.Embed (fun backend -> (unbox<Program.View> view).Invoke backend)
        | row -> ops.Keyed (fun left right -> region.Same.Invoke (left, right)) (fun () -> region.Items.Invoke instance) (expandRow ops row)

and private expandRow (ops: Operations<'view>) (row: RegionRow) (current: unit -> obj) : 'view =
    match row with
    | ViewRow -> ops.Embed (fun backend -> (unbox<Program.View> (current ())).Invoke backend)
    | ArmRow ->
        let arm = unbox<Arm> (current ())
        expandWith ops arm.Definition (arm.Reader.Invoke current)

/// <summary>Expands a definition into any view representation through its operations.</summary>
and expandWith (ops: Operations<'view>) (definition: TemplateComponent) (props: obj) : 'view =
    ops.Component (fun context -> expandNode ops (definition.Setup.Invoke (context, props)) definition.Root)
```

Directly below `let private anySame = ...`, add:

```fsharp
let private definitionSame =
    Func<obj, obj, bool>(fun left right -> obj.ReferenceEquals (left, right))
```

At the end of `TemplateBuilder<'inst>`, after `view`, add:

```fsharp
    /// <summary>An arm that renders <c>definition</c>; its props read the payload of the arm currently selected.</summary>
    member _.arm (definition: Component<unit -> 'payload>) (payload: 'payload) : Arm =
        {
            Arm.Definition = definition :> TemplateComponent
            Payload = box payload
            Reader = Func<(unit -> obj), obj>(fun current -> box (fun () -> unbox<'payload> (unbox<Arm>(current ())).Payload))
        }

    /// <summary>Renders the arm that <c>arms</c> selects for the instance's union value, between region markers.</summary>
    /// <remarks>Arms are compared by definition: selecting another definition remounts, and selecting the same definition updates the payload its props read. Writing <c>arms</c> as a <c>function</c> match lets the compiler check that every case has an arm.</remarks>
    member _.matchWith (read: 'inst -> 'union) (arms: 'union -> Arm) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Region.Same = definitionSame
                    Items =
                        Func<obj, (obj * obj) array>(fun instance ->
                            let arm = arms (read (unbox instance))
                            [| box arm.Definition, box arm |])
                    Row = ArmRow
                }
        )
```

- [ ] **Step 4: Render arm rows in the DOM backend**

In `experiments/Ranvier.View.Foundation/TemplateDom.fs`, replace `readsOnce` and `renderRow` with:

```fsharp
let private readsOnce (row: RegionRow) =
    match row with
    | ViewRow -> true
    | ArmRow -> false

let private renderRow (row: RegionRow) : (unit -> obj) -> Backend -> unit =
    match row with
    | ViewRow -> fun current backend -> (unbox<Program.View> (current ())).Invoke backend
    | ArmRow ->
        fun current backend ->
            let arm = unbox<Arm> (current ())
            backend.Template (arm.Definition, arm.Reader.Invoke current)
```

- [ ] **Step 5: Run the portable checks to verify they pass**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every earlier `PASS` line, then `PASS template matchWith`.

- [ ] **Step 6: Write the DOM tests**

In `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, add directly above `/// <summary>Checks template compilation and DOM materialisation.</summary>`:

```fsharp
type Stage =
    | Waiting
    | Working of int
    | Complete of string

let private stageLog = ResizeArray<string>()
let private stageRenders = ResizeArray<string>()

let private waitingArm =
    Component.define
        (fun setup (_: unit -> unit) ->
            stageLog.Add "setup waiting"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup waiting"))
        (fun t -> t.span [||] [| t.text "waiting" |])

let private workingArm =
    Component.define
        (fun setup (progress: unit -> int) ->
            stageLog.Add "setup working"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup working")
            progress)
        (fun t ->
            t.span
                [||]
                [|
                    t.text (fun progress ->
                        let text = "working " + string (progress ())
                        stageRenders.Add text
                        text)
                |])

let private completeArm =
    Component.define
        (fun setup (result: unit -> string) ->
            stageLog.Add "setup complete"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup complete")
            result)
        (fun t -> t.span [||] [| t.text (fun result -> "complete " + result ()) |])

let private staged =
    Component.define (fun _ (stage: Signal<Stage>) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun stage -> stage.Value) (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

let private matchSwitching () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal Waiting)
    let host = host ()
    let mounted = mount graph host (Component.render staged stage)
    equal "waiting" host.textContent
    graph.Run (fun () -> stage.Value <- Working 1)
    graph.Run (fun () -> stage.Value <- Working 2)
    equal "working 2" host.textContent
    graph.Run (fun () -> stage.Value <- Complete "ok")
    equal "complete ok" host.textContent

    equal
        [ "setup waiting"; "setup working"; "cleanup waiting"; "setup complete"; "cleanup working" ]
        (List.ofSeq stageLog)

    mounted.Dispose ()
    equal ("cleanup complete", 0.) (stageLog[stageLog.Count - 1], host.childNodes.length)

let private matchSameFlush () =
    use graph = new Graph ()
    stageLog.Clear ()
    stageRenders.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal (Working 1))
    let host = host ()
    let mounted = mount graph host (Component.render staged stage)

    graph.Run (fun () ->
        Api.batch (fun () ->
            stage.Value <- Complete "x"
            stage.Value <- Working 2))

    equal ("working 2", [ "setup working" ]) (host.textContent, List.ofSeq stageLog)

    graph.Run (fun () ->
        Api.batch (fun () ->
            stage.Value <- Working 3
            stage.Value <- Complete "y"))

    equal ("complete y", [ "setup working"; "setup complete"; "cleanup working" ]) (host.textContent, List.ofSeq stageLog)
    equal [ "working 1"; "working 2" ] (List.ofSeq stageRenders)
    mounted.Dispose ()

let mutable private haltDuringSetup: unit -> unit = ignore

let private haltingArm =
    Component.define
        (fun setup (_: unit -> string) ->
            stageLog.Add "setup halting"
            setup.OnCleanup (fun () -> stageLog.Add "cleanup halting")
            haltDuringSetup ())
        (fun t -> t.span [||] [| t.text "halting" |])

let private halting =
    Component.define (fun _ (stage: Signal<Stage>) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith (fun stage -> stage.Value) (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm haltingArm result)
            |])

let private disposedMidSwitch () =
    use graph = new Graph ()
    stageLog.Clear ()
    let stage = graph.Run (fun () -> Api.createSignal (Working 1))
    let host = host ()
    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange host None (fun context -> Program.run (Component.render halting stage) context.Backend)

    haltDuringSetup <- fun () -> mounted.Dispose ()

    try
        graph.Run (fun () -> stage.Value <- Complete "x")
    finally
        haltDuringSetup <- ignore

    equal (0., 0) (host.childNodes.length, reported.Count)

    equal
        [ "cleanup halting"; "cleanup working"; "setup halting"; "setup working" ]
        (List.sort (List.ofSeq stageLog))

let private stagedServer =
    Component.define (fun _ (stage: Stage) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith id (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

let private matchMarkup () =
    equal
        "<div><!--[--><span>working 3</span><!--]--></div>"
        (Html.render Omit (Program.run (Component.render stagedServer (Working 3))))
```

Append to `cases`:

```fsharp
        "matchWith switches arms by case and updates payloads in place", matchSwitching
        "a match changed twice in one flush settles on its final case", matchSameFlush
        "disposing the mount during an arm's setup ends the switch cleanly", disposedMidSwitch
        "matchWith server markup under Fable matches .NET", matchMarkup
```

- [ ] **Step 7: Run the DOM suite to verify it passes**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass, including the four `matchWith` tests. If `disposing the mount during an arm's setup ends the switch cleanly` fails, read the keyed path's `createEntry`/`reconcile` handling of `KeyedDisposed` before changing anything: the expected behaviour is that `ensureRegionLive` turns the row's failed append into `KeyedDisposed`, which `reconcile` absorbs.

- [ ] **Step 8: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "feat(view): select template arms by union case with matchWith"
```

---

### Task 4: `branch` arms

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (builder `branch`)
- Test: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `Arm`, `RegionRow.ArmRow`, `TemplateBuilder.arm` (Task 3).
- Produces: `TemplateBuilder<'inst>.branch<'key when 'key: equality>: ('inst -> 'key) -> ('key -> Arm) -> TemplateView<'inst>`.

- [ ] **Step 1: Write the failing portable check**

Append to `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`:

```fsharp
let decadeArm =
    Component.define (fun _ (decade: unit -> int) -> decade) (fun t -> t.span [||] [| t.text (fun decade -> string (decade ())) |])

let decades =
    Component.define (fun _ (value: int) -> value) (fun t ->
        t.div [||] [| t.branch (fun value -> value / 10) (fun decade -> t.arm decadeArm decade) |])

equal "<div><!--[--><span>4</span><!--]--></div>" (render Omit (Component.render decades 42))
equal "<div><span>4</span></div>" (Html.render Omit (Tree.run (Component.renderWith Tree.ops decades 42)))

printfn "PASS template branch"
```

- [ ] **Step 2: Run it to verify it fails**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: build failure, `The type 'TemplateBuilder<_>' does not define the field, constructor or member 'branch'`.

- [ ] **Step 3: Add the builder member**

In `experiments/Ranvier.View.Foundation/Template.fs`, at the end of `TemplateBuilder<'inst>`, after `matchWith`, add:

```fsharp
    /// <summary>Renders the arm selected for the instance's key, between region markers; a key change under <c>=</c> remounts the arm.</summary>
    member _.branch<'key when 'key: equality> (key: 'inst -> 'key) (arms: 'key -> Arm) : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Region.Same = Func<obj, obj, bool>(fun left right -> unbox<'key> left = unbox<'key> right)
                    Items =
                        Func<obj, (obj * obj) array>(fun instance ->
                            let current = key (unbox instance)
                            [| box current, box (arms current) |])
                    Row = ArmRow
                }
        )
```

- [ ] **Step 4: Run the portable checks to verify they pass**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every earlier `PASS` line, then `PASS template branch`.

- [ ] **Step 5: Write the DOM tests**

In `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, add directly above `/// <summary>Checks template compilation and DOM materialisation.</summary>`:

```fsharp
let private decadeLog = ResizeArray<string>()

let private decadeArm =
    Component.define
        (fun setup (decade: unit -> int) ->
            let initial = decade ()
            decadeLog.Add ("setup " + string initial)
            setup.OnCleanup (fun () -> decadeLog.Add ("cleanup " + string initial))
            decade)
        (fun t -> t.span [||] [| t.text (fun decade -> string (decade ())) |])

let private decades =
    Component.define (fun _ (value: Signal<int>) -> value) (fun t ->
        t.div [||] [| t.branch (fun value -> value.Value / 10) (fun decade -> t.arm decadeArm decade) |])

let private branchSwitching () =
    use graph = new Graph ()
    decadeLog.Clear ()
    let value = graph.Run (fun () -> Api.createSignal 11)
    let host = host ()
    let mounted = mount graph host (Component.render decades value)
    graph.Run (fun () -> value.Value <- 12)
    equal ("1", [ "setup 1" ]) (host.textContent, List.ofSeq decadeLog)
    graph.Run (fun () -> value.Value <- 21)
    equal ("2", [ "setup 1"; "setup 2"; "cleanup 1" ]) (host.textContent, List.ofSeq decadeLog)
    mounted.Dispose ()
    equal ([ "setup 1"; "setup 2"; "cleanup 1"; "cleanup 2" ], 0.) (List.ofSeq decadeLog, host.childNodes.length)

let private branchMarkup () =
    use graph = new Graph ()
    let value = graph.Run (fun () -> Api.createSignal 42)

    equal
        "<div><!--[--><span>4</span><!--]--></div>"
        (graph.Run (fun () -> Html.render Omit (Program.run (Component.render decades value))))
```

Append to `cases`:

```fsharp
        "branch remounts on a key change and keeps the arm for an equal key", branchSwitching
        "branch server markup under Fable matches .NET", branchMarkup
```

- [ ] **Step 6: Run the DOM suite to verify it passes**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass, including both `branch` tests.

- [ ] **Step 7: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "feat(view): switch template arms by key with branch"
```

---

### Task 5: `each` rows

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (`RegionRow.ItemRow`, `serverRow`, `expandRow`, builder `each`)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`readsOnce`, `renderRow`)
- Test: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: `Region`, `RegionRow`, `serverRow`, `expandRow`, `readsOnce`, `renderRow` (Tasks 1–3); `Stage`, `waitingArm`, `workingArm`, `completeArm`, `stageLog` in `TemplateChecks.fs` (Task 3); `regionMarkers` (Task 2).
- Produces in `Template`: case `RegionRow.ItemRow of TemplateComponent * Func<(unit -> obj), obj>` (the row definition, and the conversion of a row's item reader into its typed props); `TemplateBuilder<'inst>.each<'item, 'key when 'key: equality>: ('item -> 'key) -> ('inst -> 'item array) -> Component<unit -> 'item> -> TemplateView<'inst>`.

- [ ] **Step 1: Write the failing portable checks**

Append to `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`:

```fsharp
type Listed = { Id: int; Text: string }

let listedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

let listed =
    Component.define (fun _ (items: Listed array) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) id listedRow |])

let listedItems = [| { Id = 1; Text = "a" }; { Id = 2; Text = "<b>" } |]

equal "<ul><!--[--><li>a</li><li>&lt;b&gt;</li><!--]--></ul>" (render Omit (Component.render listed listedItems))

equal
    (stripMarkers (render Omit (Component.render listed listedItems)))
    (Html.render Omit (Tree.run (Component.renderWith Tree.ops listed listedItems)))

// An empty list keeps its markers.
equal "<ul><!--[--><!--]--></ul>" (render Omit (Component.render listed [||]))

let mutable duplicateRejected = false

try
    render Omit (Component.render listed [| { Id = 1; Text = "a" }; { Id = 1; Text = "b" } |])
    |> ignore
with :? System.ArgumentException ->
    duplicateRejected <- true

// Duplicate keys fail server rendering.
equal true duplicateRejected

printfn "PASS template each"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: build failure, `The type 'TemplateBuilder<_>' does not define the field, constructor or member 'each'`.

- [ ] **Step 3: Add item rows to the portable model**

In `experiments/Ranvier.View.Foundation/Template.fs`, in `RegionRow`, after `| ArmRow`, add:

```fsharp
    /// <summary>The item is a row value; the definition receives the props that the conversion makes from a reader of the current item.</summary>
    | ItemRow of TemplateComponent * Func<(unit -> obj), obj>
```

Replace `serverRow` with:

```fsharp
let private serverRow (backend: Backend) (row: RegionRow) (item: obj) =
    match row with
    | ViewRow -> (unbox<Program.View> item).Invoke backend
    | ArmRow ->
        let arm = unbox<Arm> item
        backend.Template (arm.Definition, arm.Reader.Invoke (fun () -> item))
    | ItemRow (definition, props) -> backend.Template (definition, props.Invoke (fun () -> item))
```

Replace `expandRow` with:

```fsharp
and private expandRow (ops: Operations<'view>) (row: RegionRow) (current: unit -> obj) : 'view =
    match row with
    | ViewRow -> ops.Embed (fun backend -> (unbox<Program.View> (current ())).Invoke backend)
    | ArmRow ->
        let arm = unbox<Arm> (current ())
        expandWith ops arm.Definition (arm.Reader.Invoke current)
    | ItemRow (definition, props) -> expandWith ops definition (props.Invoke current)
```

At the end of `TemplateBuilder<'inst>`, after `branch`, add:

```fsharp
    /// <summary>Renders one <c>row</c> instance per item, between region markers; items with equal keys under <c>=</c> keep their row, whose props read the current item.</summary>
    /// <remarks>Duplicate keys fail before the rows change.</remarks>
    member _.each<'item, 'key when 'key: equality>
        (key: 'item -> 'key)
        (items: 'inst -> 'item array)
        (row: Component<unit -> 'item>)
        : TemplateView<'inst> =
        TemplateView (
            RegionNode
                {
                    Region.Same = Func<obj, obj, bool>(fun left right -> unbox<'key> left = unbox<'key> right)
                    Items = Func<obj, (obj * obj) array>(fun instance -> items (unbox instance) |> Array.map (fun item -> box (key item), box item))
                    Row = ItemRow (row :> TemplateComponent, Func<(unit -> obj), obj>(fun current -> box (fun () -> unbox<'item> (current ()))))
                }
        )
```

- [ ] **Step 4: Render item rows in the DOM backend**

In `experiments/Ranvier.View.Foundation/TemplateDom.fs`, replace `readsOnce` and `renderRow` with:

```fsharp
let private readsOnce (row: RegionRow) =
    match row with
    | ViewRow -> true
    | ArmRow
    | ItemRow _ -> false

let private renderRow (row: RegionRow) : (unit -> obj) -> Backend -> unit =
    match row with
    | ViewRow -> fun current backend -> (unbox<Program.View> (current ())).Invoke backend
    | ArmRow ->
        fun current backend ->
            let arm = unbox<Arm> (current ())
            backend.Template (arm.Definition, arm.Reader.Invoke current)
    | ItemRow (definition, props) -> fun current backend -> backend.Template (definition, props.Invoke current)
```

- [ ] **Step 5: Run the portable checks to verify they pass**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every earlier `PASS` line, then `PASS template each`.

- [ ] **Step 6: Write the DOM tests**

In `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, add directly above `/// <summary>Checks template compilation and DOM materialisation.</summary>`:

```fsharp
type Listed = { Id: int; Text: string }

let private listedLog = ResizeArray<string>()

let private listedRow =
    Component.define
        (fun setup (item: unit -> Listed) ->
            let id = (item ()).Id
            listedLog.Add ("setup " + string id)
            setup.OnCleanup (fun () -> listedLog.Add ("cleanup " + string id))
            item)
        (fun t ->
            t.element
                (HtmlTag "li")
                [| t.Events.onClick (fun item _ -> listedLog.Add ("click " + string (item ()).Id)) |]
                [| t.text (fun item -> (item ()).Text) |])

let private listed =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) listedRow |])

let private rootListed =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.fragment [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) listedRow |])

let private eachRows () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let first = host.querySelector "li" |> unbox<HTMLElement>
    equal "ab" host.textContent
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "B" }; { Id = 1; Text = "a" } |])
    equal ("Ba", true) (host.textContent, obj.ReferenceEquals (first, host.querySelectorAll("li").item 1.))
    first.click ()
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "a" } |])
    equal "a" host.textContent
    mounted.Dispose ()

    equal
        ([ "setup 1"; "setup 2"; "click 1"; "cleanup 2"; "cleanup 1" ], 0.)
        (List.ofSeq listedLog, host.childNodes.length)

let private eachDuplicates () =
    use graph = new Graph ()
    let first, second = host (), host ()
    first.innerHTML <- "<span>keep</span>"

    let duplicated =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 1; Text = "b" } |])

    let mutable rejected = false

    try
        mount graph first (Component.render listed duplicated) |> ignore
    with :? System.ArgumentException ->
        rejected <- true

    equal (true, "<span>keep</span>") (rejected, first.innerHTML)

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "b" } |])

    let reported = ResizeArray<exn>()

    let mounted =
        Mounting.mount graph None reported.AddRange second None (fun context -> Program.run (Component.render listed items) context.Backend)

    graph.Run (fun () -> items.Value <- [| { Id = 3; Text = "c" }; { Id = 3; Text = "d" } |])
    equal ("ab", 1, true) (second.textContent, reported.Count, reported[0] :? System.ArgumentException)
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "b" } |])
    equal ("b", 1) (second.textContent, reported.Count)
    mounted.Dispose ()

let private eachEmpty () =
    use graph = new Graph ()
    listedLog.Clear ()
    let nested = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let rooted = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let first, second = host (), host ()
    let a = mount graph first (Component.render listed nested)
    let b = mount graph second (Component.render rootListed rooted)
    let list = first.querySelector "ul" |> unbox<HTMLElement>
    equal (2., 2, "") (list.childNodes.length, regionMarkers second, second.textContent)

    graph.Run (fun () ->
        nested.Value <- [| { Id = 1; Text = "a" } |]
        rooted.Value <- [| { Id = 2; Text = "b" } |])

    equal ("a", "b") (first.textContent, second.textContent)
    (second.querySelector "li" |> unbox<HTMLElement>).click ()
    equal true (listedLog.Contains "click 2")

    graph.Run (fun () ->
        nested.Value <- [||]
        rooted.Value <- [||])

    equal (2., 2, "", "") (list.childNodes.length, regionMarkers second, first.textContent, second.textContent)
    a.Dispose ()
    b.Dispose ()
    equal (0., 0.) (first.childNodes.length, second.childNodes.length)

type Job = { JobId: int; Stage: Stage }

let private jobRow =
    Component.define (fun _ (job: unit -> Job) -> job) (fun t ->
        t.element
            (HtmlTag "li")
            [||]
            [|
                t.matchWith (fun job -> (job ()).Stage) (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

let private jobs =
    Component.define (fun _ (items: Signal<Job array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (job: Job) -> job.JobId) (fun items -> items.Value) jobRow |])

let private nestedRegions () =
    use graph = new Graph ()
    stageLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { JobId = 1; Stage = Waiting }; { JobId = 2; Stage = Working 1 } |])

    let host = host ()
    let mounted = mount graph host (Component.render jobs items)
    let second = host.querySelectorAll("li").item 1. |> unbox<HTMLElement>
    equal "waitingworking 1" host.textContent

    graph.Run (fun () ->
        items.Value <- [| { JobId = 2; Stage = Working 2 }; { JobId = 1; Stage = Complete "ok" } |])

    equal ("working 2complete ok", true) (host.textContent, obj.ReferenceEquals (second, host.querySelector "li" |> unbox<HTMLElement>))
    graph.Run (fun () -> items.Value <- [| { JobId = 1; Stage = Complete "ok" } |])
    equal "complete ok" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

    equal
        [ "setup waiting"; "setup working"; "setup complete"; "cleanup waiting"; "cleanup working"; "cleanup complete" ]
        (List.ofSeq stageLog)

let private eachMarkup () =
    use graph = new Graph ()

    let items =
        graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" }; { Id = 2; Text = "<b>" } |])

    equal
        "<ul><!--[--><li>a</li><li>&lt;b&gt;</li><!--]--></ul>"
        (graph.Run (fun () -> Html.render Omit (Program.run (Component.render listed items))))
```

Append to `cases`:

```fsharp
        "each renders template rows, retains them by key and dispatches their clicks", eachRows
        "duplicate each keys fail the mount, are reported on update and keep the rows", eachDuplicates
        "an empty each region keeps its markers, fills and empties", eachEmpty
        "a matchWith inside each rows follows, moves and leaves with its row", nestedRegions
        "each server markup under Fable matches .NET", eachMarkup
```

- [ ] **Step 7: Run the DOM suite to verify it passes**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass, including the five `each` tests. `eachEmpty`'s click on a root-level row passes only with Task 2's delegation relocation.

- [ ] **Step 8: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "feat(view): render keyed template rows with each"
```

---

### Task 6: Inlined child templates

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (`TemplateNode.ChildNode`, the markup writer section, `InlinedChild`, `SkeletonLayout`, `skeletonLayout`, expansion, builder `render`)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`CompiledRegion.Owner`, `Compiled.Children`, `CompiledChild`, `tables`, `compilePart`, `compile`, `stateFor`, `bind`, `partInstance`, `materialise`)
- Test: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`

**Interfaces:**
- Consumes: everything above; `listedRow`, `Listed`, `regionMarkers`, `roundTrips`, `holed`, `staged`, `listed` in `TemplateChecks.fs`.
- Produces in `Template`: case `TemplateNode.ChildNode of TemplateComponent * Func<obj, obj>` (definition and props reader of the inlining instance); record `InlinedChild = { Definition: TemplateComponent; Props: Func<obj, obj>; Owner: int; First: int; Count: int }`; record `SkeletonLayout = { Markup: string; Targets: (TemplateNode * int array) array; Owners: int array; RootCount: int; Children: InlinedChild array }`; `skeletonLayout: TemplateNode -> SkeletonLayout`; `skeletonMarkup` keeps its signature; `TemplateBuilder<'inst>.render: Component<'props> -> ('inst -> 'props) -> TemplateView<'inst>`. Part numbering: 0 is the template, k is the k-th inlined child in document order, across every depth.
- Produces in `TemplateDom`: `CompiledRegion.Owner: int`; `Compiled.Children: CompiledChild array` (non-empty only on a root compiled form); record `CompiledChild = { Definition: TemplateComponent; Props: Func<obj, obj>; Owner: int; Offset: int; Tables: Compiled }`, whose `Tables` index nodes from `Offset` and have a null skeleton, an empty walk, and no regions or children.

State layout of a root instance after this task: `[Compiled, Instance, Owner, Graph, nodes (NodeCount, including inlined descendants' nodes), previous values, pending values, one child state per inlined child]`. A child state has the layout of plan 2a over the child's node range, copied from the root state; its `Compiled` slot holds the child's `Tables`, so the delegated dispatcher calls child handlers with the child instance.

- [ ] **Step 1: Write the failing portable checks**

Append to `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`:

```fsharp
type BadgeProps = { Count: int }

let badge =
    Component.define (fun _ (props: BadgeProps) -> props) (fun t ->
        t.span [| t.Attrs.title (fun badge -> string badge.Count) |] [| t.text (fun badge -> string badge.Count) |])

let carded =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div [||] [| t.text (fun r -> r.Name); t.render badge (fun r -> { Count = r.Title.Length }) |])

equal "<div>&lt;Ada&gt;<span title=\"3\">3</span></div>" (render Omit (Component.render carded greetingProps))
equal (render Omit (Component.render carded greetingProps)) (Html.render Omit (Tree.run (Component.renderWith Tree.ops carded greetingProps)))

let textual =
    Component.define (fun _ (value: string) -> value) (fun t -> t.text (fun value -> value))

let besideText =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.span [||] [| t.text "a"; t.render textual (fun r -> r.Name) |])

// Text beside an inlined text root is separated as sibling text is.
equal "<span>a<!--!-->&lt;Ada&gt;</span>" (render Omit (Component.render besideText greetingProps))

let mutable childCleanups = 0

let cleaned =
    Component.define (fun setup () -> setup.OnCleanup (fun () -> childCleanups <- childCleanups + 1)) (fun t -> t.span [||] [||])

let cleaning =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render cleaned id |])

// An inlined child's cleanup runs with the enclosing server component.
equal "<div><span></span></div>" (render Omit (Component.render cleaning ()))
equal 1 childCleanups

printfn "PASS template inlined children"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: build failure, `The type 'TemplateBuilder<_>' does not define the field, constructor or member 'render'`.

- [ ] **Step 3: Add inlined children to the portable model**

In `experiments/Ranvier.View.Foundation/Template.fs`, in `TemplateNode`, after `| RegionNode of Region`, add:

```fsharp
    /// <summary>A child template inlined into this template's skeleton, with the reader of its props from the inlining instance.</summary>
    | ChildNode of TemplateComponent * Func<obj, obj>
```

Replace everything from `let rec private flatten` through the end of `renderHtml` with:

```fsharp
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

/// <summary>The number of skeleton sibling nodes a flattened node occupies.</summary>
let private width node =
    match node with
    | RegionNode _ -> 2
    | _ -> 1

let private serverRow (backend: Backend) (row: RegionRow) (item: obj) =
    match row with
    | ViewRow -> (unbox<Program.View> item).Invoke backend
    | ArmRow ->
        let arm = unbox<Arm> item
        backend.Template (arm.Definition, arm.Reader.Invoke (fun () -> item))
    | ItemRow (definition, props) -> backend.Template (definition, props.Invoke (fun () -> item))

/// <summary>A child template inlined into a skeleton.</summary>
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
            state.Children[index] <- { child with Count = state.Targets.Count - child.First }
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
                raise (InvalidOperationException "Raw-text elements require an explicit HTML serialization policy")

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
                Keyed.validateUniqueKeys (fun left right -> region.Same.Invoke (left, right)) items
                append "<!--[-->"

                for _, item in items do
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
let skeletonMarkup (root: TemplateNode) =
    let layout = skeletonLayout root
    layout.Markup, layout.Targets, layout.RootCount

/// <summary>Writes an instance as server HTML with the skeleton's markers; region rows render through <c>backend</c>.</summary>
/// <remarks>An inlined child's setup receives <c>setup</c>, so its cleanups run with the enclosing component's.</remarks>
let renderHtml (definition: TemplateComponent) (backend: Backend) (setup: Setup) (props: obj) (rejectBrowser: bool) (append: string -> unit) =
    let instance = definition.Setup.Invoke (setup, props)

    writeMarkup append (Server (setup, rejectBrowser, backend)) (WriterState instance) definition.Root
    |> ignore
```

In `expandNode`, after the `RegionNode` arm, add:

```fsharp
    | ChildNode (definition, props) -> expandWith ops definition (props.Invoke instance)
```

At the end of `TemplateBuilder<'inst>`, after `each`, add:

```fsharp
    /// <summary>Inlines a child template: its markup joins this template's skeleton, and its setup runs once per instance with props read untracked from the instance.</summary>
    member _.render (definition: Component<'props>) (props: 'inst -> 'props) : TemplateView<'inst> =
        TemplateView (ChildNode (definition :> TemplateComponent, Func<obj, obj>(fun instance -> box (props (unbox instance)))))
```

- [ ] **Step 4: Run the portable checks to verify they pass**

Run: `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every earlier `PASS` line, then `PASS template inlined children`.

- [ ] **Step 5: Write the failing DOM tests**

In `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, add directly above `/// <summary>Checks template compilation and DOM materialisation.</summary>`:

```fsharp
type BadgeProps =
    {
        Count: Signal<int>
        BadgeReads: int ref
        BadgeLog: ResizeArray<string>
    }

type CardProps =
    {
        Heading: Signal<string>
        Tally: Signal<int>
        CardReads: int ref
        BadgeCounter: int ref
        CardLog: ResizeArray<string>
    }

let private badge =
    Component.define
        (fun setup (props: BadgeProps) ->
            setup.OnCleanup (fun () -> props.BadgeLog.Add "cleanup badge")
            props)
        (fun t ->
            t.button
                [| t.Events.onClick (fun badge _ -> badge.BadgeLog.Add ("badge " + string badge.Count.Value)) |]
                [|
                    t.text (fun badge ->
                        badge.BadgeReads.Value <- badge.BadgeReads.Value + 1
                        string badge.Count.Value)
                |])

let private card =
    Component.define
        (fun setup (props: CardProps) ->
            setup.OnCleanup (fun () -> props.CardLog.Add "cleanup card")
            props)
        (fun t ->
            t.div
                [| t.Events.onClick (fun card _ -> card.CardLog.Add "card") |]
                [|
                    t.text (fun card ->
                        card.CardReads.Value <- card.CardReads.Value + 1
                        card.Heading.Value)
                    t.render badge (fun card ->
                        {
                            Count = card.Tally
                            BadgeReads = card.BadgeCounter
                            BadgeLog = card.CardLog
                        })
                |])

let private cardProps (graph: Graph) =
    {
        Heading = graph.Run (fun () -> Api.createSignal "h")
        Tally = graph.Run (fun () -> Api.createSignal 1)
        CardReads = ref 0
        BadgeCounter = ref 0
        CardLog = ResizeArray<string>()
    }

let private inlinedChild () =
    use graph = new Graph ()
    let props = cardProps graph
    let host = host ()
    let mounted = mount graph host (Component.render card props)
    equal ("h1", 1, 1) (host.textContent, props.CardReads.Value, props.BadgeCounter.Value)
    graph.Run (fun () -> props.Tally.Value <- 2)
    equal ("h2", 1, 2) (host.textContent, props.CardReads.Value, props.BadgeCounter.Value)
    graph.Run (fun () -> props.Heading.Value <- "H")
    equal ("H2", 2, 2) (host.textContent, props.CardReads.Value, props.BadgeCounter.Value)
    equal true (isNull (badge :> TemplateDefinition).Compiled)
    equal 1 ((card :> TemplateDefinition).Compiled :?> TemplateDom.Compiled).Children.Length
    (host.querySelector "button" |> unbox<HTMLButtonElement>).click ()
    mounted.Dispose ()

    equal
        ([ "badge 2"; "card"; "cleanup badge"; "cleanup card" ], 0.)
        (List.ofSeq props.CardLog, host.childNodes.length)

let private framed =
    Component.define (fun _ (props: CardProps) -> props) (fun t -> t.section [||] [| t.render card id |])

let private inlinedGrandchild () =
    use graph = new Graph ()
    let props = cardProps graph
    let host = host ()
    let mounted = mount graph host (Component.render framed props)
    graph.Run (fun () -> props.Tally.Value <- 5)
    (host.querySelector "button" |> unbox<HTMLButtonElement>).click ()
    equal ("h5", [ "badge 5"; "card" ]) (host.textContent, List.ofSeq props.CardLog)
    equal 2 ((framed :> TemplateDefinition).Compiled :?> TemplateDom.Compiled).Children.Length
    mounted.Dispose ()
    equal 0. host.childNodes.length

type ShelfProps = { Shelf: Signal<Listed array> }

let private shelf =
    Component.define (fun _ (props: ShelfProps) -> props) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun props -> props.Shelf.Value) listedRow |])

let private shelved =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.section [||] [| t.text "shelf:"; t.render shelf (fun items -> { Shelf = items }) |])

let private inlinedRegion () =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" } |])
    let host = host ()
    let mounted = mount graph host (Component.render shelved items)
    equal "shelf:a" host.textContent
    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "b" }; { Id = 1; Text = "a" } |])
    equal ("shelf:ba", 2) (host.textContent, regionMarkers (host.querySelector "ul" |> unbox<HTMLElement>))
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private textual =
    Component.define (fun _ (value: Signal<string>) -> value) (fun t -> t.text (fun value -> value.Value))

let private besideText =
    Component.define (fun _ (value: Signal<string>) -> value) (fun t -> t.span [||] [| t.text "a"; t.render textual id |])

let private inlinedText () =
    use graph = new Graph ()
    let value = graph.Run (fun () -> Api.createSignal "b")
    let host = host ()
    let mounted = mount graph host (Component.render besideText value)
    graph.Run (fun () -> value.Value <- "c")
    let span = host.querySelector "span" |> unbox<HTMLElement>
    equal ("ac", 3.) (span.textContent, span.childNodes.length)
    mounted.Dispose ()

let private inlinedFailure () =
    use graph = new Graph ()

    let failingChild =
        Component.define (fun _ () -> failwith "child") (fun t -> t.span [||] [||])

    let parent =
        Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render failingChild id |])

    let host = host ()
    host.innerHTML <- "<span>keep</span>"
    let mutable failed = false

    try
        mount graph host (Component.render parent ()) |> ignore
    with error ->
        failed <- error.Message = "child"

    equal (true, "<span>keep</span>") (failed, host.innerHTML)

let private structureRoundTrips () =
    let definitions: TemplateComponent list =
        [
            holed :> TemplateComponent
            staged :> TemplateComponent
            listed :> TemplateComponent
            rootListed :> TemplateComponent
            card :> TemplateComponent
            framed :> TemplateComponent
            shelved :> TemplateComponent
            besideText :> TemplateComponent
        ]

    for definition in definitions do
        let markup, _, _ = skeletonMarkup definition.Root
        equal (markup, true) (markup, roundTrips markup)

let private inlinedMarkup () =
    use graph = new Graph ()
    let props = cardProps graph

    equal
        "<div>h<button>1</button></div>"
        (graph.Run (fun () -> Html.render Omit (Program.run (Component.render card props))))
```

Append to `cases`:

```fsharp
        "an inlined child shares the clone and keeps its own instance, effect and handlers", inlinedChild
        "a grandchild inlined through a child is hoisted and updates", inlinedGrandchild
        "an inlined child's region reads the child instance", inlinedRegion
        "text beside an inlined text root keeps separate nodes", inlinedText
        "a failing inlined child setup rolls back the mount", inlinedFailure
        "region and child skeletons round-trip", structureRoundTrips
        "inlined child server markup under Fable matches .NET", inlinedMarkup
```

- [ ] **Step 6: Run them to verify they fail**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: the Fable build fails in `TemplateChecks.fs` with `The type 'Compiled' does not define the field, constructor or member 'Children'` (in `inlinedChild` and `inlinedGrandchild`).

- [ ] **Step 7: Compile parts and bind inlined children**

In `experiments/Ranvier.View.Foundation/TemplateDom.fs`, replace the `CompiledRegion` and `Compiled` types with:

```fsharp
/// <summary>A region of a compiled template.</summary>
[<NoEquality; NoComparison>]
type CompiledRegion =
    {
        /// <summary>The node index of the region's start marker.</summary>
        Marker: int
        /// <summary>The part whose instance the region reads: 0 for the template, k for the k-th inlined child.</summary>
        Owner: int
        /// <summary>True when the markers are roots of a multi-root skeleton.</summary>
        RootLevel: bool
        Same: obj -> obj -> bool
        Items: Func<obj, (obj * obj) array>
        /// <summary>True when the items are read once, as the instance is created.</summary>
        Once: bool
        Render: (unit -> obj) -> Backend -> unit
    }

/// <summary>A definition compiled for the DOM: a skeleton, the walk to its dynamic nodes, and its slot tables.</summary>
/// <remarks>A single-root skeleton is the root node itself and its walk starts at the root; otherwise the skeleton is a fragment of <c>RootCount</c> roots. Regions and inlined children of every depth are listed on the root compiled form only.</remarks>
[<NoEquality; NoComparison>]
type Compiled =
    {
        Skeleton: Node
        RootCount: int
        Walk: int array
        NodeCount: int
        RenderSlots: (int * Slot) array
        IsolatedSlots: (int * Slot) array
        Properties: (int * Func<obj, obj, unit> * obj) array
        Events: (int * string * Func<obj, obj, unit>) array
        EventNames: string array
        Dispatch: Func<obj array, Node, obj, unit>
        Regions: CompiledRegion array
        Children: CompiledChild array
    }

/// <summary>An inlined child template of a compiled template.</summary>
and [<NoEquality; NoComparison>] CompiledChild =
    {
        Definition: TemplateComponent
        Props: Func<obj, obj>
        /// <summary>The part whose instance the props reader receives.</summary>
        Owner: int
        /// <summary>The template's node index of the child's first dynamic node.</summary>
        Offset: int
        /// <summary>The child's slot tables over its node range, without a skeleton or walk.</summary>
        Tables: Compiled
    }
```

Replace `compile` (from `/// <summary>Compiles a definition once` through the end of its record expression) with:

```fsharp
let private tables (targets: (TemplateNode * int array) array) (owners: int array) (part: int) (first: int) (count: int) =
    let renderSlots = ResizeArray<int * Slot>()
    let isolatedSlots = ResizeArray<int * Slot>()
    let properties = ResizeArray<int * Func<obj, obj, unit> * obj>()
    let events = ResizeArray<int * string * Func<obj, obj, unit>>()

    let add index (slot: Slot) =
        if slot.Mode = IsolatedMode then
            isolatedSlots.Add (index, slot)
        else
            renderSlots.Add (index, slot)

    for absolute in first .. first + count - 1 do
        if owners[absolute] = part then
            let index = absolute - first

            match fst (targets[absolute]) with
            | TextSlotNode slot -> add index slot
            | ElementNode (_, _, elementProperties, slots, elementEvents, _) ->
                for write, value in elementProperties do
                    properties.Add (index, write, value)

                for slot in slots do
                    add index slot

                for eventName, handler in elementEvents do
                    events.Add (index, eventName, handler)
            | _ -> ()

    renderSlots.ToArray (), isolatedSlots.ToArray (), properties.ToArray (), events.ToArray ()

let private compilePart
    (layout: SkeletonLayout)
    (part: int)
    (first: int)
    (count: int)
    (skeleton: Node)
    (rootCount: int)
    (walk: int array)
    (regions: CompiledRegion array)
    (children: CompiledChild array)
    : Compiled =
    let renderSlots, isolatedSlots, properties, events =
        tables layout.Targets layout.Owners part first count

    let eventNames =
        events
        |> Array.map (fun (_, eventName, _) -> eventName)
        |> Array.distinct

    {
        Skeleton = skeleton
        RootCount = rootCount
        Walk = walk
        NodeCount = count
        RenderSlots = renderSlots
        IsolatedSlots = isolatedSlots
        Properties = properties
        Events = events
        EventNames = eventNames
        Dispatch = dispatchFor count events eventNames
        Regions = regions
        Children = children
    }

/// <summary>Compiles a definition once: parses its skeleton markup and builds its walk, its slot tables, its regions and its inlined children's slot tables.</summary>
/// <exception cref="T:System.InvalidOperationException">The HTML parser restructures the markup, moving a dynamic node or changing the root count.</exception>
let compile (definition: TemplateComponent) : Compiled =
    let layout = skeletonLayout definition.Root
    let targets = layout.Targets
    let rootCount = layout.RootCount
    let template = document.createElement "template" :?> HTMLTemplateElement
    template.innerHTML <- layout.Markup
    let paths = Array.map snd targets
    let walk = walkProgram paths
    validateParsed template.content walk targets rootCount

    let regions =
        [|
            for index in 0 .. targets.Length - 1 do
                match targets[index] with
                | RegionNode region, path ->
                    yield
                        {
                            CompiledRegion.Marker = index
                            Owner = layout.Owners[index]
                            RootLevel = rootCount > 1 && path.Length = 1
                            Same = fun left right -> region.Same.Invoke (left, right)
                            Items = region.Items
                            Once = readsOnce region.Row
                            Render = renderRow region.Row
                        }
                | _ -> ()
        |]

    let children =
        layout.Children
        |> Array.mapi (fun index child ->
            {
                CompiledChild.Definition = child.Definition
                Props = child.Props
                Owner = child.Owner
                Offset = child.First
                Tables = compilePart layout (index + 1) child.First child.Count Unchecked.defaultof<Node> 0 [||] [||] [||]
            })

    compilePart
        layout
        0
        0
        targets.Length
        (if rootCount = 1 then
             unbox template.content.firstChild
         else
             template.content :> Node)
        rootCount
        (if rootCount = 1 then
             walkProgram (paths |> Array.map (fun path -> path[1..]))
         else
             walk)
        regions
        children
```

Replace `materialise` (from its doc comment through its end) with:

```fsharp
/// <summary>A state array for an instance of <c>compiled</c>, with <c>extra</c> trailing elements and every previous value unset.</summary>
let private stateFor (compiled: Compiled) (instance: obj) (extra: int) =
    let renderCount = compiled.RenderSlots.Length
    let previousBase = StateNodes + compiled.NodeCount
    let pendingBase = previousBase + renderCount + compiled.IsolatedSlots.Length
    let state = allocate (pendingBase + renderCount + extra)
    store state StateCompiled (box compiled)
    store state StateInstance instance
    store state StateOwner (box (Api.getOwner ()))
    store state StateGraph (box Graph.Current)

    for index in previousBase .. pendingBase - 1 do
        store state index unset

    state

/// <summary>Writes one part's static properties, starts its render and isolated effects, and attaches its events.</summary>
let private bind (services: MountServices) (compiled: Compiled) (state: obj array) (instance: obj) =
    let renderCount = compiled.RenderSlots.Length
    let isolatedCount = compiled.IsolatedSlots.Length
    let previousBase = StateNodes + compiled.NodeCount
    let pendingBase = previousBase + renderCount + isolatedCount
    let properties = compiled.Properties

    for i in 0 .. properties.Length - 1 do
        let nodeIndex, writer, value = at properties i

        try
            writer.Invoke (at state (StateNodes + nodeIndex), value)
        with error when services.SettingUp () ->
            services.SetupFailed error
            reraise ()

    if renderCount > 0 then
        let commitAll () =
            for k in 0 .. renderCount - 1 do
                let nodeIndex, slot = at compiled.RenderSlots k
                commit services state slot nodeIndex (previousBase + k) (at state (pendingBase + k))

        let mutable failed = false

        Api.createEffect (fun () ->
            let mutable failure: exn = null

            for k in 0 .. renderCount - 1 do
                let _, slot = at compiled.RenderSlots k

                let value =
                    try
                        slot.Read.Invoke instance
                    with
                    | NotReadyException _ -> unset
                    | error ->
                        failed <- true
                        services.ReportFailure error

                        if isNull failure then
                            failure <- error

                        unset

                store state (pendingBase + k) value

            Api.untrack commitAll

            if not (isNull failure) then
                failed <- true
                raise failure
            elif failed then
                failed <- false
                services.ClearFailure ())

        services.RaiseSetupFailure ()

    for k in 0 .. isolatedCount - 1 do
        let nodeIndex, slot = at compiled.IsolatedSlots k
        let previousIndex = previousBase + renderCount + k

        let mutable failed = false

        Api.createEffect (fun () ->
            let mutable failure: exn = null

            let value =
                try
                    slot.Read.Invoke instance
                with
                | NotReadyException _ -> unset
                | error ->
                    failed <- true
                    services.ReportFailure error
                    failure <- error
                    unset

            Api.untrack (fun () -> commit services state slot nodeIndex previousIndex value)

            if not (isNull failure) then
                failed <- true
                raise failure
            elif failed then
                failed <- false
                services.ClearFailure ())

        services.RaiseSetupFailure ()

    let mutable attached = -1

    let events = compiled.Events

    for i in 0 .. events.Length - 1 do
        let nodeIndex, eventName, handler = at events i
        let node = at state (StateNodes + nodeIndex)

        if services.IsDelegated eventName then
            if attached <> nodeIndex then
                attachState node state
                attached <- nodeIndex

            services.Delegate eventName
        else
            let owner = at state StateOwner :?> Owner
            let graph = at state StateGraph :?> Graph

            services.ListenDirect (
                unbox node,
                eventName,
                fun event ->
                    if not owner.IsDisposed then
                        graph.Run (fun () -> Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler.Invoke (instance, event))))
            )

/// <summary>The instance of a part: the template's own for 0, an inlined child's from its state array otherwise.</summary>
let private partInstance (state: obj array) (childBase: int) (part: int) : obj =
    if part = 0 then
        at state StateInstance
    else
        at (unbox<obj array> (at state (childBase + part - 1))) StateInstance

/// <summary>Materialises one instance: runs setup, clones the skeleton, binds its slots and events and those of its inlined children, appends it and mounts its regions.</summary>
let internal materialise (definition: TemplateDefinition) (props: obj) (services: MountServices) =
    let template = definition :?> TemplateComponent

    if isNull definition.Compiled then
        definition.Compiled <- box (compile template)

    let compiled = definition.Compiled :?> Compiled
    let instance = Api.untrack (fun () -> template.Setup.Invoke (services.Setup, props))
    let clone = compiled.Skeleton.cloneNode true
    let children = compiled.Children
    let state = stateFor compiled instance children.Length
    collect clone compiled.Walk state
    bind services compiled state instance

    let childBase =
        StateNodes
        + compiled.NodeCount
        + 2 * compiled.RenderSlots.Length
        + compiled.IsolatedSlots.Length

    for k in 0 .. children.Length - 1 do
        let child = at children k

        let childInstance =
            Api.untrack (fun () -> child.Definition.Setup.Invoke (services.Setup, child.Props.Invoke (partInstance state childBase child.Owner)))

        let tables = child.Tables
        let childState = stateFor tables childInstance 0

        for i in 0 .. tables.NodeCount - 1 do
            store childState (StateNodes + i) (at state (StateNodes + child.Offset + i))

        store state (childBase + k) (box childState)
        bind services tables childState childInstance

    if compiled.RootCount = 1 then
        services.Append clone
    else
        let mutable root: Node = unbox clone.firstChild

        while not (isNull (box root)) do
            let next: Node = unbox root.nextSibling
            services.Append root
            root <- next

    let regions = compiled.Regions

    for i in 0 .. regions.Length - 1 do
        let region = at regions i
        let owner = partInstance state childBase region.Owner
        let start: Node = unbox (at state (StateNodes + region.Marker))
        let finish: Node = unbox start.nextSibling
        let items = region.Items

        let read =
            if region.Once then
                let snapshot = items.Invoke owner
                fun () -> snapshot
            else
                fun () -> items.Invoke owner

        services.Region (start, finish, region.RootLevel, region.Same, read, region.Render)
```

- [ ] **Step 8: Run the DOM suite to verify it passes**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass, including the seven inlined-child tests and every earlier template test.

- [ ] **Step 9: Verify the emitted JavaScript**

Run: `rtk grep -n "export function materialise\|function bind\|function stateFor\|function partInstance" -A 40 examples/Fable.Ranvier.Playground/generated-tests/experiments/Ranvier.View.Foundation/TemplateDom.js`
Expected: `materialise` calls `stateFor`, `collect`, `bind`, then loops over `Children` and `Regions`; its only arrow functions are the setup `Api_untrack` arrow, the child setup arrow inside the children loop and the `read` arrows inside the regions loop; `bind`'s body matches the property, effect and event code of `materialise` at `19c8a8f`; no `curry`, `uncurry` or `partialApply` call appears in `stateFor`, `bind`, `partInstance` or `materialise`.

- [ ] **Step 10: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git add experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
rtk git commit -m "feat(view): inline child templates into their parent's skeleton"
```

---

### Task 7: Full verification, benchmark and documentation

**Files:**
- Modify: `docs/research/2026-10-14-component-templates.md` (new section `## Dynamic structure`)

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces: the recorded benchmark comparison and the structure description.

- [ ] **Step 1: Run every suite**

Run (from the repository root): `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`
Expected: every `PASS` line, ending with `PASS template inlined children`.

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: all tests pass.

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:browser`
Expected: all Chromium tests pass.

- [ ] **Step 2: Run the template comparison**

Run (from `examples/Fable.Ranvier.Playground`): `rtk node benchmarks/frameworks/run.mjs --build --template-comparison`
Expected: all correctness checks pass. Read the Ranvier template and Solid 2 overall medians at 1, 16 and 128 rows for mount, update and dispose, and compute each template ratio to Solid 2. The benchmark row has no dynamic structure, so each ratio must fall within the paired-run range recorded in `docs/research/2026-10-14-component-templates.md` (mount 1: 1.11–1.14, mount 16: 1.16–1.19, mount 128: 1.17–1.28, dispose 1: 1.31–1.33, dispose 16: 1.31–1.62, dispose 128: 1.48–1.65) and update ratios within 0.05 of the `a0de558` column (0.80, 1.05, 1.07).

- [ ] **Step 3: Pair against the base commit when a ratio falls outside its range**

Run only if Step 2 puts a ratio outside its range:

```bash
rtk git worktree add ../Ranvier-structure-base 19c8a8f
```

Then, alternating three times, run `rtk node benchmarks/frameworks/run.mjs --build --template-comparison` from `../Ranvier-structure-base/examples/Fable.Ranvier.Playground` (after `rtk npm ci` there once) and from this worktree's `examples/Fable.Ranvier.Playground`. A median ratio above the base's range by more than the spread of its three runs is a regression: profile it with `rtk node benchmarks/frameworks/diagnose.mjs --modes=ranvier-template,solid --phases=mount,dispose --width=1 --roots=256 --repeat=160` against the base before continuing. Remove the worktree afterwards with `rtk git worktree remove ../Ranvier-structure-base`.

- [ ] **Step 4: Document the structure**

Append to `docs/research/2026-10-14-component-templates.md`:

```markdown
## Dynamic structure

Plan 2b ([implementation plan](../superpowers/plans/2026-10-14-component-templates-structure.md)) adds dynamic structure to templates:

- `t.view (fun r -> view)` renders a `Program.View` read once per instance.
- `t.matchWith (fun r -> union) arms` renders the arm that `arms` selects, an `Arm` built by `t.arm definition payload` from a `Component<unit -> 'payload>`. Arms are compared by definition: a different definition remounts, the same definition receives the new payload through its props reader.
- `t.branch (fun r -> key) arms` remounts its arm whenever the key changes under `=`.
- `t.each key (fun r -> items) row` renders one `Component<unit -> 'item>` row per item of an array, retained by key.
- `t.render child (fun r -> props)` inlines a child template's markup into the parent's skeleton.

A region (view hole, match, branch or list) is a pair of comment markers, `<!--[-->` and `<!--]-->`, in the skeleton and in server HTML. The DOM compiler records the start marker as a dynamic node; after the clone is appended, each region mounts its rows through the mount backend's keyed path, one nested mount per item, between the markers. Markers that are roots of a multi-root skeleton become one entry of the mount's range spanning the rows. An inlined child's dynamic nodes are part of its parent's walk; the child receives its own state array over that node range, its own render effect, and the delegated dispatcher calls its handlers with the child instance. Regions and inlined children of every depth are hoisted into the root compiled form.

Server HTML writes the markers and renders region rows through the HTML backend. The default expansion maps regions to `Keyed`, view holes to `Operations.Embed` and inlined children to `Component`; its HTML equals the template's once comments are removed.

Rows of a region at a mount's top level delegate events at the range's staging fragment while the mount is built. Attaching a staged range now relocates its entries, and a relocated mount moves its delegation to its new parent; authored top-level keyed lists gained the same fix.

The benchmark row has no dynamic structure. One run of `run.mjs --template-comparison` at commit `COMMIT`, overall medians, µs per operation, with the template's ratio to Solid 2:

| Phase, rows | Template | Solid 2 | × Solid 2 |
|---|---|---|---|
| Mount, 1 | | | |
| Mount, 16 | | | |
| Mount, 128 | | | |
| Update, 1 | | | |
| Update, 16 | | | |
| Update, 128 | | | |
| Dispose, 1 | | | |
| Dispose, 16 | | | |
| Dispose, 128 | | | |
```

This section records measurements, so its numbers come from Step 2: write each median printed there into its cell, the ratio computed from the two medians into the last column, and replace `COMMIT` with the output of `rtk git rev-parse --short HEAD` (the Task 6 commit Step 2 measured). If Step 3 ran, add its three paired ratios per phase below the table.

- [ ] **Step 5: Commit**

```bash
rtk git add docs/research/2026-10-14-component-templates.md
rtk git commit -m "docs(view): record template dynamic structure and its benchmark"
```
