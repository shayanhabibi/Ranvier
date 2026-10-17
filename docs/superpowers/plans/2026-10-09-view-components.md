# Component and typed prop proof

> Execute natively with superpowers:executing-plans and TDD. This refines approved framework Task 4; the user has authorized this component/prop proof.

Goal: explicit typed setup functions and props over the experimental renderer, with no observers, memos or graphs added by adapters.
Spec: `docs/superpowers/specs/2026-10-06-fable-ranvier-framework-brief.md` C02/C03/C10 and the approved explicit-function DSL design.
Architecture: preserve Operations and Backend signatures. Add Components.fs with `Prop<'T> = Constant of 'T | Reactive of (unit -> 'T)`, Prop.read, Prop.ofSignal, Prop.ofMemo, Prop.text and `create ops setup props`. A string Constant lowers to static Text; Reactive lowers to Bind with the supplied comparer. Typed records carry callbacks and reusable child views explicitly.
Constraints: experimental, no extra dependencies, no production API freeze, no sugar library. Use the DOM worktree; caller graph and parent scope remain explicit. Preserve Browser.fs benchmark baseline.

### Task 1: Typed component semantics

Produces: Components.fs and browser/pure tests linked into DomTests and both runners.
Consumes: existing Operations.Component, Setup.OnCleanup and Mounting.mount.

- [x] Write tests first for inert definitions; setup once/untracked when mounted inside an effect; per-instance local signals versus captured shared state; static snapshots versus live signal/memo adapters; callbacks and reusable children; setup failure releasing listeners/bindings/children; disposed cleanup capability rejection; server interpretation. Run before implementation: expected missing Components API.
- [x] In SageFs, probe Constant versus Reactive reads and typed Signal/Memo adapters; no implicit subscriptions or caching. Implement the minimal surface and run all cases through both Tree and Program.
- [x] Verify constant prop text creates zero binding effects using existing baseline metrics, reactive text creates one, and a component creates the same owners/setups as direct Operations.Component. Assert helpers preserve DOM/resource behavior.
- [x] Run `rtk proxy npm --prefix examples/Fable.Ranvier.Playground test`, expected all Node/Chromium cases pass; commit.

### Task 2: Allocation evidence and examples

Produces: repeatable component definition allocation probe, generated-JS observations, typed portable examples and a concise evidence report.
Consumes: Task 1 helper and tests.

- [x] Compare equivalent direct and helper definitions using the same props and setup. Measure definition allocation separately from mounted resources. Use optimized .NET FSI and Node/V8 sampled retained definitions; record runtimes, workload, sample counts, raw data and limitations.
- [x] Inspect Release generated JS for prop branches, adapter closures and helper expansion. Do not claim allocation-free props or measured framework parity.
- [x] Record instance-local versus captured shared state and static snapshot versus live bindings with compilable examples. Update framework Task 4 progress.
- [x] Run full DOM/browser suite, Release DomTests build and scoped FCS check. Commit; perform one fresh whole-change review and one RED→GREEN fix pass if needed.

Review Focus: exceptions during component output construction; a reader/comparer disposing its scope; reactive props with mutable captured objects; adapters surviving disposal; graph ownership of component-local state; malformed/null reader adapters; nested component cleanup with borrowed roots. Existing status/keys/performance gates remain open; only explicit supported behavior is claimed.
