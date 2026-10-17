# Keyed rows and arms on core primitives

Date: 2026-10-14. Status: approved; decisions 1–3 confirmed by the user on 2026-10-14; amended with the plan's rulings ([plan](../plans/2026-10-14-keyed-rows-on-core.md)). Implemented on `poc/fable-ranvier-dom`; measurements in [keyed rows on core](../../research/2026-10-14-keyed-rows-on-core.md).

Baseline: `31e4a4b` on `poc/fable-ranvier-dom` (PR #42 head `be4e4cc` plus the view rule). Evidence: [keyed-row cost](../../research/2026-10-14-keyed-row-cost.md) on branch `research/keyed-row-cost` (`aa5b811`), whose prototypes 6 and 7 this design turns into production code. Rule: [`.claude/rules/view-uses-core.md`](../../../.claude/rules/view-uses-core.md).

## Core and view state

**Core primitives used:**

| Primitive | Job in this design |
|---|---|
| `Api.createProjectionOnce` (new, see [Core changes](#core-changes)) | A list region's rows: key identity, duplicate detection, one scope per key, creation, item updates and disposal. The factory's result is the row's nodes. |
| `ProjectionOptions.CreateFirst` (new) | The list order agreed in plan 2b: a pass creates its added rows before it retires removed ones, and a failing factory leaves the previous rows in place. |
| `Projection.Keys`, `Projection.Get` | The row order layout follows, and each row's nodes. |
| `Api.createMemo` | An arm region's items, read once per change by both the selection and the payload write. |
| `Api.createMemoWithComparer` | Arm selection: an arm stays mounted while the comparer reports the same arm. The comparer is internal to the region. |
| `Api.createRoot`, `Owner` | A mount's lifetime, each arm's lifetime, and the error list of a teardown. Replaces the `Scope` tree. |
| `Owner.OnCleanup`, reverse-order teardown | Cleanup order; node release registered first, so it runs after the owner's own cleanups. |
| `Owner.IsDisposed` | `Disposed` phase of a mount; the test for "an enclosing owner is tearing down" (`disposed` is set before children are torn down, `Core.fs:898`). |
| `Owner.ErrorCount`, `Owner.ErrorsFrom` (new) | The errors a teardown recorded, read as a slice after it. |
| `Projection.ensureUniqueKeys` (new) | Server markup's duplicate check, with the core's exception and message. |
| `Api.createEffect`, `Api.batch`, `Api.untrack`, `Api.runWithOwner` | The region's reconcile effect; arm disposal in a batch; untracked layout and setup. |
| `createMemoWithComparer` and the other core comparer overloads | Unchanged. Only the view's comparers are removed. |

**View-owned state:**

| State | Why the core cannot hold it |
|---|---|
| Region markers (`<!--[-->`, `<!--]-->`), and each row's first and last node | DOM nodes. |
| The laid-out row array of a list region | The previous DOM order, the input to the minimal-move layout. The core holds key order, not node order. |
| A mount's `Attached` flag | Attachment is a DOM event. `SettingUp` is "not attached, not disposed"; `Disposed` is read from the owner. |
| A mount's pending-release queue | Node release deferred to the outermost tearing-down mount. Node release is DOM work; see [Lifetime](#lifetime-scope-replaced-by-owner). |
| The stack of open teardown windows (owner, start counts, slices taken) | Groups core-recorded errors per teardown for the mount's reporter. The core records errors without grouping; see [Teardown reporting](#teardown-reporting). |
| The mount's last reported failures (`Mounted.Failures`) | A copy of what the reporter received, kept for the existing public property. |
| Delegated event names per host, as a set | Event delegation. Replaces the `ResizeArray` scanned by `Contains`. |
| Template tables, setters, native claims, server HTML, last-written DOM values | Unchanged from the template design. |

Removed from the view: the `Scope` tree of `Lifetime.fs` (its child list, callback list and per-scope phase), `Mounting.fs` `KeyedRegion`, `KeyedEntry`, `KeyedDisposed`, `KeyedSetupFailed`, `Keyed.validateUniqueKeys`, and every view-side key comparer.

## Decisions

Settled:

- **The view builds on the core** (rule of 2026-10-14). Lists use a core projection; arms use a core memo and a root owner per arm; mounts use core owners.
- **Create before retire for lists** (plan 2b). New rows are set up before removed rows' cleanups run, and a failed update leaves the existing rows and their DOM identities in place. Arm switches dispose first.
- **Custom comparers leave the view only.** `Keyed.list`, `Keyed.array` and `Branch.select` lose their equality parameter, as do `Backend.Keyed`, `Backend.Switch`, `Operations.Keyed`, `Operations.Switch`, `MountServices.Region` and the template `Region.Same`. Keys are compared by the core: `'K: equality`, hashed through `Platform.KeyMap`. A caller that needs another identity maps its item to a key with that identity. The core keeps `createMemoWithComparer` and every other comparer overload.
- **Node-release ordering and teardown reporting live in the view**, on top of core owners and core error lists.
- **Positional edits are not used.** `Positional.diff` is unchanged; layout is a view-side minimal-move step over row objects.
- **An uncurried factory overload is not part of this design.** It is added only if a profile of the finished design shows the `curry2` per key.

Confirmed by the user on 2026-10-14:

1. **`createProjectionOnce` as the row form.** Measured on: 300 bytes less retained heap per row (1,516 → 1,216 at 1,000 rows), `create1k` 0.891× of the memo form in the view, 24–30% of a create and 20% of a clear in the core alone. Measured off: identical heap and counters, timings within 0.8%, +850 B minified.
2. **Duplicate keys raise `InvalidOperationException`**, the core's exception and message, in place of `ArgumentException`. The test asserting `ArgumentException` changes.
3. **A layout failure disposes the region's mount** and reports. Today a failed move is rolled back to the previous order and created rows are disposed. With rows on the core, the pass has committed before layout runs, so there is no previous membership to restore. Layout raises only when the DOM refuses an insert.

## Core changes

All in `src/Ranvier`. Each gets .NET and Fable tests in the core suites, and the measurement in [Measurement](#measurement).

### `ProjectionOptions.CreateFirst`

```fsharp
[<System.Flags>]
type ProjectionOptions =
    | None = 0
    | CreateFirst = 1

val createProjectionWithOptions:
    options: ProjectionOptions -> keyOf: ('T -> 'K) -> factory: ((unit -> 'T) -> (unit -> 'V)) -> source: (unit -> 'T seq) -> Projection<'K, 'V>
```

`createProjectionWith` is `createProjectionWithOptions ProjectionOptions.None`, and its behaviour is unchanged. With `CreateFirst`, a pass:

1. Enumerates `source` and stages adds and item writes. A duplicate key fails the pass here, before any row changes (as today).
2. Creates each added row, in source order.
3. If a factory raises: disposes the rows created by this pass in reverse creation order, leaves entries, items and keys as they were, and fails the pass with the factory's exception. The projection stays `Dirty`, and the next change to a source retries it.
4. Retires removed keys. A removed row's cleanup that disposes the projection stops the pass here; the rows created in step 2 are disposed with the projection.
5. Writes survivors' items and publishes the key set.

Without `CreateFirst`, a factory failure is stored on its row and raised on read (today's behaviour). With it, the failure is the pass's. `ApplyDelta` is unchanged: it serves only the internal delta-driven projections of `Collections.fs` and `Combinators.fs`, which never receive options (ruling in the plan).

When off, the cost is one test of the option per pass in `ApplyDiff`, never per row.

### `createProjectionOnce`

```fsharp
val createProjectionOnce:
    options: ProjectionOptions -> keyOf: ('T -> 'K) -> factory: ((unit -> 'T) -> 'V) -> source: (unit -> 'T seq) -> Projection<'K, 'V>
```

The prototype at `98bab5c`, made production code:

- The factory runs once per key, untracked, inside the key's scope, and receives the key's item reader. Its result is the row's value. The row has no memo, owner link, compute closure, source list or observer list.
- `Get` and `TryGet` return the stored value. A tracked read of a row tracks the key set, so the reader wakes when the key leaves.
- Without `CreateFirst`, a factory failure is stored and raised on read. With it, the pass fails as above.
- Value readers (`NewValueReader`), `Projection.filter`, `sortBy`, `map` and the .NET `ObservableCollection` views raise `NotSupportedException` on such a projection, with a message naming `createProjectionWith`. The view uses none of them.

### `Owner.ErrorCount` and `Owner.ErrorsFrom`

```fsharp
member ErrorCount: int
member ErrorsFrom: index: int -> exn[]
```

`ErrorCount` is the number of errors the scope holds; `ErrorsFrom index` returns those recorded at or after `index`, in order. Both read the list `Errors` already exposes, in constant time for the count. A teardown's errors are `ErrorsFrom before` read after it, where `before` is `ErrorCount` read before it.

### `Projection.ensureUniqueKeys`

```fsharp
val ensureUniqueKeys: keyOf: ('T -> 'K) -> items: 'T seq -> unit
```

Raises the exception a projection pass raises for the first duplicate key, with the same message, hashing keys through `Platform.KeyMap`. Server markup calls it in place of `Keyed.validateUniqueKeys`.

## View design

### List regions (`t.each`, `Keyed.list`, `Keyed.array`)

A list region owns two markers and creates, under its owner:

```fsharp
let version = Api.createSignal 0

let rows =
    Api.createProjectionOnce ProjectionOptions.CreateFirst fst buildRow (fun () ->
        version.Value |> ignore
        snapshot :> seq<_>)
```

`snapshot` holds the items of the region's latest reconcile, and `version` is the region's own signal. The projection reads only these two.

`buildRow (item: unit -> obj * obj) : Row` runs in the key's scope:

1. Registers the row's node release first, so it runs after every cleanup the row registers.
2. Materialises the row: a template clone bound to the item reader for `t.each`, or a nested mount for an authored `Program` row (see [Authored views](#authored-views)).
3. Returns `Row (first, last)`. A row with no nodes gets one `ranvier:row` comment.

The row's release removes its nodes from `first` to `last`, and marks the row removed. A template row that is retired while its mount tears down hands its removal to the mount's node release. So its nodes leave after every cleanup of the disposed tree, in the order given in [Lifetime](#lifetime-scope-replaced-by-owner).

The region's reconcile effect:

1. Returns without work if the region's owner is disposed, as `KeyedDisposed` does today.
2. Validates the markers: both share a parent. Validates the laid-out rows' first and last nodes in order between them. A failure raises "externally mutated" before the pass runs, as today.
3. Inside the teardown window, reads `read ()` once, tracked, into `snapshot`, and bumps `version` untracked. The effect's only tracked source is the items. Then it reads `rows.Keys` untracked, and that read runs the pass inside the window. Errors from the pass's row setup, rollback and retirement are therefore reported with the update. Duplicate keys and factory failures raise here, with the previous rows and DOM in place. A tracked `Keys` read would run the pass before the effect body, when the core resolves the effect's `Check` through the projection beacon, which is outside the window. Key callbacks see one snapshot per update. Cost: one signal per region, and a reconcile on every items change, including changes to item values alone.
4. Lays out, untracked: `Get` for each key gives the target row array; the laid-out array is the previous one with removed rows skipped. The step matches the common prefix and suffix, then moves only the rows outside one longest run of rows already in order, each as a unit (`first` to `last`). When every row between the prefix and suffix is new and the rows are the region's staging content, one insertion places them all. A two-row swap moves two rows.
5. Stores the target array as the laid-out array. A layout failure after the region's own disposal (for example a disposal run by a DOM insertion) ends the update silently; any other layout failure disposes the mount and is reported (decision 3).

The pass, its row creation and its retirement run inside the core's batch. Errors recorded while it runs are reported once, after layout (see [Teardown reporting](#teardown-reporting)).

Every row removes its own nodes, in nested and top-level regions alike. The element that holds a nested region's markers can be moved out of the mount and adopted by another mount (`BuildContext.Native`), and then it outlives the mount that created the rows. A range deletion between the markers would also delete foreign nodes inserted inside rows (ruling in the plan).

Rows register delegated events at the containing mount's host, once per event name per host. Direct listeners register on the row's node and are removed by the row's owner.

### Arm regions (`t.matchWith`, `t.branch`, `Branch.select`, authored `Switch`)

Prototype 6, made production code:

- `items = Api.createMemo (fun _ -> read ())`.
- `selection = Api.createMemoWithComparer sameArm (fun _ -> first item of items)`. `sameArm` is internal and compares the items' keys with `=`: a `matchWith` key is the arm's definition, compared by reference; a `branch` key is the user key with the definition.
- A reconcile effect reads `selection`. On a change it disposes the previous arm's root owner in a batch, which runs the arm's cleanups and then its node release, and then creates the next arm's root owner under the region owner and mounts the arm into it. Dispose first.
- A payload effect reads `items` and writes the retained arm's item signal. The write cuts off when the payload is unchanged.
- An arm whose setup raises leaves the region empty and is reported; a later switch mounts.

### View holes (`t.view`)

A view hole reads its view once, untracked, and mounts it between its markers under a root owner, released with the instance. It has no projection, memo or effect. Today it goes through the keyed path with a comparer that always answers true.

### Authored views

`Program` and `Tree` views build DOM through `MountState`. An authored list row or arm stays a nested `MountState`, created on the row's key scope or the arm's root owner instead of a `Scope`. Its cost is unchanged; template rows are the fast path.

### Lifetime: `Scope` replaced by `Owner`

`Lifetime.fs` keeps `ScopePhase` and holds `MountLifetime`, a wrapper of one core owner with a parent pointer, an attached flag, a release queue and held errors; it has no child or cleanup list. A `MountState` is given an `Owner`: `createRoot` for a top-level mount, the key scope for a row, the arm's root for an arm. `Mounting.mount` and `mountWith` take `parentOwner: Owner option` in place of `parentScope: Scope option`.

- **Phase.** `Mounted.Phase` is `Disposed` when the owner is disposed, `Attached` after the mount's attach step, and `SettingUp` before it. Rows and arms read their containing mount's phase.
- **Release order.** A mount registers its node release as the first cleanup of its owner. When the release runs while an enclosing mount is tearing down, it is appended to the outermost tearing-down mount's pending-release queue instead of running. The outermost mount runs its queue in order, then its own release, after its owner's teardown finishes. So every cleanup in a disposed tree runs before any of its nodes is released: `child-clean, parent-clean, remove-child, remove-parent`. A release that runs while a descendant's `Dispose` is in progress waits for that call to finish. Mount-level cleanups run in core owner order, last registered first.
- **Late registration** and idempotent disposal are the core's.

### Teardown reporting

A mount's reporter is called once per teardown with that teardown's errors. User cleanups register directly on core owners; the view does not wrap them.

- Before a teardown the view reads `ErrorCount` of the mount's root owner and of `Graph.Root`; after it, `ErrorsFrom` on both gives the teardown's errors. Key scopes and computation scopes record into `Graph.Root`; root owners record into themselves.
- The same window wraps a list pass (removed rows' cleanups) and an arm switch.
- A teardown started inside another teardown's window reports its own slice and marks it taken, so the outer window excludes it. This keeps `a cleanup disposing a scope in another tree reports on that tree and keeps its own order`.
- A reporter that raises is caught; its exception joins `Mounted.Failures` and the remaining releases still run.

### Server markup

`Html.render`'s `Keyed` and `renderHtml`'s region branch call `Projection.ensureUniqueKeys fst items` and render each row once. A view hole renders its view; an arm renders the selected arm.

### Public surface changes

| Before | After |
|---|---|
| `Keyed.list ops equals key read render` | `Keyed.list ops key read render` |
| `Keyed.array ops equals key read render` | `Keyed.array ops key read render` |
| `Branch.select ops equals key read render` | `Branch.select ops key read render` |
| `Operations.Keyed`, `Operations.Switch`: `(obj -> obj -> bool) -> …` | the same without the comparer |
| `Backend.Keyed`, `Backend.Switch`: `(obj -> obj -> bool) * …` | the same without the comparer |
| `Mounting.mount graph (parentScope: Scope option) …` | `Mounting.mount graph (parentOwner: Owner option) …` |
| duplicate keys: `ArgumentException` | `InvalidOperationException` (decision 2) |
| `Keyed.array` "copies the input array's slots before key callbacks run" | unchanged: the slots are still copied before key callbacks run, as `array key callbacks cannot change unread snapshot slots` checks |

Every in-repository caller passes `(=)` and changes mechanically.

## Tests

Kept unchanged: the 314 DOM tests and 22 browser tests, except those below.

Changed:

| Test | Change |
|---|---|
| `duplicate each keys fail the mount, are reported on update and keep the rows` | asserts `InvalidOperationException` |
| `keyed comparer disposal prevents further reconciliation`, `initial keyed comparer disposal stops before further callbacks` | removed: no comparer exists |
| `initial keyed reader disposal stops before comparer calls`, `keyed reconciliation tracks snapshots but not comparers or cleanup` | keep their reader halves |
| `array comparers cannot change renderer snapshot slots` | removed: it exercised the comparer only; `array key callbacks cannot change unread snapshot slots` keeps the snapshot check |
| `failed keyed movement restores prior order and permits recovery`, `failed keyed rollback terminates the mount and all row resources` | replaced by `a failed keyed layout disposes the mount and reports once` (decision 3); `failed movement rollback disposes resources instead of stranding bindings` moves a whole mount and is unchanged |
| Scope unit checks in `LifetimeChecks` | rewritten against mounts on owners: order, late registration, reporter failure |
| Use-order flips from mount-level cleanups moving to core owner order | none: no view test asserts the order of two or more mount-level (`ctx.OnCleanup`, `root.OnCleanup`) or `Browser.fs` component cleanups. `range cleanup runs child first while DOM stays attached`, `template cleanups run in use order without an instance owner`, `each renders template rows, retains them by key and dispatches their clicks`, `an inlined child shares the clone and keeps its own instance, effect and handlers` and the `components clean up without stopping a sibling mount` cases already observe core owner order and keep their expected order |

New, in the owning tasks:

1. Core, both platforms: `CreateFirst` runs added rows' factories before removed rows' cleanups; a failing factory disposes this pass's created rows in reverse order and leaves keys, rows and items unchanged; the next source change retries; a removed row's cleanup disposing the projection stops the pass.
2. Core: `createProjectionOnce` rows read without recomputation; a tracked `Get` wakes when the key leaves; the unsupported readers raise `NotSupportedException`.
3. Core: `ErrorsFrom` returns exactly the errors recorded after an index; `ensureUniqueKeys` raises the pass's exception and message for `int`, `string`, record and tuple keys, and treats `1` and `1L` as `=` does.
4. View: a two-row swap of 1,000 rows performs two `insertBefore`; prepend, reverse, and remove-from-middle produce the expected DOM and keep node identities (property test against a reference order).
5. View: zero-node rows keep their position across moves and leave with their comment.
6. View: disposing a mount runs every row cleanup before a row's node leaves.
7. View: delegated clicks on a row reach its own instance after create, after a move and after the containing mount moves hosts, and stop after removal.
8. View: a teardown inside another teardown's window is reported once, on its own tree.
9. Browser (Chromium): focus and selection inside a row survive a move of another row.
10. Emitted JavaScript of `Mounting.fs`, `TemplateDom.fs` and `Template.fs` adds no `curry`, `uncurry` or `partialApply` beyond the counts at the baseline.

## Measurement

Each core option is measured off and on, as `createProjectionOnce` was in the research: op counters (`RANVIER_COUNTERS`), retained heap by constructor (CDP heap-snapshot diff), paired shuffled timings in one page (A: option compiled out, B: compiled in and off, C: on), the pure-core workload under Node and .NET, and the minified bundle. Raw JSON is committed with machine, Chromium version, date and commit. Bundle figures are measurements, not gates.

Targets on `keyed-rows.mjs` against Solid 2 (2.0.0-rc.14, native compiler), shuffled rounds: `create1k`, `append1k` and `clear1k` at or below 2.0×; `swap1k` and `remove1k` at or below 3×; `clear10k` / `clear1k` within 12×; retained heap at or below 1,300 bytes per row at 1,000 rows; `armsSwitch1k` at or below 2×. These are targets for the measurement report, not acceptance gates.

## Non-goals

- Hydration, structural boundaries, setter-failure handling, the playground migration.
- Indexed edges and constant-time unlink (#54, deferred).
- Moving the queued commit `Queue` onto core effects: investigated after this design.
- Bulk clear, on an update that empties a live list or on disposal of a top-level region.
- Owner-less static rows.

## Risks

- **Row interiors are not validated.** External mutation between a row's first and last node is undetected; moved first or last nodes are detected.
- **Cleanup order inside a row** is core owner order: reverse registration, interleaved with child owners. Nested-mount rows ran scope callbacks first.
- **Nested lists inside a row retired by a normal update** detach their rows' nodes before the outer row's earlier-registered cleanups run.
- **`Graph.Root` windows include any error recorded during the window**, including from a cleanup of an unrelated tree that the window's teardown triggers. Nested windows exclude their own slices; other sources are reported with the teardown that ran them.
- **A teardown driven by the core rather than by `Dispose`** (for example `Graph.Dispose`) reports the mount's owner errors, adopted owners' errors and release failures; errors that computation scopes record into `Graph.Root` meanwhile are not grouped into it.
- **`Graph.Root.Errors` grows without bound**, as it does today; the view reads slices and never clears it.
- **`createProjectionOnce` code is present in every bundle that uses `createProjectionWith`** (+850 B minified), because `CreateAdded` references it.
- **.NET `create1k` of the pure-core workload was 1–2% slower with the option compiled in**, unattributed. The measurement above repeats it with `ItemRow` kept sealed.
- **The cast to the unsealed `ItemRow` is unmeasured in isolation.** `ItemRow` stays unsealed so that `OnceRow` inherits it. .NET `create10k` B / A of the pure-core workload was 1.183 (interquartile range [1.050, 1.233]), unexplained in the measurement write-up.

## Follow-ups

- Done: the parts of PR #42 and its research notes that describe "every region runs through the keyed path" are marked as replaced by this design.
- Done: `delegatedNames` is a set (it was 12% of `create1k` allocation), folded into the list task.
- Open: investigate the queued commit `Queue` as binding effects under a microtask `IGraphDispatcher`.
- Open: `swap1k` (4.556×) and `remove1k` (5.000×) miss the ≤ 3× targets, and `remove10k` is 26–32% slower with memo-free rows, unexplained.
- Open: a core hook around a tracked read's staleness check would let list regions read `projection.Keys` tracked, replacing the region-local version signal.
