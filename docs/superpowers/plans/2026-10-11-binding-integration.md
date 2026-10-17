# Binding integration and measurement

> Superseded by the [text/boundary repair](../../research/2026-10-11-text-boundary-repair.md). This document records the rejected per-binding outcome experiment. Its policies, extra roots and HTML snapshot selector have been removed.

Continues framework Task 5 from `272b82b`, under the user's instruction to integrate and benchmark the policy proof. Execution is inline in the existing DOM worktree.

1. Add an explicit policy-bearing text operation to Tree/Program and Backend. Keep `Bind` as the old comparison path. Expose a direct typed text helper. Views contain policies/readers; mounts choose scheduling.
2. Add `mountWith` selecting synchronous or deferred commit mode. A top-level mount owns its queue; nested keyed mounts share it. Expose flush and latest commit errors on the returned mount. Scope disposal cancels binding slots; outer disposal releases the queue.
3. Require an explicit snapshot outcome selector when serializing these bindings as HTML. Server rendering executes the selector instead of client status/report/fallback callbacks; existing `Html.render` rejects the new operation rather than inferring server policy.
4. Prove both representations, both scheduler choices, nested/keyed lifetime and independent mounts, initial/deferred outcomes, escape behavior and setup failure. Migrate Foundation row text and a pending/error/recovery panel in the playground.
5. Measure equivalent mounted text workloads in Release Chromium: old Foundation Bind, direct policy text and typed helper, synchronous and queued modes where comparable. Separate construction/mount/update/dispose; record independent runs, warmups, medians/tails, versions and resource/cost limits. Do not claim allocation or framework parity from timing.
6. Run full browser/Node, scoped FCS/Release and portable .NET gates, canonical .NET acceptance, fresh review, docs and commit.

Ruling: the representation is provisional experimental source, so adding a required Operations record field is acceptable here; published Fable.Ranvier APIs remain unchanged. Scoped FCS impact found 47 Backend sites in four files and 26 Operations sites in nine files. Its API flag reports internal because the containing experiment is an executable; this is not evidence that the source type lacks public visibility. Linked Playground and portable check projects must also compile.

Ruling: explicit HTML snapshot selection is independent of client lifecycle callbacks and scheduling. It avoids running client side effects on the server. Full request ownership and hydration remain later tasks.

Execution record: all six steps completed for the experimental text-binding scope. Initial callback-unwind tests failed in Tree/Program before `startChecked`, then passed. Full acceptance passed 187 Node cases / 18 Chromium tests; portable CLI and scoped FCS checks passed; canonical .NET tests passed. Whole-solution build retains the five baseline Fable-test errors. Fresh review found no important defects. The [evidence](../../research/2026-10-11-binding-integration.md) records a material 2.08–2.52× synchronous update regression and the benchmark's limits; this is the next optimization gate, not approval to freeze the API or build full ergonomic sugar.
