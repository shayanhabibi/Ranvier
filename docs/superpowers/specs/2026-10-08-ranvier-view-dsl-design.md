# Ranvier view DSL design

Date: 2026-10-08. Status: typed-function direction approved, conditional on the performance proof below. No proposed API below has been implemented or compiler-validated.

Baseline: `174f07649f38b1ff7a3ebc60c6c3fb9f290d1cb0` (`chore: checkpoint DOM PoC and framework design`). This design refines the [framework brief](2026-10-06-fable-ranvier-framework-brief.md), especially C01, C02, C04, C07, C09 and C10. The existing [implementation plan](../plans/2026-10-06-fable-ranvier-framework.md) remains the delivery sequence; this document does not authorize skipping its lifecycle and portability proofs.

## Intent and selected direction

The user selected explicit typed functions: element helpers, modifiers and child lists. Build an ordinary F# authoring surface that describes reusable views, preserves explicit reactive inputs and can later support generated HTML coverage. No CE, Fable plugin or generator is required to use the initial core. Optional sugar must expand into the same operations.

Assumptions for review: a single opaque, backend-neutral `View` is the child/composition currency; element-specific typing belongs to modifier inputs and mounted references; construction of a view does not read reactive getters, allocate DOM, register listeners or establish mounted owners. Shared captured state follows ordinary F# semantics. Instance-local state belongs in component setup.

## Function shapes considered

1. **Typed modifier list plus child list (recommended).** Element constructors accept a modifier list whose element type is fixed by the tag, and return an opaque `View`. Normal elements accept `View list` children; void elements have no child argument. This is close to the PoC's vocabulary while replacing live nodes with definitions. It permits generated property/event helpers and invalid-combination compiler checks.
2. **Uniform untyped modifier list.** Every constructor accepts the same modifier type. This is smaller to implement, but input-only operations can reach unrelated elements unless checked at runtime. It can serve an internal representation, but does not satisfy the desired public element/property typing by itself.
3. **Typed props records plus child list.** Each element accepts a generated props record. This gives named fields and record updates, but optional fields, event options, separate static/reactive operations and extensible attributes make the record model larger. Defer unless real authoring examples show that modifier lists are cumbersome.

## Proposed authoring surface

Illustrative syntax only; policy and helper names below describe roles and are not final exported names:

```fsharp
let counterView count textBinding clickPolicy =
    Html.div
        [ Attr.id "counter" ]
        [ Html.button
              [ On.click clickPolicy (fun _ -> count.Value <- count.Value + 1) ]
              [ View.text "Increment" ]
          View.bindText textBinding (fun () -> string count.Value) ]

let nameView name valueBinding inputPolicy =
    Html.input
        [ Attr.id "name"
          Input.bindValue valueBinding (fun () -> name.Value)
          Input.onInput inputPolicy (fun input event -> name.Value <- input.value) ]
```

The intended constructor contract is conceptually `Html.button : Modifier<HTMLButtonElement> list -> View list -> View`, and `Html.input : Modifier<HTMLInputElement> list -> View`. Generic shared helpers such as `Attr.id` must specialize to the element type selected by the constructor. F# generic inference and constraints must be proved before these signatures are accepted. A compiler-negative example must reject an input-only value modifier applied to a `div`.

Typed element references are available through an explicitly browser-only mounted hook. Element constructors return reusable definitions rather than already-created native elements. Event helpers preserve the actual event-map type. When a handler needs its registered element, pass a separately typed element argument; the event's general `target` remains general and nullable. Do not narrow `event.target` through an unchecked cast.

## Modifier and binding contracts

Keep attributes, live properties and listeners distinct. Static operations capture their supplied value. Reactive operations receive an explicit reader and binding policy; they do not infer reactivity from a signal read performed while constructing an argument. Readers are invoked by an interpreter under the selected binding contract, never by the constructor.

Binding policy supplies explicit equality and pending/error behavior; scope policy supplies scheduling and reporting. Named reusable policy values can keep examples concise, but their behavior must be documented. Creating a reader does not automatically create a memo.

Portable modifiers carry enough metadata for browser application and server HTML interpretation. Arbitrary browser setters and mounted hooks use an explicitly browser-only surface with the fallback/rejection contract required by C07/C09. Native browser interop cannot silently become part of portable component setup.

## Composition and instance ownership

`View.empty`, `View.text` and `View.fragment` return reusable definitions. Child lists contain definitions. Instantiating the same definition twice creates independent DOM, listeners, bindings and owners, while captured state can intentionally remain shared.

Components use the brief's explicit setup scope and typed props. Setup executes once, untracked, per mounted instance. A component operation establishes child ownership; an ordinary F# helper function that merely returns a view does not implicitly create a component scope.

Reactive branches and keyed lists remain separately named operations. Ordinary F# `if`, `match` and list construction perform ordinary evaluation. The first foundation proof does not implement structural reactivity or choose identity policies implicitly.

## Internal representation and proof sequence

Keep `View` opaque so the authoring surface does not choose between the instruction-tree and backend-neutral build-program variants. Compare both behind a minimal equivalent surface in the next approved experiment. Both must represent empty/fragment/text/element/component definitions without browser nodes in the portable contract.

The experiment must prove: no DOM at definition construction; independent instantiation; escaped server output on .NET; child-first attached-DOM cleanup through mount, graph and owning-effect disposal; continued cleanup after a failure; and compiler acceptance/rejection of shared versus element-specific modifiers.

An initial SageFs audit of the existing core on 2026-10-08 returned `parent -> child` for a parent root containing a child root. This confirms the known incompatibility with the renderer's child-first contract. A renderer adapter must be proved across disposal entry points before a public view representation or component interface is frozen. This audit is not the new adapter proof.

## Performance and sugar contract

The user's approval is conditional: the core DSL must be extremely performant and support Oxpecker-style or Feliz-style sugar over it. These names describe desired authoring styles, not a commitment to depend on either library. Ergonomic wrappers must lower into the same definitions and runtime operations with the same policies.

Separate four costs: definition construction, mounted-instance creation, steady-state reactive updates and disposal. Reusable definitions may allocate lists, descriptors or closures; measure those costs rather than promising allocation-free F# authoring. Reuse must amortize definition construction without sharing mounted resources. Static content creates no reactive observers, memos or scheduler entries.

Mounting may interpret a definition once. A scalar binding update must reach its mounted target directly, without walking the view, rebuilding modifier/child lists or rerunning component setup. Neither sugar nor typed helper dispatch may introduce additional mounted owners, reactive computations, listener registrations or scheduling waves. Avoid reflection and runtime property-name lookup in typed property/event helpers; inspect generated Fable JavaScript to verify the actual lowering.

Before choosing a representation, compare the instruction and build-program candidates with identical DOM, ownership and policies. Include static-heavy trees, many small siblings, nested fragments, repeated independent mounts of a reusable definition, text/property updates and repeated mount/dispose cycles. Measure construction separately from mounting, and direct updates separately from construction. Use the existing PoC operations as a reactive baseline and equivalent direct DOM operations to expose abstraction overhead; neither comparison alone establishes framework parity.

Add tiny function-style and builder-style sugar probes during the foundation experiment, before implementing complete sugar libraries. Compare their Release JavaScript with direct typed-function authoring. Check semantics, definition allocations, mounted allocations, update work and bundle contribution. F# `inline`, generic specialization and builder syntax are hypotheses to verify, not guarantees of erasure. Any residual wrapper allocation or dispatch must be reported and either removed or explicitly accepted; do not label a wrapper zero-cost from syntax alone.

Report repeated-run timings and variability, operation/resource counts, and allocation evidence where the tooling supports it. Keep noisy heap observations separate from exact counters. Set numerical regression budgets from the measured baseline before public API freeze. A representation is not accepted merely because it passes correctness tests or wins one mount benchmark.

## Generation relationship

Begin with a handwritten representative HTML subset. A later generator can produce the same constructors and modifiers. The [catalogue-backed Wire option](../../research/xantham-ranvier-dom-evaluation.md#alternative-minimal-generator-over-xanthamtypescriptwire) must preserve this surface and its runtime contracts. As of the [2026-10-11 artifact check](../../research/2026-10-11-xantham-release-catalogues.md), Generator 0.2.0 supports portable/Brotli file catalogues, but published Core.TS 0.2.0 contains no catalogue. [Xantham #110](https://github.com/shayanhabibi/Xantham/issues/110) is closed; package distribution and supported identity lookup remain prerequisites to verify rather than assumed released capabilities.

Reuse Xantham's native bindings, including `HTMLInputElement` and non-element references such as `FileList`. Generated Ranvier definitions are a separate authoring surface. Neither generator availability nor an upstream binding-map change blocks the handwritten lifecycle and portability experiment.

## Review and acceptance

The user approved the constructor direction subject to the performance and sugar contract above. Exact helper names remain illustrative until the compiler/portability experiment selects workable signatures; this is an explicit experiment scope, not a claim of an existing API.

Narrow Task 2's implementation plan around the handwritten subset, representation/disposal proofs and sugar overhead probes. Keep existing PoC behavior available while testing the foundation. No runtime implementation belongs in this design checkpoint.
