# Delta readers, stages 2 to 4: companion note

**Status:** stage 2 implemented on `worktree-wf_c46b5816-3af-3`, pending benchmark gate. Stages 3 and 4 are open. Companion to `projection-delta-reader.md` (the design); it does not replace it.
Line references are to `src/Ranvier/*.fs` at `c631f23`. No benchmark was run for this note; §5 names the cases that
settle each cost.

## 1. Goal

Check the design against the current code, record what moved since it was written, and cut stage 2 (membership and
order readers) down to the smallest change that ships a public, C#-usable reader at zero cost to projections that
never create one.

## 2. The design's claims, checked

| Design claim | Current code | Holds |
|---|---|---|
| `ApplyDiff` collects `removed` by an O(N) scan of `entries` | `Projections.fs:674-677` | yes |
| `Retire` calls `NotifyRemoved`, then disposes the scope or row | `Projections.fs:549-563` (`NotifyRemoved` at 558) | yes |
| `publishKeys` writes `keys` only when order or membership moved | `Projections.fs:424-438`; `WriteExcept (…, puller)` at 437 | yes |
| A failed diff leaves the next pass to diff against what was applied | comment at `Projections.fs:590-592` | yes |
| Adds go through one `NewRow` | `RowsOf.NewRow` (`Projections.fs:1374-1378`) **and** `Grouping.CreateAdded` (`Combinators.fs:345-351`), which calls `Entries.Set` itself | **no: two sites** |
| `RunRow` sees the previous value in `entry.Row.Peek` | still true (`Projections.fs:459-475`); the row memo also receives the previous value, and `RunRow` does not (below) | yes |
| `AsObservableCollection` clears and re-adds on every run | replaced by stage 1 (below) | **obsolete** |
| `[1..5]` to `[1;2;3;5;4]` is `Move(4,3)` | the shipped diff gives `Move(3,4)` (`tests/Ranvier.Tests/Projections.fs:1097`) | **typo in design §6** |
| `Positional` is `PositionalChange<'K>[]` | `Positional.diff` returns `ResizeArray` (`Positional.fs:79`); `PositionalChange` is `internal` (`Positional.fs:5`) | differs |
| Views run `Enumerate` over `upstream.Keys` per pass, O(N) | `Combinators.fs:842`, `MapView` at 46-57 | yes |

## 3. What changed since the design

1. **Stage 1 shipped as option C.** `AsObservableCollection` (`Projections.fs:1195-1276`) reads every row tracked on each
   run (1221), diffs its mirror with `Positional.diff` (1243), and raises `Replace` per value moved under the graph's
   equality. Each run costs O(N) plus O(N log N), and raises `Reset` only on the first population and after a run
   that threw partway through its edits.
2. **Previous-value computes landed.** A row's compute is `'V voption -> 'V` (`Projections.fs:1380`). The row memo
   receives the previous value; `Compute` is `fun _ -> this.RunRow entry`, so `RunRow` does not. Stage 3 has to thread
   the argument into `RunRow` before it can log `Changed` by comparing against it instead of `entry.Row.Peek`.
3. **Per-row taps already exist.** `ProjectionFold` attaches a `FoldRow` observer to each row (`Combinators.fs:489-527`,
   attach at 714) and detects a replaced row by entry reference (`Combinators.fs:689`). This is the tap model of
   design §5, working, for `foldGroup`. Its `Diff` is O(N) in `Keys` (`Combinators.fs:675-718`) and is the first internal
   client for a key reader.
4. **Held-out keys.** Views publish pending keys absent from `Keys` through `HeldOut` (`Combinators.fs:4-40`) and
   `PendingExtra`/`ExtraObserved` (`Projections.fs:1130`, `1136`). A reader reports `Keys` only; held-out keys stay on
   `HeldOut`/`UngroupedKeys`.
5. **`SliceView`** (`Combinators.fs:68`) is a new view for the `ApplyDelta` rollout in stage 4.
6. **One mutating thread per graph** (`Core.fs:1082-1085`). Reader state needs no locks.

## 4. Stage 2, reduced

### 4.1 Two simplifications to the design

- **The wake source is `Keys`; stage 2 adds no stamp signal.** Every `Added` or `Removed` changes the membership of the
  pass, so `publishKeys` writes `keys` (`Projections.fs:437`). A failed, suspended or recovering pass wakes the beacon
  (`Projections.fs:611`, `630`, `651`). A `Read` that starts with `this.Keys` (`Projections.fs:931-952`) therefore wakes
  on every change it will report, raises what the pass raised, and leaves the cursor in place. `Keys` also marks the
  projection observed (948-949), so no change to the `observed` computation (`Projections.fs:705-717`) is needed. The
  stamp and its `ISource` arrive with value readers in stage 3.
- **Order needs no log entry.** `keys` is written only when the order or membership moves, so
  `OrderChanged = not (ReferenceEquals (previousKeys, keys))`. The `publishKeys` hook is dropped.

This leaves the log recording membership only. The design's shared chain (§4) and a per-reader accumulator both fit;
§6 compares them, and the sketch below uses the accumulator.

### 4.2 Hooks

The projection gains one field, `let mutable log: KeyLog<'K> = null`, non-null exactly while a reader lives.

| # | Site | Change |
|---|---|---|
| 1 | `Retire` (`Projections.fs:549`) | `if not (isNull log) then log.Record (entry.Key, KeyChange.Removed)` |
| 2 | new `member internal AddEntry (key, entry)`: `entries.Set` plus `log.Record (key, Added)` | replaces `this.Entries.Set` at `Projections.fs:1377` and `Combinators.fs:349` |
| 3 | `Dispose` (`Projections.fs:1299`) | `log.Reset ()`: every reader's next `Read` returns `IsReset` with empty `Keys` |
| 4 | `NewKeyReader` / reader `Dispose` | register or remove the reader; the last removal sets `log` to null |

`KeyLog.Record` merges into each reader's `KeyMap<'K, KeyChange>` with the design's table (§3). When a reader's map
exceeds `max(64, entries.Count)` it is cleared and the reader is flagged for reset; that reader then records nothing
until its next `Read`.

`ProjectionReader.Read` reads `projection.Keys`, then: first read or reset flag gives `IsReset`; an empty map with the
same `Keys` reference gives the reader's cached empty delta; otherwise it hands the map to a new delta, takes a fresh
map, and moves its cursor to the current `Keys`. The reader is `IOwned`, attached with
`graph.CurrentOwner.AttachLinked` as the projection is (`Projections.fs:401`).

### 4.3 Surface (C#-first, research §12)

```fsharp
type KeyChange = Added = 0 | Removed = 1 | Replaced = 2 | Changed = 3      // a CLR enum

[<Sealed>] type ProjectionDelta<'K when 'K: equality> =
    member Changes: IReadOnlyCollection<KeyValuePair<'K, KeyChange>>
    member Keys: 'K[]
    member PreviousKeys: 'K[]
    member OrderChanged: bool
    member IsReset: bool
    member IsEmpty: bool
    member Positional: IReadOnlyList<PositionalChange<'K>>                   // lazy; PositionalChange made public

[<Sealed>] type ProjectionReader<'K when 'K: equality> =
    member Read: unit -> ProjectionDelta<'K>
    interface IDisposable

type Projection<'K,'V> with member NewKeyReader: unit -> ProjectionReader<'K>
```

Against the design's §2:
- **`KeyChange` becomes an enum.** C# gets `switch`, no allocation, and a small `KeyValuePair`. A
  fieldless F# union compiles to a class compared through `Tag`. `Changed` is reserved for stage 3.
- **The reader drops `'V`.** A C# field reads `ProjectionReader<int>`, one type argument, never nested; FDA#66's
  `IOpReader<IndexList<T>, IndexListDelta<T>>` is the shape to avoid. The design's `Projection` back-reference goes
  with it; stage 3's `NewReader` returns the same type.
- **Changes are `KeyValuePair` behind a BCL interface**, not `'K * KeyChange` (FDA#77).
- `Projection.onChanges` and a C# `OnChanges(Action<ProjectionDelta<K>>)` in `Ranvier.CSharp/Extensions.fs` wait for
  stage 3, where they carry values.

## 5. Cost model

**A projection with no reader.** One reference field per projection (8 bytes on 64-bit). Per removed key, one null
check in `Retire`; per added key, one null check in `AddEntry`. Nothing on row recompute, flush, `Keys`, `Get` or
`Snapshot`. No allocation, no per-row memory. Confirm with the existing `ProjectionBenchmarks.EditOneItem` and
`Reorder` (`bench/Ranvier.Benchmarks/Projections.fs:44`, `53`) plus a new `ChurnOneKey` (remove one key and add
another per write) at 8/64/512, before and after: all within noise.

**With r key readers** (c = distinct keys changed since a reader's last read):

| Path | Cost |
|---|---|
| Pass, per added or removed key | r `KeyMap` find+set (the merge) |
| Row recompute, flush | unchanged |
| `Read`, nothing changed | one `Keys` read, one reference compare; the cached empty delta (`ReadIdle` confirms zero allocation) |
| `Read`, changes | one delta and one `KeyMap`; O(1) beyond the handover |
| `Positional` | O(N log N), only when `OrderChanged` and only when called |
| Memory per reader | O(c), capped at `max(64, N)` entries, then O(1) until read |
| Reset | O(1) to produce; the consumer rebuilds O(N) |

New `DeltaReaderBenchmarks` settle the reader side: `ReadAfterOneRemoval` at N = 64/512/10 000 (the Fabulous#258
"10K points, one removed" case) against a baseline that set-diffs two `Keys` arrays (option C), `ReadIdle`, and
`ChurnOneKey` with 1 and 4 readers.

## 6. Shared chain or per-reader accumulator

| | Chain (design §4) | Per-reader map (§4.2) |
|---|---|---|
| Record | O(1) | O(r) |
| Read | O(c) fold across nodes | O(1) handover |
| Memory | shared; one lagging reader pins the chain | per reader; capped per reader |
| Lag cap | needs node totals and a reader scan | a map count |
| Code | nodes, `Seal`, fold, totals | one map per reader |

With one or two readers per projection, the usual case for a UI list, the accumulator is smaller and exactly bounded.
The chain wins past a handful of readers on one projection. Both keep the design's merge table and API.

## 7. Tests for stage 2 (`tests/Ranvier.Tests/Projections.fs`, shared with the Fable run)

1. **Set law**, random passes with the `Lcg`/`shuffle`/`perturb` generators (lines 17-48) and two readers read at
   random intervals: `set PreviousKeys − Removed + Added = set Keys`; `Added ∉ PreviousKeys`, `Removed ∉ Keys`,
   `Replaced` in both; `Positional` replays `PreviousKeys` into `Keys` (`replay`, line 50).
2. **Replaced:** a key removed in one pass and re-added in a later one reads as `Replaced`; its old factory scope ran
   its cleanup.
3. **Churn:** added then removed between reads is absent from `Changes`.
4. **Throwing cleanup:** a factory row whose cleanup raises partway through `ApplyDiff`; the next successful `Read`
   satisfies law 1 against `Keys`.
5. **Pending pass:** `Read` raises `NotReadyException` and returns the accumulated changes after the source settles;
   the reading effect runs once per settle.
6. **Cap:** more than `max(64, N)` changes unread gives `IsReset`, empty `Changes`, current `Keys`.
7. **Lifetime:** projection `Dispose` gives `IsReset` with empty `Keys`; disposing the last reader, or its owner,
   returns `log` to null (internal assertion).
8. **Both add sites:** law 1 over `groupBy` groups and `createIndexProjection`.
9. **C#:** `tests/Ranvier.CSharp.Tests/CollectionTests.cs` switches on `KeyChange` and iterates `Changes` as
   `KeyValuePair`.

## 8. Fable, AOT and trimming

- `KeyMap`/`KeySet` for the accumulators (`Platform.fs:572`, `696`); `None` and `()` keys work, JS `Map` path.
- `KeyValuePair` already crosses Fable in `RowSnapshot` (`Projections.fs:161-162`). An F# enum compiles to a number.
- `ProjectionDelta` and `ProjectionReader` take `[<AttachMembers>]` under Fable, as `RowSnapshot` does (157).
- No interface type tests, weak references or finalizers. Reader cleanup rides `IOwned`.
- AOT/trim: no reflection, no runtime codegen, no `%A` on the hot path; generic code over `'K` with the default
  comparer that `KeyMap` already uses. Safe as written.

## 9. Breaking?

No. Stage 2 adds `KeyChange`, `ProjectionDelta<'K>`, `ProjectionReader<'K>`, `Projection.NewKeyReader`, and makes
`PositionalChange<'K>` public. Nothing in `docs/.ai/public-api-baseline.txt` changes; the baseline gains lines.
`IProjectionPass` (listed on `Grouping` at baseline line 283) is untouched until stage 4.

## 10. Stages 3 and 4, re-scoped

- **Stage 3, value readers.** Reuse `FoldRow`'s observer pattern for taps; thread the row memo's previous-value argument
  into `RunRow` (`Projections.fs:1380` discards it today) and log `Changed` there by comparing with it. Add the stamp `ISource` of design §5 here. Rebuild
  `AsObservableCollection` on `NewReader`, which turns its O(N) per run into O(c). Move `foldGroup`'s `Diff`
  (`Combinators.fs:676`) onto a key reader.
- **Stage 4, `ApplyDelta`.** Unchanged from the design, starting with `MapView`; add `SliceView` to the list.

## 11. Recommendation

**Do** stage 2 as reduced here: three hooks plus reader registration, no new signal, zero cost without a reader. Stages 3 and 4 **later**, each
reviewed with its own benchmark results.

## 12. Questions for the maintainer

1. Per-reader accumulator instead of the shared chain for stage 2? (yes/no)
2. `KeyChange` as a CLR enum rather than a union? (yes/no)
3. Reader type `ProjectionReader<'K>`, without `'V`? (yes/no)


## Reviewer corrections (applied to the body above)

Verdict: needs fixes

- Severity: minor. The two add sites, the Retire/publishKeys/beacon lines, 437, 611/630/651, 931-952, 948-949, 1130/1136, 1377, Combinators 345-351/489-527/675-718, the tests/Ranvier.Tests/Projections.fs:1097 Move(3,4), and the generator lines 17-50 all check out.
- '`Positional.diff` returns `ResizeArray` (`Positional.fs:30`)': the function is at Positional.fs:79. Line 30 is inside the Fenwick-tree helper `sumBelow`.
- '`AsObservableCollection` ... never raises `Reset` after the first population' is wrong. On an exception partway through the edits, the `with _ ->` arm sets `populated.Value <- false` (Projections.fs:1257-1260), so the next run calls `view.Clear ()` (1238), which raises Reset. Correct: '...raises Reset only on the first population and after a run that threw partway'.
- '`RunRow` sees the previous value in `entry.Row.Peek` | still true, and the compute now also receives it' and §10 'log `Changed` in `RunRow` by comparing with the compute's previous-value argument (`Projections.fs:1380`)': `Compute` is `fun _ -> this.RunRow entry`, which discards the argument, and `RunRow(entry)` takes none. Stage 3 has to thread the argument into RunRow. Say 'the row memo receives it; RunRow does not'.

## Implementation record (stage 2)

Code: `src/Ranvier/Deltas.fs` (`KeyChange`, `ProjectionDelta<'K>`, `ProjectionReader<'K>`, the internal accumulator
`KeyChanges<'K>` and `KeyLog<'K>`), hooks in `src/Ranvier/Projections.fs` (`log` field, `AddEntry`, `Retire`, `Dispose`,
`NewKeyReader`) and `Grouping.CreateAdded` in `src/Ranvier/Combinators.fs`. `PositionalChange<'K>` is public; the
`Positional` module stays internal. Tests: `deltaReaderTests` in `tests/Ranvier.Tests/Projections.fs` (also in the Fable
run) and `KeyReaderReportsMembershipChanges` in `tests/Ranvier.CSharp.Tests/CollectionTests.cs`. User docs: "Reading
changes" in `docs/content/guide/collections.fsx`, and the Collections section of `docs/content/guide/csharp.md`.

Deviations from §4 to §8:

- **Accumulator shape.** A reader's changes are a `KeyChanges<'K>`: a `ResizeArray<KeyValuePair<'K, KeyChange>>` plus
  a `KeyMap<'K, int>` of one-past-index slots, rather than a bare `KeyMap<'K, KeyChange>`. The list is `Changes`
  itself, so the handover stays O(1) and `Changes` needs no copy; a dropped key (`Added` then `Removed`) is a swap
  with the last pair. The slot map also keeps `Added = 0` distinct from an absent key, which a `KeyMap` of the enum
  cannot on .NET (default 0) or Fable (null).
- **Recording starts at the first read.** A new reader is flagged for reset, and a flagged reader records nothing, so
  changes between `NewKeyReader` and the first `Read` are never stored.
- **`PreviousKeys` on a reset** is the keys of the reader's previous read (empty on the first read), so `Positional`
  replays on a reset too.
- **`OrderChanged`** is the reference test of §4.1. It is also true when the order moved and moved back between two
  reads (a new array with equal content); `Positional` is then empty. Documented on the member.
- **`IsEmpty`** is `not IsReset && Changes.Count = 0 && not OrderChanged`.
- **Registration order.** `NewKeyReader` registers the reader with the log, then attaches it to
  `graph.CurrentOwner`, so an owner that is already disposed releases (and unregisters) the reader at once.
  A disposed projection hands back an unregistered reader whose first read is a reset with empty `Keys` (tagged
  FOR-REVIEW: `AsObservableCollection` raises `ObjectDisposedException` instead).
- **Projection `Dispose`** flags every reader for reset and leaves them registered; the reader's own `Dispose` (or its
  owner's) unregisters it, and the last one sets `log` back to null. `HasKeyReaders` (internal) exposes that for test 7.
- **`Dispose` under Fable.** With `[<AttachMembers>]`, an explicit `Dispose` member collides with
  `IDisposable.Dispose`, so the explicit member is compiled on .NET only; under Fable the interface member is the
  attached `Dispose`. For the same reason the accumulator's own count is `Size`, not `Count`.
- **Test 4 (throwing cleanup).** An owner swallows a cleanup's exception into `Owner.Errors`, so a throwing cleanup
  does not fail the pass. The test still checks that both removals and the addition are recorded and that law 1 holds.
- **Benchmarks.** `ProjectionBenchmarks.ChurnOneKey` (8/64/512, no reader) uses its own `Churn` fixture through a
  targeted `[<GlobalSetup(Target = "ChurnOneKey")>]`, so `EditOneItem`/`Reorder` keep their setup, and the file uses no
  new API and compiles against master for the A/B. `bench/Ranvier.Benchmarks/DeltaReaders.fs` holds
  `DeltaReaderBenchmarks` (N = 64/512/10 000): `ReadAfterOneRemoval` against the baseline `SetDiffAfterOneRemoval`,
  both toggling key N/2 out and back in on alternate invocations (one membership change per write), and `ReadIdle`;
  and `DeltaReaderChurnBenchmarks` (`ChurnOneKey`, Readers 1/4 by Items 8/64/512), each reader read by its own effect.
- **Sanity run only** (fsi Stopwatch loop, not BenchmarkDotNet, noisy machine): `ReadIdle` allocates 0 B; a
  non-empty read allocates about 400 B per reader (a fresh accumulator, tagged FOR-REVIEW); at N = 10 000 the reader
  path was about half the set-diff path, which is dominated by the O(N) pass common to both. The A/B gate is still
  owed.
