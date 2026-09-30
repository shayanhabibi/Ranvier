# MVU bridge, per-field stores and writable derived values: design

**Status.** implemented on worktree-wf_c46b5816-3af-8, pending benchmark gate (research item B8,
`RESEARCH-ecosystem-pain-points.md` §13). §10 records how the implementation departs from this note. Code references
name the member rather than a line, because the implementation moved `Api.fs`. Numbers come from a BenchmarkDotNet `--short` run (ShortRun, 3 iterations) in a
throwaway worktree on this container. Compare them as ratios, never as absolutes.

## 1. Goal

Three features from §13, in the recommended order:

- **(c) Writable derived value.** A value seeded from upstream that can be edited locally. When upstream changes, the
  edit is either reset (Solid 2.0 writable memo) or kept until the caller resets it (FDA#114, a form draft).
- **(a) MVU bridge.** An Elmish-style `update` over a model held in a signal, read through selector memos with an
  equality cutoff, so Elmish, Fabulous or FuncUI code can adopt Ranvier one view at a time.
- **(b) Per-field store without codegen.** A record whose fields are individually reactive, in the spirit of Clef's
  `Store<'T>` and Solid's `createStore`.

Owners replace the rules of hooks (FuncUI#212): a node belongs to the scope that was current when it was created,
not to a call position. The docs for (a) state this.

## 2. What already exists

- **Previous-value computes are implemented.** `createMemo` takes `'T voption -> 'T` (`Api.createMemo`), and `Memo.Run`
  passes the last published value (`Core.fs`, `Memo.Run`). (c) builds on it.
- **Focused reads are the selector half of (a).** `createMemo (fun _ -> store.Value.Owner)` over a root signal,
  `Signal.update`, `List.updateBy` / `Array.updateBy` and `createOptionMemo`
  (all in `Api.fs`) are all shipped and documented in `guide/collections.fsx` §Deep updates (lines 372–515). A write
  re-runs the memos on the path, their direct children and the root's readers. Only readers whose value changed wake.
- **Keyed selection.** `createSelector` and `createLookup` (`Api.fs`) cover "which row is
  selected". `Combinators.fs` provides projection views only (`Projection.map`/`filter`/…, `Combinators.fs:848–1148`).
  There is no general `select` combinator over a record.
- **Cutoff.** `Signal.Value <- v` compares under the graph's policy before notifying (`Core.fs:2018–2035`). `Memo.Run`
  compares `previous` with `value` and notifies only when the value moved (`Core.fs:2386–2403`). The default
  `JsIdentityPolicy` compares value types and strings by value and records by reference (`Types.fs:197–236`).
- **Off-thread entry.** `Graph.Dispatch(Action)` (`Core.fs:1441`) runs the work inline on the graph thread and
  posts it to the inbox otherwise.
- **Writes from effects.** A write inside an effect joins the running flush (`contracts.md:46`).
- `guide/index.md:78` lists lenses and prisms as not implemented.
- The signal-maps specs in `superpowers/specs/` cover docs visualisation and are unrelated to B8.

## 3. (c) Writable derived value

### 3.1 API

```fsharp
type Writable<'T> =                    // sealed, owned by the current scope
    member Value : 'T with get, set    // tracked read; set = local edit
    member Peek : 'T
    member TryValue : Reading<'T>
    member Status : Status
    member IsEdited : bool             // tracked
    member Upstream : 'T               // tracked; the seed's current value
    member Reset : unit -> unit        // drops the edit

val createWritable : seed: ('T voption -> 'T) -> Writable<'T>   // an upstream change resets the edit
val createDraft    : seed: ('T voption -> 'T) -> Writable<'T>   // the edit survives until Reset
```

C#: `Reactive.Writable(Func<T>)` and `Reactive.Draft(Func<T>)`. `ReactiveBindings.Writable` already exists as a
signal-backed property (`Bindings.fs:422`), so a `Writable<T>` type name sits beside it. §9 Q1 covers this.

### 3.2 How it works

It is a composition of existing nodes, with no change to `Core.fs`:

1. `seed` is a memo of `struct (version, value)`. The version increments when the seed publishes a value unequal to
   the previous one under the graph's comparer. It reuses the previous tuple otherwise, so the memo's cutoff holds.
2. `edit` is a `Signal<struct (int * 'T) voption>` that records the seed version current at the time of the edit.
3. `value` is a memo. `createWritable` returns the edit while its version equals the seed's version, and the seed
   otherwise. `createDraft` returns the edit whenever one exists.

The edit is a version stamp, not a comparison with the seed value, so upstream A → B → A resets the edit when B was
published. Writes that collapse into one run (a batch, or an unobserved seed) and end at an equal value are not a
change, which matches the "one step per run" rule of previous-value computes. The setter reads the seed untracked.
While the seed is pending, the edit records the last settled version. When the seed settles to a new value,
`createWritable` drops that edit. A stale edit stays in the signal until the next edit or `Reset`.

### 3.3 Cost

| Case | Mean |
| --- | --- |
| `PlainSignalWrite` (baseline: one signal, one effect) | 52 ns |
| `LocalEdit` (edit, one effect reading the writable) | 91 ns |
| `UpstreamChange` (source → seed → writable → effect) | 121 ns |

The error bars are wide (±100 ns half-interval at 3 iterations). A full run is needed before anyone quotes these.

- **Allocations.** Measured 0 B per edit for `int` on .NET: the struct tuple sits inside the `voption` struct.
  Under Fable, a struct tuple is a JS array and `ValueSome` goes through the `some()` helper, so each edit allocates.
- **Per instance.** One `Signal` and two `Memo`s, plus the wrapper. There is no byte figure because none was measured:
  a construction case in `Lifetimes.fs` would settle it.
- **Code that does not use it.** It pays nothing, because no existing type changes.
- **A core node** (a memo with a setter that writes `value` and notifies like `Signal.Value`, `Core.fs:2018`) would
  save one memo and the version tuple. It would also add a second writer to `Memo`'s state machine (freshness,
  pending, the scope discharge). Do it only if the composition benchmarks show the extra memo matters.

## 4. (a) MVU bridge

### 4.1 API

```fsharp
type Mvu<'Model, 'Msg> =
    member Model : 'Model                    // tracked read of the root
    member Dispatch : 'Msg -> unit
    member Select : ('Model -> 'A) -> Memo<'A>

module Mvu =
    val create  : init: 'Model -> update: ('Msg -> 'Model -> 'Model) -> Mvu<'Model, 'Msg>
    val withCmd : init: ('Model * Cmd<'Msg>) -> update: ('Msg -> 'Model -> 'Model * Cmd<'Msg>) -> Mvu<'Model, 'Msg>

type Cmd<'Msg> = (('Msg -> unit) -> unit) list   // abbreviation; the same shape as Elmish 4's Cmd
```

### 4.2 How it works

- The root is a private `Signal<'Model>`. `Dispatch msg` = `Signal.update root (update msg)`, so the root's cutoff
  applies. An `update` that returns its argument wakes nothing.
- `Select f` = `createMemo (fun _ -> f root.Value)`. Selectors can be nested (select a sub-model, then fields of that
  memo), which gives the path-limited re-runs shown in `collections.fsx:430`.
- Commands run after the write, untracked, with `Dispatch` as their argument. A dispatch from an effect joins the
  running flush (`contracts.md:46`). An off-thread dispatch goes through `Graph.Dispatch` (`Core.fs:1441`).
- Adoption path: keep `init`/`update` unchanged, move one view to selectors, and keep the rest on the Elmish loop by
  subscribing it to `Model`.

### 4.3 Cost

Each dispatch re-runs every *observed* selector over the root. That is Elmish `lazy`'s cost profile at memo
granularity. Measured with one field changed per write and one effect per field (`FieldWriteBenchmarks`, new):

| N fields | `FieldSignalWrite` (one signal per field) | `SelectorMemoWrite` (root + N memos) | `ModelCopyOnly` |
| --- | --- | --- | --- |
| 8 | 45 ns, 0 B | 529 ns, 56 B | 123 ns, 56 B |
| 64 | 43 ns, 0 B | 3,777 ns, 280 B | 143 ns, 280 B |

Selector fan-out costs about 56 ns per selector. The allocation is the model copy. An unobserved selector does not run
(memos are pull-based: `Memo.EnsureCurrent` and `Memo.Pull` run the body only on a read). Nested selectors cut the re-run set to the path plus its siblings. The bridge
adds one closure per `Select` and one list walk per command, and nothing on the write or flush path of other code.

## 5. (b) Per-field store without codegen

Clef's `Store<'T>` (fetched from the draft spec) exposes `create`, `state`, `setState : ('T -> 'T) -> unit` and
`subscribe`. The *compiler* desugars each record field to a `Signal`, and `setState` invalidates only the changed
fields. Nested records and collections are unspecified, and the spec defines no writable derived value.

F# gives three ways to do this without Clef's compiler:

1. **Reflection** (`FSharpValue.GetRecordFields`, one `Signal<obj>` per field). Boxes every field, needs trimming
   annotations and is not AOT-safe. Rejected.
2. **A focus value** (`Store.field get set` returning a readable/writable view over a root signal). This is a lens.
   Reads cost what (a) costs, O(selectors) per write, and every write copies the record. It overlaps (a) and the
   deferred lens item (`guide/index.md:78`).
3. **A record of signals, written by hand**: `type Form = { Name: Signal<string>; Age: Signal<int> }`. Each write
   costs one signal write, flat in N (the 43–45 ns column above). Needs no library code. A snapshot or a whole-record
   set is a hand-written function per type, and that function is what codegen would produce.

Option 3 already works and is the fastest. The only library addition worth considering is a guide section that shows
it, including `batch` for multi-field sets and (c)'s `createDraft` per field for forms.

## 6. Fable, AOT and trimming

All three are compositions of `Signal`, `Memo` and `Graph.Dispatch`. There is no reflection, no `Emit` and no
`#if`, so they are AOT and trim safe and compile under Fable. Fable-specific costs: struct tuples and `ValueSome` in
(c) allocate per edit, and the `Cmd` list in (a) is an F# list either way. The Fable test suite should run (c)'s
version-stamp cases, because under Fable's default policy struct tuples compare by reference, so the seed must reuse the
same stamp object for the cutoff to hold.

## 7. Breaking changes

None. The public API baseline (`public-api-baseline.txt`) gains `Editable'1`, `Api.createEditable`,
`Api.createDraft`, the `Mvu'2` type and `MvuModule`, and `Reactive.Editable`/`Draft` in `Ranvier.CSharp`. Existing
entries are unchanged.

## 8. Alternatives considered

- **Writable as a `Memo` setter.** Every memo pays a field and a branch for a feature few use. Rejected.
- **Reset by comparing values instead of versions.** A → B → A would keep a stale edit. Rejected.
- **An MVU bridge that depends on Elmish.** A package reference just to name `Cmd`. The structural abbreviation
  interoperates without it. Fabulous's own `Cmd` shape needs checking before the docs claim compatibility with it.
- **Adaptify-style codegen for (b).** Out of scope: §13 records its build breakage (#40, #30).

## 9. Recommendation and order

1. **(c) Do.** It is small, has no cost for non-users, closes FDA#114, and the forms case in (b) uses it.
   Tests: A → B → A resets the edit, a batch that ends equal keeps it, an edit while the seed is pending, `Reset`,
   `IsEdited` wakes, and the same cases under Fable.
2. **(a) Do, after (c).** A thin module plus a guide page on migrating from Elmish, with the fan-out table above
   and the nested-selector advice. Before quoting its numbers, confirm them with a full (non-short) run of
   `FieldWriteBenchmarks` (N = 8, 64, 256).
3. **(b) Don't ship a type.** Document the record-of-signals pattern. Revisit when lenses are taken up.

### Questions for the maintainer

1. Name the (c) type `Writable<'T>` despite `ReactiveBindings.Writable` (yes / rename)?
2. Should the MVU bridge take the Elmish-shaped `Cmd` abbreviation (yes / no)?
3. Keep (b) to docs only (yes / no)?

Answers (wave-b `decisions.md`): 1. renamed, to `Editable<'T>`; 2. no, plain command functions and no Elmish
dependency; 3. yes.

## 10. As implemented

Deviations from §3–§5, each tagged `FOR-REVIEW` at its site:

- **Names.** The type is `Editable<'T>`, made by `createEditable` and `createDraft` (C#: `Reactive.Editable`,
  `Reactive.Draft`). `Mvu.withCmd` is `Mvu.withCommands`, and there is no `Cmd` abbreviation: `withCommands` takes
  `'Model * (('Msg -> unit) -> unit) list`, which is Elmish's `Cmd<'Msg>` after abbreviation expansion.
- **Seed stamp.** The seed memo holds an internal sealed `Stamp<'T>` (version and value), not a struct tuple. On .NET
  a struct-tuple memo compares with `EqualityComparer.Default`, which deep-compares a record `'T` on every equal
  re-run; under Fable it compares by reference. The class compares by reference on both targets, and the seed reuses
  the previous stamp when the value is equal under the graph's comparer. Cost: one allocation per unequal seed value.
- **Edit comparer.** The edit signal is `Signal<struct (int * 'T) voption>` built with an internal comparer (versions
  equal and values equal under the graph's comparer for `'T`), so an equal edit wakes nothing on both targets.
- **Setter.** The setter pulls the seed (`ISource.UpdateIfNecessary`, untracked, no closure) and stamps the edit with the
  version of the seed's last published value, kept in a field the seed body writes. Before the first publication that
  version is 0, so `createEditable` drops an edit made before the seed first settles.
- **Pending and failed seeds.** While the seed is pending an edit made against its last settled version stays in force;
  when the seed fails, an editable reads the failure and a draft with an edit reads the edit.
- **Surface additions.** `Editable` also has `Dispose ()` (disposes both memos). `Mvu` exposes `Model`, `Dispatch` and
  `Select` only. `Dispatch` runs `update` and the commands untracked (a closure only when called inside a computation)
  and posts to the graph's inbox when called off the graph's thread.
- **Allocation.** A dry BenchmarkDotNet run measured `LocalEdit` at 0 B and `UpstreamChange` at 24 B (the new
  stamp). The allocation test in `tests/Ranvier.Tests/Editables.fs` asserts only that an edit read by one effect
  allocates no more than a write through a two-memo chain, because a script-hosted measurement showed memo runs
  allocating there.
- **Benchmarks.** `bench/Ranvier.Benchmarks/Models.fs`: `FieldWriteBenchmarks` (`FieldSignalWrite`, `SelectorMemoWrite`,
  `MvuDispatch`, `ModelCopyOnly`; N = 8, 64, 256) and `EditableBenchmarks` (`PlainSignalWrite`, `LocalEdit`,
  `UpstreamChange`). Only a dry run was made; the numbers in §3.3 and §4.3 still need the full run.
- **Docs.** `guide/forms.md` (editable values, the record-of-signals pattern for (b)) and `guide/elmish.md` (migrating
  from Elmish). The cost table on the Elmish page gives shapes, not numbers, until the full run.


## 11. Package split

`Mvu` moved out of `Ranvier` into its own project and package, `src/Ranvier.Elmish` (maintainer decision). `Editable`,
`createEditable` and `createDraft` stay in `Ranvier`.

- **Names.** The package, assembly and namespace are `Ranvier.Elmish`; the type stays `Mvu<'Model,'Msg>` and the module
  `Mvu`. A type or module named `Elmish`, or one reusing Elmish's `Program` or `Cmd`, would clash with the Elmish
  package's namespace and types in a file that opens both `Elmish` and `Ranvier.Elmish`. `Mvu` clashes with neither.
- **Dependencies.** `Ranvier.Elmish` depends on `Ranvier` only, not on the Elmish package: a command is still
  `('Msg -> unit) -> unit`. Its traced build packs as `Ranvier.Elmish.Traced` and depends on `Ranvier.Traced`. The
  untraced build is reflection-free and AOT-compatible, and `tests/Ranvier.AotSmoke` roots it.
- **Public API only.** The bridge no longer reaches `Ranvier` internals. `Dispatch` calls `Graph.Untrack` on the graph's
  thread and `Graph.Dispatch` elsewhere, so it allocates one closure per on-thread dispatch where the core version
  allocated one only inside a computation. `Select` uses the public `Memo` constructor on .NET and `createMemo` under
  Fable, where that constructor does not exist; under Fable the memo belongs to `Graph.Current`. Both are tagged
  `FOR-REVIEW` at their sites. `Ranvier.Benchmarks` builds its bridge with `Mvu.create` under `Graph.Activate`
  instead of the internal constructor.
- **Tests.** `tests/Ranvier.Tests/MvuBridge.fs` stays in the shared suite and opens `Ranvier.Elmish`; `Ranvier.Tests`
  and `fable/Ranvier.Tests.Fable` reference the project.
- **Public API baseline.** `tools/verify-trace.fsx` compares only the packed `Ranvier.dll` with
  `docs/.ai/public-api-baseline.txt`, which never listed `Mvu`, so the move removes nothing from it.

## Reviewer corrections (applied)

Verdict: needs fixes

- Severity: minor-to-moderate. Api.fs line references are off by about 20 lines (src unchanged between c631f23 and HEAD): '`createMemo` takes `'T voption -> 'T` (`Api.fs:88`)' → Api.fs:93; '`Signal.update` (`Api.fs:399`)' → 423; '`List.updateBy` / `Array.updateBy` (`Api.fs:425`, `:470`)' → 444, 491; '`createOptionMemo` (`Api.fs:372`)' → 391; '`createSelector` (`Api.fs:353`)' → 372; '`createLookup` (`Api.fs:340`)' → 359.
- §6 'the Fable test suite should run (c)'s version-stamp cases, because Fable compares struct tuples structurally through its own helpers': under the default `JsIdentityPolicy`, Fable resolves every type to `ReferenceComparer`, and Types.fs:193-195 says struct tuples compare by reference there. Structural comparison applies only under `StructuralPolicy`. The version-stamp design still works under reference comparison because it reuses the previous tuple object, but the stated reason is wrong. Correct: 'because under Fable's default policy struct tuples compare by reference, so the seed must reuse the same tuple object for the cutoff to hold'.
- 'unobserved selector does not run (memos are pull-based: `Memo.EnsureCurrent` and `Memo.Pull` run the body only on a read)': 2420 is inside `ResolveCheck`'s remarks and does not show laziness. Cite `Memo.EnsureCurrent`/`Pull` instead.
- '`designs/previous-value-computes.md` is shipped': the design file carries no shipped status. The code confirms the feature (Core.fs:2356; Api.fs:93), so cite the code rather than the design's status.
