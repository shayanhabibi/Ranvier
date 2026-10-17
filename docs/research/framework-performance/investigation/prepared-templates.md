# Prepared template cloning

This follows the binding setup reduction at `4d2d109`. `Prepared.prepare` caches a static HTML skeleton on its first browser mount. `Prepared.view` and `Prepared.createComponent` clone that skeleton per instance and run a setup callback that attaches typed modifiers (`Prepared.apply`) and text bindings (`Prepared.text`) to nodes of the clone. The ordinary element construction path is unchanged; the prepared path is opt-in.

Every cloned descendant, including `<template>` content, is claimed and tracked by the owned range, so moved descendants are still removed on disposal and overlapping native ownership is still rejected. Prepared operations accept only nodes of the clone under setup. Skeletons accept static elements, text and attributes; reactive instructions, browser hooks, keyed views and components are rejected before their callbacks run. Server serialization rejects prepared views. The skeleton is created from the global `document`, so a template is bound to the document of its first mount.

## Validation

The full playground test command passes 205 Node tests and 22 Chromium tests. Eight new cases in `PreparedChecks.fs` cover skeleton reuse with independent synchronous and queued bindings, ownership of externally moved descendants, setter failure rollback, disposal during setup, foreign and non-text targets, static-only preparation, and `<template>` content. The framework harness verifies all five adapters at all three widths in each browser process.

## Measurements

`prepared-framework-results.json` compares the ordinary and prepared Ranvier adapters with Solid, Ripple and React through one harness run: three independent Chromium processes, Release Fable, minified production bundles. At 128 rows, in µs per operation:

- Mount: Ranvier 960.78; prepared 900.62; Ripple 555.94; Solid 316.25; React 588.75. Prepared is 6.3% faster than ordinary in every process median; it remains 1.62× Ripple and 2.85× Solid.
- Update: Ranvier 128.45; prepared 125.50; Ripple 132.70; Solid 126.78; React 281.08. All signal frameworks are equivalent.
- Disposal: Ranvier 359.38; prepared 346.09; Ripple 53.59; Solid 49.06; React 94.22. One process regresses; cloning does not address the disposal architecture.
- Retained JS heap: Ranvier 426.6 KiB; prepared 320.2 KiB; Ripple 162.9 KiB; Solid 129.7 KiB; React 189.0 KiB. Prepared retains 24.9% less, consistently across processes.

Cloning removes element construction and static attribute writes, which reduces memory substantially but mount time only modestly. The remaining mount cost lies in per-instance component scopes, binding setup and range tracking, not DOM creation. Disposal remains about 7× Solid and Ripple.

## Reproduction

From `examples/Fable.Ranvier.Playground`:

```powershell
rtk node benchmarks/frameworks/run.mjs --build --prepared-comparison
```

The artifact records source hashes, per-adapter correctness checks and every raw sample. Both Ranvier adapters run in the same processes, so the ordinary adapter serves as the in-run baseline.
