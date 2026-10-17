# Component templates design

Date: 2026-10-13. Status: design approved section by section; awaiting review of this document. Nothing below is implemented.

Baseline: `1c317d2` on `poc/fable-ranvier-dom`. Motivation: the [mount cost rationale](../../research/framework-performance/investigation/mount-rationale.md) and the [allocation cuts](../../research/2026-10-13-allocation-cuts.md). After those cuts a 128-row mount allocates about 513 KiB against Solid 2's 140 KiB; about 140 KiB of Ranvier's comes from each instance rebuilding its view definition, and a further share from one effect, owner link and observer entry per binding.

## Intent

Build a component's definition once per component type instead of once per instance, with bleeding-edge mount performance as the goal and Solid 2 as the reference. Prior art, including Fable.Ripple's templates, is a comparison point only.

Decisions taken during design:

- **Explicit, opt-in API.** A template is a typed value whose dynamic parts receive the instance as a parameter. The type system guarantees that a template captures no instance state. Existing `createComponent` code is unchanged. Implicit caching was rejected because closures capture instance values, structure can vary per instance, closures created per call have no stable identity, and setup must still run per instance. A Fable compiler plugin was rejected as it replaces the no-compiler design point; it remains possible later on top of this API.
- **Portable.** A template is a backend-neutral value. The DOM backend compiles it; server HTML (.NET) and the tree representation expand it.
- **Server means .NET.** Templates and every `T.*` helper compile and run on .NET. DOM types are type tags there; no `Emit` or `jsNative` code is reachable on the server path.
- **Hydration is out of scope**, but the skeleton is round-trippable HTML so a later hydrating mount can reuse the walk.

## Goals and non-goals

Goals:

- Per instance: one `cloneNode`, one state object, one render effect, one property write per event node, and one fixed-dependency binding per direct slot. Everything else exists once per component type.
- 128-row benchmark row, against Solid 2.0.0-rc.14 in the same processes: mount within 1.3× Solid 2; JavaScript allocation within 1.5× Solid 2 (about 210 KiB). These are targets for the measurement section, not acceptance gates.
- Every guarantee kept by owner lifetime and delegated listeners: `use`-order cleanup with DOM attached, rollback after a failing setter, owner-checked handlers, and a reader failure confined to its slot.

Non-goals:

- Hydration, server state transfer and streaming.
- Slimmer keyed row mounts; rows remain nested mounts and are a separate, list-benchmark-driven change.
- Runtime code generation (`new Function`); a possible later specialisation behind a CSP check.
- Changes to ordinary components, which keep their own owner.

## Authoring surface

```fsharp
type Row = { Index: int; Source: Signal<string>; Disabled: Memo<bool>; Local: Signal<int> }

let row : Component<RowProps> =
    Component.define
        (fun setup props ->
            { Index = props.Index; Source = props.Source; Disabled = props.Disabled; Local = Api.createSignal 0 })
        (T.div [| T.Attrs.className "panel"
                  T.Attrs.id (fun r -> "row-" + string r.Index)
                  T.Attrs.title (T.from (fun r -> r.Source)) |]
               [| T.input [| T.Props.Input.value (T.from (fun r -> r.Source)) |]
                  T.button [| T.Props.Button.disabled (fun r -> r.Disabled.Value)
                              T.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1) |]
                           [| T.text (fun r -> string r.Local.Value) |] |])

H.Program.section [||] [| for i in 1..128 -> Component.render row { Index = i; Source = source; Disabled = disabled } |]
```

- `Component.define (setup: Setup -> 'props -> 'inst) (template: Template<'inst>) : Component<'props>` is created once, normally at module level. Its value is the component's identity.
- `T.*` helpers are generated from the same catalogue as `H.Program.*`, with the same element typing. A value argument gives a static part; an `'inst -> value` argument gives a dynamic part. Static and dynamic forms are overloads.
- `T.from (fun r -> r.Source)` binds a part directly to one source: the accessor returns a `Signal<'v>` or `Memo<'v>` rather than reading it, so the dependency is known from the type. A direct binding has no tracking and no effect (see Direct bindings). A distinct marker is used rather than an overload on the lambda's return type, which F# overload resolution cannot reliably distinguish.
- Event handlers are `'inst -> Event -> unit`: one shared function per template.
- `T.isolated part` gives one dynamic part its own effect, for readers too expensive to re-run with the instance's other slots; the documented remedy before it is a memo created in setup.
- Dynamic structure: `T.render childDef (fun r -> props)`, `T.view (fun r -> view)`, `T.matchWith (fun r -> union) arms`, `T.branch (fun r -> key) arms`, `T.each key (fun r -> items) row`. `T.matchWith` is the preferred branch form: arms are chosen by union case, the compiler checks exhaustiveness, and each arm is a component definition whose props come from the case payload.
- `Component.render def props : Program.View` and `Component.renderWith ops def props : 'view` embed a template component in any representation.

### Generated catalogue clarifications

Plan 2c ([implementation plan](../plans/2026-10-14-component-templates-catalogue.md)) generates the builder's element, attribute, property and event members. `T.*` in this document means these members.

- **Builder members.** Generated helpers are members of the template builder: `t.div`, `t.Attrs.title`, `t.Props.Input.value`, `t.Events.onClick`. They are optional type extensions in `[<AutoOpen>] module Ranvier.View.Foundation.HtmlTemplate` (`Generated/HtmlTemplate.fs`); `open Ranvier.View.Foundation` brings them into scope.
- **Names.** Tags and properties keep their declared names, backticked where they are F# keywords (``` t.``base`` ```). Attribute members take the `authoring.json` renames that `Html.Authoring` uses, so the class attribute is `t.Attrs.className`. Events are `on` followed by the event name with its first letter upper-cased (`onClick`, `onAnimationcancel`).
- **Grouping.**

  | Builder | Flat members | Groups |
  |---|---|---|
  | `t` | one member per tag of `HTMLElementTagNameMap` (112); void tags take parts only; raw-text tags (`iframe`, `script`, `style`, `noscript`) take no children | |
  | `t.Attrs` | the policy group with `"module": ""`, plus `data` and `aria` | one group per other policy group (`t.Attrs.Button.disabled`) |
  | `t.Props` | settable properties declared on `HTMLElement` or its ancestors and mixins | one group per other declaring type (`t.Props.Input.value`) |
  | `t.Events` | events of `HTMLElementEventMap` | element-specific events, grouped by the tag's native type (`t.Events.Video.onWaitingforkey`); media events appear under `Audio` and `Video` |

  A group is named by its native type with a leading `HTML` and a trailing `Element` removed: `HTMLInputElement` gives `Input`, `HTMLTableCellElement` gives `TableCell`. Template attribute groups are named by native type too, so `t.Attrs.TextArea` matches `t.Props.TextArea`; `Html.Attrs` keeps the policy module names (`Html.Attrs.Textarea`).
- **Element typing.** A flat or grouped attribute or property member is generic in `'native` with `'native :> <declaring native type>`, and a tag member fixes `'native` to the tag's native type, so `t.Props.Input.value` inside `t.div` is a compile error. Grouped events are constrained to the tag's native type: `t.Events.Video.onWaitingforkey` takes `'native :> HTMLVideoElement`, although `HTMLMediaElementEventMap` declares the event.
- **Child-replacing setters.** Template property members exclude setters that replace the element's child nodes: `text` on `HTMLAnchorElement`, `HTMLOptionElement`, `HTMLTitleElement` and `HTMLScriptElement`, `value` and `defaultValue` on `HTMLOutputElement`, and `defaultValue` on `HTMLTextAreaElement`. `Html.fs` keeps them. Coverage lists them under `templateOmissions` with the reason "setter replaces child nodes".
- **Raw-text elements.** A raw-text element (`iframe`, `script`, `style`, `noscript`) mounts in the DOM when it has no children. A raw-text element with children fails when the template compiles, and server markup rejects every raw-text element, as `Html.render` does.
- **Overloads.**

  | Kind | Overloads |
  |---|---|
  | string attribute | `string`, `string option`, `'inst -> string`, `'inst -> string option`, `Source<'inst, string>`, `Source<'inst, string option>` |
  | boolean attribute | `bool`, `'inst -> bool`, `Source<'inst, bool>` |
  | ARIA boolean attribute | `bool`, `bool option`, `'inst -> bool`, `'inst -> bool option`, `Source<'inst, bool>`, `Source<'inst, bool option>` |
  | `data`, `aria` | `(suffix, string)`, `(suffix, 'inst -> string option)`, `(suffix, Source<'inst, string option>)` |
  | property of type `'v` | `'v`, `'inst -> 'v`, `Source<'inst, 'v>` |
  | event of type `'e` | `'inst -> 'e -> unit` |

- **Attribute values.** Generated attributes have `Typed`'s semantics: a string attribute is removed on `None`; a boolean attribute is `""` when true and absent when false; an ARIA boolean writes `"true"` or `"false"` and is removed on `None`. String readers and sources map `null` to `None`, which removes the attribute. `data-` and `aria-` names are checked by `Typed.prefixedAttributeName`.
- **Name validation.** `t.Attrs.attribute` validates its name when the template is built, so an invalid name raises at `Component.define`.
- **Attribute order.** Template markup lists static attributes before dynamic ones; `Html.Program` keeps authoring order. A template and its `createComponent` twin produce the same markup up to attribute order.
- **Emitted JavaScript.** Under Fable, each extension member compiles to a module-level function that takes the builder first. Vite removes unused members; a used group accessor constructs one small `Groups.*` instance.

## Definition and instance model

`Component<'props>` derives from a non-generic `ComponentDefinition` holding the erased setup, the template tree, and one lazily filled compiled-form slot per backend kind.

The template tree is immutable. Nodes: element (tag, static attributes and properties, dynamic slots, event slots, children), static text, text slot, child template, view hole, branch, keyed list, fragment. Slots are typed by value kind: `string`, `string option`, `bool`, `int`, `float`, and a general kind carrying its comparer. Each kind has a specialised comparison (`===` for the primitive kinds) and the cheapest write for its target: `Text.data` for text, direct property assignment, `toggleAttribute` for boolean attributes, and `setAttribute` or `removeAttribute` for optional strings. Only the typed `T.*` constructors create slots. Static attribute names are validated when the template is built.

Per instance:

1. Setup runs untracked under the enclosing owner and returns the instance record.
2. The backend materialises the template for that record.

A template instance has no owner of its own. It cannot outlive or be disposed apart from its enclosing mount, branch arm or keyed row, each of which already has an owner, so its cleanups and render effect register on the enclosing owner and are torn down in `use` order. `Setup.OnCleanup` runs when the enclosing structure is disposed. A throwing setup or setter rolls back the mount; handlers are inert once the enclosing owner is disposed.

## DOM backend

### Compilation, once per definition

- **Markup.** One portable function, shared with the server path, renders the template's static structure, static attributes and properties, and static text into an HTML string with markers:
  - `<!--[-->` and `<!--]-->` delimit a region: branch, keyed list or view hole.
  - A text slot is a text node containing one space. Where it would be adjacent to other text or to a region, a `<!--!-->` separator is emitted so parsing preserves node boundaries.
  - Child templates are inlined: their markup is part of the parent's, so one clone covers a parent and all inlined descendants.
- **Skeleton.** The markup is parsed once through a `<template>` element. The skeleton must be round-trippable: serialising it and parsing the result yields the same node structure. Compilation asserts this in development builds.
- **Walk program.** An integer array of `FirstChild`, `NextSibling`, `Parent` and `Collect` steps reaching every dynamic node of a clone in document order, interpreted by a tight loop.
- **Slot table.** Per slot: node index, target (attribute, property, text, event, structure), value kind, binding mode (render effect, isolated or direct), shared reader, accessor or handler, and for child templates the inlined child's slot range.
- **Specialised materialiser.** Compilation records which stages the template needs (render effect, isolated effects, direct bindings, events, regions, child templates) and composes a materialiser from only those stages, so instances never test for absent features.

### Materialisation, per instance

1. `skeleton.cloneNode(true)`; the walk collects dynamic nodes.
2. One state object: a fixed-length array whose layout is fixed per template, holding the instance record, enclosing owner and graph, dynamic node references, and previous slot values. Every instance of a template shares one array shape.
3. One render effect over the render-effect slots: read, compare by the slot's kind, write only on change, record. The first run writes every slot. Each reader is guarded on its own; a throwing reader fails its slot and leaves the others running. A throwing setter during setup rolls back the mount. Each `T.isolated` slot gets its own effect.
4. Direct bindings: each `T.from` slot attaches a fixed-dependency binding to its source (see Direct bindings), writes the current value, and writes again when the source changes.
5. Events: each event node receives one property, `node.$$ranvier = state`. The delegated dispatcher, finding that property, locates the node among the state's dynamic nodes, takes the event slot for that node and event type from the template's slot table, and calls `handler state.Inst event` under the state's graph and owner, untracked; it does nothing if that owner is disposed. Non-bubbling events attach a direct listener as today.
6. Inlined child templates: each child instance runs its setup, receives its own state object and render effect over its slot range, and reuses the parent's cloned nodes.
7. Regions mount their content at the anchors. `T.matchWith` compiles each arm's definition once and switches arms when the union case tag changes; a payload change within the same case updates the current arm's props without remounting it. `T.branch`, `T.each` and view holes use the existing branch, keyed and interpreter paths. An arm switch of `T.matchWith` or `T.branch` runs the previous arm's cleanups and removes its nodes before the new arm is set up; `T.each` sets up and places new rows before removed rows' cleanups run.
8. The clone joins the mount through the existing append path.

### Direct bindings

A direct binding is a computation with exactly one source, fixed when it is created. It re-collects no dependencies, allocates no tracking scope, and is scheduled with effects, so it honours batching, queued commits and `flush()`. It is owned by the enclosing owner and disposed with it. A `Memo` source that is pending or failed leaves the bound value unchanged, as a failed reader does in the render effect.

Ranvier's core exposes no such primitive: sources notify observers through the internal `IComputation` interface, and the public consumers, effects and memos, re-collect their dependencies on every run. The fixed-dependency binding is a core addition, made in its own change to `master` before this work depends on it, with its own tests and benchmarks. That change starts with a core benchmark of creation, disposal and update for a binding against an equivalent effect. The binding is added only if the benchmark shows a gain; otherwise `T.from` remains an alias for a render-effect slot and core is unchanged.

## Non-DOM backends

`Backend` gains `abstract Template: ComponentDefinition * obj -> unit` with a default implementation that runs setup and expands the template into existing `Backend` calls: static parts to `Attribute`, `Property` and `Text`; slots to `ReactiveAttribute`, `ReactiveProperty` and `Bind` with readers applying the slot to the instance; events to `Listen`; structure to `Component`, `Keyed` for lists and `Switch` for arm switches. The mount backend overrides it with the compiled path. Server HTML, the tree representation and `Browser.fs` use the default; prepared skeletons reject templates through it.

Server HTML for a template is the same markup function's output with slot values filled, so markers and escaping are identical on .NET and under Fable.

## Supersession

`Prepared` templates are superseded: the template adapter replaces the prepared benchmark adapter, and `Prepared` is marked superseded in its documentation. Removal is a later decision.

## Testing

- Equivalence: each template feature has a `createComponent` twin; both produce identical server HTML on .NET and under Fable, and identical live DOM under the same updates.
- Cross-runtime: a template's HTML on .NET byte-matches its HTML under Fable.
- Materialisation: clone and walk including `<template>` content, multiple roots and separators; comparison and writes for each slot kind; first-run writes; `T.isolated`; direct bindings including batching, flush, pending and failed memos, and disposal; reader failure confined to its slot; setter failure rolling back the mount; delegated template handlers including after disposal and after movement; cleanup order without an instance owner; materialisers composed without absent stages.
- `T.matchWith`: arm switching by case, payload updates within a case, disposal of the previous arm before the new arm's setup, and compile-time exhaustiveness.
- Structure: inlined child templates, view holes, branch switching and disposal, keyed lists of template rows.
- Round-trip: every test skeleton round-trips.
- Fallback: templates render through `Browser.fs` and the tree representation, and prepared skeletons reject them.
- Existing suites pass unchanged.

## Measurement

- A `ranvier-template` adapter in the framework harness renders the benchmark row as a template, alongside the authored adapter, against Solid 2 with `flush()` after timed writes.
- Allocation per mount from the sampler against Solid 2 and Ripple.
- The ladder's component-ownership rung re-run to confirm the per-instance owner cost is absent for templates.

## Risks and open questions

- **Readers re-run together.** Any signal change re-evaluates every non-isolated reader of an instance. Mitigation: memos in setup, `T.isolated`.
- **Generated helper surface.** `T.*` doubles the generated surface; the generator must emit both forms from one catalogue entry.
- **Typed slot storage.** Fable function adaptation can reintroduce closures where typed slots meet the backend; the implementation must verify emitted JavaScript, as the allocation cuts did.
- **Core primitive.** Direct bindings depend on a core addition; until it lands, `T.from` slots fall back to the render effect, so the template work is not blocked.
- **Inlining depth.** Deeply nested inlined children lengthen the walk program; measure before adding limits.
- **Keyed rows** remain nested mounts; list performance is a separate design.
- **Text slot placeholders.** A single-space text node is dropped or moved by the HTML parser in some contexts, such as directly inside `table` or `select`. The development-build round-trip assertion detects this; in those contexts the slot uses a `<!--#-->` placeholder that materialisation replaces with a text node.
