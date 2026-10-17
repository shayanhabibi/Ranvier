# Mount cost rationale

This accounts for the remaining 128-row mount gap after [owner lifetime](../../2026-10-13-owner-lifetime.md): Ranvier 884 µs against Solid 2 353 µs and Ripple 512 µs. Each cost is classified as buying a guarantee the other frameworks lack, buying a design choice, or buying nothing.

Solid is `solid-js` and `@solidjs/web` 2.0.0-rc.14, compiled by Solid's native compiler `@solidjs/compiler` 2.0.0-rc.14. Its adapters call `flush()` after each write, so batched DOM work is measured.

## Behaviour probes

`benchmarks/frameworks/semantics.mjs` runs the same scenarios against all three frameworks in Chromium; [raw results](semantics-results.json).

| Scenario | Ranvier | Solid 2 | Ripple beta.9 |
|---|---|---|---|
| Caret: input node, focus and selection 2–2 kept after a reactive input write | kept | kept | kept |
| Row 3 of 4 throws from a property setter: host afterwards | `<span>keep</span>` | `<span>keep</span>` | `<span>keep</span>` |
| … row cleanups run | 3 | 3 | 0 |
| … reactive reads caused by a later source write | 0 | 0 | 3 |
| Row 3 of 4 throws from its title reader: mount | completes, row 3 title unset | throws; 3 cleanups run | throws |
| … reactive reads caused by a later source write | 3, from a live mount | 0 | 2, with no mount to dispose |

Caret preservation is common to all three. Solid 2's `render` disposes its partial root before rethrowing, so a failed Solid 2 mount leaves no running bindings, as Ranvier's does; it also reports the error to the page's error handler. A failed Ripple mount leaves the bindings created before the failure running, without a handle to dispose them. A throwing reader is a failed value in Ranvier, and a failed mount in Solid 2 and Ripple.

## Measured costs

`ladder-results.json` holds the owner-lifetime ladder with two variant rungs compared against production; the ladder measures Ranvier alone. At 128 rows, µs per operation:

| Cost | Mount | Dispose | Solid 2 | Ripple | Buys |
|---|---|---|---|---|---|
| Per-element `addEventListener`, owner-aware wrapper and cleanup registration | 118 (−121 / −72 / −56 per process) | 57 | delegates `onClick` | pays | nothing beyond delegation: the delegated variant keeps the owner check, batching and untracked handlers |
| Per-component core owner | 49 | 49 | absent: components are functions | pays (`Signal.root` per row) | component cleanup and disposal after a failed mount |
| Setup checks | 28, mixed | — | absent | absent | rejection of view construction outside live setup |
| Attribute validation | not resolved (−62 / +57 / +18) | — | absent | absent | rejection of `on*` and `srcdoc` attribute names |
| Binding guards | not resolved | — | equal-write skip only | equal-write skip only | rollback on a failing setter, which Solid 2 also provides; queued commits |
| Range ownership | 3 | — | absent | absent | shared hosts, movable mounts, borrowed native nodes |
| Reactive core | 124 | 14 | lighter nodes | lighter nodes | pending and failure values, async flights, `use`-order teardown |
| Backend dispatch and mount | 337 (+499 / +422 / +360) | 109 | compiled template clones and direct bindings | direct functions, eager DOM | reusable, portable view values without a compiler |

## Dispatch profile

`owner-profile/` holds source-mapped CPU profiles of 256 mounts per framework and an allocation sample. The profiled runs take 364 ms for Ranvier, 124 ms for Solid 2 and 203 ms for Ripple. Of the 240 ms gap to Solid 2:

- Garbage collection: 58 ms (80 ms against 22 ms).
- Native DOM work: 38 ms. Ranvier spends 79 ms in `createElement`, `appendChild`, `insertBefore`, `setAttribute`, `addEventListener` and `createTextNode`; Solid 2 spends 41 ms, 28 ms of it in `cloneNode`.
- Ranvier's own JavaScript: the remaining 144 ms.

Sampled JavaScript allocation per mount is 603 KiB for Ranvier, 369 KiB for Ripple, 140 KiB for Solid 2 and 30 KiB for handwritten DOM. Ranvier's largest allocation sites:

- Per-instance view definitions in the authoring fixture: about 112 KiB. Each component instance rebuilds its modifier arrays and view closures.
- `startBinding`: 89 KiB, about 174 bytes per binding in closures and captured `FSharpRef` cells.
- Core nodes, including effects, owner links and cleanup arrays.
- Listener wrappers: 60 KiB in `Typed.listener` and `Events_onClick`.

F# compilation artefacts appear as CPU self time without semantic purpose: `uncurry2` adapters, `FSharpRef` construction, structural `equals` for string option comparison, and `Seq` enumerators in modifier loops.

## Classification

- Buys a guarantee Solid 2 or Ripple lacks, and stays: per-component owners, setup checks, attribute validation, range ownership and the reactive core's semantics. Rollback after a failing setter is shared with Solid 2 and absent from Ripple. Together roughly 200–250 µs of the gap, more than half of it the reactive core.
- Buys a design choice: interpreting view values at runtime rather than compiling them. Its costs are per-instance allocation and element-by-element construction; Solid 2's template clones make its native DOM work 38 ms cheaper per 256 profiled mounts, about 0.15 ms per mount under the profiler.
- Buys nothing: per-element listeners, about 118 µs mount and 57 µs disposal, since replaced by [delegated listeners](../../2026-10-13-delegated-listeners.md), and the F# compilation artefacts and per-instance allocation inside dispatch, most of the 337 µs.

## Reproduction

From `examples/Fable.Ranvier.Playground`:

```powershell
rtk node benchmarks/frameworks/semantics.mjs --build
rtk node benchmarks/frameworks/ladder.mjs --build
rtk node benchmarks/frameworks/diagnose.mjs --build --out=owner-profile
rtk node benchmarks/frameworks/summarize-profiles.mjs --out=owner-profile
rtk node benchmarks/frameworks/allocations.mjs --out=owner-profile
```
