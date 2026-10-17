# Component templates

[Component templates](../superpowers/specs/2026-10-13-component-templates-design.md) build a component's view once per component type and materialise each instance as one skeleton clone, one state array, one render effect and one property write per event node. `Template.fs` holds the portable model: a backend-neutral template tree, a typed builder, `Component.define`/`render`/`renderWith`, an expansion into the existing `Operations`, and one markup writer shared by the DOM skeleton and server HTML. `TemplateDom.fs` compiles a definition once into a parsed skeleton, a walk program and slot tables; the mount backend's `Backend.Template` override materialises instances through it, and the delegated dispatcher finds template handlers on the node's state array. Other backends render templates through the default expansion.

The implementation plan fixes five points the spec left open:

- **Builder parameter.** Templates are written against a builder, `Component.define setup (fun t -> t.div [| ... |] [| ... |])`. The builder's type parameter fixes the instance type before F# resolves the overloaded part constructors; the spec's static `T.*` sketch cannot type its lambdas.
- **Static properties** are written once per instance during materialisation, not baked into the skeleton: a clone copies attributes, not property values, and server HTML omits properties.
- **`from` slots** evaluate inside the render effect until the fixed-dependency binding lands.
- **Queued commit mode** renders templates through the default expansion; the compiled path serves synchronous mounts, which the benchmark uses.
- **Component-ownership check.** The allocation sample of the template adapter replaces the spec's ladder re-run, because the ladder measures authored components and has no template rung.

The benchmark row is the existing framework row written as a template: a `div` with a computed `id`, a static class and a `title` from the shared source; an `input` whose `value` follows the source; a `button` whose `disabled` follows a shared memo, with a click handler and a text slot reading a per-row signal. The `ranvier-template` adapter is `FrameworkPerf.mountTemplate`, built on the same `mountCore` helper as the authored `ranvier` adapter.

## Measurements

All numbers below are from one run on this machine (AMD Ryzen 9 9900X, Chromium 153.0.8010.12) at commit `a0de558`, measured against Solid 2.0.0-rc.14 compiled with `@solidjs/compiler`. The machine has slow phases: the comparisons are ratios between frameworks measured in the same processes; absolute values are not comparable with earlier notes. The previous figures, at `8762e5c`, are from one run of the same benchmark.

The measured code includes binding reader-failure reporting: every synchronous binding and template effect wraps its reads in a `try`/`catch` and checks a failure flag on each run. It also includes the parse validation in `compile`, which runs once per definition.

[Framework results](framework-performance/investigation/template-framework-results.json) (`run.mjs --template-comparison`), three Chromium processes, all 45 correctness checks passing. At 128 rows, µs per operation, overall median with the per-process medians below it:

| | Ranvier (authored) | Ranvier template | Solid 2 | Ripple | React |
|---|---|---|---|---|---|
| Mount | 564 | 333 | 286 | 414 | 495 |
| | 576 / 570 / 537 | 333 / 385 / 322 | 296 / 294 / 273 | 370 / 389 / 446 | 519 / 516 / 481 |
| Update | 114 | 114 | 107 | 115 | 246 |
| | 115 / 114 / 111 | 112 / 117 / 113 | 107 / 111 / 106 | 116 / 111 / 117 | 250 / 243 / 244 |
| Dispose | 75 | 39 | 25 | 41 | 76 |
| | 69 / 75 / 85 | 35 / 40 / 37 | 25 / 26 / 24 | 41 / 39 / 44 | 73 / 74 / 81 |
| Retained JS heap per mount | 345 KiB | 111 KiB | 116 KiB | 163 KiB | 189 KiB |

The template path against Solid 2 and Ripple at every width, µs per operation (overall median), with the ratios at `8762e5c`:

| Phase, rows | × Solid 2 at `8762e5c` | × Ripple at `8762e5c` | Template at `a0de558` | Solid 2 | Ripple | × Solid 2 | × Ripple |
|---|---|---|---|---|---|---|---|
| Mount, 1 | 1.07 | 0.97 | 5.9 | 5.0 | 6.1 | 1.18 | 0.97 |
| Mount, 16 | 1.08 | 0.86 | 43.3 | 37.4 | 49.3 | 1.16 | 0.88 |
| Mount, 128 | 1.10 | 0.79 | 333.4 | 285.9 | 414.1 | 1.17 | 0.81 |
| Update, 1 | 0.81 | 0.87 | 1.0 | 1.3 | 1.1 | 0.80 | 0.93 |
| Update, 16 | 1.04 | 1.00 | 14.2 | 13.4 | 14.1 | 1.05 | 1.01 |
| Update, 128 | 1.07 | 1.00 | 113.8 | 106.7 | 114.6 | 1.07 | 0.99 |
| Dispose, 1 | 1.53 | 3.05 | 1.3 | 1.0 | 0.6 | 1.32 | 2.28 |
| Dispose, 16 | 1.47 | 0.94 | 5.1 | 4.1 | 6.4 | 1.25 | 0.80 |
| Dispose, 128 | 1.35 | 0.79 | 38.6 | 24.7 | 40.6 | 1.56 | 0.95 |

Per process, the one-row mount is 1.10 / 1.21 / 1.21× Solid 2 and the one-row disposal 1.34 / 1.29 / 1.31×; the 128-row mount is 1.13 / 1.31 / 1.18×. The two columns of ratios come from different sessions, and the commits between `8762e5c` and `08526f9` changed range validation, staging and delegation; the paired runs under [per-mount state](#per-mount-state) isolate the change to the mount state.

Against the authored path in the same processes, templates mount in 0.59× the time, dispose in 0.51× and retain 0.32× the heap; updates are 1.00×. Against Solid 2, the authored path mounts in 1.97×. Templates retain 0.96× Solid 2's heap per 128-row mount. The one-row heap measurement varies between processes by more than the difference it would show (template 3.7, 5.1 and 5.2 KiB; Solid 2 1.7, 3.1 and 3.2 KiB) and is not compared.

[Allocation sampling](framework-performance/investigation/template-profile/allocations.json), one browser process, 4096-byte interval, per 128-row mount:

| | Ranvier (authored) | Ranvier template | Solid 2 | Ripple |
|---|---|---|---|---|
| Allocated | 471 KiB | 142 KiB | 136 KiB | 373 KiB |

Templates allocate 1.04× Solid 2 (1.02× at `8762e5c`) and 0.30× the authored path; the authored path allocates 3.46× Solid 2.

### Per-mount state

At `08526f9`, `mountCore` kept a mount's state in local variables and created about 48 closures per mount over them: 16 local helpers, 18 backend delegates in a `MountMembers` record that `MountBackend` forwarded to, 3 `BuildContext` callbacks, 5 setup callbacks handed to core, 5 `Mounted` callbacks and an `onRoot` callback. A mount's first template added a `MountServices` record of 10 more closures. Six commits replace them:

- `f74084a`: the allocation sampler takes `--modes`, `--width`, `--roots`, `--interval` and `--top`.
- `b575422`: `MountState`, one class per mount, holds the state as fields and is the backend; its helpers compile to functions taking the instance.
- `316d61a`: `MountServices` is an interface implemented by the mount state; the template `Setup` record is created by the first template.
- `02f164e`: `BuildContext` and `Mounted` are classes over the mount state. A build function that reads `ctx.Backend` from an unannotated parameter now needs a `BuildContext` annotation.
- `9cf54fe`: the cleanup-error list, the delegated-event list and the range's generated-node list are created on first use; delegation release is registered only for a mount with delegated events, and `mountWith` passes no `onRoot` callback.
- `a0de558`: the range's staging fragment is created by the first staging or movement; a range whose roots attach individually never creates it.

A one-row template mount with a delegated click now creates 8 closures in `Mounting.fs`: the scope's release callback, the 5 setup callbacks handed to core, the delegation release and the template `Setup` callback.

[Allocation sampling](framework-performance/investigation/template-root-allocations/) of 4096 one-row mounts at a 128-byte interval (`allocations.mjs --modes=ranvier-template,solid --width=1 --roots=4096 --interval=128`), one browser process per variant, bytes per mount, [at `08526f9`](framework-performance/investigation/template-root-allocations/before/allocations.json) and [at `a0de558`](framework-performance/investigation/template-root-allocations/after/allocations.json):

| Site | `08526f9` | `a0de558` |
|---|---|---|
| Mount state: `mountCore` closures, `MountMembers`, `Mounted` and `BuildContext`; then the `MountState` instance, `Run`, `Mounted` and `BuildContext` | 1616 | 249 |
| Setup callbacks handed to core | 61 | 56 |
| Template services; then the template `Setup` record | 230 | 63 |
| `OwnedRange`: range, boundary comments, entries | 249 | 216 |
| `mountWith` `onRoot` callback | 29 | |
| Other | 18 | 14 |
| `Mounting.fs` total | 2206 | 603 |
| Whole mount, Ranvier template | 5894 | 4374 |
| Whole mount, Solid 2 | 3985 | 3967 |

A one-row template mount allocates 1.10× Solid 2, against 1.48× at `08526f9`. The remainder is in core (1709 bytes: effects, owners and the fixture's own `Graph` per mount), the fixture (592) and `TemplateDom.materialise` (about 410: the state array, the render effect and its closures).

Paired runs of `run.mjs --template-comparison`, alternating `08526f9` and `a0de558` three times each in one session; median ratio of the three runs, with their range:

| Phase, rows | × Solid 2 at `08526f9` | × Solid 2 at `a0de558` | × Ripple at `08526f9` | × Ripple at `a0de558` |
|---|---|---|---|---|
| Mount, 1 | 1.22 (1.19–1.23) | 1.12 (1.11–1.14) | 1.08 | 0.97 |
| Mount, 16 | 1.17 (1.16–1.18) | 1.17 (1.16–1.19) | 0.90 | 0.89 |
| Mount, 128 | 1.17 (1.16–1.24) | 1.21 (1.17–1.28) | 0.75 | 0.80 |
| Dispose, 1 | 1.34 (1.30–1.38) | 1.33 (1.31–1.33) | 2.21 | 2.23 |
| Dispose, 16 | 1.50 (1.39–1.53) | 1.46 (1.31–1.62) | 0.97 | 0.92 |
| Dispose, 128 | 1.65 (1.53–1.66) | 1.52 (1.48–1.65) | 0.90 | 0.91 |

The one-row mount fell from 6.1 to 5.7 µs while Solid 2 stayed at 5.0 µs; the other widths and disposal moved within the spread between runs. In [the one-row CPU profiles](framework-performance/investigation/template-root-profile/) (`diagnose.mjs --modes=ranvier-template,solid --phases=mount,dispose --width=1 --roots=256 --repeat=160`, one session per variant), the mount's garbage collection fell from 55.4 to 31.5 ms and `Mounting.js` self time from 49.7 to 33.2 ms; the busy-time ratio to Solid 2 moved from 1.39 to 1.21 for mount and from 1.36 to 1.18 for disposal.

The range boundary comments stay. They give an empty mount a position, anchor `Move` and `Relocate`, delimit keyed rows, and their claims let the native-ownership check detect an empty mount inside a native node. In the `a0de558` profile of 40960 one-row mounts and disposals, `createComment` took 6.5 ms, `claim` 7.1 ms and `unclaim` 6.8 ms for three nodes per mount, two of them boundaries, and `remove` 25.2 ms for the same three nodes. Counting two thirds of the per-node costs, the boundaries take about 32 ms of 352 ms busy, or 0.8 µs per mount and disposal.

## CPU profile

### Mount at 128 rows

[The mount profile](framework-performance/investigation/template-mount-cpu-profile.json) (`diagnose.mjs --modes=ranvier-template,solid --phases=mount --roots=1024`, then mapped through the bundle's source map) samples one run of 1024 128-row mounts per framework at 100 µs, in one Chromium process. Busy time excludes idle and unattributed `(program)` samples.

At `5e69af2` the template mount was busy for 1167 ms against Solid 2's 722 ms. Ranked by self time:

| Cost | ms | Share | Solid 2 |
|---|---|---|---|
| `cloneNode` | 199 | 17.0% | 166 |
| Garbage collection | 173 | 14.9% | 109 |
| `input.value` writer (native setter) | 166 | 14.2% | inside the row's template function, 152 in total |
| `materialise` | 99 | 8.5% | |
| `appendChild` of each row into the section | 84 | 7.2% | 18 (`insertBefore`) |
| `insertBefore` of the section into the host | 64 | 5.4% | 48 (`appendChild`) |
| `collect` | 50 | 4.3% | |
| `setAttribute` | 47 | 4.1% | 54 |
| `SourceList.Add` (core) | 39 | 3.3% | |
| `write` | 31 | 2.6% | |
| `Array.fill` for the state array | 24 | 2.1% | |

Inclusive, the render effects took 358 ms (30.7%), of which 249 ms were slot writes. Two template-path costs exceeded Solid 2's equivalents. A row cloned from the skeleton fragment is still a fragment child: appending it costs 2.3× appending a cloned element, which a separate Chromium micro-measurement reproduced (about 56 against 33 µs per 128 rows). The bounds-checked `item`/`setItem` calls and the `fill` kept `materialise` at 99 ms of self time.

Two commits address them: a single-root template clones its root node and walks from it, appending the clone without a roots array; multi-root templates append the fragment's children directly (`51d0783`). State-array and slot-table accesses compile to plain indexing, and the state array is allocated without a fill pass (`19ea929`). After both, the template mount was busy for 686 ms against Solid 2's 679 ms in the same profiling session: `materialise` self time fell to 12 ms, `collect` to 36 ms, `fill` disappeared, and row insertion fell to 50 ms (`appendChild`) and 37 ms (`insertBefore`), against Solid 2's 46 and 25 ms.

Two variants were measured and rejected. `document.importNode` in place of `cloneNode` raised the mount ratio from about 1.3× to 1.56× in a five-process spot check. Hoisting the commit closure out of the render effect does not change the emitted JavaScript: Fable inlines a single-use lambda binding, including a `Func`, back into the effect body.

### Disposal and per-root costs

The remaining gap fell with width, so the cost was per mount root and, for disposal, per row. [The disposal and per-root profile](framework-performance/investigation/template-dispose-cpu-profile.json) (`diagnose.mjs --modes=ranvier-template,solid --phases=mount,dispose --repeat`, mapped through the bundle's source map) samples, at 100 µs in one Chromium process per width, 160 runs of 256 one-row roots and 96 runs of 32 128-row roots. A single profiled run of 1024 roots in one document measures document size instead: its disposal cost 285 µs per 128-row root, against about 50 µs in the benchmark's 32-root batches. Busy time counts the samples inside the timed runs, including the garbage collector; a category holds JavaScript self time by source, with native DOM time and `fable-library` time attributed to the nearest Ranvier or Solid caller.

At `9798fd2`, before the fixes:

| Phase, rows | Ranvier template busy | Solid 2 busy | Ratio |
|---|---|---|---|
| Mount, 1 (40960 roots) | 645.5 ms | 300.4 ms | 2.15 |
| Dispose, 1 (40960 roots) | 173.0 ms | 128.7 ms | 1.34 |
| Dispose, 128 (3072 roots) | 234.7 ms | 143.8 ms | 1.63 |

One-row mount, ranked by excess over Solid 2:

| Cost | Ranvier | Solid 2 |
|---|---|---|
| View-layer JavaScript, including `fable-library` | 249.6 ms (38.7%) | 96.0 ms (`@solidjs/web` and the compiled row) |
| Garbage collection | 108.3 ms (16.8%) | 30.7 ms |
| Native DOM work | 184.2 ms (28.5%) | 124.5 ms |
| Core (`src/Ranvier`): effect creation and first run | 58.2 ms (9.0%) | 49.2 ms |

Within the view layer: each mount evaluated an object expression, so Fable emitted a new backend class per mount; its construction took `Prepared.Backend` 23.3 ms and part of `mountCore`'s 26.4 ms of self time, and the fixture's view, whose backend calls met a different class on every mount, took 31.6 ms of self time (1.5 ms at `8762e5c`). `OwnedRange.Attach` took 84.9 ms inclusive: range validation through `Option` closures and enumerators (`intactAt` 29.2 ms, `ValidateDestination` 10.4 ms) and the insertion of the staging fragment (46.3 ms; Solid 2's `appendChild` and `insertBefore` together took 27.1 ms). The per-host delegation helper allocated a `Map`, a listener closure and an immediately invoked arrow per host (25.5 ms inclusive with `addEventListener`).

One-row disposal: `remove` of the two range comments and the root took 35.1 ms, which matches Solid 2's `textContent` clear (34.9 ms). The rest was view-layer bookkeeping: `unclaim` 14.4 ms, two calls per single-node entry; `Scope` disposal through enumerators, a shared `ref` cell and a failure list built on success (about 37 ms); delegation release 22.0 ms. Core owner teardown took 32.0 ms against Solid 2's 15.1 ms.

128-row disposal: core owner teardown took 118.6 ms (50.5%) against Solid 2's 44.6 ms (31.0%); native removal matched (95.7 ms `remove` against 94.7 ms `textContent`); the view layer took 12.6 ms.

Eight commits change the view layer, each with both playground suites passing:

- `3d43e1c`: owned ranges walk their entries with index loops and plain `parentNode`/`nextSibling` reads, share one no-op callback, unclaim a single-node entry once, and add the mount's own created roots without the native-ownership overlap checks.
- `3cc4812`: scope disposal uses index loops and builds its failure list only on failure.
- `10afa06`: the mount backend is one `MountBackend` class that forwards to a record of per-mount delegates. The emitted code keeps the one `curry2` adapter the `Browser` member already had.
- `e3ddb17`: element modifiers apply through an index loop instead of an enumerator.
- `0f2b455`: a scope tree keeps its disposal order on its topmost scope instead of a `ref` cell per root.
- `9535212`: one delegated listener per event name serves every mount host; the host itself carries its mount count, and the listener reads its host from `this`.
- `0bf14ff`: marking child scopes attached uses an index loop.
- `8762e5c`: a mount's created roots stay detached during setup and attach one by one; the staging fragment is used once a keyed region, a native node or a root that arrives in a cloned fragment needs a shared parent. In a separate Chromium measurement, attaching two comments and a row this way took 1.60 µs against 1.75 µs through a fragment.

At `8762e5c`:

| Phase, rows | Ranvier template busy | Solid 2 busy | Ratio |
|---|---|---|---|
| Mount, 1 | 431.5 ms | 323.9 ms | 1.33 |
| Dispose, 1 | 160.3 ms | 118.8 ms | 1.35 |
| Dispose, 128 | 235.6 ms | 176.1 ms | 1.34 |

The one-row mount's garbage collection fell from 108.3 to 63.6 ms (Solid 2: 33.8 ms) and its view-layer JavaScript from 249.6 to 142.7 ms (Solid 2: 105.4 ms). Profiled ratios move between sessions on this machine: the 128-row disposal ratio changed from 1.63 to 1.34 with no change to its per-row path. The benchmark tables above give the before and after ratios.

### Core costs

These costs are in `src/Ranvier` and are left unchanged:

| Cost | Ranvier at `8762e5c` | Solid 2 |
|---|---|---|
| Per-row effect disposal at 128 rows: `Owner.TearDown` → `Effect.Dispose` → `SourceList.Clear`/`trimFrom` → `RemoveObserver` → `ObserverSet.Remove` | 116.8 ms of 235.6 (49.6%), about 0.30 µs per row; `ObserverSet.Remove` 54.9 ms self, `SourceList.trimFrom` 38.6 ms self | `disposeChildren` → `clearDeps` → `unlinkSubs`: 61.1 ms (34.7%), about 0.16 µs per row |
| One-row owner teardown | 37.7 ms of 160.3 (23.5%) | `disposeRootSelf` 25.0 ms (21.0%) |
| Dependency edges at a 128-row mount: `SourceList.Add` with its growth `fill`, `ObserverSet.Add`, `positionOf` and its growth `fill` | about 97 ms of 1773.5 (5.5%) | `link` 13.5 ms |

An `ObserverSet` above eight observers keeps a `Map` index; every removal deletes the observer from the index and moves the last observer into the hole, which writes the index again. The shared source and memo of the benchmark have 128 observers per mount, so each row's disposal pays two `Map` writes per shared edge. Solid 2 keeps each edge in doubly linked lists and unlinks it with a constant number of pointer writes. A swap-remove without the index, an index-free removal while the whole owner is being disposed, or linked edges would remove most of this cost; each is a core change.

## Spec targets

- **Mount within 1.3× Solid 2: met.** The 128-row template mount is 1.17× Solid 2 (333.4 against 285.9 µs); each process measured between 1.13× and 1.31×.
- **Allocation within 1.5× Solid 2: met.** The 128-row template mount allocates 1.04× Solid 2 (141.5 against 136.0 KiB).

## Next costs

Disposal at 16 and 128 rows is dominated by the core edge removal above. The remaining view-layer costs, from the one-row profiles and allocation samples at `a0de558` (4.3 KiB per Ranvier mount against 3.9 KiB for Solid 2):

| Cost | Size |
|---|---|
| Range boundary comments: two `createComment` calls, two of three `WeakSet` claims and two of three `remove` calls per mount | about 32 ms of 352 ms busy over 40960 one-row mounts and disposals; Solid 2 owns its host and clears it with `textContent` |
| `TemplateDom.materialise` per instance: the state array, the render effect and its closures | about 0.4 KiB per one-row mount |
| Setup callbacks handed to core: `Graph.Run`, `createRoot`, `onCleanup`, `Scope.OnCleanup` and `untrack` | 5 closures per mount |
| `collect` and `write` per row | 94 and 68 ms of the 128-row mount profile at `8762e5c` |

The render effect still allocates an `untrack` arrow per run; it recurs on updates, not on mount. Removing it needs a value Fable does not inline, or an untracked entry point in core that takes no closure.

The fixture gives every Ranvier mount its own `Graph`; its construction and activation (`Graph`, `Graph.Activate`) allocate about 0.4 KiB per mount. Solid 2's roots share one scheduler.

## Acceptance

With plan 2c, the playground suites pass 312 Node tests and 22 Chromium tests; the 14 portable .NET representation checks print `PASS`, and the 8 pure lifetime checks pass.

## Dynamic structure

Plan 2b ([implementation plan](../superpowers/plans/2026-10-14-component-templates-structure.md)) adds dynamic structure to templates:

- `t.view (fun r -> view)` renders a `Program.View` read once per instance.
- `t.matchWith (fun r -> union) arms` renders the arm that `arms` selects, an `Arm` built by `t.arm definition payload` from a `Component<unit -> 'payload>`. Arms are compared by definition: a different definition remounts, the same definition receives the new payload through its props reader.
- `t.branch (fun r -> key) arms` remounts its arm whenever the key changes under `=`.
- `t.each key (fun r -> items) row` renders one `Component<unit -> 'item>` row per item of an array, retained by key.
- `t.render child (fun r -> props)` inlines a child template's markup into the parent's skeleton.

> Replaced on 2026-10-14 by [keyed rows and arms on core primitives](../superpowers/specs/2026-10-14-keyed-rows-on-core-design.md). Lists now run on a create-first core projection, arms on a core memo with a root owner per arm, and view holes mount once. The paragraphs below describe the earlier keyed path.

A region (view hole, match, branch or list) is a pair of comment markers, `<!--[-->` and `<!--]-->`, in the skeleton and in server HTML. The DOM compiler records the start marker as a dynamic node; after the clone is appended, each region mounts its rows through the mount backend's keyed path, one nested mount per item, between the markers. Markers that are roots of a multi-root skeleton become one entry of the mount's range spanning the rows. An inlined child's dynamic nodes are part of its parent's walk; the child receives its own state array over that node range, its own render effect, and the delegated dispatcher calls its handlers with the child instance. Regions and inlined children of every depth are hoisted into the root compiled form.

Every region kind (view hole, `matchWith`, `branch` and `each`) mounts through the keyed path. An arm switch (`matchWith`, `branch`) is dispose-first, as in Solid 2: the previous arm's cleanups run and its nodes are removed, inside a batch, before the new arm is set up. If the new arm's setup throws, the region is left empty and the error is reported. An arm retained under an equal key keeps its row and receives the new payload. Keyed lists (`each`, `Keyed.list`) keep the create-then-retire order: new rows are set up and placed before removed rows' cleanups run. `materialise` iterates the compiled form's children and regions arrays for every instance, including a template whose arrays are empty.

Server HTML writes the markers and renders region rows through the HTML backend. The default expansion maps arm regions to `Switch`, list regions to `Keyed`, view holes to `Operations.Embed` and inlined children to `Component`; its HTML equals the template's once comments are removed.

Rows of a region at a mount's top level delegate events at the range's staging fragment while the mount is built. Attaching a staged range now relocates its entries, and a relocated mount moves its delegation to its new parent; authored top-level keyed lists gained the same fix.

### Cost of templates without structure

All numbers in this section are from this machine (AMD Ryzen 9 9900X, Chromium 153.0.8010.12) on 2026-10-14, against Solid 2.0.0-rc.14. The raw results are under [`template-structure/`](framework-performance/investigation/template-structure/); every `run.mjs` result records the commit it measured and the hashes of its sources.

At `729a5a2`, the end of the structure tasks, the benchmark row (no dynamic structure) had regressed against `19c8a8f`, the commit before them. Paired runs of `run.mjs --template-comparison`, alternating the two commits three times in one session, give the median ratio to Solid 2 with the range of the three runs. Three sessions were run: [`729a5a2`](framework-performance/investigation/template-structure/paired-729a5a2/), [`64f8bf7`](framework-performance/investigation/template-structure/paired-64f8bf7/) and [`f4273e6`](framework-performance/investigation/template-structure/paired-f4273e6/). `f4273e6` has the sources of `c85916f`; `64f8bf7` differs from it only by `bind` not being inlined.

| Phase, rows | `19c8a8f` | `729a5a2` | `19c8a8f` | `64f8bf7` | `19c8a8f` | `f4273e6` |
|---|---|---|---|---|---|---|
| Mount, 1 | 1.12 (1.11–1.15) | 1.16 (1.16–1.17) | 1.12 (1.09–1.13) | 1.13 (1.10–1.14) | 1.11 (1.10–1.12) | 1.11 (1.11–1.14) |
| Mount, 16 | 1.15 (1.14–1.16) | 1.21 (1.20–1.23) | 1.16 (1.14–1.17) | 1.16 (1.16–1.17) | 1.16 (1.13–1.19) | 1.16 (1.16–1.17) |
| Mount, 128 | 1.13 (1.13–1.15) | 1.21 (1.17–1.21) | 1.16 (1.15–1.19) | 1.15 (1.14–1.20) | 1.16 (1.14–1.22) | 1.21 (1.17–1.22) |
| Update, 1 | 0.78 (0.76–0.78) | 0.78 (0.78–0.81) | 0.80 (0.78–0.81) | 0.78 (0.78–0.79) | 0.79 (0.77–0.79) | 0.78 (0.77–0.79) |
| Update, 16 | 1.03 (1.03–1.04) | 1.04 (1.04–1.04) | 1.04 (1.04–1.04) | 1.04 (1.03–1.05) | 1.05 (1.03–1.08) | 1.04 (1.04–1.04) |
| Update, 128 | 1.06 (1.06–1.06) | 1.07 (1.07–1.08) | 1.07 (1.06–1.07) | 1.07 (1.06–1.07) | 1.06 (1.05–1.09) | 1.06 (1.05–1.07) |
| Dispose, 1 | 1.33 (1.25–1.40) | 1.36 (1.35–1.39) | 1.31 (1.26–1.39) | 1.26 (1.17–1.35) | 1.27 (1.22–1.27) | 1.27 (1.25–1.28) |
| Dispose, 16 | 1.21 (1.15–1.25) | 1.43 (1.28–1.62) | 1.25 (1.19–1.34) | 1.28 (1.27–1.29) | 1.26 (1.18–1.36) | 1.34 (1.06–1.36) |
| Dispose, 128 | 1.35 (1.32–1.48) | 1.59 (1.51–1.75) | 1.35 (1.34–1.35) | 1.39 (1.37–1.39) | 1.48 (1.44–1.62) | 1.45 (1.34–1.57) |

At `729a5a2`, the mount at 16 and 128 rows and the 16-row disposal exceed the base's range by more than its spread. The 128-row update, 1.07 against 1.06–1.06, exceeds it by 0.01, the table's resolution. The 128-row disposal, 1.59, exceeds the base's range of 1.32–1.48 by 0.11, less than its spread of 0.16. After the two fixes, mount matches the base. Two cells still sit above the base's range: the 128-row disposal at `64f8bf7` (1.39 against 1.34–1.35) and the 128-row update at `64f8bf7` (1.07 against 1.06–1.06, 0.01 above). The 128-row mount at `f4273e6` has a median of 1.21, within the base's range of 1.14–1.22 but 0.05 above its median. None of the three differences recurs in the other session: the 128-row disposal at `f4273e6` is 1.45 against the base's 1.48, the 128-row update at `f4273e6` is 1.06 against 1.06, and the 128-row mount at `64f8bf7` is 1.15 against 1.16. All three are left as unresolved session-to-session variation, not as a measured cost.

The cause of the `729a5a2` regression was the skeleton's document. Task 2 (`2379c2d`) made `compile` import every skeleton into the main document, so that region rows insert between the markers of a clone that belongs to the main document. Clones of an imported skeleton cost more to mount and to dispose than clones of the template element's inert content. `8e4fec2` imports a skeleton only when the template has regions. The `729a5a2` and `64f8bf7` columns differ by this change and by a documentation comment. An interleaved scratch sampler (not committed; both frameworks alternating, 18 rounds in each of three processes) also isolated it at `729a5a2`: the 16-row mount was 1.19–1.20× Solid 2 with the inert skeleton against 1.25–1.27× with the imported one, and the 16-row disposal 1.23–1.32× against 1.47–1.51×.

The split of `materialise` into `stateFor` and `bind` added one closure context per instance: the effect closures captured `bind`'s context and the setup closure `materialise`'s. Allocation sampling of 4096 one-row mounts at a 128-byte interval (`allocations.mjs --modes=ranvier-template,solid --width=1 --roots=4096 --interval=128`), bytes per mount:

| | `TemplateDom.js` | Whole template mount | Solid 2 |
|---|---|---|---|
| [`19c8a8f`](framework-performance/investigation/template-structure/allocations-64f8bf7/one-row-base.json), session with `64f8bf7` | 514 | 4387 | 3971 |
| [`64f8bf7`](framework-performance/investigation/template-structure/allocations-64f8bf7/one-row-head.json) | 550 | 4427 | 3967 |
| [`19c8a8f`](framework-performance/investigation/template-structure/allocations-f4273e6/), session with `f4273e6`, two samples | 511 / 519 | 4380 / 4389 | 3971 / 3960 |
| [`f4273e6`](framework-performance/investigation/template-structure/allocations-f4273e6/), two samples | 518 / 526 | 4385 / 4392 | 3980 / 3967 |

`c85916f` inlines `bind`, and the one-row template mount allocates what it allocated at `19c8a8f`. At 128 rows (`allocations.mjs --modes=ranvier-template,solid`, 4096-byte interval, interleaved, two samples each), the template allocated 149.5 and 148.3 KiB per mount at `f4273e6` (1.06 and 1.04× Solid 2) against 143.7 and 145.4 KiB at `19c8a8f` (1.03 and 1.02×). By source, `TemplateDom.js` measured 58.7 and 56.1 KiB against 55.8 and 56.1 KiB. The rest of the difference is attributed to the benchmark row's setup function in `FrameworkPerf.js` (3.6 and 3.0 KiB against 0.7 and 0.8 KiB). That source is unchanged between the two commits, and the one-row samples, at a 32 times finer interval, show no such difference.

The benchmark row has no dynamic structure. [One run](framework-performance/investigation/template-structure/run-c85916f/run-2.json) of `run.mjs --template-comparison` at commit `c85916f`, all 45 correctness checks passing, overall medians, µs per operation, with the template's ratio to Solid 2:

| Phase, rows | Template | Solid 2 | × Solid 2 |
|---|---|---|---|
| Mount, 1 | 5.9 | 5.1 | 1.14 |
| Mount, 16 | 45.1 | 37.9 | 1.19 |
| Mount, 128 | 484.7 | 416.9 | 1.16 |
| Update, 1 | 1.0 | 1.3 | 0.78 |
| Update, 16 | 14.8 | 14.1 | 1.05 |
| Update, 128 | 155.2 | 156.5 | 0.99 |
| Dispose, 1 | 1.4 | 1.1 | 1.31 |
| Dispose, 16 | 8.8 | 6.5 | 1.35 |
| Dispose, 128 | 55.2 | 39.1 | 1.41 |

The run fell in one of the machine's slow phases: Solid 2's 128-row mount took 416.9 µs against 285.9 µs in the `a0de558` run, and the 128-row update ratio per process was 1.03 / 1.18 / 1.24. [The run before it](framework-performance/investigation/template-structure/run-c85916f/run-1.json) was slower still (128-row mount 1.39× overall, 1.17 / 1.22 / 1.17× per process). The paired `f4273e6` columns above are the comparison with `19c8a8f`.

## Generated catalogue (plan 2c)

Plan 2c ([implementation plan](../superpowers/plans/2026-10-14-component-templates-catalogue.md)) generates the template builder's element, attribute, property and event members from the catalogue that generates `Html.Program.*`. `tools/Ranvier.Dom.Generate` writes `Generated/HtmlTemplate.fs` beside `Generated/Html.fs`. The file declares `[<AutoOpen>] module Ranvier.View.Foundation.HtmlTemplate`, so `open Ranvier.View.Foundation` brings the members into scope. Each member is an optional type extension on a handwritten builder (`TemplateBuilder`, `AttributeBuilder`, `PropertyBuilder`, `EventBuilder`) or on a marker class in the nested `Groups` module, and its body is one call into the handwritten `TemplateParts` module of `Template.fs`. The handwritten members it replaces are removed; `t.Props.value` is now `t.Props.Input.value` and `t.Props.disabled` is `t.Props.Button.disabled`.

The surface, condensed from the [spec clarifications](../superpowers/specs/2026-10-13-component-templates-design.md#generated-catalogue-clarifications):

| Builder | Flat members | Groups |
|---|---|---|
| `t` | one per tag of `HTMLElementTagNameMap`: `t.div parts children`; void tags take parts only | |
| `t.Attrs` | the policy's common attributes, plus `data` and `aria` | `t.Attrs.Button`, `t.Attrs.Input`, `t.Attrs.Select`, `t.Attrs.TextArea`, `t.Attrs.Option` |
| `t.Props` | settable primitive properties declared on `HTMLElement` or its ancestors and mixins | one per other declaring type: `t.Props.Input.value` |
| `t.Events` | events of `HTMLElementEventMap`: `t.Events.onClick` | element-specific events by the tag's native type: `t.Events.Video.onWaitingforkey`; media events appear under `Audio` and `Video` |

Names follow `Html.fs`, with the `authoring.json` renames for attributes (`t.Attrs.className`). A group is named by its native type without `HTML` and `Element` (`TableCell`); attribute groups use the same names, while `Html.Attrs` keeps the policy module names (`Html.Attrs.Textarea`). Flat and grouped attribute and property members are generic in `'native :> <declaring native type>`, grouped events in `'native :> <tag native type>`, and a tag fixes `'native`, so an element-specific member on another element is a compile error. Property members exclude the seven setters that replace child nodes (`t.Props.Anchor.text`, `t.Props.Output.value` and others), which `Html.fs` keeps. Raw-text tags (`t.iframe`, `t.script`, `t.style`, `t.noscript`) mount without children; server markup rejects them. A value gives a static part, an `'inst -> value` reader a dynamic part and a `Source` from `t.from` a source part; string and ARIA boolean attributes add `option` forms that remove the attribute on `None`, and string readers and sources map `null` to `None`. Template markup lists static attributes before dynamic ones, while `Html.Program` keeps authoring order; the two agree up to attribute order.

Counts from `Generated/Html.coverage.json`:

| Key | Value |
|---|---|
| `templateTagCount` | 112 |
| `templateAttributeCount` | 34, plus `data` and `aria` |
| `templatePropertyCount` | 402: 68 flat, 334 in 59 groups (`templatePropertyGroups`); 7 child-replacing setters in `templateOmissions` |
| `templateEventCount` | 132: 108 flat, 24 in 3 groups (`templateEventGroups`: `Audio`, `Body`, `Video`) |

`HtmlTemplate.fs` has 5,230 lines and 399,960 bytes. `Html.fs` is byte-identical to its pre-plan output (587,597 bytes).

### Bundle and emitted JavaScript

The benchmark bundle (`run.mjs --build`: Fable Release with counters off, then `vite build`) holds every adapter, including `ranvier-template`:

| | Raw | Vite gzip | `gzip -6` |
|---|---|---|---|
| Before (`a4d310c`) | 419,342 B | 127.83 kB | 126,358 B |
| After (`f484e89`) | 419,612 B | 127.86 kB | 126,420 B |

The catalogue adds 270 bytes raw and 62 bytes gzipped; the paired builds below, at `0e82e46` and `e8abf81`, reproduce both raw sizes (419.34 and 419.61 kB). Under Fable each extension member is an exported module-level function that takes the builder first, and a call site passes all arguments at once (the curried `t.div parts children` becomes one three-argument call). Vite removes unused members, for example `Groups.TableCellProperties.abbr` and `Groups.TemplateProperties.shadowRootCustomElementRegistry`. An unminified build of the same configuration keeps only the members the benchmark and the bundled test modules use (`div`, `button`, `input`, `className`, `id`, `title`, `onClick`, the `Button` and `Input` property accessors, `ButtonProperties.disabled` and `InputProperties.value`), 8 `TemplateParts` helpers, and the two marker classes those accessors construct, `Groups_ButtonProperties$1` and `Groups_InputProperties$1`.

At `e8abf81`, `curry`, `uncurry` and `partialApply` occur 0 times in the emitted `Generated/HtmlTemplate.js`, `Template.js` and `TemplateDom.js` (Playground `generated-tests/experiments/Ranvier.View.Foundation/`), as before the plan.

### Paired benchmark

The benchmark row uses the same parts as before the plan, now through the generated members. [Paired runs](framework-performance/investigation/template-catalogue/paired-e8abf81/) of `run.mjs --template-comparison` on 2026-10-14 (AMD Ryzen 9 9900X, Chromium 153.0.8010.12, Solid 2.0.0-rc.14) alternate `0e82e46`, the commit before plan 2c's code, and `e8abf81` three times each, every run passing its 45 correctness checks. Median ratio of the template to Solid 2 over the three runs, with their range:

| Phase, rows | `0e82e46` | `e8abf81` |
|---|---|---|
| Mount, 1 | 1.11 (1.11–1.13) | 1.13 (1.09–1.13) |
| Mount, 16 | 1.16 (1.16–1.16) | 1.17 (1.15–1.17) |
| Mount, 128 | 1.16 (1.14–1.16) | 1.15 (1.14–1.22) |
| Update, 1 | 0.80 (0.77–0.80) | 0.79 (0.79–0.79) |
| Update, 16 | 1.04 (1.03–1.05) | 1.04 (1.03–1.04) |
| Update, 128 | 1.06 (1.05–1.08) | 1.07 (1.06–1.08) |
| Dispose, 1 | 1.29 (1.24–1.33) | 1.27 (1.27–1.27) |
| Dispose, 16 | 1.29 (1.13–1.41) | 1.23 (1.18–1.42) |
| Dispose, 128 | 1.34 (1.32–1.35) | 1.36 (1.34–1.48) |

In every cell the two commits' ranges overlap. The medians differ by at most 0.06, at the 16-row disposal (head lower), and by at most 0.02 in every other cell: the head is lower at the 128-row mount, the one-row update and the one-row disposal, and higher at the 1- and 16-row mounts, the 128-row update and the 128-row disposal. Solid 2's 128-row mount took 270 to 284 µs across the six runs. The generated catalogue leaves the benchmark row's cost unchanged within run-to-run variation.
