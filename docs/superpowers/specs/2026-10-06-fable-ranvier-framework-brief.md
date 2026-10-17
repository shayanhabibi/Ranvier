# Fable.Ranvier framework brief

Date: 2026-10-06. Status: proposed specification, consolidating the design interview. Implementation mechanisms remain proposals until validated.

Companion documents: [variants, performance and gotchas](2026-10-06-fable-ranvier-framework-variants-and-risks.md) and [implementation plan](../plans/2026-10-06-fable-ranvier-framework.md).

## Purpose

Build a typed F#/Fable frontend framework powered by Ranvier's fine-grained reactivity. Components create DOM directly; updates target bindings and structural regions without a virtual DOM or repeated component rendering. Aim for Solid-like performance, accepting modest, measured overhead where it materially simplifies F# authoring. Performance parity is a target, not an established result.

The core makes reactive inputs, ownership, scheduling and rendering policies explicit. Convenience libraries and an optional Fable plugin may supply sugar with documented expansion into that core. Ordinary F# functions must remain sufficient to build a complete application.

Keep the reactive foundation consistent between .NET and Fable. Prepare a renderer boundary for server HTML and hydration, without claiming that browser rendering alone provides SEO or that both runtimes have identical scheduling.

## Agreed contracts

### C01 — Definitions and mounted instances

`View` is an opaque, reusable definition. Constructing it does not allocate live DOM or a mounted owner. It supports empty content, text, elements and fragments. Instantiating a view creates an independently disposable mounted instance with a DOM range. Multiple roots and fragments are normal cases, not special single-element exceptions.

Reusable definitions can capture shared state. Reuse does not clone closures, signals or objects. Browser integrations that capture an existing native node must explicitly state transfer or borrowing semantics; they cannot promise independent reuse of that node.

### C02 — Components and props

A component is an ordinary typed setup function receiving an explicit component scope and typed props. An explicit component operation creates a lightweight child owner and runs setup once, untracked. Components do not inherently establish a detached root, create a graph or become observers.

The conceptual prop surface is:

```fsharp
type Prop<'T> =
    | Constant of 'T
    | Reactive of (unit -> 'T)
```

Signals and memos have explicit adapters. A reader does not imply caching; callers use a memo when they want caching. Props do not carry setters unless the type explicitly includes write access or callbacks. Read-only access does not make a referenced mutable object immutable.

Static and reactive operations are distinguishable. Passing the result of a signal read to a static operation captures a snapshot. State created during setup is fresh per component instance; state captured outside setup is shared according to ordinary F# semantics.

### C03 — Ownership and lifecycle

Callers supply the graph, parent scope and required policies. A mount owns its subtree, listeners, bindings and scheduled work, but does not dispose a caller-owned graph. Policy records may be supplied once through an explicit scope; core operations must not discover policy through a hidden global default.

Dispose descendants before parent cleanup, with their DOM still attached during cleanup, then remove the owned range. Attempt all cleanup even when one callback fails, aggregate failures through an explicit reporting contract, and make repeated disposal harmless. Failed cleanup is not retried implicitly. Other operations through a disposed scope report a lifecycle error.

Distinguish setup, attachment, mounted access and teardown capabilities. F# types can restrict the surface, but runtime checks remain necessary because F# does not enforce linear ownership.

**Implementation gate:** Ranvier's documented core cleanup order is parent cleanup before child disposal. The renderer's child-first contract requires a proven adapter or a separately reviewed core extension. Do not silently change core semantics.

### C04 — Reactive bindings

Track the reader and execute DOM writes untracked. Supply binding equality explicitly, including an always-write option. Source equality, memo equality and DOM-write equality serve different purposes and remain separate choices.

Ordinary text bindings read strings and perform equality-checked writes. Pending/error presentation belongs to explicit boundaries. Core computation boundaries can produce fallback values for readers now; structural view boundaries and coordinated reveal require a separate renderer contract. An unhandled pending or failed read aborts the effect before writing; this mechanical behavior does not select application presentation. Per-binding outcome/status policy records are excluded from the minimal text API.

### C05 — Structural reactivity

Branching takes an explicit selection reader, identity key, comparer and instance factory. Equal keys retain the mounted instance and its state; retained instances read the latest selected value. Changed keys dispose and replace the instance. The caller must include the branch discriminator in identity when a different component shape requires replacement.

Keyed lists preserve instances and ranges while moving them. Removed entries are disposed; reinsertion creates a new instance. Duplicate keys are errors under the explicit list contract. A positional list is a separate operation with different identity semantics. Snapshot and incremental input adapters share the same identity and lifecycle rules.

Ordinary F# `if` and `match` remain ordinary evaluation. Reactive control flow uses explicit operations. A future CE or plugin must lower into those operations, including nested branches, rather than infer ownership from only the outer syntax.

### C06 — Events and scheduling

Event registration, batching and DOM scheduling are explicit. A convenience event helper may batch automatically, provided its name or supplied policy communicates that behavior. Synchronous and microtask scheduling are separate strategies; microtasks remain a separate module.

A commit processes a fixed snapshot. Reentrant updates belong to a subsequent wave governed by the selected scheduler. Recursive flushing is prohibited. Failure policy explicitly chooses fail-fast or collect-and-continue; neither implies rollback. Disposal always attempts all cleanup regardless of commit failure policy.

Graph batching, DOM-write coalescing and visual reveal are separate concepts. Neither a batch nor a commit promises atomic visual presentation.

### C07 — DOM surface and interop

Provide typed elements, events, attributes and properties over a small implementation core. Keep attributes, live properties and listeners distinct. Text uses text-node semantics; raw HTML is a separately named injection operation.

Children are reusable views or factories. Moving an existing mounted instance between owners requires an explicit transfer API if later supported. Dependencies arrive through props or an explicit typed environment.

Native DOM removal does not dispose an owner automatically. Framework range boundaries must not be mutated externally. Owned native integrations explicitly transfer cleanup/removal responsibility; borrowed integrations explicitly describe release behavior. Browser-only operations require a distinct server fallback or rejection contract.

### C08 — Async work

Offer an explicit component-owned task helper with cooperative cancellation and guards against late DOM work. Ordinary F# tasks retain ordinary behavior; creating a task inside a component does not automatically make it owned. Longer-lived work belongs to a longer-lived scope.

Do not promise ambient tracking or ownership after a suspended await. Async results must reenter an explicit capability and check whether their destination is still alive.

### C09 — Portability, SSR and hydration

Keep graph and ownership contracts consistent across .NET and Fable while documenting differences in promise completion, threading, equality and representation. Request-local server state must be explicitly created per request; a captured or singleton signal is shared state regardless of whether sharing was intended.

Portable view operations must not require browser `Node` values. A server renderer must be able to interpret portable elements, text, component composition and structural selection. Browser refs and imperative integration use separate capabilities. Hydration later attaches behavior to deterministic server output with an explicit mismatch policy.

Server HTML is valuable for indexing and first content; crawler behavior varies, so client JavaScript alone is not the whole SEO strategy. See [Google's JavaScript SEO guidance](https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics).

### C10 — Plugin independence

The plugin is optional. Every supported sugar operation must have a documented plugin-free equivalent and matching ownership, pending/error and scheduling semantics. The plugin may optimize static structure or generate typed bindings; it must not secretly choose application policies.

## Delivery scope

First usable release: plugin-free views, fragments, components, explicit props, scopes, reactive bindings, keyed branches/lists, typed events, lifecycle handling and explicit status/error behavior. Include compatibility guidance for the PoC.

Follow-on releases: server HTML, hydration, coordinated async boundaries, optional CE/plugin sugar, template specialization and measured event delegation. Routing, animation, state persistence and a broad component library are ecosystem work rather than prerequisites for validating this renderer.

## Current baseline and evidence limits

The [PoC README](../../../src/Fable.Ranvier/README.md) documents split read/write bindings, mounting, listener ownership and optional queued updates. Its ambient batched event helper and last-successful-value retention need explicit compatibility policies in the new surface. Its documented single-node mount restriction must expand to ranges. A component implementation file already exists; its precise suitability has not been semantically audited for this brief.

The [core contracts](../../content/concepts/contracts.md) establish existing ownership, error and cross-runtime constraints. The [scheduling benchmark](../../.ai/benchmarks/dom-scheduling/README.md) is evidence about specific PoC workloads, not proof of framework parity. This proposal changes no implementation and makes no claim that its conceptual API has type-checked.

## Acceptance criteria

- A full example works without a plugin, hidden reactive props or ambient policy selection.
- Two instances of one view have independent mounted resources; intentionally captured state remains shared.
- Removing a branch releases its effects, listeners and queued work exactly once; child cleanup observes attached DOM.
- Stable keys preserve local state through reorder; replacement keys reset it.
- Pending, errors, scheduling and equality can be selected explicitly and tested independently.
- Server interpretation is proven on a small portable example before committing to the public `View` representation.
- Release Fable output, memory behavior and realistic browser workloads substantiate performance decisions.
