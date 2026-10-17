# Owner lifetime

Components now use the core owner as their only lifetime. The renderer creates no per-component `Scope`; each mount keeps one `Scope` for its phase, failures and keyed regions. The owned range records the mount's roots, native nodes and keyed boundaries; generated descendants and prepared clone descendants are appended directly. This relies on core teardown in reverse acquisition order ([#50](https://github.com/shayanhabibi/Ranvier/pull/50)): the mount registers its DOM release before building, so release runs after every component cleanup.

The decision and its measurements are recorded in the [mount cost ladder](framework-performance/investigation/mount-ladder.md), where this model was the R5 variant.

## Guarantees

Kept:

- Every cleanup runs while its DOM is attached, on explicit, owner-driven and graph-driven disposal.
- Bindings and listeners stop when their mount is disposed.
- A node inside a live mount cannot be claimed by another mount.
- `Native (node, Owned | Borrowed)` ownership, setup rollback, and attach validation are unchanged.
- Cleanup errors report through the mount's reporter. Registering a component cleanup after disposal raises, and a component disposed during its own setup fails the mount.

Changed:

- Cleanup follows `use` order. A component's children are torn down before cleanups it registered earlier, and sibling components are torn down last-created-first.

Dropped:

- A generated or prepared descendant moved out of its mount is neither removed when that mount is disposed nor protected against other mounts. `Native` adopts it into another mount.
- Disposal detaches each root once. Descendants stay inside the detached root.

The suites encode these rules. Ten checks were rewritten: four structural-selection cases, wide cleanup at 128 and 512 components, and four moved-descendant cases across generated nodes, reparented nodes, prepared clones and prepared template content. The full playground suite passes 205 Node tests and 22 Chromium tests.

## Measurements

[Raw results](framework-performance/investigation/owner-lifetime-framework-results.json) use the framework harness: three Chromium processes, Release Fable, minified production bundles, all 36 correctness checks passing. Solid is 2.0.0-rc.14 compiled by `@solidjs/compiler` 2.0.0-rc.14. At 128 rows, µs per operation:

| | Ranvier | Solid 2 | Ripple | React |
|---|---|---|---|---|
| Mount | 884 | 353 | 512 | 661 |
| Update | 133 | 122 | 130 | 283 |
| Dispose | 168 | 37 | 59 | 102 |
| Retained JS heap | 397 KiB | 115 KiB | 163 KiB | 189 KiB |

The renderer-scope model measured 961 µs mount, 359 µs disposal and 427 KiB retained heap in the [prepared comparison](framework-performance/investigation/prepared-templates.md). Disposal falls by about half, to 4.5× Solid 2 and 2.8× Ripple. Mounting remains 2.5× Solid 2 and 1.7× Ripple; the ladder attributes most of the remaining mount cost to backend dispatch.

## Reproduction

From `examples/Fable.Ranvier.Playground`:

```powershell
rtk node benchmarks/frameworks/run.mjs --build
```

The harness writes `docs/research/framework-performance/results.json`; the committed copy of this run is the raw results file linked above.
