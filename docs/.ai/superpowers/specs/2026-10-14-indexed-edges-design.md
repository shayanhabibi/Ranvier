# Indexed edges (compile-time variant) — design

Status: **deferred**, 2026-10-14. Template performance in the view proof of concept is satisfactory without this
work: mount 1.12-1.18× Solid 2, updates at parity, retained heap 0.98×. The remaining dispose gap at scale
(1.25-1.56×) sits in core edge removal, and this document is the research for picking it up later. Two independent
reviews found correctness problems in the draft below and argued for a different delivery. Read Review findings and
Revised direction first; the earlier sections are kept as the reviewed draft.

## Review findings

Two reviewers read this draft against the code at `e2e0f95`. Both confirmed that the description of the current
design is accurate. Their findings, most severe first:

1. **The last-linker shortcut loses edges.** A reader links A, a later run skips A (`trimFrom` removes the edge), and
   a run after that reads A again. The record still names the reader, so the read counts as a repeat, the slot stores
   `-1`, and no edge is linked: the reader stops updating on A. The nested-run form, where an inner memo overwrites the
   record and the record is later restored, is not covered by the planned tests. Two fixes were proposed:
   - **Handle-only record:** the source keeps the last handle only. A hit requires `handle < count` and the observer at
     that handle to be the adding reader. The check validates itself after any removal or swap, and the record holds
     no reference.
   - **Run-stamped record:** the record carries the reader and its run number, and a hit requires both to match.
2. **Removal is no longer idempotent.** Today, removing an absent observer does nothing. Two places rely on that:
   `Combinators.fs:894` removes every row on dispose, and `trimFrom` with `from > 0` can remove a source twice when
   it repeats in the dropped suffix. With bare positions, a second removal unlinks whichever observer now sits at the
   handle. Fix:
   - Make `RemoveObserverAt` take the expected observer and check its identity in every build, which costs one compare.
   - Have every remover write `-1` back into its handle.
   - Make `-1` a no-op.

   With these, drift fails loudly instead of corrupting the graph.
3. **The projection summary watch cannot route `MovedIn`.** One `RowWatch` observes N row memos
   (`Projections.fs:522, 601`), and `MovedIn(slot, position)` carries no source identity. Two options were proposed:
   - A forwarding `IComputation` per pending row, as `ValueRow` and `FoldRow` already have.
   - The source passed in `MovedIn`. That needs a source-to-entry map, which is a hash.

   The per-row forwarder is preferred.
4. **The last-linker record retains garbage.** A strong record keeps a disposed reader, its closure and its DOM
   reachable from a long-lived source. That is the same failure that made tombstoning lose on V8. Fix: the handle-only
   record from finding 1, or null the record on removal.
5. **Mixed edges in stage 2.** A transitional hash for readers that link directly puts two kinds of edge in one
   `ObserverSet`, and stage 3 would then measure that mix as if it were the final design. Fix: migrate the direct
   linkers before measuring, or measure a throwaway prototype.
6. **The reader-side fallback must be lazy.** A reader-side hash maintained on every append recreates the cost being
   removed. Build it on the first positional miss in a run, and drop it at `EndRun`.
7. **Fable parallel arrays.** `Platform.itemAt` is unchecked, so a length mismatch reads `undefined`, which `-1` tests
   do not catch. Grow and blank both arrays in one helper. Test what `Array.zeroCreate<int>` fills with under Fable.
8. **Small fan-out.** At 8 or fewer observers, today's scan stays within cache. The variant adds an interface call per
   swap: `DisposeInCreationOrder` swaps on every removal, while `DisposeRoot` never swaps. Measure at 8.
9. **Out-of-order wide readers** move from an O(1) hash to an O(n) scan on add.

Neither review found a re-entrancy fault. Swap-remove is unchanged, and `MovedIn` is a single integer write that is
safe mid-walk.

### On delivery

Both reviews argue against publishing the variant as a second package for users to choose:

- API and semantics are identical, so users cannot predict which profile they need, and the choice is global to an
  application.
- Two packages with the same assembly name create diamond conflicts when a library references `Ranvier` and the
  application references `Ranvier.IndexedEdges`.
- If the Fable package ships source, the consumer's defines choose the profile, not the package id. This is
  unverified.
- Gate 1 relates only the default build to master. No gate relates the two builds to each other.
- `#if` would stay in every implementer, and CI would need 4-8 cells.

Their recommendation: use the switch as a development instrument only (a byte-identical default and A/B benchmarks),
then ship one model. A runtime option would put a branch on every link, so both reject it. The doubly linked `Link`
model stays as the fallback if handles lose. Both called the review point toothless, because "kept if any workload
favours it" guarantees it is kept. The default at review should be removal unless the published numbers justify the
variant.

## Revised direction (when resumed)

1. **First, the hash-free experiment** (about 50 lines, no interface change):
   - Keep today's structures and delete `RefIndex` from `ObserverSet`.
   - Removal scans: the last slot and index 0 are checked first, so teardown in either order is O(1), and only
     scattered removal on a wide source is O(n).
   - Duplicate reads move to the reader: the handle-only check from finding 1 where it applies, and otherwise the lazy
     per-run fallback from finding 6.

   Measure on Fable/V8 with `--trace-gc` and on .NET, interleaved with master:
   - `DisposeRoot`, `DisposeInCreationOrder`, and a new scattered order at 8, 64 and 1024;
   - `RelinkOne` at 8;
   - the out-of-order reader at 64 and 1024;
   - 128-row browser dispose.

   If scattered removal is the only loss and is tolerable, this becomes the single model.
2. **Otherwise, indexed handles with findings 1-8 applied.** That means identity-checked removal and `-1` discipline,
   per-row forwarders for the watch, lockstep arrays, and direct linkers migrated before measuring. If they win on
   Fable, they replace the current model outright. If they lose on Fable, move to the `Link` model.
3. **Offering users a choice of profile, if still wanted.** Use a documented build property for those who build from
   source, not a second published package.

## Summary

Each reactive edge is stored twice: in the source's `ObserverSet` and in the reader's `SourceList`. This design stores
each end's position in the other, so removing an edge is a fixed number of array writes. That retires the hash index
`ObserverSet` keeps above 8 observers.

The design ships as a compile-time variant, `RanvierIndexedEdges=true` (define `RANVIER_INDEXED_EDGES`), next to
`RanvierTrace` and `RanvierCounters`. With the switch off, the build is unchanged, and `tools/verify-trace.fsx` Gate 1
proves it. With the switch on, the same test suites and benchmarks run against the indexed build.

Public API, semantics and the API baseline are the same in both builds. They differ in performance profile only:
where the cost of an edge sits (adding, removing, re-reading), how it scales with fan-out and with the number of
sources a reader reads, memory per edge, and how each behaves on .NET and on Fable/V8. Neither build is assumed to
replace the other. The work produces a measured profile for each, so that users and maintainers can say which one
they prefer. Both builds stay selectable until that preference is settled (see Profiles and choosing).

## Evidence

- A Chromium profile of disposing 128 view rows (Fable build, `poc/fable-ranvier-dom`) spends 117 of 236 ms (49.6%)
  in `Owner.TearDown` → `Effect.Dispose` → `SourceList.trimFrom` → `ObserverSet.Remove`, 55 ms of it self time in
  `refIndexRemove`/`refIndexSet`. Solid 2 spends 61 ms on the same unlinking.
- Linking edges at mount (`SourceList.Add`, `ObserverSet.Add`) takes about 97 ms of a 1774 ms profile, against
  Solid 2's `link` at 13.5 ms.
- Tombstoning the `ObserverSet` while keeping the hash was rejected
  (`docs/.ai/observer-removal-benchmark.md`, PR #54). It made disposal 22-31% faster on .NET and about 30% slower
  under Fable/V8, through extra GC. Only removing the hash removes the cost.

## Goals

- Edge removal costs a fixed number of array operations at any fan-out, on .NET and Fable.
- Edge addition does no hash work.
- The default build is identical to master, with untraced IL equal method by method.
- Both builds pass the same tests, including the observer tests added in PR #54.

## Non-goals

- The doubly-linked `Link`-per-edge model (Solid 2, alien-signals). It rewrites both halves of the edge model and
  allocates an object per edge. The indexed variant keeps arrays and swap-remove.
- Any change to notification order, `Check` resolution, scheduling, ownership or disposal order.
- Any public API.

## The switch

- `Directory.Build.targets` gains a `RanvierIndexedEdges` property. When it is `true`, it adds `RANVIER_INDEXED_EDGES`
  to `DefineConstants`, repo-wide, in the same way as `RanvierTrace`. The default is `false` in every configuration.
- Fable reads the property from the environment, as CI does for `RanvierTrace`
  (`RanvierIndexedEdges: 'true'` on the `dotnet fable` step).
- A build with the switch on packs only under its own package id (`Ranvier.IndexedEdges`), in the way an explicit
  `RanvierTrace=true` packs `Ranvier.Traced`. Assembly name, namespaces and version stay the same, so an application
  chooses a profile by choosing a package. Publishing that package is a separate decision. Until it is made, CI builds
  and tests the variant but does not publish it.
- The switch combines with `RanvierTrace` and `RanvierCounters`. Trace hooks and counter calls keep their sites and
  arguments in both builds.

## Design

### Edge handles

An edge handle is the observer's position in the source's `ObserverSet`. Under the switch:

```fsharp
type internal ISource =
    inherit INode
    /// Links the observer and returns its position in this source's observer list.
    abstract AddObserver: IComputation * slot: int -> int
    /// Unlinks the observer at the given position.
    abstract RemoveObserverAt: position: int -> unit
    abstract UpdateIfNecessary: unit -> unit
```

`slot` is the reader's position for this source in its own `SourceList`, or a fixed value for readers that keep a
single edge to the source. Without the switch, `ISource` keeps its current members. Implementers carry both shapes
under `#if RANVIER_INDEXED_EDGES`, so the default build compiles exactly as today.

### ObserverSet

- Each slot holds the observer and its `slot` (the reader-side position).
- `Add` appends and returns the new position. No membership check (see Duplicate reads).
- `RemoveAt position` swap-removes as today. The observer moved from the end into `position` is told its new
  position through `IComputation`:

  ```fsharp
  abstract MovedIn: slot: int * position: int -> unit
  ```

  It writes `position` into its own slot `slot` for this source. That is one array write on each side.
- `RefIndex`, `IndexThreshold`, `positionOf` and `buildIndex` are compiled out under the switch.
- The notification walk is unchanged: backwards, re-clamped each step. Swap-remove keeps today's guarantees. An
  observer removed mid-walk moves an already-visited observer down, nothing unvisited is skipped, and observers added
  mid-walk are not notified.

### SourceList

- Each slot holds the source and the handle `AddObserver` returned. In `first`/`rest` the handle sits beside the
  source, in the same struct on .NET (see Fable representation).
- Removal in `trimFrom` calls `RemoveObserverAt handle` on the slot that owns the edge.
- `MovedIn (slot, position)` updates the handle at `slot`.
- A source read in more than one slot owns one edge, held by its first slot. Later slots for the same source hold
  `-1`. This matches today's rule that a source held by a kept slot keeps its observer entry. Truncation from a point
  drops the owning slot only together with every later slot, so a `-1` slot never outlives its owner.

### Duplicate reads

Today, `ObserverSet.Add` ignores an observer it already holds. That covers a non-adjacent repeat read (A, B, A), which
`SourceList.Add` appends as a new slot. Under the switch the reader decides instead:

- **Fast path.** Each source records the last observer that linked to it and that observer's handle. If the adding
  reader is that observer, the read is a repeat. The new slot stores `-1`.
- **Fallback.** Otherwise, a scan of this reader's earlier slots (`heldBefore`), which costs O(sources read) and only
  on an append that missed positional matching.

The fallback makes a body that reads many sources out of order cost O(n²) per run. Today that body costs O(n) when its
sources are wide, because of the hash. The prototype measures this case (see Measurement) before the design is
accepted. A generation stamp per run (alien-signals' approach) is the alternative if the fallback shows up.

### Readers that link directly

These keep their handle in a field, or a parallel array for a list of sources, and pass a fixed `slot`:

| Site | Edge kept by |
| --- | --- |
| `Bindings.fs` `Binding` | one field |
| `Combinators.fs:535-550` reader with `read`/`order` | parallel handle list beside `order` |
| `Combinators.fs:740, 796, 894` row edges | field on the row |
| `Projections.fs:522-525, 601` summary watch | field on the row entry |
| `ValueReaders.fs:67, 93` row edges | field on the row |

Sources that forward to an inner `ObserverSet` (`Core.fs` signal, memo, async kinds, `Projections.fs:93, 1812`,
`Combinators.fs:908`, `Timed.fs:270`, `ValueReaders.fs:187`) pass handles through unchanged.

### Fable representation

`ObserverSlot` and `SourceSlot` are structs on .NET because an array of an interface type type-checks every store.
Under Fable a struct is an object, so every `Add` already allocates one: `ObserverSlot_$ctor` is about 3% of a
teardown-heavy profile. With two fields per slot, the variant stores parallel arrays under Fable (`observers` and
`slots`, `sources` and `handles`) and keeps the struct on .NET. Both representations sit behind the same members.

### Tracing and counters

- `Tracer.ObserverAdd`/`ObserverRemove`/`EdgeAdd`/`EdgesRemove` keep their call sites. The observer passed to
  `ObserverRemove` is read from the slot before removal.
- `ITracedEdges.Ids` iterates the live slots in both builds.
- `Counters.ObserverInserted`/`ObserverRemoved`/`EdgeAdded`/`EdgeRemoved` count the same events.
- The trace discipline holds: hooks stay single statements outside `#if RANVIER_TRACE`. Blocks under
  `#if RANVIER_INDEXED_EDGES` are outside the lint's rules but follow the same declarations-or-statements style as
  the surrounding code.

## Verification

- **Default build unchanged.** `dotnet fsi tools/verify-trace.fsx`, run in an elevated shell, passes every gate. Gate 1
  reports untraced IL equal to the merge-base across all methods.
- **Same tests, both builds.** CI gains a job running `dotnet test tests/Ranvier.Tests -c Release
  -p:RanvierIndexedEdges=true` (and traced), plus the Fable test build with the switch on. The observer tests from
  PR #54 cover removal and compaction during notification, observers added mid-walk, exact `Count`, re-adding after
  removal on a wide source and wide-source retention. They must pass in both builds.
- **New tests.** The tests below run in both builds and pass in the default build first:
  - A, B, A reads link one edge.
  - Truncation through a repeated source keeps the edge only while a slot still holds it.
  - A swap during a notification walk that removes the walking observer.
  - Each direct-linking reader disposed in reverse and in creation order.
- **Counters.** `counters.ps1` with the switch on shows the same library counters and fewer instructions on add and
  remove.

## Measurement

The `ObserverRemovalBenchmarks` and `ObserverChurnBenchmarks` from PR #54 run against both builds, on .NET with
BenchmarkDotNet, and under Fable/V8 with master and the variant interleaved in one process. Cases:

- `DisposeRoot` and `DisposeInCreationOrder` at 8, 64 and 1024 observers.
- `RelinkAll` and `RelinkOne`.
- `WriteWide`.
- A new case: one reader reading N sources out of order, at N = 8, 64 and 1024. This measures the duplicate-read
  fallback.
- The browser framework comparison on `poc/fable-ranvier-dom`, with mount, update and dispose at 1, 16 and 128 rows,
  built with the switch on and off.

## Profiles and choosing

Neither build is a default to be retired. The deliverable is a profile of each, measured on both runtimes and
published where users choose a package.

- **Profile page.** `docs/content/benchmarks/` gains an "Edge models" page. It sets out the two builds side by side,
  with every case above on .NET and Fable/V8. Each table states its date, commit and machine, following the
  benchmark pages' conventions. It also states memory per edge for each build.
- **Plain-language trade-offs.** The page says which workloads favour each build, in terms a user can match to their
  application. Examples:
  - wide sources with frequent disposal, such as long lists sharing a signal;
  - narrow graphs with frequent re-reads;
  - readers of many sources that re-read out of order;
  - .NET compared with browser targets.
- **Choosing.** An application picks a build by referencing `Ranvier` or `Ranvier.IndexedEdges`. A repo build picks
  one with `-p:RanvierIndexedEdges=true`.

The variant is kept only if it has a workload where it is clearly better. Each of these, measured on either runtime,
is a reason:

- `DisposeRoot` or `DisposeInCreationOrder` is faster at 64 or 1024.
- The browser comparison shows dispose at 128 rows faster.

Where it is worse than the default build, the profile page says so with the numbers. Those cases also need to be
taken into account:

- any case at 8 observers;
- `RelinkAll`, `RelinkOne` or `WriteWide`;
- the out-of-order read case;
- mount or update in the browser comparison.

If the variant is better nowhere, it is removed and its numbers are recorded in `docs/.ai/observer-removal-benchmark.md`.

Whether one build later becomes the only build is a separate decision. It is made once users have stated a preference.
Retiring the switch is out of scope for this design.

## Plan of work

1. Switch plumbing and pack refusal. Prove that the default build is unchanged.
2. `ObserverSet` and `SourceList` under the switch, with the duplicate-read rule. During this stage, readers that link
   directly use a transitional `AddObserver` overload that keeps the hash for their edges only.
3. Measure on both runtimes. Stop here if the variant is better in no workload (see Profiles and choosing).
4. Migrate the readers that link directly, then delete the transitional hash path under the switch.
5. Full gates, CI jobs for both builds, package-id plumbing, benchmarks, and the "Edge models" profile page. The
   write-up in `docs/.ai/observer-removal-benchmark.md` is updated.

## Risks

- **Two shapes of `ISource` in one codebase, possibly long-term.** Every implementer carries
  `#if RANVIER_INDEXED_EDGES`, and both builds are maintained for as long as both profiles are offered. The CI jobs
  for both builds keep them compiling and passing the same tests. New `ISource` implementers must handle both shapes,
  and the contributor notes say so.
- **Handle drift.** A missed `MovedIn` leaves a stale handle, and the next removal unlinks the wrong observer. Debug
  builds assert that the observer at a handle is the expected one, before `RemoveObserverAt`.
- **Duplicate-read fallback cost.** This is measured before acceptance (see Duplicate reads).
- **Fable parallel arrays.** Their growth and blanking must keep today's retention behaviour. The wide-source
  retention test from PR #54 covers it on .NET only, so the variant adds a Fable heap check.

## Open questions

- Fast path for duplicate reads: a last-linker record per source, or a generation stamp per run.
- Whether `MovedIn` belongs on `IComputation` or on a narrower internal interface. A narrower interface needs either
  an extra field or a type test, which Fable does not support for interfaces.
