# Component Templates Catalogue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the template builder's element, attribute, property and event members from the same catalogue as `Html.Program.*`, so templates reach every HTML tag and every generated modifier with the same element typing.

**Architecture:** `tools/Ranvier.Dom.Generate` gains a second output, `Generated/HtmlTemplate.fs`, written from the data it already resolves for `Generated/Html.fs`. The new file holds optional type extensions on the handwritten builders in `Template.fs` (`TemplateBuilder`, `AttributeBuilder`, `PropertyBuilder`, `EventBuilder`) and on generated group marker classes. Every generated member is one call into a small handwritten `TemplateParts` module in `Template.fs`. Optional extensions compile under Fable to module-level functions, so a bundler (the Playground's `vite build`) can remove unused members.

**Tech Stack:** F#, Fable 5, Fantomas.Core 7.0.5 (pinned in the generator), Xantham.Fable.Core.TS 0.2.1 catalogue, Node test runner with jsdom, Playwright/Chromium, Vite production builds.

**Spec:** `docs/superpowers/specs/2026-10-13-component-templates-design.md` (Authoring surface; Risks: generated helper surface, typed slot storage). Prior plans: `docs/superpowers/plans/2026-10-13-component-templates-core.md` (2a), `docs/superpowers/plans/2026-10-14-component-templates-structure.md` (2b). Research note: `docs/research/2026-10-14-component-templates.md`.

## Global Constraints

- Templates are written against the builder, `Component.define setup (fun t -> t.div [| ... |] [| ... |])`; the spec's static `T.*` sketch cannot type its lambdas (2a ruling, recorded in the research note). `T.*` in the spec means the builder's members.
- "`T.*` helpers are generated from the same catalogue as `H.Program.*`, with the same element typing. A value argument gives a static part; an `'inst -> value` argument gives a dynamic part. Static and dynamic forms are overloads." A `Source<'inst, 'value>` from `t.from` gives a direct part.
- "the generator must emit both forms from one catalogue entry."
- "Fable function adaptation can reintroduce closures where typed slots meet the backend; the implementation must verify emitted JavaScript." After any change to `Template.fs`, `TemplateDom.fs`, `Mounting.fs` or `Generated/HtmlTemplate.fs`, grep the emitted `Template.js`, `TemplateDom.js` and `HtmlTemplate.js` (Playground `generated-tests/`) for `curry`, `uncurry` and `partialApply`; none may be new.
- Generated code is regenerated, never hand-edited. Output is deterministic (byte-identical across runs), UTF-8, LF, formatted by the generator's pinned Fantomas.
- Generated attribute semantics equal `Typed`'s: string attributes remove on `None`; boolean attributes are present as `""` when true and absent when false; ARIA booleans write `"true"`/`"false"` and remove on `None`; `data-`/`aria-` names go through `Typed.prefixedAttributeName`. Event-handler attributes and `srcdoc` stay rejected.
- Property setters exist only for writable primitive properties (`string`, `bool`, `float`, options of them) outside the generator's `excluded` set, exactly as `Html.fs`.
- Fable: no interface type tests (they compile to false); no secondary constructors with a self-identifier.
- Shell commands are prefixed with `rtk`. `rtk git commit` drops stdin messages: use `-m`. Do not use `rtk git branch -u`.
- Commits: conventional commits, a one-line summary, no body, no `Co-Authored-By` or other AI attribution, no `!`.
- Run `dotnet fantomas` on changed handwritten `.fs` files; the pre-commit hook enforces formatting and rejects `FOR-REVIEW` comments and trailing whitespace.
- Comments follow `C:\Users\shaya\RiderProjects\Ranvier\.claude\rules\comments.md`; `///` docs follow the fsharp-xml-docs conventions. Generated members carry no per-member docs.
- Framework comparisons are against Solid 2 (2.0.0-rc.14). Benchmark numbers in notes trace to committed raw JSON with machine, Chromium version, date and commit; compare ratios within a run and use paired, alternating runs.
- Gates, all must pass at the end of every task: from `examples/Fable.Ranvier.Playground`, `rtk npm run test:dom` and `rtk npm run test:browser`; from the repository root, `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release` (13 PASS lines at the start of this plan).

## Review Focus

- Two records in scope that share a field name, used in a template lambda (`fun r -> r.Index`): the lambda must type against the builder's instance type, not the most recent record with that label. Pinned in Task 1.
- A `string option` reader on a string attribute (`t.Attrs.title (fun r -> r.Nickname)`) must pick the removal overload and remove the attribute on `None`; a `string` reader keeps it. Pinned in Task 2.
- Boolean attributes at `false` are absent and ARIA booleans at `false` read `"false"`, identical to `Html.Program` output. Pinned in Task 2.
- Members whose names are F# keywords (`t.``base```, `t.Attrs.Button.``type```) compile and render. Pinned in Task 1's type checks.
- An element-specific property on another element (`t.Props.Input.value` inside `t.div`) fails to compile. Pinned in Task 1's type checks.

---

## Surface decisions

These rules define the generated surface. Every task follows them.

| Builder | Flat members | Groups |
|---|---|---|
| `t` (`TemplateBuilder<'inst>`) | one member per tag in `HTMLElementTagNameMap` (112): `t.div parts children`; void tags take parts only | none |
| `t.Attrs` (`AttributeBuilder<'inst>`) | entries of the `attributes.json` group with `"module": ""`, plus `data` and `aria` | `t.Attrs.<module>.<helper>` for each other group (`Button`, `Input`, ...) |
| `t.Props` (`PropertyBuilder<'inst>`) | settable properties whose declaring type is `HTMLElement` or one of its ancestors/mixins | `t.Props.<Group>.<member>` for other declaring types |
| `t.Events` (`EventBuilder<'inst>`) | events of `HTMLElementEventMap` | `t.Events.<Group>.on<Event>` for element-specific events, grouped by the tag's native type |

- **Group name:** the native type name with a leading `HTML` and a trailing `Element` removed when something remains: `HTMLInputElement` → `Input`, `HTMLMediaElement` → `Media`, `HTMLTableCellElement` → `TableCell`, `HTMLHyperlinkElementUtils` → `HyperlinkElementUtils`, `PopoverTargetAttributes` → `PopoverTargetAttributes`.
- **Member names** are those of `Html.fs`: tags and properties as declared (backticked), attribute helpers from `attributes.json`'s `helper`, events `on` + the event name with its first letter upper-cased (`onClick`, `onAnimationcancel`).
- **Constraints:** a flat or grouped attribute, property or event member is generic in `'native` with `'native :> <declaring native type>`; a tag member fixes `'native` to the tag's native type.
- **Overloads per entry:**

| Kind | Overloads |
|---|---|
| string attribute | `string`, `string option`, `'inst -> string`, `'inst -> string option`, `Source<'inst, string>`, `Source<'inst, string option>` |
| boolean attribute | `bool`, `'inst -> bool`, `Source<'inst, bool>` |
| ARIA boolean attribute | `bool`, `bool option`, `'inst -> bool`, `'inst -> bool option`, `Source<'inst, bool>`, `Source<'inst, bool option>` |
| `data`, `aria` | `(suffix, string)`, `(suffix, 'inst -> string option)`, `(suffix, Source<'inst, string option>)` |
| property of type `'v` | `'v`, `'inst -> 'v`, `Source<'inst, 'v>` |
| event of type `'e` | `'inst -> 'e -> unit` |

- **Reserved names:** a generated member never takes a handwritten member's name. Handwritten and kept: `TemplateBuilder` — `Attrs`, `Props`, `Events`, `element`, `fragment`, `text`, `from`, `isolated`, `view`, `arm`, `matchWith`, `branch`, `each`, `render`; `AttributeBuilder` — `attribute`; `PropertyBuilder` — `propertyStatic`, `propertyWith`, `propertyFrom`; `EventBuilder` — `on`. Group names must not equal a flat member name of the same builder. The generator fails on any collision.
- **Group marker classes** live in a nested `Groups` module of `HtmlTemplate.fs` (`InputAttributes<'inst>`, `InputProperties<'inst>`, `VideoEvents<'inst>`), so their members are optional extensions declared outside the module that defines the class.

## File Structure

- Modify `experiments/Ranvier.View.Foundation/Template.fs`: add `NoPart` to `Part`; add `module internal TemplateParts`; remove the handwritten members the catalogue replaces (`div`, `section`, `span`, `button`, `input`, `Attrs.className/id/title`, `Props.value/disabled`, `Events.onClick/onInput/onFocus`).
- Create `experiments/Ranvier.View.Foundation/Generated/HtmlTemplate.fs`: generated.
- Modify `tools/Ranvier.Dom.Generate/Program.fs`: collect template entries in the existing loops; write `HtmlTemplate.fs`; add coverage counts.
- Modify `tools/Ranvier.Dom.Generate/tests.py`, `check-types.py`, `README.md`.
- Modify compile lists: `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj`, `RepresentationChecks.fsproj`, `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, and any other project that compiles `Template.fs` (grep for `Template.fs" ` in `*.fsproj`).
- Create `examples/Fable.Ranvier.Playground/tests/TemplateCatalogueChecks.fs` and `tests/template-catalogue.test.mjs`; add the runner to `test:dom` in `package.json`.
- Migrate `t.Props.value` → `t.Props.Input.value` and `t.Props.disabled` → `t.Props.Button.disabled` in `tests/TemplateChecks.fs` and `tests/FrameworkPerf.fs`.
- Docs: the research note, the spec's clarifications, the generator README, the framework plan's status (`docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md`) and the checkpoint index (`docs/research/2026-10-11-view-poc-checkpoint.md`).

---

### Task 1: Generate the template catalogue

Generate the whole surface in one pass from the generator's existing data, migrate the property call sites, and pin type inference. The generated output can be sliced, regenerated and corrected; nothing is hand-written first.

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Template.fs:428-631`
- Modify: `tools/Ranvier.Dom.Generate/Program.fs:340-960`, `tools/Ranvier.Dom.Generate/tests.py`, `tools/Ranvier.Dom.Generate/check-types.py`
- Create (generated): `experiments/Ranvier.View.Foundation/Generated/HtmlTemplate.fs`; modify (generated): `Generated/Html.coverage.json`
- Modify: the `.fsproj` compile lists named in File Structure (insert `Generated/HtmlTemplate.fs` directly after `Template.fs`)
- Create: `examples/Fable.Ranvier.Playground/tests/TemplateCatalogueChecks.fs`, `examples/Fable.Ranvier.Playground/tests/template-catalogue.test.mjs`
- Modify: `examples/Fable.Ranvier.Playground/package.json` (`test:dom`), `tests/TemplateChecks.fs`, `tests/FrameworkPerf.fs`

**Interfaces:**
- Produces in `Template`: `Part.NoPart`; `module internal TemplateParts` with `element`, `attribute`, `optionalAttribute`, `readAttribute`, `sourceAttribute`, `mapSource`, `presence`, `ariaText`, `ariaOption`, `property`, `readProperty`, `sourceProperty`, `event` (signatures in Step 3).
- Produces in `Generated.HtmlTemplate` (`[<AutoOpen>]`): all 112 tags, every `attributes.json` entry, every settable property and every event that `Html.fs` covers, in the shapes of the Surface decisions table; coverage keys `templateTagCount`, `templateAttributeCount`, `templatePropertyCount`, `templateEventCount`, `templatePropertyGroups`, `templateEventGroups`.

- [ ] **Step 0: Record baselines**

From the Playground at the plan's starting commit: run `rtk npm run test:dom` once, then record `rtk grep -c "curry\|uncurry\|partialApply" generated-tests/Template.js`. Build the benchmark bundles with `rtk node benchmarks/frameworks/run.mjs --build --template-comparison` (stop it after the Vite build if it proceeds to run). Locate the bundle containing the `ranvier-template` adapter (read `run.mjs` and `benchmarks/frameworks/vite.config.mjs` for the output directory) and record its raw and gzip byte sizes.

- [ ] **Step 1: Write the failing tests**

Create `tests/TemplateCatalogueChecks.fs`:

```fsharp
module TemplateCatalogueChecks

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

let private host () =
    globalThis.document.createElement "main"

let private mount graph (host: HTMLElement) view =
    Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

type Decoy = { Index: string }
type Card = { Index: int; Name: string; Busy: Signal<bool>; Value: Signal<string> }

let private card =
    Component.define (fun _ (props: Card) -> props) (fun t ->
        t.div
            [| t.Attrs.className "card"; t.Attrs.id (fun r -> "card-" + string r.Index) |]
            [|
                t.input [| t.Props.Input.value (t.from (fun r -> r.Value)) |]
                t.button
                    [| t.Props.Button.disabled (fun r -> r.Busy.Value); t.Events.onClick (fun r _ -> r.Busy.Value <- true) |]
                    [| t.span [||] [| t.text (fun r -> r.Name) |] |]
            |])

let private labelInference () =
    let graph = Graph.create ()
    let host = host ()
    let busy = graph.Run (fun () -> Api.createSignal false)
    let value = graph.Run (fun () -> Api.createSignal "v")
    use _ = mount graph host (Component.render card { Index = 7; Name = "Ada"; Busy = busy; Value = value })
    equal "card-7" ((host.querySelector "div").getAttribute "id")
    equal "v" (unbox<HTMLInputElement> (host.querySelector "input")).value
    (unbox<HTMLButtonElement> (host.querySelector "button")).click ()
    equal true (unbox<HTMLButtonElement> (host.querySelector "button")).disabled

let cases =
    [|
        "a template lambda types against the instance when another record shares the field name", labelInference
    |]
```

The `Decoy` record declared before `Card` with the same `Index` label is the point of the test: `r.Index` must resolve to `Card.Index` (an `int`), so `string r.Index` yields `"7"`.

Adjust the `use _ = mount ...` and `Graph.create`/`graph.Run` lines to the exact helpers `tests/TemplateChecks.fs` uses for mounting and disposal (read its `mount` and a test that disposes, for example `eachRows`); keep the assertions.

Create `tests/template-catalogue.test.mjs` as a copy of `tests/template.test.mjs` importing `../generated-tests/TemplateCatalogueChecks.js`. Add `TemplateCatalogueChecks.fs` to `tests/DomTests.fsproj` directly after `TemplateChecks.fs`, and `tests/template-catalogue.test.mjs` to the end of the `test:dom` script.

Generator tests. Append to `tests.py`, inside the `with` block:

```python
    template = first.with_name(first.stem + "Template.fs")
    template_second = second.with_name(second.stem + "Template.fs")
    assert template.read_bytes() == template_second.read_bytes(), "template catalogue must be byte-identical"
    ttext = template.read_text()
    assert ttext.startswith("// <auto-generated>")
    assert "module Ranvier.View.Foundation.Generated.HtmlTemplate" in ttext
    assert coverage["templateTagCount"] == 112
    assert coverage["templateAttributeCount"] == coverage["attributeCount"]
    assert coverage["templatePropertyCount"] == ttext.count("TemplateParts.property ")
    assert coverage["templatePropertyCount"] == text.count("Typed.staticProperty (fun (node: 'native)"), "every shared settable property needs a template part"
    assert coverage["templateEventCount"] == ttext.count("TemplateParts.event ")
    assert "member _.``div`` (parts: Part<Dom.HTMLDivElement, 'inst> array) (children: TemplateView<'inst> array)" in ttext
    assert "member _.``br``(parts: Part<Dom.HTMLBRElement, 'inst> array) : TemplateView<'inst>" in ttext
    assert "type Groups.InputProperties<'inst> with" in ttext
    assert "member _.onClick<'native when 'native :> Dom.HTMLElement>(handler: 'inst -> Dom.MouseEvent -> unit)" in ttext
    assert "TemplateParts.readAttribute \"aria-expanded\" (fun instance -> Some (TemplateParts.ariaText (read instance)))" in ttext
    assert "uncurry" not in ttext and "curry" not in ttext
```

Adjust the two `templatePropertyCount` lines if the shared-property count and the per-tag exposure counts differ by design: the requirement is that every setter emitted under `Html.Properties.<owner>` has exactly one template property entry (three overloads), and the test must assert that equality using whichever count the generator records for it.

Append to `check-types.py`'s `examples` (each entry is `(name, body, expected diagnostic or None)`; follow the file's existing `prefix`, which must also open `Ranvier.View.Foundation.Template` and `Ranvier.View.Foundation.Generated.HtmlTemplate`):

```python
    ("template catalogue keywords", '''
type R = { Kind: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [||] [| t.``base`` [||]; t.button [| t.Attrs.Button.``type`` (fun r -> r.Kind) |] [||] |])
''', None),
    ("template property on another element", '''
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.div [| t.Props.Input.value (fun r -> r.Value) |] [||])
''', "FS0001"),
    ("template void tag with children", '''
type R = { Value: string }
let view = Component.define (fun _ (p: R) -> p) (fun t -> t.br [||] [||])
''', "FS0003"),
```

Use the diagnostic codes the compiler actually reports for the last two (run once and read them); the requirement is that both fail to compile.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `rtk proxy python tools/Ranvier.Dom.Generate/tests.py`
Expected: FAIL, because `firstTemplate.fs` does not exist.

- [ ] **Step 3: Add `NoPart` and `TemplateParts` to `Template.fs`**

Add the case to `Part` and skip it in `elementNode`:

```fsharp
type Part<'native, 'inst> =
    internal
    | StaticAttribute of string * string
    | StaticProperty of Func<obj, obj, unit> * obj
    | DynamicPart of Slot
    | EventPart of string * Func<obj, obj, unit>
    | NoPart
```

```fsharp
        | EventPart (name, handler) -> events.Add (name, handler)
        | NoPart -> ()
```

Insert after `definitionSame` (before `AttributeBuilder`):

```fsharp
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
        Source (Func<obj, obj>(fun instance -> box (map (unbox<'value> (read.Invoke instance)))))

    let presence enabled = if enabled then Some "" else None

    let ariaText enabled = if enabled then "true" else "false"

    let ariaOption (value: bool option) = Option.map ariaText value

    let property (write: 'native -> 'value -> unit) (value: 'value) : Part<'native, 'inst> =
        StaticProperty (writer write, box value)

    let readProperty (write: 'native -> 'value -> unit) (read: 'inst -> 'value) : Part<'native, 'inst> =
        DynamicPart (slot (PropertyTarget (writer write)) RenderMode read primitiveSame)

    let sourceProperty (write: 'native -> 'value -> unit) (source: Source<'inst, 'value>) : Part<'native, 'inst> =
        DynamicPart (sourceSlot (PropertyTarget (writer write)) source primitiveSame)

    let event name (handler: 'inst -> 'event -> unit) : Part<'native, 'inst> =
        EventPart (name, Func<obj, obj, unit>(fun instance event -> handler (unbox instance) (unbox event)))
```

`slot`, `sourceSlot`, `writer` and `elementNode` are `private` today; make them `internal` if the compiler rejects their use from the module (it is in the same file, so it should not).

Remove from the builders: `AttributeBuilder.className/id/title` (all overloads), `PropertyBuilder.value/disabled` (all overloads), `EventBuilder.onClick/onInput/onFocus`, and `TemplateBuilder.div/section/span/button/input`. Keep `attribute`, `propertyStatic`, `propertyWith`, `propertyFrom`, `on`, `element` and every structure member. Remove `someOf` if nothing else uses it (use `TemplateParts.mapSource present`).

- [ ] **Step 4: Collect template entries in the generator's existing loops**

In `generate`, beside `programElements`:

```fsharp
    let templateTags = ResizeArray<string>()
    let templateProperties = System.Collections.Generic.SortedDictionary<string, ResizeArray<string * string * string>>()
    let templatePropertyOwners = System.Collections.Generic.Dictionary<string, Type>()
    let templateGroupEvents = System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.SortedDictionary<string, string * string>>()
    let templateAttributes = ResizeArray<string * string * string * string * string>()

    let groupName (native: Type) =
        let name = native.Name
        let name = if name.StartsWith ("HTML", StringComparison.Ordinal) && name.Length > 4 then name.Substring 4 else name

        if name.EndsWith ("Element", StringComparison.Ordinal) && name.Length > 7 then
            name.Substring (0, name.Length - 7)
        else
            name
```

In the tag loop, after the `programElements.Add` calls:

```fsharp
        if voidTags.Contains tag then
            templateTags.Add
                $"    member _.``{tag}``(parts: Part<{name}, 'inst> array) : TemplateView<'inst> = TemplateParts.element {expression} parts [||]"
        else
            templateTags.Add
                $"    member _.``{tag}`` (parts: Part<{name}, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> = TemplateParts.element {expression} parts children"
```

Inside `if emittedProperties.Add (...)`, under `if canSet then`:

```fsharp
                            if not (templateProperties.ContainsKey owner.Name) then
                                templateProperties.Add (owner.Name, ResizeArray())
                                templatePropertyOwners.Add (owner.Name, owner)

                            templateProperties[owner.Name].Add (memberName, ownerName, valueType)
```

In the event loop, for an event that is not common (the branch that emits `on{...}` into the tag module, excluding the `input`/`click` compatibility re-emission):

```fsharp
            if not (commonEvents.Contains event.Key) then
                let group = groupName native

                if not (templateGroupEvents.ContainsKey group) then
                    templateGroupEvents.Add (group, System.Collections.Generic.SortedDictionary())

                match templateGroupEvents[group].TryGetValue event.Key with
                | true, (previous, _) -> require (previous = eventType) $"Conflicting template event type: {group}.{event.Key}"
                | _ -> templateGroupEvents[group].Add (event.Key, (eventType, name))
```

In the attribute policy loop, beside the `emit` calls for each entry:

```fsharp
            templateAttributes.Add (groupName, nativeName, helper, attribute, kind)
```

(`groupName` here is the attribute loop's existing local, the policy's `"module"` value; rename the new function above to `nativeGroupName` if the names clash.)

- [ ] **Step 5: Emit `HtmlTemplate.fs`**

After `contents` is computed, build and write the template file with the same `format`:

```fsharp
    let reserved =
        dict
            [
                "TemplateBuilder", set [ "Attrs"; "Props"; "Events"; "element"; "fragment"; "text"; "from"; "isolated"; "view"; "arm"; "matchWith"; "branch"; "each"; "render" ]
                "AttributeBuilder", set [ "attribute"; "data"; "aria" ]
                "PropertyBuilder", set [ "propertyStatic"; "propertyWith"; "propertyFrom" ]
                "EventBuilder", set [ "on" ]
            ]

    let t = ResizeArray<string>()
    let tEmit (line: string) = t.Add line
    tEmit "// <auto-generated> tools/Ranvier.Dom.Generate; regenerate instead of editing. </auto-generated>"
    tEmit "[<AutoOpen>]"
    tEmit "module Ranvier.View.Foundation.Generated.HtmlTemplate"
    tEmit ""
    tEmit "open Fable.Core.TS"
    tEmit "open Ranvier.View.Foundation"
    tEmit "open Ranvier.View.Foundation.Representation"
    tEmit "open Ranvier.View.Foundation.Template"
    tEmit ""
```

Then, in this order, each with `require` checks that no member name repeats within a builder or group, no name is in `reserved` for its builder, and no group name equals a flat member name of the same builder:

1. `module Groups =` with one `[<Sealed>] type <Group><Kind><'inst> internal () = class end` per attribute group (`Kind` = `Attributes`), property group (`Properties`) and event group (`Events`).
2. `type TemplateBuilder<'inst> with` + `templateTags` (tags are emitted sorted by name, as the loop already iterates).
3. `type AttributeBuilder<'inst> with`: `data` and `aria` (three overloads each, `TemplateParts.attribute (Typed.prefixedAttributeName "data-" suffix) value`, `readAttribute`, `sourceAttribute` with the same prefixed name), the flat attributes, then one `member _.<Group> = Groups.<Group>Attributes<'inst>()` per attribute group; then `type Groups.<Group>Attributes<'inst> with` + its entries.
4. `type PropertyBuilder<'inst> with`: flat properties (owners for which `owner.IsAssignableFrom typeof<Dom.HTMLElement>`), then group accessors; then each `type Groups.<Group>Properties<'inst> with` + its properties.
5. `type EventBuilder<'inst> with`: one member per `sharedEvents` entry, then group accessors; then each `type Groups.<Group>Events<'inst> with` + its events, constrained to the group's native type.

The member bodies, with `{c}` the constraint `'native when 'native :> {owner}`:

| Kind | Member lines |
|---|---|
| string attribute | `({c}) (value: string) = TemplateParts.attribute "{attr}" value`; `(value: string option) = TemplateParts.optionalAttribute "{attr}" value`; `(read: 'inst -> string) = TemplateParts.readAttribute "{attr}" (fun instance -> Some (read instance))`; `(read: 'inst -> string option) = TemplateParts.readAttribute "{attr}" read`; `(source: Source<'inst, string>) = TemplateParts.sourceAttribute "{attr}" (TemplateParts.mapSource Some source)`; `(source: Source<'inst, string option>) = TemplateParts.sourceAttribute "{attr}" source` |
| boolean attribute | `(value: bool) = TemplateParts.optionalAttribute "{attr}" (TemplateParts.presence value)`; `(read: 'inst -> bool) = TemplateParts.readAttribute "{attr}" (fun instance -> TemplateParts.presence (read instance))`; `(source: Source<'inst, bool>) = TemplateParts.sourceAttribute "{attr}" (TemplateParts.mapSource TemplateParts.presence source)` |
| ARIA boolean | `(value: bool) = TemplateParts.attribute "{attr}" (TemplateParts.ariaText value)`; `(value: bool option) = TemplateParts.optionalAttribute "{attr}" (TemplateParts.ariaOption value)`; `(read: 'inst -> bool) = TemplateParts.readAttribute "{attr}" (fun instance -> Some (TemplateParts.ariaText (read instance)))`; `(read: 'inst -> bool option) = TemplateParts.readAttribute "{attr}" (fun instance -> TemplateParts.ariaOption (read instance))`; `(source: Source<'inst, bool>) = TemplateParts.sourceAttribute "{attr}" (TemplateParts.mapSource (fun value -> Some (TemplateParts.ariaText value)) source)`; `(source: Source<'inst, bool option>) = TemplateParts.sourceAttribute "{attr}" (TemplateParts.mapSource TemplateParts.ariaOption source)` |
| property `'v` | `(value: {v}) = TemplateParts.property (fun (node: 'native) (value: {v}) -> (node :> {owner}).``{m}`` <- value) value`; the same writer with `TemplateParts.readProperty ... read` for `(read: 'inst -> {v})` and `TemplateParts.sourceProperty ... source` for `(source: Source<'inst, {v}>)` |
| event | `on{E}<{c}>(handler: 'inst -> {eventType} -> unit) : Part<'native, 'inst> = TemplateParts.event "{event}" handler` |

Every attribute, property and event member is `member _.``{name}``<{c}>(...) : Part<'native, 'inst> =`. Format `t` with `format`, then write it to `Path.Combine (Path.GetDirectoryName (Path.GetFullPath output), Path.GetFileNameWithoutExtension output + "Template.fs")` with `UTF8Encoding (false)`. Add the six coverage keys to the anonymous record.

Expected output, excerpted for the members tests use today:

```fsharp
// <auto-generated> tools/Ranvier.Dom.Generate; regenerate instead of editing. </auto-generated>
[<AutoOpen>]
module Ranvier.View.Foundation.Generated.HtmlTemplate

open Fable.Core.TS
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

module Groups =
    [<Sealed>]
    type ButtonProperties<'inst> internal () =
        class
        end

    [<Sealed>]
    type InputProperties<'inst> internal () =
        class
        end

type TemplateBuilder<'inst> with
    member _.``button`` (parts: Part<Dom.HTMLButtonElement, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateParts.element Button parts children

    member _.``div`` (parts: Part<Dom.HTMLDivElement, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateParts.element Div parts children

    member _.``input``(parts: Part<Dom.HTMLInputElement, 'inst> array) : TemplateView<'inst> =
        TemplateParts.element Input parts [||]

    member _.``section`` (parts: Part<Dom.HTMLElement, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateParts.element (HtmlTag "section") parts children

    member _.``span`` (parts: Part<Dom.HTMLSpanElement, 'inst> array) (children: TemplateView<'inst> array) : TemplateView<'inst> =
        TemplateParts.element (HtmlTag "span") parts children

type AttributeBuilder<'inst> with
    member _.``className``<'native when 'native :> Dom.HTMLElement>(value: string) : Part<'native, 'inst> =
        TemplateParts.attribute "class" value

    member _.``className``<'native when 'native :> Dom.HTMLElement>(value: string option) : Part<'native, 'inst> =
        TemplateParts.optionalAttribute "class" value

    member _.``className``<'native when 'native :> Dom.HTMLElement>(read: 'inst -> string) : Part<'native, 'inst> =
        TemplateParts.readAttribute "class" (fun instance -> Some (read instance))

    member _.``className``<'native when 'native :> Dom.HTMLElement>(read: 'inst -> string option) : Part<'native, 'inst> =
        TemplateParts.readAttribute "class" read

    member _.``className``<'native when 'native :> Dom.HTMLElement>(source: Source<'inst, string>) : Part<'native, 'inst> =
        TemplateParts.sourceAttribute "class" (TemplateParts.mapSource Some source)

    member _.``className``<'native when 'native :> Dom.HTMLElement>(source: Source<'inst, string option>) : Part<'native, 'inst> =
        TemplateParts.sourceAttribute "class" source

    // the same six overloads for ``id`` ("id") and ``title`` ("title")

type PropertyBuilder<'inst> with
    member _.Button = Groups.ButtonProperties<'inst>()
    member _.Input = Groups.InputProperties<'inst>()

type Groups.ButtonProperties<'inst> with
    member _.``disabled``<'native when 'native :> Dom.HTMLButtonElement>(value: bool) : Part<'native, 'inst> =
        TemplateParts.property (fun (node: 'native) (value: bool) -> (node :> Dom.HTMLButtonElement).``disabled`` <- value) value

    member _.``disabled``<'native when 'native :> Dom.HTMLButtonElement>(read: 'inst -> bool) : Part<'native, 'inst> =
        TemplateParts.readProperty (fun (node: 'native) (value: bool) -> (node :> Dom.HTMLButtonElement).``disabled`` <- value) read

    member _.``disabled``<'native when 'native :> Dom.HTMLButtonElement>(source: Source<'inst, bool>) : Part<'native, 'inst> =
        TemplateParts.sourceProperty (fun (node: 'native) (value: bool) -> (node :> Dom.HTMLButtonElement).``disabled`` <- value) source

type Groups.InputProperties<'inst> with
    // the same three overloads for ``value`` : string on Dom.HTMLInputElement

type EventBuilder<'inst> with
    member _.onClick<'native when 'native :> Dom.HTMLElement>(handler: 'inst -> Dom.MouseEvent -> unit) : Part<'native, 'inst> =
        TemplateParts.event "click" handler

    member _.onFocus<'native when 'native :> Dom.HTMLElement>(handler: 'inst -> Dom.FocusEvent -> unit) : Part<'native, 'inst> =
        TemplateParts.event "focus" handler

    member _.onInput<'native when 'native :> Dom.HTMLElement>(handler: 'inst -> Dom.InputEvent -> unit) : Part<'native, 'inst> =
        TemplateParts.event "input" handler
```

This excerpt is the expected shape of the generated output for the members tests use today; the generator writes every overload in full. Use the event types `Generated/Html.fs` uses for `click`, `focus` and `input`. `Template.fs`'s `Tag` cases `Div`, `Button`, `Input`, `Select`, `Option` are used for those tags, `HtmlTag "<tag>"` otherwise.

If the compiler rejects optional extensions on these generic builders, curried extension members, or overloads of generic extension members: record the exact error in the report and switch the generator to intrinsic members. To do that, split `Template.fs` into `TemplateModel.fs` (everything before `AttributeBuilder`) and `TemplateBuilder.fs` (the builders, `Component`), and make the generated `HtmlTemplate.fs` define the four builder classes whole between them, with the handwritten members emitted from a fixed preamble the generator copies. Record that ruling in the report.

- [ ] **Step 6: Add the file to every compile list and migrate the property call sites**

Insert `<Compile Include="...Generated/HtmlTemplate.fs" .../>` directly after each `Template.fs` entry found by `rtk grep -n "Template.fs\"" --glob "*.fsproj"`. Replace `t.Props.value` with `t.Props.Input.value` and `t.Props.disabled` with `t.Props.Button.disabled` in `tests/TemplateChecks.fs` and `tests/FrameworkPerf.fs`.

- [ ] **Step 7: Regenerate and run the generator tests**

Run from the repository root:
```bash
rtk dotnet run --project tools/Ranvier.Dom.Generate/Ranvier.Dom.Generate.fsproj -c Release
rtk proxy python tools/Ranvier.Dom.Generate/tests.py
rtk proxy python tools/Ranvier.Dom.Generate/check-types.py
```
Expected: generation succeeds; `Generated/HtmlTemplate.fs` is written; both scripts pass. `git diff --stat` shows `Generated/Html.fs` unchanged except for nothing at all (the Html output must stay byte-identical), and `Html.coverage.json` changed only by the new keys.

- [ ] **Step 8: Run the gates and check emitted JavaScript**

Run: `rtk npm run test:dom`, `rtk npm run test:browser`, `rtk dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release`.
Expected: all pass; the catalogue case passes; 13 PASS lines.

Then `rtk grep -n "curry\|uncurry\|partialApply" generated-tests/HtmlTemplate.js generated-tests/Template.js`.
Expected: no matches in `HtmlTemplate.js`; the `Template.js` count is at most Step 0's. Confirm the extension members are emitted as module-level functions, not class methods, and record two emitted names in the report.

- [ ] **Step 9: Measure the bundle**

Rebuild the benchmark bundles as in Step 0 and record raw and gzip sizes. Pick a generated property member the benchmark never uses and report whether its emitted name appears in the bundle, and the size change against Step 0. This is a measurement, not a gate. If unused members are retained, name the construct that keeps them (for example group accessors or class methods).

- [ ] **Step 10: Commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs examples/Fable.Ranvier.Playground/tests/TemplateCatalogueChecks.fs
rtk git add -A tools/Ranvier.Dom.Generate experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/tests examples/Fable.Ranvier.Playground/package.json
rtk git commit -m "feat(view): generate the template catalogue from the HTML element map"
```

---

### Task 2: Equivalence with `Html.Program`

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/tests/TemplateCatalogueChecks.fs`
- Modify: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx` (server-markup parity on .NET)

**Interfaces:**
- Consumes: the generated catalogue (Task 1), `Html.Program.*`, `Html.Authoring.*`, `Html.render`.

- [ ] **Step 1: Write the equivalence tests**

Add to `TemplateCatalogueChecks.fs` (open `Ranvier.View.Foundation.Generated`):

```fsharp
type Flags = { Name: string; Nickname: string option; Busy: bool; Expanded: bool option; State: string }

let private flagsTemplate =
    Component.define (fun _ (props: Flags) -> props) (fun t ->
        t.button
            [|
                t.Attrs.title (fun r -> r.Name)
                t.Attrs.id (fun r -> r.Nickname)
                t.Attrs.Button.disabled (fun r -> r.Busy)
                t.Attrs.ariaExpanded (fun r -> r.Expanded)
                t.Attrs.data ("state", fun r -> Some r.State)
            |]
            [| t.text "go" |])

let private flagsProgram (flags: Flags) =
    Html.Program.button
        [|
            Html.Authoring.Attrs.title (fun () -> flags.Name)
            Html.Authoring.Attrs.id (fun () -> flags.Nickname)
            Html.Attrs.Button.disabledReactive (fun () -> flags.Busy)
            Html.Attrs.ariaExpandedReactive (fun () -> flags.Expanded)
            Html.Attrs.dataReactive "state" (fun () -> Some flags.State)
        |]
        [| Program.text "go" |]

let private attributeParity () =
    for flags in
        [
            { Name = "a"; Nickname = None; Busy = false; Expanded = None; State = "x" }
            { Name = "b"; Nickname = Some "n"; Busy = true; Expanded = Some false; State = "y" }
            { Name = "c"; Nickname = Some "m"; Busy = false; Expanded = Some true; State = "z" }
        ] do
        equal
            (Html.render Omit (Program.run (flagsProgram flags)))
            (Html.render Omit (Program.run (Component.render flagsTemplate flags)))
```

Use the exact module paths and member names `Generated/Html.fs` emits (`Html.Authoring.Attrs` overloads for string attributes, `Html.Attrs.<Group>.<helper>Reactive` for the others); `ariaExpanded` must be a flat attribute and `disabled` a `Button` attribute per `attributes.json`. Add the static forms (`t.Attrs.Button.disabled false`, `t.Attrs.ariaExpanded false`, `t.Attrs.id (None: string option)`) in a second template and compare against the static `Html` helpers the same way.

Add a live-DOM test that mounts a template using one property of each value type the catalogue emits — `string` (`t.Props.Input.value`), `bool` (`t.Props.Input.``checked```), `float` (a `float` property such as `t.Props.Meter.value` or `t.Props.Progress.value`, whichever the generated file contains), an option property if one exists (search the generated file) — in static, reader and source forms, writes new values through signals, and asserts each node property after `Api.flush ()` (or the flush helper `TemplateChecks.fs` uses).

Add an event test: a grouped element-specific event (pick one from a `Groups.<Group>Events` type in the generated file, for example a `Video` or `Dialog` event) and a flat one (`onInput`), dispatched with `new Event(name, { bubbles = true })` through jsdom, asserting the handler received the instance.

Add an isolation test: `t.isolated (t.Attrs.title (fun r -> ...))` on a generated attribute part mounts, updates through its own effect (the reader runs when its signal changes and other slots' readers do not; count reader calls), and `t.isolated (t.Attrs.title "static")` raises `ArgumentException` at definition time.

Register each in `cases` with names stating the guarantee.

- [ ] **Step 2: Run them to verify they pass, then make one fail**

Run `rtk npm run test:dom`. Expected: PASS. Then temporarily change the boolean `false` mapping in `TemplateParts.presence` to `Some "false"`, rerun, confirm `attributeParity` fails, and revert. Record both runs in the report.

- [ ] **Step 3: Add the .NET markup check**

In `RepresentationChecks.fsx`, add a check that renders `flagsTemplate`'s equivalent (define a template there with the same parts) through `Html.render Omit` on .NET and compares with the string the Fable test produced, following how the existing `... server markup under Fable matches .NET` checks pair a .NET string with a Fable test (read one, for example `matchMarkup`, and its .NET counterpart). Expected: 14 PASS lines.

- [ ] **Step 4: Run all gates, check emitted JS, commit**

```bash
rtk dotnet fantomas examples/Fable.Ranvier.Playground/tests/TemplateCatalogueChecks.fs
rtk git add examples/Fable.Ranvier.Playground/tests experiments/Ranvier.View.Foundation/RepresentationChecks.fsx
rtk git commit -m "test(view): pin generated template parts to Html.Program output"
```

---

### Task 3: Measurement and documentation

**Files:**
- Modify: `docs/research/2026-10-14-component-templates.md`
- Create: raw benchmark JSON under `docs/research/framework-performance/investigation/template-catalogue/`
- Modify: `docs/superpowers/specs/2026-10-13-component-templates-design.md` (clarifications)
- Modify: `tools/Ranvier.Dom.Generate/README.md`
- Modify: `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` (status and checkboxes)
- Modify: `docs/research/2026-10-11-view-poc-checkpoint.md` (index)

- [ ] **Step 1: Paired benchmark against `0e82e46`**

Run the template comparison at `0e82e46` and at this branch's head alternately, three runs each, with `rtk node benchmarks/frameworks/run.mjs --build --template-comparison` (from the Playground; build each commit in its own worktree under the scratchpad). Commit the raw JSON under `docs/research/framework-performance/investigation/template-catalogue/` with machine, Chromium version, date and commit in the file names or a `README.md` beside them. Expected: mount, update and dispose ratios to Solid 2 at 1, 16 and 128 rows within each other's run-to-run ranges; the benchmark row uses the same parts as before, now through extensions.

- [ ] **Step 2: Research note**

Add a section "Generated catalogue (plan 2c)" stating: the surface rules (the Surface decisions table, condensed); the generated counts from `Html.coverage.json`; the generated file size; the bundle sizes and the unused-member result from Task 1 Steps 0 and 9; the benchmark table with links to the raw JSON; the emitted-JS check result. Update the Acceptance line's test totals to the suites' current counts.

- [ ] **Step 3: Spec clarifications and generator README**

In the spec's clarifications, add: generated members are builder members (`t.div`, `t.Attrs.title`, `t.Props.Input.value`, `t.Events.onClick`); the grouping rule; the overload table. In the generator README, add a paragraph describing the second output, its coverage keys and its regeneration command, and update the generated-size sentence.

- [ ] **Step 4: Refresh the framework plan's status**

For every unchecked item in `2026-10-06-fable-ranvier-framework.md`, check it only when a committed test or research note demonstrates it, and append the evidence link in the same bullet; leave it unchecked otherwise. Items known to be demonstrated by this branch: Task 6's keyed branches and snapshot keyed lists (`tests/TemplateChecks.fs`, `tests/RangeChecks.fs`, `docs/research/2026-10-11-view-poc-checkpoint.md`), Task 7's generated subset (now the full 112-tag map plus the template catalogue). Rewrite the Status paragraph to state what exists today (components, keyed lists and branches, templates with dynamic structure, the generated DOM and template catalogues, Solid 2 comparisons) and what remains open (list/keyed-row performance, structural boundaries, server HTML and hydration, sugar). Add plans 2a, 2b and 2c to the checkpoint index with one line each.

- [ ] **Step 5: Commit**

```bash
rtk git add docs tools/Ranvier.Dom.Generate/README.md
rtk git commit -m "docs(view): record the generated template catalogue and refresh the framework status"
```
