## The view builds on the reactive core

The view renders what the core computes. Ownership, disposal, keyed identity, change detection
and scheduling belong to the core (`src/Ranvier`). The view uses the core's primitives for them,
the way Solid's `<For>` uses `mapArray`:

- Lists and keyed collections: `createProjection` / `createProjectionWith` /
  `createIndexProjection`. The view lays out the rows' nodes in the projection's order.
- Arm and branch selection: a memo with a comparer (`createMemoWithComparer`,
  `createOptionMemo`), with an owner per selected arm.
- Ownership and cleanup: `Owner`, `createRoot`, `onCleanup`, `runWithOwner`.
- Skipping unchanged values: the comparer overloads (`createEffectOnWithComparer`,
  `createMemoWithComparer`).
- Scheduling: `batch`, `flush` and the graph's dispatcher.

The view owns only state with no core equivalent: DOM nodes and their layout, template tables,
event delegation, ownership claims on native nodes, attribute and property setters, and server
HTML. The last value written to a DOM target is DOM state, so the view keeps it.

A view-side ownership tree, key matcher, reconciler, selection cache, commit queue or other state
manager that duplicates a core primitive requires agreement with the user before it is written.
Raise it as a question: name the core primitive, and the behaviour it lacks. When the core lacks
a behaviour, the default proposal is to add it to the core.

Every view spec and plan opens with a **Core and view state** section, before any design:

- **Core primitives used:** each primitive and the job it does in the design.
- **View-owned state:** each piece of state the view keeps, and why the core cannot hold it.

A spec without the section, or with view-owned state outside the list above and no recorded
agreement, is not ready for review.

Duplicates replaced on 2026-10-14: the keyed region reconciler (formerly `KeyedRegion` and
`Keyed.validateUniqueKeys`) by core projections and `Projection.ensureUniqueKeys`, arm switches by a
core memo with an owner per arm, and the renderer `Scope` tree by `MountLifetime` over core owners
(`Lifetime.fs`). The benchmark baseline `BindingBaselineMounting.fs` keeps a frozen copy of the old
code. Under review: the queued commit `Queue` (`Scheduling.fs`).
