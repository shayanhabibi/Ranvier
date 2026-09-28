# Delta readers for `Projection<'K,'V>`: design

This is a design only; nothing in the repo was changed. I tested the parts that carry the risk in a SageFs session loaded with `src/Ranvier/Ranvier.fsproj` (session `0365ee1a`, since stopped):

- **Today's baseline.** One move plus one value change on a 5-row projection gives `AsObservableCollection` a `Reset` followed by 5 `Add` events.
- **The shared op-log and its per-reader cursors.** The merge laws hold and slow readers coalesce correctly. The results are below.
- **An LIS-based positional diff.** 5000 random cases passed. Each replays correctly through a real `ObservableCollection` (`RemoveAt`/`Insert`/`Move`), and each uses at most `survivors − LIS` moves.

## 1. How changes surface today

- **Where changes happen.** A pass (`Run`) calls `Enumerate` to fill `passKeys`/`seen`, then runs `ApplyDiff` inside one `graph.Batch`. `ApplyDiff` works in this order:
  1. It collects `removed` by scanning `entries` (O(N)).
  2. It calls `Retire` on each removed key, which calls `NotifyRemoved` and disposes the key's scope.
  3. `CreateAdded` creates the new rows.
  4. `CommitWrites` writes item signals.
  5. `publishKeys` compares element by element and writes the `keys` array only if order or membership moved.
- **What a consumer can see.** Only the current state is visible: `Keys` (an immutable `'K[]`), `Get`, `Snapshot`, `AnyPending` and `PendingKeys`. Which keys were added or removed, and which rows changed, is known inside `ApplyDiff`, but none of it is published.
- **Rows are lazy.** A row `Memo` applies its cutoff in `Recompute` using the graph equality comparer, and value changes are only known once someone pulls the row. `RunRow` runs before the memo commits, so `entry.Row.Peek` there is still the previous value.
- **`AsObservableCollection`** is an `Effect` that reads every row on each run, then calls `Clear` and re-adds all values. That is O(N) per run, and it sends `Reset` for any change.
- **The views** (`MapView`, `FilterView`, `SortView`, `Grouping`) each run `Enumerate` over `upstream.Keys` on every pass. The module doc states "A membership or order change costs O(N) per view".

FSharp.Data.Adaptive, checked with `fcs_nuget_members` against `bench/Ranvier.Counters`:
- `IOpReader<'State,'Delta>` has `GetChanges(AdaptiveToken) : 'Delta`, `State` and `Trace`.
- `History<'State,'Delta>` has `Perform`, `NewReader()` and `NewReader(trace, mapping)`.
- `Traceable` has `tempty`, `tapplyDelta : 'State -> 'Delta -> 'State * 'Delta`, `tcomputeDelta`, `tmonoid`, `tsize` and `tprune`.
- `ElementOperation<'T>` is `Set of 'T | Remove`.
- `IndexListDelta` is keyed by `Index` (a dense order key), which is why its moves come for free.

Ranvier has no `Index`. Order is a `'K[]`, so moves have to be computed rather than logged.

## 2. Proposed public API

```fsharp
namespace Ranvier

/// What happened to a key between two reads by the same reader.
[<RequireQualifiedAccess>]
type KeyChange =
    /// Live now, absent at the previous read.
    | Added
    /// Absent now, live at the previous read.
    | Removed
    /// Live at both reads with the same row; the row's settled value moved (or settled for the first time).
    | Changed
    /// Removed and re-added between the reads: a new row, and a new scope in the factory form.
    | Replaced

/// A change to a positional mirror of Keys, valid when applied in sequence.
[<RequireQualifiedAccess>]
type PositionalChange<'K> =
    | RemoveAt of index: int
    | InsertAt of index: int * key: 'K
    | Move of oldIndex: int * newIndex: int

/// The changes to a projection between two reads by one reader.
[<Sealed>]
type ProjectionDelta<'K when 'K: equality> =
    /// The keys whose membership or row moved; unordered. Empty on a reset.
    member Changes: IReadOnlyCollection<KeyValuePair<'K, KeyChange>>
    /// The key order at this read. Shares the array the projection published.
    member Keys: 'K[]
    /// The key order at the previous read; empty on the first read.
    member PreviousKeys: 'K[]
    /// Whether the order or membership of Keys differs from PreviousKeys.
    member OrderChanged: bool
    /// Set on the first read, after the reader fell behind the log, and after the projection is disposed.
    /// The consumer rebuilds from Keys; Changes is empty.
    member IsReset: bool
    member IsEmpty: bool
    /// Edits turning PreviousKeys into Keys: removals back to front, then at most (survivors − LIS) moves and the inserts.
    /// Computed on first call, O(N log N); empty when OrderChanged is false.
    member Positional: PositionalChange<'K>[]

/// A cursor over one projection's changes. Owned by the scope that creates it.
[<Sealed>]
type ProjectionReader<'K, 'V when 'K: equality> =
    /// The changes since this reader's previous Read; advances the cursor. Tracked: the reader's computation
    /// wakes on the next membership, order, or (value readers) row change.
    /// Raises what the pass raised while the pass is pending or failed, as Keys does, and leaves the cursor in place.
    member Read: unit -> ProjectionDelta<'K>
    member Projection: Projection<'K, 'V>
    interface IDisposable

type Projection<'K, 'V> with
    /// A reader of membership and order. KeyChange.Changed never appears.
    member NewKeyReader: unit -> ProjectionReader<'K, 'V>
    /// A reader of membership, order and settled row values. Makes every row observed while the reader lives.
    member NewReader: unit -> ProjectionReader<'K, 'V>

module Projection =
    /// An effect that applies each non-empty delta; values are read with Projection.Get / GetSettled in `apply`.
    val onChanges: apply: (ProjectionDelta<'K> -> unit) -> upstream: Projection<'K, 'V> -> unit
```

Design choices:
- **Changes carry no values.** Rows are lazy. Putting the value in the op, as `ElementOperation.Set of 'T` does, would force every row to compute. A consumer reads values with `Get` or `GetSettled` for the keys it cares about.
- **`Replaced` is kept separate from `Changed`.** For a factory row it means the old per-key scope was disposed and a new one was created. A consumer that holds per-row resources, such as a DOM node, has to rebuild it.
- **`Changed` follows the `Snapshot` rule for rows.** It fires when the last settled value moves, including the first settle. It does not fire when a row goes pending or fails with the old value unchanged; those transitions stay on `AnyPending`, `PendingKeys` and `Get`. This is the rule `AsObservableCollection` already uses.

## 3. How changes are represented and stored

There is one shared log per projection: a singly linked chain of nodes. Each node holds a `Platform.KeyMap<'K, KeyChange>` plus an `OrderMoved` flag. The projection holds only the tail. Each reader holds its own node (its cursor) and the `Keys` array it last returned.

Changes are recorded only while at least one reader exists, which a `readers` count checks. When no reader exists the cost is zero:

| Where | Records |
|---|---|
| `Retire` | `Removed k` |
| `CreateMapped` / `CreateFactored` (via `NewRow`) | `Added k` |
| `publishKeys`, when it writes | `OrderMoved` |
| `RunRow`, when `entry.Settled` is false, or the equality comparer reports the value differs from `entry.Row.Peek`, and value readers exist | `Changed k` |
| `Dispose` | a reset marker; readers return `IsReset` with empty `Keys` |

When a key is recorded into a node that already holds it, the two changes merge. The prototype confirmed this table:

| previous \ next | Updated/Changed | Removed | Added |
|---|---|---|---|
| Added | Added | *(drop)* | n/a |
| Changed | Changed | Removed | n/a |
| Removed | n/a | n/a | Replaced |
| Replaced | Replaced | Removed | n/a |

Each change is recorded when it is applied, not at the end of the pass. So if a removal's cleanup throws partway through `ApplyDiff`, the log still matches `entries`. The existing comment says the next pass "diffs against whatever the failed one applied", and the log follows the same rule.

## 4. Independent readers, each with its own cursor, without O(N) per read

- **`Seal()`.** If the tail holds changes, `Seal()` appends a fresh empty node and returns it; otherwise it returns the current tail. A reader calls `Seal()` once when it is constructed and once per `Read`.
- **`Read`.** It walks from its node to the sealed tail, folding each node's changes into an accumulator with the merge table, then moves its cursor to the sealed tail.
- **Sharing.** Readers that read with no writes in between share the same empty tail, so an idle read allocates nothing on the chain.
- **Garbage collection.** Nodes behind the oldest cursor become unreachable and are collected. The chain never holds its head, so no weak references are needed, which matters for Fable.
- **Cost.** A read costs O(changes recorded since the cursor, deduplicated within each node), not O(N). A value reader also pays O(pending suspects) first; see §5.
- **Prototype.** The fast reader read three times and got `{a:Added, b:Updated}`, then `{a:Updated, c:Removed, moved}`, then `{b:Removed, c:Added}`. The slow reader read once and got `{a:Added, b:Removed, c:Replaced, moved}` after walking 6 changes across 3 nodes, and its next read was empty.
- **Positional changes are per reader but lazy.** The delta keeps `PreviousKeys` and `Keys`. Both are immutable arrays that the projection already allocates, so this adds O(1) memory. `Positional` runs the LIS diff only when a consumer asks for it. Readers at the same cursor can share the result by caching it on the sealed node, keyed by the reference of `PreviousKeys`.
- **Readers that fall behind.** The projection keeps a list of its readers. Each node stores a running total of changes. When `tail.total − cursor.total > max(64, entries.Count)`, the lagging reader is moved to the tail and its next `Read` returns `IsReset`. That caps both the chain's memory and the cost of the read at O(N). A reader that is never disposed and never read would otherwise pin the whole chain. The reader is an `IOwned` node, disposed with the scope that created it.

## 5. Pending and failed passes, batching, and waking readers

- **A pending or failed pass.** `Read` runs through `ReadAfterPass`/`PullFor` in the same way `Keys` does. While the pass is suspended or failed it raises what the pass raised, links the awaited source (`linkAwaited`), and does not advance the cursor. When the pass settles, the beacon's `NotifyFailure` wakes the reader, and the next `Read` returns everything accumulated in the meantime. This matches `Keys` and `Snapshot`. `AsObservableCollection`'s current "keeps its last contents while suspended" behaviour is kept because its effect catches the exception.
- **Batching.** A node is sealed only by a read, so every pass and batch between two reads by the same reader coalesces into one node, with churn cancelled (Added then Removed disappears). The log adds no flushes. The reader wakes once through the pass's existing `graph.Batch`.
- **Waking on membership and order.** The log owns a `Signal<int>` change stamp. It is bumped once per pass that recorded anything, at the end of `ApplyAdditions`, with `WriteExcept(puller)` as `publishKeys` does. `Read` tracks the beacon and the stamp.
- **Waking on row values.** This follows the pattern `RowWatch`/`touchSummary` already use. While a value reader exists, each row gets a small per-key tap observer. Its `MarkCheck`/`MarkDirty` adds the key to a `suspects` `KeySet` and calls `NotifyCheck` on the stamp.
  - The stamp is a custom `ISource`, like `ProjectionBeacon`. Its `UpdateIfNecessary` pulls each suspect row (`UpdateIfNecessary` on the row). `RunRow` logs `Changed` only if the value actually moved, and then bumps the stamp to Dirty.
  - So a reader of a row whose upstream wrote an equal value stays asleep.
  - Pulling suspects is shared across readers: it happens once, not once per reader.
  - `RunRow` logs no matter who pulled the row, so `Get` from another reader also produces `Changed`.
- **Laziness contract.** Taps make rows count as observed (`rowObserved`), so writes schedule passes while a value reader lives. That is correct: a live reader is an observer. A key reader installs no taps and leaves rows lazy.

## 6. Rebuilding `AsObservableCollection` and the existing views on readers

**`AsObservableCollection`** becomes an `Effect` over `NewReader()`, plus a mirror `visible : ResizeArray<'K>` and `index : KeyMap<'K,int>`.
- **On a reset:** `Clear`, then `Add` the settled rows.
- **On an order change:** apply `Positional`, mapping `RemoveAt`/`InsertAt`/`Move` to `RemoveAt`/`Insert`/`Move` on the collection. Tested: `[1..5]` to `[1;2;3;5;4]` becomes a single `Move(4,3)`, where today it is `Reset` plus 5 `Add`.
- **On `Changed k`:** `view[index[k]] <- GetSettled k`, which raises one `Replace` event.
- **On `Replaced k`:** the same, since the position is unchanged.
- **Rows that have never settled** are absent, as today, so the mirror tracks only settled keys. A first settle arrives as `Changed` and becomes an `Insert` at the key's rank among the visible keys. That costs O(N) to scan, or O(log N) with a Fenwick tree if needed.
- **Cost:** O(changes) per run for value changes, and O(N log N) only when the order moves. Today every run costs O(N) and resets the collection.

**Views.** Add an optional `IProjectionPass.ApplyDelta: ProjectionDelta<'K> -> bool`, which returns false to fall back to a full `Enumerate`:
- **`MapView`:** `Added` goes to `CreateAdded` and `Removed` to `Retire`. Its keys are upstream's keys, so it can publish `delta.Keys` by reference. No O(N) enumeration and no `seen` rebuild.
- **`FilterView`:** reads the inclusion projection's value reader. A `Changed k` toggles membership. When membership moves it rebuilds the filtered array (O(N) copy, but without N `TryValue` calls and N hash lookups).
- **`SortView`:** a `Changed k` on the sort key re-places k by binary search against the kept ranks (O(log N) plus an O(N) array insert). An upstream order change is a no-op unless ties exist.
- **`Grouping`:** a group key `Changed` moves one key between two inner views. Inner views get their own readers.
- **What stays O(N) inside `RowsOf`:** `ApplyDiff`'s `entries.Iterate` over `seen` and the per-pass `passKeys`/`seen` rebuild. A delta-driven pass has to skip these and apply `Added`/`Removed` directly, so the pass pipeline needs a second entry point. That entry point is the real implementation work.
- **Rolling it out:** `AsObservableCollection` first, which is public, contained, and the biggest win. Then `MapView`, then the rest.

## 7. Cost model (N = live keys, c = changes recorded since a reader's cursor, s = suspect rows)

| Operation | No readers | With readers |
|---|---|---|
| Pass (write path) | unchanged | +O(adds + removes) KeyMap writes, +1 stamp write |
| Row recompute | unchanged | +1 equality compare and 1 map write (value readers only) |
| Upstream row invalidation | unchanged | +1 tap callback, a KeySet add, and `NotifyCheck` (value readers) |
| `Read`, membership only | n/a | O(c) |
| `Read`, value reader | n/a | O(s) shared + O(c) |
| `Positional` | n/a | O(N log N), only when `OrderChanged`; cacheable per node |
| Memory | 0 | O(Σ distinct changes per unsealed node) ≤ ~N per lagging reader (capped by reset); taps O(N) while value readers live |
| Reset fallback | n/a | O(1) to produce; the consumer rebuilds O(N) |

## 8. Fable constraints

- **Maps and sets.** Use `Platform.KeyMap`/`KeySet` for per-node changes and suspects, not `Dictionary`. Keys include `None` and `()`, and KeyMap's JS `Map` path is the fast one. Iteration order is unspecified (primitive keys first), which is fine because `Changes` is documented as unordered and order comes from `Keys`.
- **No weak references and no finalizers.** Chain collection relies on the chain holding no head. Readers are `IOwned` and are disposed through their scope. The reset cap handles readers that are never disposed.
- **Type tests.** No `:?` on interfaces in the hot path. `Awaited.source` already shows the Fable workaround. The stamp `ISource` is a concrete sealed type.
- **Positional changes are the main Fable payoff.** `AsObservableCollection` is `#if !FABLE_COMPILER`, but a keyed DOM reconciler (the Solid/Feliz `For` equivalent) needs exactly `RemoveAt`/`InsertAt`/`Move` with the fewest moves. Place a DU with a generic field as a reference union and avoid `[<Struct>]` there. Keep the LIS on `int[]` and `ResizeArray` so it compiles to plain JS arrays.
- **Attributes.** `ProjectionDelta` and `ProjectionReader` need `[<AttachMembers>]` under Fable, as `RowSnapshot` has, so JS consumers can call members.

## 9. Alternatives

**A. Shared op-log chain with per-reader cursors (sections 3 to 5).** Closest to FDA's `History`. Reads cost O(changes), churn coalesces, and passes pay nothing when no readers exist. Costs: a log to manage (the reset cap), and taps for value readers.

**B. Version stamps on entries.** The projection keeps a global `version`. Each `RowEntry` gets `ChangedAt` and `AddedAt`, and removed keys go into a tombstone list stamped with the version. A reader stores its version, and `Read` scans `entries` plus the tombstones newer than that version. Costs:
- A read is O(N), plus O(removed).
- No log memory and no problem with lagging readers.
- It is trivially Fable-safe.
- Tombstones still need trimming to the oldest reader's version, which needs a reader registry anyway.

It fits when readers are few and N is small. It does not scale for the "1 change in 10k rows" case, which is the main reason to have deltas.

**C. Per-reader snapshot diffing, with no core changes.** Each reader keeps its previous `Keys` array and a `KeyMap<'K,'V>` of settled values, and on wake diffs them against `Keys` plus `Snapshot`. This is a library-level helper (`Projection.onChanges`) that could ship now. Costs:
- O(N) time and O(N) memory per reader per read.
- Every row computes, because `Snapshot` pulls all stale rows.
- It cannot tell `Replaced` from `Changed`.

It still fixes `AsObservableCollection`'s `Reset` storm by emitting the minimal `Move`/`Insert`/`Replace` events.

**D. Pushing changes to callbacks from inside `ApplyDiff`.** Rejected. Callbacks would run inside `inCleanup` and the batch, where re-entrant writes are deferred (`deferredDirty`). There is no coalescing across passes, and it contradicts the pull model everything else follows.

## 10. Recommendation

Adopt **A**, and take one idea from B: log `Changed` from `RunRow`'s pre-commit comparison, not from a stamp on each row. Deliver it in stages:
1. Ship `Positional` diffing and a C-style `AsObservableCollection` rewrite first. There is no core change, it removes `Reset` for every move or value change, and it gives a correctness oracle.
2. Add the log with membership and order only (`NewKeyReader`): the hooks in `Retire`, `NewRow` and `publishKeys`, the stamp, cursors, and the reset cap. Test that a reader's accumulated `Changes` applied to `PreviousKeys` always equals `Keys` as a set, across random passes including a removal cleanup that throws.
3. Add value readers (taps plus `RunRow` logging), then move `AsObservableCollection` onto `NewReader`.
4. Add `ApplyDelta` to the pass pipeline, starting with `MapView`. Before changing any `Projection` member or `Projection` module signature, run `fcs_refactor_impact`. The new API is additive, but `IProjectionPass` is internal.

Relevant files: `C:\Users\shaya\RiderProjects\Ranvier\src\Ranvier\Projections.fs` (`RunRow`, `Retire`, `ApplyDiff`/`ApplyAdditions`, `publishKeys`, `PullFor`/`ReadAfterPass`, `AsObservableCollection`), `C:\Users\shaya\RiderProjects\Ranvier\src\Ranvier\Combinators.fs` (`MapView`, `FilterView`, `SortView`, `Grouping`, `Projection` module), and `C:\Users\shaya\RiderProjects\Ranvier\src\Ranvier\Platform.fs` (`KeyMap`/`KeySet`).