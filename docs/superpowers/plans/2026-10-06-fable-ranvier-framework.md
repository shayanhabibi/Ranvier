# Fable.Ranvier framework implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** deliver a plugin-free, explicit F#/Fable renderer with fine-grained DOM updates and a proven path to server rendering.

**Architecture:** opaque reusable views instantiate into owned DOM ranges; components run setup once; explicit readers drive bindings and keyed regions; a small renderer boundary separates portable definitions from browser capabilities.

**Tech stack:** F#, Ranvier, Fable, browser DOM, existing Expecto/jsdom/Playwright infrastructure. Validate Debug and Release behavior; performance runs use Release output.

**Spec:** [framework brief](../specs/2026-10-06-fable-ranvier-framework-brief.md). **Tradeoffs and risk IDs:** [variants and gotchas](../specs/2026-10-06-fable-ranvier-framework-variants-and-risks.md).

Status (2026-10-15): the experimental Foundation renderer has owned ranges, components on owner lifetime with delegated listeners, keyed lists and keyed branches, component templates with dynamic structure (view holes, `matchWith`, `branch`, `each` and inlined children), the generated DOM catalogue (`Generated/Html.fs`, all 112 tags of `HTMLElementTagNameMap`) and the generated template catalogue (`Generated/HtmlTemplate.fs`), and production benchmarks against Solid 2 (2.0.0-rc.14), Ripple and React. The [checkpoint index](../../research/2026-10-11-view-poc-checkpoint.md) links each step. Keyed lists and arms run on core projections, memos and owners ([spec](../specs/2026-10-14-keyed-rows-on-core-design.md), [measurements](../../research/2026-10-14-keyed-rows-on-core.md)). Open: list performance (`swap1k` at 3.04x, within the run-to-run spread of its target, and `remove1k` at 3.29x Solid, against the <=3x targets, measured at `e447e21` on 2026-10-15; `clear1k` meets its <=2x target at 1.92x, part of which is Solid 2's shift; see [Where we stand](#where-we-stand-2026-10-15)), structural boundaries, server HTML and hydration, and sugar; production integration, public API freeze and release budgets follow them. The core indexed-edges work (draft PR #54) is deferred. See the [baseline report](../../research/2026-10-08-fable-ranvier-baseline.md) for fresh results and known whole-solution/Fable-test failures. The user approved explicit typed functions subject to extreme performance and efficient Oxpecker-style or Feliz-style sugar; the [DSL design](../specs/2026-10-08-ranvier-view-dsl-design.md) records the proof requirements. File names below are proposed seams; verify existing symbols before choosing final names. API descriptions are design sketches, not compiler-validated signatures.

## Where we stand (2026-10-15)

Branch `poc/fable-ranvier-dom` at `e447e21`, pushed as [PR #42](https://github.com/shayanhabibi/Ranvier/pull/42) up to `f0f27c3`, with master at `d264171` merged in. Master carries the core list work as [PR #55](https://github.com/shayanhabibi/Ranvier/pull/55) and the full-visit fallback for untrimmable passes as [PR #56](https://github.com/shayanhabibi/Ranvier/pull/56). Since `85e0f8c`, view keyed regions give the core projection the caller's item objects and a key function ([plan](2026-10-15-list-item-identity.md)). Since `1f1f1df`, each keyed pass validates and looks up only the changed window, `Row.Layout` moves only the rows of an end swap, and an emptied list is cleared in one operation ([plan](2026-10-15-list-region-passes.md)). Gates at `e447e21`: 2208 untraced and 2384 traced core .NET tests; 84 C# tests per target framework; 923 of 941 Fable core tests pass with inline delivery and 1011 of 1033 traced, the others being the known Fable/.NET differences; 364 Node DOM tests; 23 Chromium tests; the portable representation checks, the trace lint and fantomas pass.

### Performance

Ratios are Ranvier's time over the other framework's; below 1 is faster. Every figure is a measurement, not a budget. The component rows were measured at `85e0f8c` ([rerun note](../../research/2026-10-15-poc-rerun.md)), with the earlier figure in brackets; the component code is unchanged since. Rider used about one core during the run.

Components, template path:

| Phase, rows | × Solid 2 | × Ripple |
|---|---|---|
| Mount, 1 / 16 / 128 | 1.13 / 1.17 / 1.15 (1.18 / 1.16 / 1.17) | 0.99 / 0.82 / 0.74 (0.97 / 0.88 / 0.81) |
| Update, 1 / 16 / 128 | 0.79 / 1.02 / 1.07 (0.80 / 1.05 / 1.07) | 0.91 / 0.97 / 0.98 (0.93 / 1.01 / 0.99) |
| Dispose, 1 / 16 / 128 | 1.43 / 1.33 / 1.39 (1.32 / 1.25 / 1.56) | 2.33 / 0.93 / 0.88 (2.28 / 0.80 / 0.95) |
| Retained heap, 128-row mount | 0.97 (112 / 116 KiB) | — |

The authored path retains 383 KiB per 128-row mount, against 345 KiB at `a0de558`.

Keyed lists and arms, production rows against Solid 2 only, measured at `e447e21` ([region passes note](../../research/2026-10-15-list-region-passes.md)), with the `1f1f1df` figure ([item identity note](../../research/2026-10-15-list-item-identity.md)) in brackets:

| Operation | × Solid 2 | Target |
|---|---|---|
| create1k / create10k | 1.67 / 1.53 (1.63 / 1.49) | ≤ 2 (met) |
| append1k / replace1k | 1.51 / 1.53 (1.47 / 1.47) | ≤ 2 (met) / — |
| update10th1k | 0.46 (0.46) | — |
| swap1k | 3.04 (4.33) | ≤ 3 (not met) |
| remove1k / remove10k | 3.29 / 4.67 (3.33 / 4.84) | ≤ 3 (not met) / — |
| clear1k / clear10k | 1.92 / 2.09 (2.16 / 2.44) | ≤ 2 (met) |
| armsCreate1k / armsSwitch1k | 1.76 / 1.67 (1.71 / 1.67) | — / ≤ 2 (met) |
| armsPayload1k / armsClear1k | 0.55 / 2.60 (0.53 / 2.34) | — |
| Retained heap per row | 1,180 / 905 B (snapshot); 1,405 / 1,050 B (`getHeapUsage`) | ≤ 1,300 B |

- `swap1k`: production fell from 0.46 to 0.355 ms; Solid 2 ran its fast mode (0.11–0.14 ms) in all five processes, as at `1f1f1df`. Per process the ratio is 2.71–3.46 (3.25–4.67 at `1f1f1df`); the median is within the run-to-run spread of the 3x target.
- `remove1k`: production is 0.36 ms in both runs; Solid 2 is 0.11 ms. In the steady-state loop remove1k fell from 0.100 to 0.075 ms (2.1–2.5x Solid 2), but after the timing protocol's forced GC it is 0.33 ms.
- `clear1k`: production fell from 1.18 to 1.115 ms; Solid 2 rose from 0.540 to 0.575 ms, so against the `1f1f1df` Solid 2 median the ratio would be 2.06x, and the loop (1.4–1.5x, Solid 2 unchanged) is the stronger evidence. One `textContent` write replaces the per-row native `remove` calls.
- In the steady-state loop the ratios are swap1k 2.0–2.2x, remove1k 2.1–2.5x and clear1k 1.4–1.5x, below the targets, which are defined on the timing protocol.

The list benchmark has no Ripple adapter.

The core alone, with no DOM ([trimmed pass](../../research/2026-10-14-core-trimmed-pass.md)): since #55 a keyed pass trims the common prefix and suffix and skips `keyOf` for unchanged items. Since the item identity change the view's lists reach that trim: `KeyedProjection.Trim` takes 0.008 ms of remove1k in the loop profile, against 0.122 ms at `85e0f8c`. Since the region passes, validation and lookups cover only the changed window: `Row.Validate` was 27% of remove10k in the loop profile and the window validation is 0.5% ([profile](../../research/2026-10-15-list-region-passes.md#where-the-time-goes-now)).

### Done

- Owned ranges, components and props, minimal text bindings and queued commits (Tasks 3–5, experimental).
- Keyed lists and arms on core projections, memos and owners; `MountLifetime` over core owners replaces the `Scope` tree ([spec](../specs/2026-10-14-keyed-rows-on-core-design.md)).
- Component templates with dynamic structure and the generated template catalogue (plans 2a–2c).
- The generated DOM catalogue, all 112 tags.
- Production comparisons against Solid 2, Ripple and React.

### Open, in proposed order

1. remove1k (3.29x) and swap1k (3.04x) under the timing protocol: after its forced GC remove1k takes 0.33 ms against 0.075 ms in the loop ([region passes note](../../research/2026-10-15-list-region-passes.md#open)). In the loop, `RegionSite.Settle` is 32% of remove and 46% of swap; the row rebuild still copies every row reference (18% of remove10k).
2. Retained-row item signal writes (the [profile note's](../../research/2026-10-15-list-remove-profile.md#changes-ranked-by-the-saving-the-profiles-show) change 6): `SettleAdditions` is 12–15% of remove and swap in the loop.
3. armsClear1k rose from 2.34x to 2.60x while clear1k fell; not yet profiled.
4. Component dispose: 1.33–1.43× Solid 2 at every width.
5. Investigate the queued commit `Queue` (`Scheduling.fs`) as binding effects under a microtask `IGraphDispatcher` (agreed follow-up).
6. Structural view boundaries and live setter-failure handling (Task 5).
7. Owned async cancellation and the playground screen migration (Task 7).
8. Server HTML: precomputed static segments in `renderHtml`, escaping, request isolation; then hydration (Task 9).
9. Performance and release acceptance, then sugar (Tasks 8 and 10).

Indexed edges ([#54](https://github.com/shayanhabibi/Ranvier/pull/54)) stay deferred; the core projection pass is 10–26% of remove and swap in the loop profile.

Recorded by the item identity change:

- Key functions must be pure. A key function that reads a signal no longer re-runs the list region when that signal changes.
- `KeyedItems` has internal fields, so a backend outside `Ranvier.View.Foundation` cannot read a keyed region's items or key.

Recorded by the region passes:

- Rows outside a pass's changed window are not validated on that pass, so a foreign node inserted among them raises no error on that pass (accepted).
- An item keeps its key while it is in the list.
- Removed rows leave the document after the pass's projection and its row cleanups, before layout.
- A node inserted inside the region by a cleanup during a clear is deleted with the region's rows, as in Solid.

Smaller items recorded by the keyed-rows plan:

- No tests yet for `Complete` handing held errors to a window that serves its reporter, or for `Abandon` disposing inside another reporter's window.
- `createProjectionOnce` without `CreateFirst` no longer stops a pass when a factory disposes the projection; the view always uses `CreateFirst`.
- No test for an arm reader failing on an update.
- Two benchmark JSON files lack machine fields; their `commit` fields read `ab68bde` plus a dirty list, and the measured tree `4353219` is recorded only in the write-up.
- The [keyed-row cost research](../../research/2026-10-14-keyed-row-cost.md) is on unmerged branch `research/keyed-row-cost` (`aa5b811`), so that link is broken on this branch.

## Global constraints

- Preserve explicit core policies and plugin independence. Do not add silent scheduling, ownership inference, implicit async retention or automatic memoization.
- Follow repository AGENTS.md. Use fslangmcp `check` before symbol queries and `fcs_refactor_impact` before public-signature changes. If required tools are missing, follow the repository setup-permission instructions.
- Read the SageFs and XML documentation skills before applicable F# changes. The user has started the SageFs daemon; do not stop, restart or reinstall it without user authorization. Use the CLI for final acceptance.
- Keep microtask implementation separate. Preserve legacy behavior only through documented explicit compatibility configuration or a named adapter.
- Do not claim Solid parity before equivalent production benchmarks. Do not let optimization change observable identity or timing.
- Run the full repository acceptance suite at release gates, not just filtered tests. Determine the current build-runner command from its help before execution.

## Review focus

The highest-risk seams are child-first cleanup over current core semantics, status propagation through split effects, portable component setup, and key equality across .NET/Fable. Public API freeze depends on those proofs. Scheduling, list performance and compiler sugar follow correctness.

## Proposed module boundaries

- `src/Fable.Ranvier/Types/View.fs`: opaque definitions, component/prop contracts and backend-neutral composition.
- `src/Fable.Ranvier/Types/Policies.fs`: explicit scheduler and structural-boundary configuration types.
- `src/Fable.Ranvier/Runtime/Scope.fs`: lifetime state machine, child registration, cleanup and capability validation.
- `src/Fable.Ranvier/Runtime/Range.fs`: empty/single/multiple-node insertion, movement and removal.
- `src/Fable.Ranvier/Runtime/Binding.fs`: tracked string read/untracked write and committed equality; outcome presentation belongs to boundaries.
- `src/Fable.Ranvier/Runtime/Branch.fs` and `Runtime/KeyedList.fs`: retained identity and structural updates.
- Existing `Types/DomScheduling.fs` and `Types/DomQueue.Microtask.fs`: scheduler interface and optional queue strategy, evolving after audit.
- Existing `Dom.fs` and `Mount.fs`: audit and migrate existing responsibilities; avoid parallel competing component APIs. The empty `Types/Component.fs` placeholder was removed in the checkpoint.
- `src/Fable.Ranvier/Dom/Attributes.fs`, `Properties.fs`, `Events.fs`, `Lifecycle.fs`: typed browser surface over the runtime.
- Future `src/Ranvier.View/` and `src/Ranvier.Html/`: possible extraction of portable contracts and server interpreter, only after the portability experiment justifies the package split.
- `examples/Fable.Ranvier.Playground/tests/`: expand the existing tests, registering new F# files in `DomTests.fsproj` and generated test entry points in existing JS runners.

The implementation may consolidate small modules; these names identify responsibility and dependency direction, not a requirement for one file per abstraction.

## Task 1 — Audit the PoC and establish the acceptance harness

Contracts: C01–C10. Risks: G03, G09, G12. No public API changes in this task.

- [x] Read current project instructions and manifests; confirm the working tree and isolate implementation work as appropriate.
- [x] Run fslangmcp `check`, then inspect mount, component, scheduler, owner disposal and status/effect APIs semantically. Record existing signatures and public API snapshots. No component implementation remains after the checkpoint's empty placeholder removal; clean scoped checks preceded queries. The whole-solution check reports baseline Fable-test errors.
- [x] Compare the implementation with `src/Fable.Ranvier/README.md`; identify where its documented behavior has drifted. Updated the stale global reference in the example, app and fragment-root test.
- [x] Establish current full .NET and browser test results. Run existing `test:dom`, `test:browser` and Release build scripts from the playground. Canonical .NET tests, complete playground suites and production build pass; whole-solution build/Fable core acceptance remain blocked by recorded baseline errors.
- [x] Record legacy behavior requiring migration: ambient batched events, retained successful DOM on pending/error, initial versus deferred setter failures, and single-node mounting.

**Exit:** a baseline report appended to this plan with actual commands/results and an API migration list. Existing failures are attributed before feature work begins.

Execution record: starting checkpoint `174f07649f38b1ff7a3ebc60c6c3fb9f290d1cb0`. The [baseline report](../../research/2026-10-08-fable-ranvier-baseline.md) and [API snapshot](../../research/2026-10-08-fable-ranvier-api.json) contain the commands, results, migration list and tool limitations. No new view runtime has been implemented. SageFs observed current owner disposal as `parent -> child`; Task 2 must prove the renderer's child-first adapter.

## Task 2 — Prove disposal and portable view representation

Contracts: C01, C03, C09. Variants: V01, V03. Risks: G03, G11, G12, G15. This is a blocking experiment.

The [focused lifetime proof plan](2026-10-08-renderer-lifetime-proof.md) executes the disposal gate first. Representation, portability and sugar measurements follow its result; completing that focused plan does not complete all of Task 2.

The [representation experiment](2026-10-09-view-representation.md) compares portable trees/build functions and minimal typed/function/builder surfaces. Its [evidence](../../research/2026-10-09-view-representation.md) provisionally favors the tree. The .NET/static-browser proof and small sugar/resource checks pass; keyed branches, production status, JS instance/retained allocations and stable timing budgets remain outstanding. Task 2 and public API freeze remain open.

- [x] Write lifecycle trace tests first: nested children clean up before parent while nodes remain attached; a throwing child does not prevent other cleanup/removal; disposal repeats are harmless.
- [x] Run the same traces through mount disposal, graph disposal, failed setup and owner disposal during reactive reruns. Include two mounts sharing one graph.
- [x] Prototype the renderer child registry. Existing core cleanup hooks pass the tested entry points; see the [lifetime proof report](../../research/2026-10-08-renderer-lifetime-proof.md) for traces, evidence limits and known allocation/unlinking costs. No core extension was required.
- [ ] Prototype a small portable instruction definition and a backend-neutral executable definition for the same component, fragment, text binding and keyed branch.
- [x] Interpret a static selection on .NET into escaped HTML and instantiate the equivalent definition in Fable. The setup surface exposes cleanup only; native hooks use an explicit browser token and server Reject/Omit policy. This does not prevent arbitrary user closures capturing browser globals; production package separation remains Task 9.
- [ ] Measure definition/instance allocations and mount cost in Release JS; inspect generated closure/DU dispatch. Choose A or B from V01 and record the reason.
- [ ] Compare equivalent direct-function, function-sugar and builder-sugar examples in both representations. Separate construction, mount, update and disposal measurements; include static-heavy trees, sibling-heavy trees, nested fragments and repeated independent mounts. Inspect generated JS and report residual wrapper work before selecting the core representation.
- [ ] Assert static content creates no reactive machinery and a scalar update touches only its binding target. Sugar must preserve setup counts, owners, observers, listeners and scheduling waves. Establish numerical regression budgets from repeated baseline runs before public API freeze.

**Exit:** lifecycle, portability and performance/sugar proofs pass. If not, revise the proposed representation before public API freeze. The HTML proof is not yet a production SSR renderer; the sugar probes are not complete ergonomic libraries.

## Task 3 — Implement scopes, ranges and explicit mount policies

Contracts: C01, C03, C07. Risks: G03, G04, G11, G15.

The [focused owned-range proof](2026-10-09-view-owned-ranges.md) implements these contracts experimentally with the provisional instruction tree. Its [evidence and review](../../research/2026-10-09-view-owned-ranges.md) cover staging, movement, borrowed restoration, parent/graph disposal and failure recovery. Production integration and the Task 2 performance/API-freeze gates remain open.

Proposed operation: `mount(graph, parentScope, policies, host, view) -> MountedView`. `MountedView` exposes disposal and explicit mounted capabilities, not arbitrary mutation of its range internals.

- [x] Add failing tests for empty, one-node and fragment views; sibling preservation; failed attachment; reparented owned nodes; borrowed-node release; and post-disposal operations.
- [x] Implement a scope state machine that distinguishes setup, attached and disposed states and tracks children/cleanup deterministically.
- [x] Implement ranges with stable insertion boundaries, explicit movement and idempotent removal. Validate boundaries before destructive structural operations.
- [x] Make partial mount failure unwind created listeners, bindings, children and inserted nodes while preserving unrelated host content.
- [x] Aggregate disposal errors through the supplied reporter; guard reporter failure so it cannot interrupt resource release.
- [x] Extend the playground's existing mount tests and run the full DOM/browser test scripts.

**Exit:** mount ownership is independent of graph ownership and every lifecycle entry point follows Task 2's proven order.

## Task 4 — Implement components and props

Contracts: C02, C10. Risks: G01, G02, G14.

Proposed operation: `component(setup, props) -> View`, where setup receives its explicit child scope at instantiation. `Prop.read` evaluates constants/readers; it never memoizes or creates a subscription by itself.

- [x] Add tests demonstrating setup runs once, untracked; two mounts have distinct instance state; captured state remains shared; static snapshots stay static; reactive props remain live.
- [x] Add compile-time API examples for typed props, explicit callback writes, read-only adapters and reusable child views.
- [x] Implement component instantiation using Task 3's child scope; normalize prop adapters once where useful.
- [x] Reject use of disposed setup capabilities; test component setup failure and cleanup registration before failure.
- [x] Document instance state versus captured state with one minimal example of each.

Experimental proof completed 2026-10-09 in Components.fs; see [component evidence](../../research/2026-10-09-view-components.md). Production API freeze remains open.

**Exit:** no plugin, dynamic invocation trick or props proxy is required for a typed component example.

## Task 5 — Minimal bindings, boundaries and scheduling

Contracts: C04, C06. Variants: V02, V05. Risks: G06–G09.

Corrected 2026-10-11 after the user rejected per-binding outcome presentation. The [repair record](../../research/2026-10-11-text-boundary-repair.md) supersedes the policy proof and integration plan. Ordinary text takes a string reader and equality; it uses one effect owned by the enclosing mount/component. Core computation boundaries supply pending/error values. Deferred mounts add queue cancellation and version checks. Initial DOM setter failure unwinds setup.

- [x] Remove per-text outcome policies, status/report callbacks, extra roots and server snapshot selector.
- [x] Prove two text nodes share a core boundary through pending, failure, recovery and disposal in both representations and commit modes.
- [x] Preserve queued coalescing, fixed snapshot waves, stale-value cancellation, recursive flush rejection, failure policies and disposal.
- [x] Measure the corrected runtime beside a frozen original implementation with the same current core in Release Chromium.
- [ ] Expand source/memo/binding equality and arbitrary setter/reentry coverage when typed properties are added.
- [ ] Implement structural view boundaries and coordinated reveal semantics. Core computation boundaries do not catch descendant effect failures; this remains a separate renderer contract.

The [structural composition proof](../../research/2026-10-11-structural-boundary-composition.md) demonstrates keyed loading/error/content selection from a shared core computation boundary, with same-key identity, replacement cleanup and portable snapshots. It adds no runtime API. Structural reconciliation remains synchronous under queued text commits; dedicated branches, descendant effect-error capture and coordinated reveal remain open.
- [ ] Define explicit handling of live synchronous DOM setter failures and deferred commit reporting for the production API.
- [ ] Measure retained allocations, microtask latency and property workloads before public API freeze.

**Exit:** minimal binding operations and independently selected boundary presentation/scheduling, backed by production coverage and measured budgets.

## Task 6 — Keyed branches and lists

Experimental keyed-list checkpoint: retained values/identity, reorder, removal/reinsertion and app integration are implemented. The [correctness follow-on](../../research/2026-10-11-keyed-correctness.md) covers initial/live failures and recovery, rollback termination, callback-triggered disposal, nested composition and tracking isolation. Open: the `swap1k` and `remove1k` timing targets (<=3x Solid) are not met, and `remove10k` is 26-32% slower with memo-free rows, unexplained ([measurements](../../research/2026-10-14-keyed-rows-on-core.md)). See the [current checkpoint](../../research/2026-10-11-view-poc-checkpoint.md).

Contracts: C05. Variant: V04. Risks: G05, G10, G15.

Current experimental branch input: selection reader, key selector, equality and factory receiving a current-value reader. `Branch.select` delegates to the existing keyed runtime; outcome presentation comes from enclosing boundaries. The [branch and snapshot experiment](../../research/2026-10-11-branches-and-snapshots.md) records its tests and list/array measurements. A specialized branch engine and internal array storage remain unimplemented; descendant effect-error boundaries and coordinated reveal remain open. List input additionally supplies key lookup semantics, including compatible hashing if required by the implementation.

- [x] Test equal-key data replacement without setup rerun; changed-key replacement; variant-discriminated keys; duplicate keys; remove/reinsert; and cleanup during replacement. Evidence: [`TemplateChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs) "branch remounts on a key change and keeps the arm for an equal key" (no setup on an equal key; cleanup before the changed key's setup), "matchWith switches arms by case and updates payloads in place", "each sets up a replacing row before the replaced row's cleanup" and "duplicate each keys fail the mount, are reported on update and keep the rows"; [`RangeChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/RangeChecks.fs) "keyed rows preserve identity and current data across reorder and removal" (a reinserted key gets a fresh row) and "duplicate keys fail before mounting mutates the host".
- [x] Implement keyed branches using owned ranges; avoid capturing only the initial selected value in retained children. Evidence: `Branch.select` in the [branch and snapshot experiment](../../research/2026-10-11-branches-and-snapshots.md); template `matchWith` and `branch` first mounted through the keyed path and now mount through the arm region on a core memo ([component templates, dynamic structure](../../research/2026-10-14-component-templates.md#dynamic-structure)), and a retained arm receives the new payload ([`TemplateChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs) "matchWith switches arms by case and updates payloads in place", "two cases selecting the same matchWith definition keep the arm and update its payload").
- [x] Implement snapshot keyed lists, prevalidating duplicate keys before DOM mutation. Cover prepend, append, reverse, random reorder, multi-node rows and empty rows. Implemented. Covered: [`RangeChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/RangeChecks.fs) "duplicate keys fail before mounting mutates the host", "keyed rows preserve identity and current data across reorder and removal" (reorder, insertion and removal) and "nested keyed rows update and dispose after outer movement" (two-element swaps, two-node rows). Empty rows, prepend, reverse and random reorder: [`TemplateChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs) "zero-node rows keep their place through their marker" and "list updates match a reference order and keep node identities".
- [x] Test state and listener identity through moves, and focus/selection in real browsers. State and listener identity through moves is covered ([`RangeChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/RangeChecks.fs) "keyed rows preserve identity and current data across reorder and removal"; [`TemplateChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs) "template handlers dispatch after a move", "root-level each regions beside other roots update, reorder, move and dispose"). Focus and selection during a list move in Chromium: [`range.spec.mjs`](../../../examples/Fable.Ranvier.Playground/tests/range.spec.mjs) "template rows keep focus and selection across list moves in Chromium".
- [ ] Measure large-list lookup complexity under each supported key comparer. Do not substitute JS identity for structural equality.
- [ ] Add a separately named positional API only if needed. Defer incremental adapters and LIS optimization until baseline measurements show a useful improvement.

**Exit:** stable keys retain mounted instances, changed keys recreate them, and removal cancels all owned work.

## Task 7 — Typed DOM, events, lifecycle and owned async helpers

2026-10-12 follow-on: the [authoring performance check-in](../../research/authoring-performance/2026-10-12-gc-correction.md) establishes exact resource and construction-allocation parity for the overloaded pilot; timing budgets remain open. The [authored form contracts](../../research/2026-10-12-authoring-form-contracts.md) exercise focused composition echo and checkbox event/programmatic updates in Chromium through the same renderer. These advance the form proof without completing the whole task.

Contracts: C07, C08. Risks: G08, G10, G11, G13.

Generation follow-on, implemented subset 2026-10-12: the [published catalogue integration](../../research/2026-10-12-catalogue-dom-generation.md) consumes Cloudsmith Core.TS 0.2.1 with Wire 0.3.0 and pins Core 0.2.0 in the consumer. The five-tag generated surface passes compiler and DOM checks; broader generation, attributes/portable property metadata, application migration and performance gates remain open.

- [x] Verify the published Core.TS catalogue payload, provenance and compatible compiler/Wire inputs; consume it from a restored pinned package without a sibling checkout.
- [x] Prove declaration-to-native-binding identity/arity resolution for input properties, FileList, inherited members and an input event. Fail on unsupported or ambiguous types; avoid duplicating native bindings or parsing API fingerprints as signatures.
- [x] Generate a five-tag subset against Foundation's typed functions and handwritten operations. Compare Release JS, resource counts and browser behavior with handwritten equivalents, and check deterministic regeneration and rejected readonly/incompatible setters before expanding the surface. Evidence: the subset, emitted JS, tracked-reader counts, browser behavior and regeneration/negative compiler checks pass in the [published catalogue integration](../../research/2026-10-12-catalogue-dom-generation.md) and [`GeneratedChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/GeneratedChecks.fs) "generated five-tag subset and select initialization". Generation has since expanded to the [112-tag map](../../research/2026-10-12-full-html-generation.md) and the [template catalogue](../../research/2026-10-14-component-templates.md#generated-catalogue-plan-2c), with byte-identical regeneration and compiler rejection checks in `tools/Ranvier.Dom.Generate/tests.py` and `check-types.py`. Open, under Task 8: full retained-resource and performance budgets.

- [ ] Implement distinct static/reactive attribute and property operations, with typed event registration and explicit batch/scheduler context. Implemented: static and reactive attributes and properties, typed listeners, and the mount's explicit synchronous or queued commit mode ([catalogue integration](../../research/2026-10-12-catalogue-dom-generation.md), [attribute authoring](../../research/2026-10-12-attribute-authoring.md)). Covered: [`AttributeChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/AttributeChecks.fs) "tree sync generated attribute removal and lifetime" and its queued and Program variants; [`GeneratedChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/GeneratedChecks.fs) "tree sync generated properties and listener ownership" and its queued and Program variants, which mount under `SynchronousCommits` and `QueuedCommits`. Open: a test of a generated listener inside an explicit batch.
- [x] Test boolean attributes, null/removal, `value`, `checked`, selection and IME behavior. Add browser-only tests where jsdom cannot represent behavior adequately. Evidence: [`TemplateCatalogueChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/TemplateCatalogueChecks.fs) (boolean and ARIA boolean attributes, null and absent values, `Input.value` and `Input.checked` in static, reader and source forms); [`TemplateChecks.fs`](../../../examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs) "null attribute values remove the attribute"; Chromium tests in [`playground.spec.mjs`](../../../examples/Fable.Ranvier.Playground/tests/playground.spec.mjs) "input node, focus and selection survive a reactive input event" and "authored input composition preserves node focus and caret", recorded in the [authored form contracts](../../research/2026-10-12-authoring-form-contracts.md) with checkbox event and programmatic updates.
- [ ] Add mounted access hooks and explicit owned/borrowed integrations; document raw HTML as a separate unsafe-content boundary.
- [ ] Implement cooperative owned-task cancellation with generation/liveness checks; test success after disposal, replacement races and noncooperative completion.
- [ ] Add explicit portable metadata/server fallback for browser-only operations rather than pretending arbitrary setters serialize.
- [ ] Migrate one realistic playground screen using only the public plugin-free API. Keep compatibility helpers clearly named and outside the minimal core.

**Exit:** the first usable renderer demonstrates interactive forms, keyed content, async status and reliable cleanup without compiler sugar.

## Task 8 — Performance and release acceptance

2026-10-12 comparison checkpoint: the [production DOM framework comparison](../../research/framework-performance/README.md) measures Ranvier against compiled Solid JSX, published Fable.Ripple.Dom and React synchronous commits on equivalent component workloads. Updates are close to the fine-grained libraries; mounting, disposal and retained JavaScript heap show larger gaps. Profile those paths before setting release budgets. This fixture does not complete the keyed-list, branch, async or integrated-release performance gates.

Contracts: all first-release contracts. Variants: V01–V05. No predetermined parity claim.

- [ ] Extend `examples/Fable.Ranvier.Playground/benchmarks/` with mount, update, reorder and disposal workloads; preserve the scheduling benchmark as a baseline.
- [ ] Include shared-graph fan-out, independent mounts, constant-heavy views, cheap/expensive setters and long-lived mount/unmount cycles.
- [ ] Record browser/Node/Fable versions, hardware, production settings, warmup, sample count, independent runs, medians and tail behavior. Measure retained heap after settled disposal and bundle size separately.
- [x] Compare pinned Solid implementations with equivalent state, DOM, event and scheduling behavior; report differences that cannot be normalized. Evidence: the [framework comparison](../../research/framework-performance/README.md) records the shared workload and each framework's differences; `run.mjs` pins Solid 2 (2.0.0-rc.14 with `@solidjs/compiler`), checks identity, clicks and disposal DOM for every adapter, and current Solid 2 results are in the [component templates note](../../research/2026-10-14-component-templates.md).
- [ ] Optimize only demonstrated hot paths: constant elimination, adapter allocation, list lookup/moves, and scheduler storage. Re-run affected correctness suites after each change.
- [ ] Run whole-solution .NET acceptance and complete playground tests/build in Release-compatible configuration. Test additional browsers where supported; explicitly record missing coverage.
- [ ] Update public docs, migration examples and API snapshots. Report benchmark improvements and regressions without hiding unfavorable workloads.

**Exit:** evidence supports a usable release and documents its limits. Set numeric performance budgets from the measured baseline rather than inventing percentages now.

## Task 9 — Server HTML and hydration follow-on

Contracts: C09. Risks: G02, G09, G11–G13. Depends on portable proof and stable identity semantics.

- [ ] Extract portable contracts only if the chosen representation requires a separate project; keep browser dependencies out of the server package.
- [ ] Implement escaped text/attributes, portable property serialization, deterministic identity and request-local scopes; define explicit server pending/error behavior.
- [ ] Test two concurrent requests for accidental shared state, scope release on failure, and absence of browser lifecycle callbacks.
- [ ] Define hydration markers, state transfer, escaping and mismatch policy. Test empty/fragment/keyed regions, forms, malformed HTML normalization and server/client differing selections.
- [ ] Add first-content and hydration benchmarks. Treat streaming, event replay and selective hydration as separate later proposals.

**Exit:** real server HTML plus tested hydration; client-only rendering is no longer relied on for initial indexable content.

## Task 10 — Optional sugar and specialization

Contracts: C10. Variant: V06. Risk: G14. Depends on settled runtime contracts.

- [ ] Add optional ergonomic wrappers that select clearly documented policies and expand to explicit operations.
- [ ] Recheck complete Oxpecker-style or Feliz-style authoring wrappers against Task 2's direct-function baseline: equivalent resource counts and update paths, with construction overhead and bundle contribution reported separately. Preserve the measured regression budgets.
- [ ] Prototype CE control flow using actual F# compiler behavior; verify nested selection and continuations rather than relying on surface syntax assumptions.
- [ ] Add Fable plugin golden-output and behavioral-equivalence tests for setup count, prop reads, nested branches, cleanup, pending/error outcomes and timing.
- [ ] Measure static-template specialization and generated bundle size against the plugin-free baseline. Keep a complete no-plugin test application permanently.

**Exit:** sugar improves authoring or measured performance without becoming necessary for correctness.

## Contract coverage and validation commands

C01/C03: Tasks 2–3; C02: Task 4; C04/C06: Task 5; C05: Task 6; C07/C08: Task 7; C09: Tasks 2 and 9; C10: Tasks 4, 7 and 10. Task 8 validates the integrated first release.

Existing playground scripts, to run from the repository root during implementation:

```powershell
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run test:dom
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run test:browser
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run build
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run bench:dom
rtk proxy dotnet build Ranvier.slnx --configuration Release
rtk proxy dotnet fsi build.fsx -- --help
```

Use the build runner's verified full-test command for the whole .NET suite; a successful build is not a substitute. New test modules must be included in both project compilation and relevant runners. These commands are proposed implementation checks, not results of this documentation task.
