# DOM PoC checkpoint — 2026-10-11

This status describes the work preserved from the DOM worktree's uncommitted changes, based on `ca01abf`. The earlier comparison with Ripple described that committed base and missed the subsequent keyed-list and application work.

## Working experimentally

The Foundation renderer now supports keyed lists in addition to reusable views, typed components/props, owned ranges and child-first cleanup. Retained keys keep their row mount and DOM identity, while a reader supplies the latest item value. Reordering moves existing ranges; removal releases row listeners/resources; reinsertion creates a fresh row. Duplicate snapshots are rejected before reconciliation mutates the host. Tree and Program can both render keyed snapshots as escaped HTML.

The playground compiles Foundation sources directly and mounts an add/reverse keyed-row panel alongside the original DOM PoC. The browser test exercises this consumer and verifies retained node identity. This is application integration of experimental source, not a public package or a complete application framework.

The solution includes the portable representation-check project. The relative SageFs project setting selects the solution without embedding a machine-specific path. No daemon/session configuration was changed; the existing daemon reports a separate home-directory watcher limitation.

## Review and repairs

A fresh reviewer found that top-level keyed regions bypassed the outer range's root bookkeeping. A new regression failed with `before::afteronetwo` instead of `before:onetwo:after`. The range now records a keyed span, moves it with the outer mount and updates retained rows' placement after movement.

The reviewer then identified two issues in that fix. Each was reproduced by a failing regression: foreign nodes inserted inside keyed rows were accepted during outer movement, and a failed start-marker removal skipped the end marker. Region validation now checks child ranges before movement; endpoint releases are attempted independently. All three regressions pass in the complete Node and Chromium runs.

## Verification

- Complete playground acceptance: 113 Node tests and 16 Chromium tests passed.
- Release DomTests build: zero warnings/errors; fresh scoped FCS check: zero errors/warnings.
- Portable .NET representation executable: all six groups passed.
- Playground production bundle: build passed.
- XML comment audit of Representation/Mounting: zero findings after wrapping one overlong comment; staged formatting/comment checks run during commit.

These are scoped DOM/portable gates. Whole-solution acceptance and production API freeze remain open.

## Remaining work

Follow-on: the [keyed correctness repair](2026-10-11-keyed-correctness.md) adds coverage and fixes for the failure/reentry/nested-composition gaps below. The verification counts above describe the original checkpoint; consult the follow-on for its newer results.

This was a useful keyed-list correctness checkpoint, not completion of framework Task 6. Its duplicate-update reporting, failed live reconciliation, reentrant disposal and nested-composition gaps are addressed by the follow-on within the tested contracts. Branch selection and broader comparer/focus cases remain open. The implementation uses quadratic duplicate checks, retained-key lookup and removed-entry filtering; it has no list throughput or allocation budget yet. Large-list performance must be measured and optimized before claiming the intended performance target.

The [text/boundary repair](2026-10-11-text-boundary-repair.md) supersedes the per-binding outcome experiment. Ordinary text uses a tracked string reader and committed string equality. Core computation boundaries supply pending/error fallback values; mounts select synchronous or queued commits. The rejected policy API, per-binding roots and HTML snapshot selector have been removed. Shared-boundary, lifetime and queue regressions remain covered.

Broad typed HTML/SVG coverage, generated scaffolding, structural view boundaries, server request ownership, hydration, retained-allocation budgets and complete ergonomic sugar remain unfinished. No Ripple/Solid speed ranking is established by this PoC.

Latest follow-on: [typed native interop](2026-10-11-native-interop.md) preserves the user's native binding conversion and fixes the browser scheduler receiver. The [structural composition proof](2026-10-11-structural-boundary-composition.md) demonstrates loading/error/content selection through a core boundary and existing keyed region, with same-key identity and replacement cleanup. The [branch and snapshot experiment](2026-10-11-branches-and-snapshots.md) adds a scalar `Branch.select` adapter over that runtime and measures lists versus arrays on Fable and .NET. Current scoped acceptance is 164 Node cases and 18 Chromium tests. Arrays merit an internal storage experiment; no runtime migration or branch throughput improvement is established. A specialized branch engine, descendant effect-error boundaries and coordinated reveal remain open.

The [DOM array migration](2026-10-12-dom-array-migration.md) implements the subsequent decision: keyed snapshots and reconciliation storage use arrays, with list authoring adapters. Core Ranvier is unchanged. Its scoped acceptance passes 166 Node cases and 18 Chromium tests. The earlier benchmark records remain evidence from before this migration.

The [published catalogue integration](2026-10-12-catalogue-dom-generation.md) adds the five-tag Wire-backed generator and typed static/reactive property and listener helpers over handwritten runtime operations. Latest scoped acceptance passes 176 Node cases and 18 Chromium tests. Broad generation, application migration, portable property metadata and runtime error/reveal work remain open.

The [HTML expansion](2026-10-12-full-html-generation.md) covers 112 tag constructors, supported properties and typed events, with shared inherited helpers and explicit omission metadata. Foundation's playground uses generated helpers and arrays. Its acceptance passes 180 Node cases and 18 Chromium tests.

The [common attribute authoring](2026-10-12-attribute-authoring.md) adds 34 generated static/reactive attributes plus data-/aria- helpers over the existing owned binding path. Latest scoped acceptance passes 188 Node cases and 18 Chromium tests. SVG, methods, additional type shapes, complete portable property metadata and runtime error/reveal work remain open.

Components now use [owner lifetime](2026-10-13-owner-lifetime.md): the core owner is the only component lifetime, and the owned range records mount roots only. Cleanup follows `use` order, and moved generated descendants leave their mount's ownership. Its acceptance passes 205 Node cases and 22 Chromium tests.

Event handlers now use [delegated listeners](2026-10-13-delegated-listeners.md): bubbling events dispatch from one listener per mount host. Its acceptance passes 211 Node cases and 22 Chromium tests.

Mount-path allocation is reduced by [allocation cuts](2026-10-13-allocation-cuts.md): children arrays, pass-through handlers and setters, and shared binding commits. Its acceptance passes 211 Node cases and 22 Chromium tests.

Components can be written as [component templates](2026-10-14-component-templates.md): a component type builds its view once, and each instance clones a shared skeleton with one state array and one render effect, built by one `MountState` object per mount instead of per-mount closures. In one run on this machine at commit `a0de558`, a template mount is 1.18×, 1.16× and 1.17× Solid 2 at 1, 16 and 128 rows (0.97×, 0.88× and 0.81× Ripple) and disposal 1.32×, 1.25× and 1.56× (2.28×, 0.80× and 0.95× Ripple); in paired runs against `08526f9` the one-row mount fell from 1.22× to 1.12× Solid 2 and its allocation from 5.9 to 4.4 KiB (Solid 2: 4.0 KiB). At 128 rows it allocates 1.04× Solid 2, against 1.97× mount time and 3.46× allocation for authored components, so both spec targets (1.3× mount, 1.5× allocation) are met. Disposal at 16 and 128 rows is led by core edge removal. Its acceptance passes 258 Node tests and 22 Chromium tests.

Binding `T.from` slots directly with the core fixed-dependency binding was [measured against the render effect](2026-10-14-direct-binding-spike.md): for the benchmark row it makes mount 2.16×, allocation 1.88× and update 1.58× slower, so `T.from` slots stay in the render effect.

The component templates were built by three plans, each recorded in the [component templates note](2026-10-14-component-templates.md):

- Plan 2a, [core](../superpowers/plans/2026-10-13-component-templates-core.md): the template model, the builder, `Component.define`, the compiled DOM path and the per-mount state, with the Solid 2 benchmark and allocation targets met.
- Plan 2b, [structure](../superpowers/plans/2026-10-14-component-templates-structure.md): view holes, `matchWith`, `branch`, `each` and inlined child templates, with dispose-first arm switches.
- Plan 2c, [catalogue](../superpowers/plans/2026-10-14-component-templates-catalogue.md): the generated template catalogue, `Generated/HtmlTemplate.fs`, covering every tag and generated attribute, property and event; paired runs show the benchmark row's cost unchanged. Its acceptance passes 312 Node tests and 22 Chromium tests.

[Keyed rows and arms on core primitives](../superpowers/specs/2026-10-14-keyed-rows-on-core-design.md) then moved the view's keyed lists, arm switches and mount lifetimes onto core projections, memos and owners, under the rule that the view builds on the core (`.claude/rules/view-uses-core.md`). The [measurements](2026-10-14-keyed-rows-on-core.md) put list creation at 1.69× Solid 2 and arm switches at 1.77×; `swap1k` (4.56×) and `remove1k` (5.00×) miss their ≤ 3× targets. Its acceptance passes 328 Node tests and 23 Chromium tests. The framework plan's [where we stand](../superpowers/plans/2026-10-06-fable-ranvier-framework.md#where-we-stand-2026-10-14) section summarises the current state.
