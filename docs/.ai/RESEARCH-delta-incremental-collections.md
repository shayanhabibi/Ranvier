# Delta-based incremental collections and value readers

Research date: 2026-10-02. Ranvier inspected at `523a316`. This is a design recommendation, not an implementation or performance result. The core project passed a fresh `fslangmcp check`; symbol definitions and the source were inspected. No production code changed.

## Recommendation

Extend Ranvier's existing collection readers with **settled value-change readers**, then give projection passes a **delta application path**. Keep the graph's pull model, per-key lifetimes, and bounded reader buffers. Start with the current per-reader accumulator; a shared history is a later optimization justified by reader fan-out measurements.

These are separate capabilities:

1. A delta reader tells a consumer which keys need attention since its previous read.
2. An incremental operator updates its own retained state using those changes.
3. An incremental source records edits directly, avoiding a whole-input scan to discover them.

Implementing only the first improves adapters but does not make a pipeline of snapshot-based projections O(changes). This distinction is visible in Ranvier's current enumerating views and in FDA's delta-consuming operator implementations. [Ranvier views](../../src/Ranvier/Combinators.fs), [FDA map/filter readers](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/AdaptiveIndexList/AdaptiveIndexList.fs#L401-L550)

## Primary-source references

FSharp.Data.Adaptive is the closest reference for a pull design. Its reader starts with empty state and advances its own state when it evaluates changes. Its history applies deltas to authoritative state and records effective operations; stale/pruned reader history can recover by differencing the reader's previous state against current state. This supports independent readers and state synchronization, not lossless replay of every intermediate edit. FDA also supplies delta composition/application/differencing contracts through `Traceable`. These are useful design references, rather than a reason to replace Ranvier's graph. [History](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/Traceable/History.fs), [Traceable](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/Traceable/Traceable.fs)

FDA's operators demonstrate the retained information required: list mapping handles Set/Remove deltas; filtering caches predicate outcomes; sorting caches previous sort keys and maintains an ordered mapping; adaptive mapping tracks inner dependencies. Its `IndexList` separates order identity from integer position, and `CountingHashSet` represents overlapping contributions with counts. Recommendation: borrow the contracts and cache patterns, while retaining Ranvier's explicit scope ownership and pending/error semantics. [Map/filter](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/AdaptiveIndexList/AdaptiveIndexList.fs#L401-L550), [adaptive map](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/AdaptiveIndexList/AdaptiveIndexList.fs#L552-L632), [sort](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/AdaptiveIndexList/AdaptiveIndexList.fs#L945-L1025), [IndexList](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/Datastructures/IndexList.fs), [counting sets](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/Traceable/CountingHashSet.fs#L270-L309)

DynamicData is a useful C# interface reference. Its keyed cache exposes `Connect()` as an observable stream of change sets and `Edit()` for batched edits. A keyed `Change` includes reason, key, current value, optional previous value, and positional information; reasons distinguish Update, Refresh, and Moved. This shows how a value-carrying contract differs from a changed-key hint. Its stream/scheduling model should not be imported into Ranvier merely to expose deltas. [SourceCache](https://github.com/reactivemarbles/DynamicData/blob/main/src/DynamicData/Cache/SourceCache.cs), [Change](https://github.com/reactivemarbles/DynamicData/blob/main/src/DynamicData/Cache/Change.cs), [ChangeReason](https://github.com/reactivemarbles/DynamicData/blob/main/src/DynamicData/Cache/ChangeReason.cs)

Differential Dataflow is relevant to multiplicities, joins, and shared indexes, but has a substantially different execution contract: timestamped updates, arrangements, progress frontiers, and compaction. These are not prerequisites for a single-threaded latest-state reader. Recommendation: use its indexing and contribution ideas where useful; defer logical-time/progress machinery unless concurrent versions or iterative dataflow become a requirement. [Arrangements](https://timelydataflow.github.io/differential-dataflow/chapter_5/chapter_5.html), [join](https://timelydataflow.github.io/differential-dataflow/chapter_2/chapter_2_5.html), [progress probes](https://timelydataflow.github.io/differential-dataflow/chapter_3/chapter_3_2.html), [compaction](https://timelydataflow.github.io/differential-dataflow/chapter_5/chapter_5_3.html)

The paragraphs above describe source evidence; the remaining proposed contracts, implementation choices, cost bounds, and delivery order are Ranvier design deductions. FDA citations are pinned to `76afc00027b10933606e733ff6258275c53224b3`; DynamicData links use its moving main branch as inspected on the research date.

## What Ranvier already has

`Projection.NewKeyReader()` returns an owned `ProjectionReader<K>`. Its first `Read()` is a reset; subsequent reads report added, removed, and replaced keys, plus order information. `Changed` exists in `KeyChange` but is reserved for value readers. Each reader accumulates its own changes; changes beyond `max(64, live entry count)` switch it to a reset. A pending or failed projection pass raises before the cursor advances. Disposing the projection resets readers to empty keys; disposing the reader makes further reads throw. [Reader implementation](../../src/Ranvier/Deltas.fs), [registration and disposal](../../src/Ranvier/Projections.fs#L1386)

`ProjectionDelta` supplies current and previous key arrays and lazily computed positional edits. An order that moves and moves back can still set `OrderChanged`, even though the positional edits are empty. `Added` then `Removed` cancels; `Removed` then `Added` means `Replaced`, preserving the fact that row identity and its scope changed. [Delta representation and accumulator](../../src/Ranvier/Deltas.fs)

`AsObservableCollection` already emits moves and replacements rather than resetting for every update. It still reads all visible rows and constructs full mirrors; its documented cost is O(N log N) per change. `MapView`, filter, and sort enumerate key arrays. `ProjectionFold` already has row observers, a suspect queue, and retained contributions, but its membership reconciliation scans keys. [Observable adapter](../../src/Ranvier/Projections.fs#L1258), [views and fold](../../src/Ranvier/Combinators.fs)

The earlier [reader design](designs/projection-delta-reader.md) and [stage-2 implementation record](designs/projection-delta-reader-stage-2.md) therefore remain useful, but their proposed value-publication hook needs revision.

## Reader contract to settle before implementation

**Independent baselines.** Two readers may read at different times. Each receives changes relative to its own last successful read. A reader is a stateful cursor and belongs to one logical consumer; sharing it between two effects would make them consume each other's changes.

**State synchronization, not an event history.** Several changes to one row may coalesce. A price changing 10 → 11 → 12 between reads needs one changed-key entry and the current value 12. If it changes 10 → 11 → 10, retaining a `Changed` entry is valid: it means a relevant change occurred and the consumer should refresh the key. Suppressing that entry requires comparison against each reader's baseline. Do not promise minimal net value deltas without paying for those baselines. Removed-and-readded keys remain `Replaced` even when their values compare equal.

**A value reader observes values.** It activates row observation and may force rows that a key reader would leave lazy. Creating the first active value reader requires initial row discovery and settling attempts, O(N); steady-state work can use suspects. Detach the shared row observers when the last value reader goes away. Keep key readers free of those taps.

**Settled-value semantics.** For the initial consumer, mirror `Snapshot`/`AsObservableCollection`: never-settled rows have no displayed value; previously settled rows retain it while pending or failed. First settlement is a value change even if the result is `default(T)`. Pending/error changes alone are not value changes. Existing pending/error reads and summaries remain authoritative. A future state reader would need an explicit state-change contract and must not overload `Changed` with every status transition. [Snapshot and adapter semantics](../../src/Ranvier/Projections.fs#L1221)

**Reset is a supported result.** First read, overflow, source disposal, and adapter recovery need an explicit rebuild route. Reset carries an authoritative current key order. Rebuilding visible values requires querying which rows have settled; `Keys` alone is insufficient. Old deltas remain readable after later reads; buffer reuse must not mutate them. The current implementation hands over an accumulator for this reason. [Current reset and buffer lifetime](../../src/Ranvier/Deltas.fs)

**Read versus apply failure.** Successful `Read()` advances the cursor before user code applies the result. An adapter that throws halfway through application cannot simply read again and expect the same delta. It must retain the delta for retry or mark its mirror invalid and rebuild on the next attempt. Prefer a simple internal reset/rebuild mechanism before adding a public acknowledgement protocol. This matches the existing observable adapter's recovery strategy. [Adapter failure handling](../../src/Ranvier/Projections.fs#L1337)

**Batching is coalescing, not rollback.** Read after a graph batch to obtain its settled effects. Explicit reads inside a batch may establish intermediate baselines. Do not promise that one batch equals exactly one delta, or that thrown user code rolls back preceding writes. Readers should add no independent flush or asynchronous scheduler.

**Async completion is a separate commit.** Do work outside the synchronous mutation batch and apply accepted results in a new batch, preserving cancellation, generation checks, and graph affinity. A reader does not make overlapping async work ordered. FDA likewise implements a synchronous transaction action with thread-static context; returning future work does not extend that transaction. [Thread-static context](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/Core/Transaction.fs#L77-L87), [transaction action](https://github.com/fsprojects/FSharp.Data.Adaptive/blob/76afc00027b10933606e733ff6258275c53224b3/src/FSharp.Data.Adaptive/Core/Transaction.fs#L257-L287)

## Proposed value-reader implementation

Start with an additive interface sketch, retaining the existing key-only delta shape:

```text
Projection<K,V>.NewValueReader() -> ProjectionReader<K>
reader.Read() -> ProjectionDelta<K>
delta.Changes includes Changed as well as Added/Removed/Replaced
```

This is a proposed surface, not callable code today. `NewValueReader` makes the observation cost clear. A key-only delta does not retain old V values and lets internal operators fetch only relevant rows.

Use one shared row-observation module per projection while value readers exist. Each tap references the actual row entry, records invalidation into a deduplicated suspect queue, and marks a reader wake source for **Check**. On pull, bring the pass current, reconcile added/removed/replaced rows, then read suspects untracked after their memo execution finishes. Compare against the tap's last accepted settled value; log first settlement or a changed result. Pending/failed rows retain that value. A replaced row gets a new tap/baseline. Consume invalidations raised during settling without losing them or recursively evaluating a running row. `ProjectionFold` is an existing local model for much of this machinery. [Fold taps and post-read comparison](../../src/Ranvier/Combinators.fs#L489)

Only promote the wake source to **Dirty** for a relevant value or membership change. Equal recomputation should resolve Check without waking every downstream consumer. Membership readers must not receive value-only changes. A value read advances its cursor only after the pass and suspect processing produce a valid result; infrastructure/comparer failures leave its baseline available for recovery.

Suspect draining must also be failure-safe: publish accepted tap baselines together with their reader entries, or preserve already-recorded changes and requeue failing/unprocessed suspects. Leaving only the reader cursor unchanged is insufficient if tap state advanced or queue entries were discarded. Do not promise rollback of arbitrary row computations or cleanups.

The equality policy belongs to this module's contract. Initially resolve the projection's graph-policy `IEqualityComparer<V>` once, as its existing adapter and fold do. A later per-reader comparer requires separate accepted baselines per policy, so it is not free. Source-node comparers already affect which invalidations reach a row; they do not implicitly become the collection reader's comparer. Mutable payloads require immutable replacement, explicit refresh, or tracked property signals; retaining a reference cannot reconstruct its prior contents.

### Why not publish in `RunRow`?

The older design proposed comparing inside `Projection.RunRow`. That method returns the body result before `Memo.Run` finishes its purity checks, source reconciliation, and cutoff comparison. A throwing cutoff comparer can restore the old memo value and fail the run. Recording a value delta beforehand would publish an outcome that never committed. Threading the previous-value argument into `RunRow` does not fix that ordering. [Projection body](../../src/Ranvier/Projections.fs#L518), [memo commit and cutoff](../../src/Ranvier/Core.fs#L2801)

A post-read tap avoids modifying primitive memo hot paths. If measurements later favor an internal memo commit hook, place it after the accepted outcome is finalized and make it opt-in; account for both its branch cost and its state semantics. Do not duplicate the body/cutoff protocol in projections.

### Values in deltas

A keys-only delta is a refresh instruction, not a historical snapshot of values. If a consumer stores it and reads values later, those values may belong to a newer state. Public UI consumers also need a way to retrieve a last-settled value without losing pending/error semantics: `Get` can throw, `Snapshot` scans, and `GetSettled` is currently internal and still raises for failed rows. [Current reads](../../src/Ranvier/Projections.fs#L1038)

Before exposing a general external value-reader workflow, choose either a small settled-value query (with explicit absence and failure behavior) or a separate `CollectionDelta<K,V>` carrying the values captured by that read. Keep `ProjectionDelta<K>` unchanged. Captured old/current values are useful for joins, reversible folds, and deferred adapters, but retain payloads and increase allocation. Arbitrary mutable objects still cannot be deep-snapshotted by this interface.

## Making the operators incremental

**Map first.** On Added create the row and scope; on Removed retire them; on Replaced do both; on Changed invalidate/update the relevant input. Reuse the upstream key order. A new `ApplyDelta` pass must bypass full `Enumerate`, `seen` rebuilding, and removal scanning. Reading a delta and then calling the old full pass is not an incremental implementation. Key-only readers may already suffice for membership of maps whose row dependencies propagate value changes directly.

**Filter.** Retain predicate outcome and membership per key. A changed predicate updates one decision. A global predicate dependency can legitimately invalidate every key. Preserve today's pending predicate membership and failed/hidden-row behavior. Building the published filtered array can still cost O(N).

**Sort.** Retain key → sort rank and an ordered index. Reposition changed ranks; preserve upstream-order tie-breaking. Reordering the upstream can affect all tied rows. Binary search is O(log N), but insertion into a flat array is O(N); do not advertise O(log N) total updates while that representation remains.

**Group and join.** Retain previous group/join keys and bucket indexes. Move only affected memberships. Many-to-many joins can produce O(output changes) work far larger than the input delta. Distinct projections need multiplicity/reference counts so removing one contributing row does not remove a value still contributed by another.

**Fold.** Reuse the current inverse-add/subtract approach for group-like aggregates. General folds are not automatically reversible; min/max require retained candidate structures or recomputation when their selected value leaves. Noncommutative/order-sensitive folds must respond to order changes.

**Incremental inputs.** A projection over a replaced array/sequence still has to inspect that snapshot to discover edits. Add a keyed mutable collection source with operations such as add/update/remove and a batched edit scope only after reader/operator contracts are working. Keep snapshot constructors as compatible adapters with explicit O(N) discovery cost. Stable keys, value replacement, row-scope replacement, and ordering must be separate decisions.

## Complexity and benchmark gates

Let N be live rows, r live readers, c accumulated distinct changed keys, and s suspect rows. The current accumulator records membership in O(r) per affected key. A value-reader design adds O(N) shared taps/accepted values while active, O(s) observation overhead plus row recomputation/dependency traversal/comparer cost, and O(r × changed keys) recording. Reader delivery is proportional to its returned changes when enumerated; positional edits remain O(N log N) when requested. Immutable key-array publication and ordinary array-based consumers can still be O(N).

The existing cap bounds pending entries, not all retained memory: readers and retained deltas also reference old key arrays; captured values would retain payloads. Disposal must release observer links, suspect entries, and buffers. A shared history may reduce write fan-out at large r but introduces lag, pruning, and reset/reconstruction costs. Measure before replacing the simpler current representation.

Validate on both .NET and Fable:

- Replay a reader's membership/order edits into its own mirror and compare with the authoritative state. Generate random add/remove/readd/reorder/value/state sequences and independent reader schedules.
- Test Changed after Added/Replaced retains that stronger kind; Changed then Removed becomes Removed; Added then Removed cancels; Removed then Added preserves replacement identity. The current merger cannot just accept Changed: its default branch would overwrite Added/Replaced. [Merger](../../src/Ranvier/Deltas.fs#L40)
- Cover equal results, first default-valued settlement, comparer/purity failures, pending/error recovery, hidden predicates, disposal during cleanup, reentrant settling, slow-reader reset, retained deltas, and partial adapter failure.
- Benchmark N = 100 / 10,000 / 100,000, one-row edits versus 1%/100% changes, 0/1/4/32 readers, and active/idle readers. Count rows evaluated and keys scanned as well as time and allocation.
- Separate producer discovery, operator processing, reader delivery, positional reconciliation, and DOM/ObservableCollection application. Show a multi-stage map/filter/sort/group flow; compare outputs first, then timing. No speedup is established by this research.

## Delivery order

1. Shared settled-value observation plus merge laws and bounded readers; no primitive hot-path changes.
2. Adapt `AsObservableCollection`; settle the public value retrieval/reset contract and then build a keyed DOM consumer that preserves row identity on Changed and rebuilds on Replaced.
3. Delta-driven map and fold membership paths, followed by filter, sort, grouping, and slices, each retaining a correct reset fallback.
4. Incremental mutable collection inputs; indexed ordering if workloads justify it. Shared history and richer value/state deltas only after measured need.

Stages 1–2 improve consumption. Stages 3–4 are what make collection processing incremental end to end.
