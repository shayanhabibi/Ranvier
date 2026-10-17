# Xantham generation for Ranvier DOM

Assessed 2026-10-08; release investigation 2026-10-11. Superseded for availability and implementation by the [2026-10-12 catalogue integration](2026-10-12-catalogue-dom-generation.md): published Core.TS 0.2.1 ships its catalogue, and the five-tag Wire-backed generator is compiled and tested. The remaining proposal and cautions below describe broader work beyond that subset.

## Verdict

Use Xantham as the source of typed DOM knowledge and native binding identity. Let Ranvier own the generated public vocabulary and the runtime semantics. Explore a minimal catalogue-backed HTML generator over Wire, with Xantham's generator as the alternative when its authenticated catalogue/application machinery is needed. Neither approach assumes that the stock property companion hook generates the whole renderer-facing scaffold.

The [earlier release investigation](2026-10-11-xantham-release-catalogues.md) verified that Core.TS 0.2.0 lacked a catalogue. Core.TS 0.2.1 now ships one; the narrow generator verifies its descriptor/payload/assembly and uses Wire 0.3.0. Fable.Ranvier references Core 0.2.0 and Core.TS 0.2.1. A public standalone catalogue loader/resolver remains unavailable; the adapter uses explicit pinned artifacts.

Upstream capabilities and constraints are recorded in [the source investigation](xantham-generation-sources.md). Ranvier requirements come from the [proposed framework brief](../superpowers/specs/2026-10-06-fable-ranvier-framework-brief.md), especially C01, C04, C06, C07, C09 and C10. Existing behavior is documented in the [DOM PoC README](../../src/Fable.Ranvier/README.md); the proposal is not an implementation audit.

## Proposed pipeline

1. Inspect a newly published catalogue-bearing Core.TS nupkg from Cloudsmith. Pin its version, package hash, catalogue provenance and compatible Wire/compiler inputs. Check the downloaded artifact, not just the producer checkout.
2. Prove a bridge from compiler declaration identity to the catalogue's qualified F# name, arity and ownership, and verify it against the actual binding assembly. Reuse native bindings throughout; do not regenerate them alongside the scaffold.
3. Query tag maps, inheritance, writable properties and event maps through Wire or a structured Xantham projection. Normalize a narrow HTML model plus a versioned Ranvier policy map for names, nullability, form semantics and explicit exceptions. The catalogue authenticates binding identities; it does not supply complete member metadata.
4. Generate ordinary typed element functions and distinct static/reactive property, attribute and listener helpers targeting the existing experimental Foundation runtime. Keep ownership, scheduling and policy in handwritten runtime operations.
5. Validate deterministic regeneration, negative typing examples, Release Fable compilation and browser behavior. Compare generated helpers with handwritten equivalents before expanding coverage or freezing the public surface.

The snapshot adapter must establish that the required tag maps, inheritance and event types survive in a usable form. The public customization contract currently uses opaque binding-type handles and a display string; it does not expose a complete structured type decomposition or dedicated tag-map/event-map queries. Follow-up inspection found that the wider generator API already exposes `FsTypeRef`, `FsMember`, `FsDecl` and `RenderModel.Decls`: a Ranvier-owned emitter can inspect declarations returned by `Pipeline.generateWith` and write its own files without a new output hook. This path requires the needed declarations to be reached and shipped, and does not preserve every source-semantic distinction. Small upstream inspection and qualified-name functions would improve the richer semantic path. Avoid reverse-engineering emitted F# text or parsing display strings to reconstruct semantic types.

## Alternative: minimal generator over Xantham.TypeScript.Wire

Discussed on 2026-10-08; revised against the 2026-10-11 release evidence. [Xantham #110](https://github.com/shayanhabibi/Xantham/issues/110) proposed package catalogue discovery/distribution and is now closed. Portable compatibility and Brotli file transport shipped, but Core.TS catalogue packing and a standalone resolver are not established by the released artifacts. The minimal Ranvier emitter remains a proposal.

Use `Xantham.TypeScript.Wire` directly for TypeScript checker queries and AST access, then implement a narrowly scoped Ranvier HTML model and F# scaffold emitter. Select tag maps, inherited writable properties and event maps; generate reusable views and distinct static/reactive helpers that delegate behavior to Ranvier's runtime.

Reuse `Xantham.Fable.Core.TS` native bindings throughout, including native types within the HTML domain. For example, an input scaffold uses the existing `HTMLInputElement`, and its `files` property references the existing `FileList`. Generate the Ranvier authoring surface without redeclaring these native types.

The main integration requirement is a reliable bridge from TypeScript declaration identity to the existing qualified F# binding name, generic arity and representation. Wire supplies TypeScript facts, not Xantham's F# mapping. The released catalogue format records qualified names, identity, arity, constraints and provenance, but its API field is an authentication fingerprint rather than recoverable member signatures. Core.TS 0.2.0 does not ship an instance. File loading/authentication remains private or internal, and no public standalone Wire-to-catalog resolver was identified. Shipping the producer's Brotli catalogue with Core.TS is the first improvement; a small supported identity lookup and authenticated loader would then avoid duplicating Xantham's compatibility machinery. Automatic package discovery can follow: the first prototype can use an explicit path to the payload in a restored, pinned package.

Requested upstream on 2026-10-11 via the active Codex task “Review Xantham's ready PRs”: generate the catalogue from the exact packaged-binding inputs/configuration, include the Brotli payload and ownership/version descriptor where supported, verify the downloaded Cloudsmith package, and add a restored-package consumer test without a sibling checkout. Request delivery is recorded here; implementation and publication remain unverified.

Pin compatible Wire/compiler versions and the TypeScript declaration inputs associated with the Core.TS package. Support a small verified type grammar: explicit primitive, optional, array, generic and callback conversion rules; catalog-resolved named types; and curated union/HTML exceptions. Unsupported shapes must produce generation diagnostics rather than silently widening to `obj`.

The attraction is direct access to the compiler's semantic data and full control of a small HTML emitter. The maintenance risk is gradually rebuilding Xantham's general TypeScript-to-F# mapper. First validate an input with `value`, `checked`, `files` and an input event to exercise primitives, native references, optionality and event typing. No Wire-based prototype has been compiled or run.

## Author's strongest case

HTML has a broad, repetitive typed surface. Generating it can remove string tag names, repeated casts and handwritten setters from application code while retaining ordinary F# functions. Fixing a naming or type-mapping rule once improves all generated helpers. Static and reactive operations can share conventions without concealing which one a caller selected. Since generation runs at build time, applications need not load Xantham's generator or parse declarations at runtime.

For the framework author, the central benefit is coverage and maintenance consistency. For the application author, the benefit is discoverable element-specific properties and event types, backed by a consistent lifetime contract. Neither benefit proves smaller bundles or faster rendering.

## Sceptical case

- **Types are insufficient behavioral specifications.** A writable boolean property does not establish boolean-attribute serialization. Live form properties, default values, removal, focus and IME behavior require explicit rules. Read-only properties must not produce setters.
- **Current customization is narrower than arbitrary scaffolding.** Property companions help adapt properties; constructors, event registration and reusable views require another generation pass or extension work. A raw interface replacement escape is a poor default for systematic HTML generation.
- **The target is still experimental.** The Foundation PoC now supports reusable definitions, typed components, owned ranges and keyed views, as recorded in the [current checkpoint](2026-10-11-view-poc-checkpoint.md). Generate against that runtime direction, while treating helper signatures and broader scheduling/property policies as provisional.
- **Ownership cannot be generated from DOM declarations.** Generated listeners and bindings must delegate disposal, untracked writes, outcome handling and scheduling to Ranvier. Duplicating these behaviors in each helper multiplies failure points.
- **Browser setters cannot by themselves support server rendering.** Portable operations need metadata and curated serialization rules; arbitrary native callbacks remain explicitly browser-only.
- **A huge generated surface still has a cost.** Measure compiler time, declaration size, editor completion quality and bundled output. Upstream changes must produce reviewable diffs rather than silently expanding or breaking Ranvier's public interface.
- **Element-specific typing needs a proof.** Shared modifiers should work on derived elements, while input-only helpers must fail on unrelated elements. Typed events must not pretend every event target is the element that registered the listener.

The declaration source supplies useful tag, property and event information: [TypeScript DOM generator baseline](https://github.com/microsoft/TypeScript-DOM-lib-generator/blob/main/baselines/dom.generated.d.ts). The behavioral cautions above are design implications, not claims that Xantham implements Ranvier semantics.

## Smallest decisive experiment

First prove catalogue identity resolution for `HTMLInputElement`, inherited `HTMLElement` members, `FileList`, optional `files`, `value`, `checked` and an input event. Include generic references and callback/array shapes where reached; missing, ambiguous or unsupported mappings must fail explicitly. Verify the catalogue against the referenced binding version rather than assuming package version equality authenticates its contents.

Then generate only `div`, `button`, `input`, `select` and `option`. Compare them with equivalent handwritten definitions using the same runtime. Prove:

- Two mounts of one definition create independent nodes, bindings and listeners.
- Generated static and reactive helpers preserve the documented differences; no policy is inferred from a getter.
- Form properties and attributes behave correctly, including false/removal and redundant input writes.
- Inherited helpers compile; incompatible element/property combinations fail to compile.
- Event types are preserved and listeners are removed on disposal.
- Disposal cancels queued writes; pending/error handling matches the chosen runtime policy.
- Two regeneration runs are identical; input/version changes produce attributable diffs.
- Release JS, bundle size and mount/update/disposal workloads remain comparable to the handwritten equivalent.
- A small portable definition can be interpreted as escaped server HTML without browser nodes or globals.

Proceed to broader coverage after these gates pass. This update reorients the documentation and Task 7 generation prerequisites. It changes no runtime code or dependencies and ran no generated-output compilation, runtime tests or performance benchmarks.
