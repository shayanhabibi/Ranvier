# HTML generation expansion

> Execute inline with superpowers:executing-plans; review the completed change once.

Goal: expand the published-catalogue generator to every entry in HTMLElementTagNameMap, supported properties and typed events, then exercise it in the playground.

Scope approved in chat: preserve native types and existing handwritten runtime semantics; do not widen unsupported types to obj or add general DOM methods/SVG. Core Ranvier and descendant error/reveal behavior remain unchanged.

- [x] Add named HTML tags to the representation and shared void-element rules. Test br/img/textarea HTML and owned DOM behavior; update all interpreters to use the same void contract.
- [x] Enumerate compiler tag-map entries deterministically. Resolve their catalogue/native identities, emit typed constructors, getters and supported primitive setters; exclude files setters and DOM-overwriting properties by policy. Emit common and element-specific typed events, resolving inherited maps and rejecting conflicting types. Write coverage/omission metadata beside the generated file. Preserve atomic failure and deterministic generation tests; add compiler negatives for void children and new property types.
- [x] Replace Foundation playground constructors/listeners with generated helpers and use array row readers. Run full Node/Chromium acceptance, generator/negative compilation checks, Release builds, XML audit and one fresh review. Commit verified changes in the DOM worktree.

Validation commands are the existing playground npm test/build, generator tests.py/check-types.py, scoped FCS check and portable RepresentationChecks Release executable. No new collection or throughput experiments are required.

Execution record: full tag enumeration uses shared inherited helper modules to avoid duplicate output; supported shapes preserve native types and omissions are explicit. Review found raw-text and initial-newline serialization gaps; both have parser-roundtrip coverage and follow-up approval. Acceptance passes 180 Node / 18 Chromium cases, deterministic generation, compiler negatives, portable and Release checks.
