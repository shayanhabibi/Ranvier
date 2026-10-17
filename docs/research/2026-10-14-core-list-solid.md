# Keyed lists on the reactive core against Solid 2's `mapArray`

Date: 2026-10-14. Commit `5c247cd`, with `bench/Ranvier.CoreListBench/` added uncommitted (the files of this note's commit). AMD Ryzen 9 9900X, Node 26.7.0, Fable 5.18.0, `@solidjs/signals` 2.0.0-rc.14 (prod build).

## Method

[`core-list-solid.json`](framework-performance/investigation/core-list-solid/core-list-solid.json), `node bench/Ranvier.CoreListBench/run.mjs --build`. There is no DOM: the workload is the reactive core's keyed list alone.

- **Ranvier:** the production row form, `createProjectionOnce ProjectionOptions.CreateFirst` keyed by id (`Fixture.fs`, Fable Release build). Writes run inside `Graph.Run`.
- **Solid 2:** `mapArray` keyed by item identity, as `<For>` uses it. Each write is followed by `flush()`.
- **Both:** items are records with a label signal. Each row creates one effect writing the label to the row, as a text binding does. One effect reads the list's rows: `Keys` and `Get` per key, untracked, for Ranvier; the mapped array for Solid 2.

Seven Node processes, each with 10 warmup and 31 timed rounds per operation. A round times the operation once per framework in a seeded random order, each on a fresh list after a forced GC. Before timing, each operation's resulting rows (order and text) are checked equal across the two frameworks.

## Results

Median ms, then the median same-round ratio Ranvier / Solid 2 with its interquartile range, and the per-process medians:

| Operation | Solid 2 (ms) | Ranvier (ms) | Ratio | Per process |
|---|---|---|---|---|
| create1k | 1.041 | 2.857 | 2.78 [2.36, 3.28] | 2.78, 2.71, 2.66, 2.86, 3.24, 2.45, 2.88 |
| create10k | 4.564 | 17.841 | 3.50 [2.50, 4.81] | 3.53, 3.42, 3.12, 4.00, 4.89, 3.53, 3.43 |
| replace1k | 1.376 | 3.016 | 2.23 [1.92, 2.57] | 2.10, 1.98, 2.37, 2.31, 2.33, 2.57, 2.20 |
| append1k | 1.211 | 2.513 | 2.12 [1.76, 2.51] | 1.98, 1.90, 2.14, 2.15, 2.35, 2.06, 2.23 |
| update10th1k | 0.153 | 0.105 | 0.70 [0.57, 0.93] | 0.66, 0.72, 0.82, 0.64, 0.72, 0.61, 0.74 |
| swap1k | 0.122 | 0.554 | 4.70 [3.48, 6.95] | 6.45, 5.43, 4.39, 5.50, 3.73, 5.37, 4.14 |
| swap10k | 0.437 | 4.212 | 9.14 [6.62, 12.45] | 11.55, 10.65, 8.24, 9.95, 6.79, 7.50, 8.91 |
| remove1k | 0.162 | 0.629 | 4.01 [3.06, 5.20] | 4.85, 4.46, 3.64, 3.83, 3.79, 3.66, 3.96 |
| remove10k | 0.831 | 4.567 | 5.52 [4.34, 7.11] | 6.58, 5.48, 5.12, 5.80, 4.64, 5.35, 5.43 |
| clear1k | 0.441 | 0.985 | 2.25 [1.76, 2.94] | 2.27, 2.50, 2.12, 1.93, 2.47, 2.17, 2.27 |
| clear10k | 2.454 | 5.680 | 2.28 [1.69, 3.50] | 2.47, 2.65, 2.17, 2.40, 2.09, 2.12, 2.22 |

- Ranvier is faster on item updates (0.70×).
- Creating, appending, replacing and clearing take 2.1–3.5× Solid 2. These ratios are higher than the DOM benchmark's (1.5–1.7×), where DOM work common to both dilutes them.
- Swaps and removals take 4–9× Solid 2 and grow with list length: a swap is 4.7× at 1,000 rows and 9.1× at 10,000. Solid 2's cost stays near the size of the change.

## Where a structural pass spends

CPU profile of 400 swaps on a 10,000-row list (`node --cpu-prof --cpu-prof-interval 50`), self time:

| Share | Function | Work |
|---|---|---|
| 16.6% | `KeySet.Add` | `Seen.Add` per item: the duplicate check and the pass's key set |
| 9.5% | `KeyMap.Find` | `Entries.Find` per item, and per entry in `RetireRemoved` |
| 9.1% + 6.9% | `Enumerate`, the `seq` enumerator | the source read as `seq<'T>` through a JavaScript iterator |
| 6.8% | `ApplyDiff` | `Entries.Iterate` with `Seen.Contains` per entry, to find removed keys |
| 2.3% | `Signal.set_Value` | `CommitWrites`: one item write per surviving row |
| 8.1% | garbage collector | |
| ≈ 15% | `Get`, `GetRow`, `ReadRow`, `Row.Id`, the fixture's layout effect | the reader of every row; Solid 2's reader walks an array |

Removals show the same distribution. Each pass visits every item: it computes its key, hashes it twice, stages and commits an item write, and walks every entry for removals. Solid 2's `mapArray` compares items by identity and skips the common prefix and suffix. It then tries a bounded small-move path (`trySmallMove`, at most 32 displaced items) before falling back to a map over the middle.

`Positional.diff` takes no part in this pass. It serves only delta readers (`ProjectionDelta.Positional`) and filtered projections. Trimming its prefix and suffix leaves `swap1k` and `remove1k` unchanged.

## What would close the gap

A pass with a cost that follows the size of the change would need the following changes in `RowsOf` / `KeyedProjection`:

1. **Item identity before the key.** Keep the previous pass's items. Where the item at an index is the same reference, the key and the row are unchanged, and both the key computation and the item write are skipped. This requires `keyOf` to be pure, as Solid 2's identity keying already assumes.
2. **Skip the common prefix and suffix**, then reconcile only the middle range.
3. **Duplicate detection over the middle.** A middle key that maps to an entry outside the previous middle range duplicates a trimmed key. That check is O(change) instead of a key set over every item.
4. **Removed keys from the middle only**, instead of `Entries.Iterate` over every entry.
5. **An array fast path for the source**, instead of a `seq` enumerator.

The pass's other consumers rely on it visiting every item: hidden keys, pending rows, delta readers, `CreateFirst` rollback and the held-error state. Each of them needs a test against a trimmed pass.
