# Keyed list item identity implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the trimmed keyed pass from #55 effective for view lists, and make a pass it cannot trim cost no more than the full visit.

**Architecture:** Two changes on two branches. The core change (Task 1) lands on master as its own PR and reaches `poc/fable-ranvier-dom` by a master merge. The view change (Tasks 2–3) lands on `poc/fable-ranvier-dom`. Task 4 measures both at the PoC head.

**Tech Stack:** F# (.NET 10, Fable 5.18), Expecto, Node DOM tests, Playwright, the keyed-list harness on local branch `measure/poc-rerun-2026-10-15`.

**Spec:** [`docs/research/2026-10-15-list-remove-profile.md`](../../research/2026-10-15-list-remove-profile.md), ranked changes 1 and 2. The trimmed pass is specified in [`2026-10-14-core-trimmed-pass-design.md`](../specs/2026-10-14-core-trimmed-pass-design.md).

## Global Constraints

- The core change goes to master on a branch from master; never rebase `poc/fable-ranvier-dom`, merge master into it.
- No public API change in `src/Ranvier`. The view contract change (Task 2) is internal to `Ranvier.View.Foundation` and its backends.
- Untraced core build compiles to the same members as before except inside `KeyedProjection.Enumerate`/`Trim`.
- Gates (all must pass on the branch a task lands on): core .NET untraced and traced; C# tests; Fable core inline failures unchanged at 18 untraced / 22 traced with `Report.mjs` exit 0; for the PoC also `test:dom`, `test:browser`, the .NET RepresentationChecks; `dotnet fsi tools/verify-trace.fsx --lint`; `dotnet fantomas` (pinned 8.0.5) on changed `.fs`/`.fsx` files.
- Comments follow `.claude/rules/comments.md`; no `FOR-REVIEW` comments committed.
- Commits: conventional, one line, no body, no attribution.
- Benchmarks run only on an idle machine, sequentially.

## Review Focus

1. A list whose items are recreated on every read (fresh records/tuples under Fable's reference equality) must produce the same keys, rows and DOM as before, for insert, remove, swap, reverse and clear.
2. A list that keeps item references must still trim: `keyOf` is called only for changed positions (the existing trimmed-pass tests pin this; they must stay green unchanged).
3. Duplicate keys introduced by a pass that falls back to the full visit must still fail before rows change.
4. A row's props read the current item after an equal-key update with a new item value (the item signal write must still happen on fallback and on trim).
5. Switch/branch arms keep their behaviour: they keep the `(key, item)` contract and are not part of Task 2.

---

### Task 1: An untrimmable pass takes the full visit (core, master)

**Files:**
- Modify: `src/Ranvier/Projections.fs` (`KeyedProjection.Trim`, `KeyedProjection.Enumerate`, around lines 2203–2320)
- Test: the trimmed-pass test list in `tests/Ranvier.Tests` (grep `trim` / `keyOf` call counts added by #55)
- Bench: `bench/Ranvier.CoreListBench/Fixture.fs`, `run.mjs`

**Interfaces:** none new. Behaviour: when the trim would treat every position as changed, the pass runs the existing full-visit loop (`this.Visit (this.KeyOf item, item)` over `passItems`) instead of `Trim`'s per-position bookkeeping.

The criterion is decided before any side effect of `Trim`:
- Unequal lengths: nothing trimmed at either end (`start = 0` and `end0 = n0 - 1`).
- Equal lengths: every position differs under `same` (count the differing positions in the existing compare loop before touching `previousChanged`).

- [ ] **Step 1: Write the failing tests.** In the trimmed-pass test list, add a projection over a reference-equality item type recreated on every write (e.g. a class `Box(id: int, v: int)` without equality; on .NET make sure the graph's equality policy treats two instances as unequal). For each of insert-at-0, remove-at-middle, swap-1-and-n-2, reverse and clear on 1,000 items, assert `Keys`, row identity per key (rows kept for kept keys) and that each kept row's reader sees the new item. Add one test that a fresh-items pass with a duplicate key raises the duplicate-key error and leaves `Keys` unchanged. These pin behaviour; they may already pass.
- [ ] **Step 2: Add a cost probe.** Add an internal counter (traced build only, like the existing trace hooks; or a test-only counter if the project already has one for `KeyOf` calls) of hash-set operations, or measure in the bench instead: add a `fresh` mode to `bench/Ranvier.CoreListBench/Fixture.fs` whose operations rebuild every item object (`Array.map (fun it -> { it with Id = it.Id })` with a reference-equality item type) and run `remove1k`, `swap1k`, `remove10k` under Node with `run.mjs`. Record the before numbers.
- [ ] **Step 3: Implement the fallback** in `Enumerate`/`Trim` per the criterion above. The fallback leaves `trimValid` handling as it is (`Applied` re-arms it).
- [ ] **Step 4: Run the tests and the bench.** Expected: all tests green; fresh-mode `remove1k`/`swap1k` at or below the full-visit cost (the profile measured the trim-off build at 0.570 ms against 0.765 ms for `remove1k` in the DOM benchmark); stable-item modes unchanged within noise.
- [ ] **Step 5: Gates, commit** `perf(core): take the full visit when a keyed pass cannot trim`, push the branch and open a PR against master. Ask before merging.
- [ ] **Step 6:** After merge, merge master into `poc/fable-ranvier-dom` (`chore: merge master into poc/fable-ranvier-dom`) and run the PoC gates.

### Task 2: Keyed regions project the caller's items (view, PoC)

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Representation.fs` (`Backend.Keyed`, `Operations.Keyed`, `View.Keyed`, `Keyed.create`, the expansion at ~line 436)
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (`Region`, `TemplateBuilder.each`, the expansion at ~lines 332 and 399)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (region record at ~60, `MountServices.Region` at ~380, the region loop at ~651)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`RegionOf`, `NestedRegion`, `ListRegion`)
- Modify: `experiments/Ranvier.View.Foundation/Browser.fs`, `Prepared.fs` (their `Keyed` members)
- Test: `examples/Fable.Ranvier.Playground/tests` DOM tests for keyed lists and templates

**Interfaces:**
- New internal record in `Representation.fs`:
  ```fsharp
  /// <summary>A keyed list's items, as the caller's array, with the key of each item.</summary>
  [<NoEquality; NoComparison>]
  type KeyedItems = { Read: unit -> obj array; Key: Func<obj, obj> }
  ```
- `Backend.Keyed: KeyedItems * ((unit -> obj) -> Backend -> unit) -> unit`; `Operations.Keyed: KeyedItems -> ((unit -> obj) -> 'view) -> 'view`; `View.Keyed of KeyedItems * ((unit -> obj) -> View)`.
- Template `Region` becomes `{ Items: Func<obj, obj array>; Key: Func<obj, obj>; Row: RegionRow }`. `each` sets `Items` to the caller's array boxed (`items (unbox instance) |> unbox<obj array>` where the array is already an `obj`-compatible reference array under Fable; on .NET use `Array.map box`, which still keeps each item's reference) and `Key` to `fun item -> box (key (unbox item))`. Arm and view regions keep their one-element `(key, value)` tuple, boxed into the `obj array`, with `Key = fun t -> fst (unbox<obj * obj> t)`; `ArmRegion` unboxes the tuple where it reads it today. `each` and `Keyed.create` return the caller's item objects, so an unchanged item keeps its reference across reads.
- `ListRegion` read: `unit -> obj array` plus `key: Func<obj, obj>`; projection: `Api.createProjectionOnce ProjectionOptions.CreateFirst key.Invoke buildRow (...)`; `buildRow (item: unit -> obj)` with `current = item`.
- `ArmRegion` and `Switch` keep `(obj * obj) array`.

- [ ] **Step 1: Write the failing test.** A DOM test (Node, `test:dom`) that mounts a template `each` over an array of records, removes the middle item by `Array.removeAt`, and asserts that the row factory ran zero times and the key function ran at most a small constant number of times (count calls in the test's key function; before the change it runs once per item because the trim never matches). Same for authored `Keyed.array`. Run: `npm run test:dom` in `examples/Fable.Ranvier.Playground`; expected FAIL on the key-call count.
- [ ] **Step 2: Change the contract** in `Representation.fs` and both backends (`Browser.fs`, `Prepared.fs`) and the expansion; build with `dotnet build` to find every site.
- [ ] **Step 3: Change the template path** (`Template.fs`, `TemplateDom.fs`) and `Mounting.fs` `ListRegion`/`RegionOf`/`NestedRegion`.
- [ ] **Step 4: Run** `test:dom`, `test:browser`, the RepresentationChecks and the .NET view tests. Expected: the new test passes; every existing test passes unchanged.
- [ ] **Step 5: Gates, commit** `perf(view): project keyed regions over the caller's items` on `poc/fable-ranvier-dom`. Do not push without asking.

### Task 3: Duplicate and stale-item checks for the new contract (view, PoC)

**Files:**
- Test: `examples/Fable.Ranvier.Playground/tests` (keyed list and template DOM tests)

- [ ] **Step 1:** Tests for Review Focus 1, 3 and 4 at the view level: a list whose items are recreated on every read (`Array.map (fun r -> { r with Label = r.Label })`) for insert, remove, swap, reverse, clear, asserting DOM text order and node identity per key; a duplicate key introduced by an update fails before the DOM changes; an equal-key item replaced by a new value updates the row's text.
- [ ] **Step 2:** Run `test:dom` and `test:browser`. Expected: PASS (they pin behaviour of Task 2; fix Task 2 if any fails).
- [ ] **Step 3: Commit** `test(view): pin keyed rows over recreated and duplicate items`.

### Task 4: Measure and record (PoC)

**Files:**
- Modify: harness on `measure/poc-rerun-2026-10-15` (worktree `C:\Users\shaya\RiderProjects\Ranvier-measure-rerun`) — merge or cherry-pick the PoC head into it so it measures Tasks 1–3.
- Create: `docs/research/2026-10-15-list-item-identity.md`, data under `docs/research/framework-performance/investigation/list-item-identity/`
- Modify: `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` "Where we stand" and "Open"

- [ ] **Step 1:** Run the production-vs-Solid 2 page with the protocol of the rerun note (5 processes × (3 warmups + 21 shuffled rounds)) and the steady-state loop protocol from the profile note, at the new PoC head.
- [ ] **Step 2:** Write the note: tables in the rerun note's shape with the `85e0f8c` ratios beside the new ones; state whether `swap1k`/`remove1k` meet ≤ 3× and whether `clear1k` meets ≤ 2×.
- [ ] **Step 3:** Update "Where we stand" and reorder "Open": the profile's changes 3–6 (per-pass `Row.Validate` and row-array rebuild, small moves in `Row.Layout`, single-call clear, retained-row signal writes) become the next list items if the targets are still unmet.
- [ ] **Step 4: Commit** `docs(research): measure keyed lists after the item identity change`.
