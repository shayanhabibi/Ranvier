# Keyed list region pass costs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the view's keyed list region cost follow the size of a change for remove and swap, and clear a list in one DOM operation, to reach remove1k and swap1k ≤ 3× and clear1k ≤ 2× Solid 2.

**Architecture:** Every change is inside `ListRegion` and `Row` in `experiments/Ranvier.View.Foundation/Mounting.fs`, on `poc/fable-ranvier-dom`. Task 1 compares each pass's items with the previous pass's by reference to find the unchanged prefix and suffix. It then validates only the rows around the changed window and reuses the prefix and suffix rows in place of a projection lookup per key. Task 2 adds a swap-ends branch to `Row.Layout`, and Task 3 defers row node removal to the end of a pass so an emptied list is cleared in one operation. Task 4 measures.

**Tech Stack:** F# (Fable 5.18), Node DOM tests (`test:dom`), Playwright (`test:browser`), the keyed-list harness on local branch `measure/poc-rerun-2026-10-15`.

**Spec:** [`docs/research/2026-10-15-list-item-identity.md`](../../research/2026-10-15-list-item-identity.md) "Open", and the ranked changes 3–5 of [`docs/research/2026-10-15-list-remove-profile.md`](../../research/2026-10-15-list-remove-profile.md). Change 6 (retained rows' item signal writes) is in the core projection and largely covered by the trimmed pass; it is out of scope here.

## Global Constraints

- Changes are confined to `experiments/Ranvier.View.Foundation` and the Playground tests and benchmarks. No change in `src/Ranvier` (core changes go to master by PR).
- Never rebase `poc/fable-ranvier-dom`. Never push without asking.
- Gates for every task: `test:dom`, `test:browser`, the .NET RepresentationChecks, `dotnet fsi tools/verify-trace.fsx --lint`, and `dotnet fantomas` (pinned 8.0.5) on changed `.fs` files. Core .NET and Fable core suites run once in Task 4 (no core change is expected to move them: .NET 2208 / 2384, Fable 923/941 and 1011/1033, `Report.mjs` exit 0). The gate script is `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\poc-gates.sh`.
- Every existing test passes unchanged. A test that has to change is a plan conflict: stop and report it.
- Mutation check per code task: at least six mutants of the task's new branches, each run against `test:dom`; every mutant is killed, or the report argues why it is equivalent. Record them in the task report.
- Comments follow `.claude/rules/comments.md`; no `FOR-REVIEW` comments committed.
- Commits: conventional, one line, no body, no attribution.
- Benchmarks run only on an idle machine, sequentially.

## Review Focus

1. A caller that mutates its own array in place and writes it back (same array object, different contents) must reconcile exactly as a fresh array would. The region's previous-items snapshot must be its own copy.
2. A pass that fails (duplicate key, row factory throws, layout throws) must leave the region's previous-pass state such that the next pass is correct. The next pass then validates and looks up every row.
3. A foreign node inserted inside the changed window, or between the window and its neighbouring rows, still raises the existing "Keyed-list rows or contents were externally mutated" error before rows change.
4. Clearing a list whose markers share their parent with foreign siblings removes only the region's nodes. Clearing a list of authored rows (`Keyed.*` with nested mounts) keeps their nested mounts' disposal order.
5. Row cleanups still run before that row's nodes leave the document, on clear, on remove and on mount disposal.

---

### Task 1: Validate and look up only the changed window

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs`: `ListRegion` (~1610–1700), `Row.Validate` (~757).
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, beside `stableRemove` and the `recreated*` tests.

**Interfaces:**
- Produces: `Row.ValidateWindow(regionStart: Node, regionEnd: Node, laidOut: ResizeArray<Row>, parent: Node, first: int, last: int)`. It validates the markers' parent as `Row.Validate` does today and validates each laid-out row in `first .. last` (inclusive, clamped to the laid-out range). For each of those rows it checks `cursor = row.First`, starting at `regionStart`'s next sibling when `first = 0` and otherwise at the next sibling of row `first - 1`'s `Last`. If `last` is the final row, it also checks that the cursor after it is `regionEnd`. It also runs `row.Mount.Validate ()` for authored rows in the range. `Row.Validate` stays and is the `first = 0, last = laidOut.Count - 1` case.
- `ListRegion` state added: `previousItems: obj array` (the region's own copy of the last successful pass's items, `[||]` initially) and `previousRows: Row array` (the last successful layout's rows).

Pass algorithm in `reconcile`:

```fsharp
// before the pass, inside the capture window
let items' = items.Read ()
let n0 = previousItems.Length
let n1 = items'.Length
let mutable start = 0
while start < n0 && start < n1 && obj.ReferenceEquals (previousItems[start], items'[start]) do
    start <- start + 1
let mutable end0 = n0 - 1
let mutable end1 = n1 - 1
while end0 >= start && end1 >= start && obj.ReferenceEquals (previousItems[end0], items'[end1]) do
    end0 <- end0 - 1
    end1 <- end1 - 1
// rows start-1 .. end0+1 of the previous layout are validated; the rest are untouched by this pass
Row.ValidateWindow (regionStart, regionEnd, site.LaidOut, parent.Value, start - 1, end0 + 1)
```

After the pass, `next` takes `previousRows[i]` for `i < start` and `previousRows[end0 + 1 + (j - end1 - 1)]` for `j > end1`, provided that row is not `Removed`. Every other position uses `projection.Get keys[j]`. On a successful layout, `previousItems <- Array.copy items'` and `previousRows <- next`. On any failure, or when the region was disposed, both reset to `[||]` so the next pass validates and looks up every row.

Use `LanguagePrimitives.PhysicalEquality`, or `obj.ReferenceEquals`, which Fable compiles to `===`. A boxed value-type item on .NET never compares equal, so it takes the full path. That path is correct, only slower. Keys must be pure (already documented), so an item with the same reference has the same key and the same row.

- [ ] **Step 1: Write the failing tests.**
  - Count validated rows and `projection.Get` calls with a test-only counter under `#if RANVIER_VIEW_COUNTERS`, defined by the `test:dom` build only. If the DOM tests already have a counter hook for the region, use it instead, and say which in the report.
  - Remove the middle item of 200 stable items, and assert that at most 4 rows are validated and at most 1 is looked up.
  - Review Focus 1: mutate the caller's array in place (`arr[3] <- newItem; items.Value <- arr`, with the same array object) and assert that the DOM text order and node identity are the same as for a fresh array.
  - Review Focus 2: a duplicate-key update fails, then a valid update. Assert that the DOM matches the valid items and every kept key keeps its node.
  - Review Focus 3: insert a foreign `<li>` between the row before the window and the first removed row, then remove that row. Assert the existing "externally mutated" error is reported and the rows are unchanged.

  Run `npm run test:dom` in `examples/Fable.Ranvier.Playground`. The counter tests are expected to FAIL; Review Focus 1–3 may already pass.
- [ ] **Step 2: Implement** `Row.ValidateWindow` and the pass algorithm above.
- [ ] **Step 3: Run** `test:dom`, `test:browser` and the RepresentationChecks. Expected: everything passes.
- [ ] **Step 4: Mutation check.** Use at least these mutants: no suffix trim; `start - 1` → `start`; `end0 + 1` → `end0`; skip the reset on failure; reuse a `Removed` row; skip `Array.copy`. Each must be killed.
- [ ] **Step 5: Gates, commit** `perf(view): validate and look up only a keyed pass's changed rows`.

### Task 2: Swap-ends branch in `Row.Layout`

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs`: `Row.Layout` (~805–882).
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, beside `eachSwapMovesTwo`.

**Interfaces:** none new.

After the prefix and suffix loops, before `sources` is built, add this branch:

```fsharp
let count = bEnd - bStart
if count >= 2 && aEnd - aStart = count
   && same previous[aStart] next[bEnd - 1]
   && same previous[aEnd - 1] next[bStart]
   && (let mutable inner = true
       let mutable offset = 1
       while inner && offset < count - 1 do
           inner <- same previous[aStart + offset] next[bStart + offset]
           offset <- offset + 1
       inner) then
    // next[bStart] is the old last row of the window, next[bEnd - 1] the old first.
    let firstAnchor = previous[aStart].First
    Row.Place (next[bStart], parent, firstAnchor)
    Row.Place (next[bEnd - 1], parent, anchor)
else
    // existing sources / increasingRun path
```

`anchor` is the existing `if bEnd < next.Length then next[bEnd].First else regionEnd`. `live ()` is checked before each `Place`, as the existing loops check it.

- [ ] **Step 1: Write the failing test.** Swap rows 1 and 998 of 1,000 and count `insertBefore` calls on the list parent (the existing `eachSwapMovesTwo` shows how it observes moves). Assert exactly 2 moves and no call to `increasingRun`. If observing `increasingRun` needs a hook, assert the move count only, and add a check that two adjacent swapped rows (`k = 2`) and a swap of the first and last rows both give the right order and node identity. Run `test:dom`. The adjacent and end cases may already pass. Record whether the move-count assertion fails before the change. It may already pass, because the longest-run path also moves only two rows; the saving is the `sources`/`increasingRun` allocation. If it does already pass, keep it as a pin and say so in the report.
- [ ] **Step 2: Implement** the branch.
- [ ] **Step 3: Run** `test:dom`, `test:browser` and the RepresentationChecks.
- [ ] **Step 4: Mutation check.** Use at least these mutants: `count >= 2` → `count >= 3`; drop the inner-equality loop; swap the two `Place` anchors; inner loop bound `count - 1` → `count`; `aEnd - aStart = count` dropped; the `live ()` check dropped. Each must be killed or argued equivalent.
- [ ] **Step 5: Gates, commit** `perf(view): move only the two rows of a keyed end swap`.

### Task 3: Clear an emptied list in one operation

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs`: `RegionSite.ReleaseRow` (~1003), `ListRegion.reconcile`, and `Row` (a new `Row.ClearBetween`).
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, `RangeChecks.fs` if marker tests live there.

**Interfaces:**
- Produces: `RegionSite.Deferring: bool` (set by `reconcile` around the projection pass) and `RegionSite.FlushReleased(cleared: bool)`.
- While `Deferring` is set, `ReleaseRow` adds template rows to a pending list instead of calling `releaseNodes`. Authored rows keep today's behaviour.
- `FlushReleased true` removes the region's contents in one operation and marks every pending row `Removed`. `FlushReleased false` calls `releaseNodes` on each pending row in order.
- `Row.ClearBetween(parent: Node, regionStart: Node, regionEnd: Node)`:
  - when `regionStart` is `parent.firstChild` and `regionEnd` is `parent.lastChild`, set `parent.textContent <- ""` and re-append `regionStart` and `regionEnd`;
  - otherwise remove the nodes between the markers with a `Range` (`setStartAfter regionStart`, `setEndBefore regionEnd`, `deleteContents`). If the Node DOM test environment lacks `Range`, use the per-node loop as the fallback for this case and say so in the report.

`reconcile` sets `Deferring` before it reads `projection.Keys` and clears it in a `finally`. It then flushes:
- `cleared = true` only when the pass succeeded, `keys` is empty, the mount root is not disposed, every laid-out row was pending, and every pending row is a template row;
- otherwise `cleared = false`.

The flush also runs on the failure path, so no released row keeps its nodes.

- [ ] **Step 1: Write the failing test.** Mount 1,000 template rows, clear them, and count `remove` calls on row nodes. Wrap `ChildNode.prototype.remove` in the test, or use an existing counter. Assert 0 per-node removals and an empty region between intact markers.
  - Review Focus 4: a list with foreign siblings before and after the markers clears only its own nodes.
  - Review Focus 4: an authored `Keyed.array` list clears correctly, with its nested cleanups in their current order.
  - Review Focus 5: a row cleanup that reads `row.isConnected` sees `true`, on clear, on remove and on mount disposal.
  - A clear inside a pass that also fails (for example, a cleanup that throws) still leaves no row nodes in the document.

  Run `test:dom`. The `remove` count test is expected to FAIL.
- [ ] **Step 2: Implement** `Deferring`, `FlushReleased` and `Row.ClearBetween`.
- [ ] **Step 3: Run** `test:dom`, `test:browser` and the RepresentationChecks.
- [ ] **Step 4: Mutation check.** Use at least these mutants:
  - drop the `every laid-out row pending` condition;
  - drop the template-row condition;
  - never flush on failure;
  - `textContent` path without re-appending the markers;
  - use the `Range` start without `setStartAfter`;
  - clear `Deferring` before the flush.

  Each must be killed.
- [ ] **Step 5: Gates, commit** `perf(view): clear an emptied keyed list in one operation`.

### Task 4: Measure and record

**Files:**
- Modify: the harness on `measure/poc-rerun-2026-10-15` (worktree `C:\Users\shaya\RiderProjects\Ranvier-measure-rerun`). Apply the PoC code from `1f1f1df..HEAD` (excluding `docs/`) as before. Never push.
- Create: `docs/research/2026-10-15-list-region-passes.md`, data under `docs/research/framework-performance/investigation/list-region-passes/`.
- Modify: `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` "Where we stand", the dated status line and "Open".

- [ ] **Step 1:** Run the full gate script once (core .NET and Fable included) at the PoC head.
- [ ] **Step 2:** Run the production-against-Solid 2 page with the rerun protocol (5 processes × (3 warmups + 21 shuffled rounds)), the loop protocol check and the loop-protocol CPU profile with the same buckets as the item identity note.
- [ ] **Step 3:** Write the note.
  - Use tables in the item identity note's shape, with the `1f1f1df` ratios beside the new ones, and the per-task contribution where the profile separates it.
  - State for each target whether it is met: swap1k ≤ 3×, remove1k ≤ 3×, clear1k ≤ 2×.
  - Note the Solid 2 fast-mode spread for swap1k as before.
- [ ] **Step 4:** Update the framework plan's "Where we stand", the dated status line and "Open".
- [ ] **Step 5: Commit** `docs(research): measure keyed list region passes`.
