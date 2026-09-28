(**
---
title: Collections
order: 5
---
*)
(**
:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

How to derive a keyed collection whose rows are separately observable, and a pointwise lookup over
an open key domain. For the node types rows are built from, see [Getting started](getting-started.md);
for pending rows, the pending channel is described in [Async and pending](async-and-pending.md).

The examples on this page run inside an active graph, as in [Getting started](getting-started.md).

## Choosing a form

| Creator | Keyed by | Row value | Per-key nodes |
|---------|----------|-----------|---------------|
| `createProjection keyOf map source` | `keyOf item` | `map item`, re-run when the key's item or a value `map` read changes | none |
| `createProjectionWith keyOf factory source` | `keyOf item` | the reader returned by `factory`; `factory` runs once per key, untracked | created in `factory`, disposed when the key is removed |
| `createIndexProjection map source` | position | as `createProjection`; the row at a slot survives its item changing | none |
| `createIndexProjectionWith factory source` | position | as `createProjectionWith`; the factory runs once per slot | created in `factory`, disposed when the slot is removed |
| `createLookup f affected source` | any key read through `Get` | `f state key`, recomputed for the keys `affected` names | none |
| `createSelector source` | any key read through `Get` | `true` for the selected key, `false` otherwise | none |

The index forms are Solid's `indexArray`. A projection is a key set plus one row per key, and each
of those is observed separately. A lookup has no key set: it holds a cell for each key that has been read.

## Reading a projection

The source is a function returning a sequence, usually a signal's value.
*)
(*** hide ***)
// #load-ed: the page type-checks against the current sources, and the built assembly stays unlocked. Keep this list
// in the order of the <Compile> items in Ranvier.fsproj.
#r "nuget: Fable.Core, 5.3.0"
#load "../../../src/Ranvier/Types.fs"
#load "../../../src/Ranvier/PlatformDispatcher.fs"
#load "../../../src/Ranvier/Platform.fs"
#load "../../../src/Ranvier/Core.fs"
#load "../../../src/Ranvier/Projections.fs"
#load "../../../src/Ranvier/Api.fs"
#load "../../../src/Ranvier/Combinators.fs"

let graph = new Ranvier.Graph ()
let active = graph.Activate ()

open Ranvier

type Todo = { Id: int; Title: string }

let todos =
    createSignal [
        { Id = 1; Title = "Write the guide" }
        { Id = 2; Title = "Review it" }
        { Id = 3; Title = "Publish" }
    ]

let titles = createProjection _.Id _.Title (fun () -> todos.Value)

titles.Keys

(**

```text
[|1; 2; 3|]
```
*)

titles.Get 2

(**

```text
"Review it"
```

`Snapshot` returns the settled rows in key order, read untracked.
*)

titles.Snapshot |> Seq.map (fun row -> row.Key, row.Value) |> List.ofSeq

(**

```text
[(1, "Write the guide"); (2, "Review it"); (3, "Publish")]
```

- `Keys` is the key set in source order. A reader of `Keys` wakes only when membership or order changes.
- `Count` is the number of keys, read through `Keys`.
- `Get key` is a tracked read of that row alone. It raises `KeyNotFoundException` for an absent key
  (`The projection has no key 99.`).
- `TryGet key` returns `None` for an absent key.
- `Snapshot` is an untracked read of every row.

A write to the source recomputes the rows whose item changed, and wakes their readers when the row's
value changes. A survivor whose item is unchanged under the graph's equality policy is skipped: its
`map` or reader keeps its last result, and its readers stay asleep. The default policy compares records by
reference, so a record rebuilt with equal contents counts as a changed item and re-runs `map`; the
row's readers wake only if the new value differs.
*)

todos.Value <-
    todos.Value
    |> List.map (fun t -> if t.Id = 2 then { t with Title = "Review it twice" } else t)

titles.Get 2

(**

```text
"Review it twice"
```

Pinned by `editing one row wakes that row and no other`, `reordering the collection wakes Keys and no row`
([Projections.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Projections.fs))
and `an unchanged survivor is skipped`
([MapSemantics.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/MapSemantics.fs)).

## Identity

`keyOf` chooses identity. Keying by an id gives keyed reuse: an item edited under its key reaches the
existing row. Keying by the item itself (`id`) gives Solid's unkeyed semantics: an edited item is a
new key, and its row is built afresh. A record or union used as its own key compares by contents, so
two equal items in one source are a duplicate key.

Keys compare structurally (`HashIdentity.Structural`) regardless of `GraphOptions.Equality`. The
equality policy applies to items and row values only. A key built fresh on every pass, such as a
tuple or an array, matches the previous pass's key when the contents are equal.

A pass that produces the same key twice fails. Every read of the projection raises the failure:
*)

let duplicated =
    createProjection _.Id _.Title (fun () -> [ { Id = 1; Title = "a" }; { Id = 1; Title = "b" } ])

(**

The read raises `InvalidOperationException` with the message
`The projection produced the key 1 twice in one pass. Keys must be unique; check the keyOf function.`
`Projection.Error` holds the same exception for a pass that the scheduler ran with no reader to raise to.

## The factory form

`createProjectionWith keyOf factory source` runs `factory` once per key when the key enters, untracked.
The factory receives an accessor that returns the key's latest item, and returns the row's reader.
The row's value is the reader's result. The reader runs when the row is read and is stale, and re-runs
when the key's item changes or a value it read changes.

The factory runs inside a scope that lives until the key is removed. Nodes created in the factory
body belong to that scope, and cleanups registered there run when the key is removed.
*)

let removed = ResizeArray<int> ()

let rows =
    createProjectionWith
        _.Id
        (fun item ->
            let id = (item ()).Id
            let shout = createMemo (fun () -> (item ()).Title.ToUpper ())
            onCleanup (fun () -> removed.Add id)
            fun () -> shout.Value)
        (fun () -> todos.Value)

rows.Get 1

(**

```text
"WRITE THE GUIDE"
```
*)

todos.Value <- todos.Value |> List.filter (fun t -> t.Id <> 3)
rows.Keys, List.ofSeq removed

(**

```text
([|1; 2|], [3])
```

An unobserved projection runs its pass at the next read, so the removal and its cleanup follow the read of `rows.Keys`.

A cleanup registered in the factory may write the projection's own source; the removal completes and
the graph settles (`a removed item's cleanup may write the source without hanging`).

A factory that reads a pending source fails its row with an `InvalidOperationException` whose message
begins `The projection's factory for key ... read a pending source`. The factory runs once and cannot
wait for the source to settle. Read the source inside the returned reader instead.

<div class="alert alert-warning">

**Create owned nodes in the factory body.** A reader, or a `map`, that
creates an owned node (a memo, effect, async value, boundary, root, projection, lookup, selector or
`onCleanup`) raises `InvalidOperationException`, and that exception becomes the row's error. The value
form's message begins `A projection's map created an owned node`; the factory form's begins
`A projection row's reader created an owned node`.
</div>
*)

let misplaced =
    createProjection _.Id (fun t -> (createMemo (fun () -> t.Title)).Value) (fun () -> todos.Value)

(**

`misplaced.Get 1` raises the error above. Moving the memo into `createProjectionWith`'s factory, as
`rows` does, creates it once per key.

<div class="alert alert-warning">

**The check covers direct constructors too.** A reader or `map` that builds a node with
`Memo (graph, ...)`, `new Effect (...)`, `Graph.CreateRoot` or `Graph.OnCleanup` fails with the same
exception.
</div>

A node created inside a memo that a projection row pulls belongs to that memo. A `createMemoWith`
memo keeps it until the memo's next run or disposal, whichever key or projection first read it. A
`createMemo` body that creates a node fails with `InvalidOperationException` naming
`createMemoWith`.

## Index projections

An index projection keys rows by position. The row at a slot survives its item changing, and
shortening the source drops the trailing slots.
*)

let letters = createSignal [ "a"; "b"; "c" ]
let slots = createIndexProjection (fun (s: string) -> s.ToUpper ()) (fun () -> letters.Value)

(**

*)

letters.Value <- [ "z" ]
slots.Keys, slots.Get 0

(**

```text
([|0|], "Z")
```

## Pending and failed rows

A row whose value is pending raises `NotReadyException` from `Get` and `TryGet`, which a suspense
boundary catches. The key set resolves independently of the rows' values, so `Keys` is available while
rows are still in flight. A failed row re-raises its reader's exception from `Get`.

- `AnyPending` wakes its reader only when the answer changes. A read is O(1) while no row is pending, and
  costs a pass over the pending rows otherwise.
- `PendingKeys` lists the pending rows in key order, and costs a pass over them on every read while any
  row is pending.
- Both count only rows that have been read: an unread row is absent from the summary.
- A read of either brings the pending rows current first, so a reader of the summary and of a row sees
  them agree, and runs once when a row settles. A row the reader's own run makes pending shows only in
  the run's later reads of the summary.
- `Snapshot` and `AsObservableCollection` show a pending or failed row's last settled value, and leave
  out a row that has never settled.

The pending channel itself is described in [Async and pending](async-and-pending.md).

## Laziness

A projection with no observed row, key set or pending summary is lazy: writes to its source run no pass,
and the next read runs one pass. Once an effect or memo observes it, a write to the source schedules
the pass eagerly.

## Binding to a UI list

`AsObservableCollection ()` returns an `ObservableCollection` holding the row values in key order, kept
current by an effect owned by the calling scope.
*)

let view = titles.AsObservableCollection ()

(**

*)

todos.Value <- todos.Value @ [ { Id = 4; Title = "Celebrate" } ]
List.ofSeq view

(**

```text
["Write the guide"; "Review it twice"; "Celebrate"]
```

<div class="alert alert-warning">

**Every change resets the collection.** Each change raises one `Reset`, followed by one `Add` per value.
A bound list control rebuilds its items on every change.
</div>

The updates stop when the calling scope is disposed or re-runs, or when the projection is disposed.

## Lookups

`createLookup f affected source` derives a value for any key, computed as `f state key`. A cell exists
for each key read through `Get`, and a cell with no observers is evicted at the lookup's next
transition or read of another key. A source change recomputes only the live cells among the keys `affected prev next` returns.
*)

let stock = createSignal (Map [ "apples", 3; "pears", 0 ])

let changedKeys (prev: Map<string, int>) (next: Map<string, int>) =
    Seq.append prev.Keys next.Keys
    |> Seq.distinct
    |> Seq.filter (fun k -> prev.TryFind k <> next.TryFind k)

let onHand =
    createLookup (fun (m: Map<string, int>) k -> m.TryFind k |> Option.defaultValue 0) changedKeys (fun () ->
        stock.Value)

onHand.Get "apples", onHand.Get "plums"

(**

```text
(3, 0)
```

<div class="alert alert-warning">

**The `affected` function is a contract.** `affected prev next` must name every key whose value can differ between
the two states. A key left out keeps its stale value. An extra key costs one recomputation.
</div>

A key whose computation throws holds the exception, and is recomputed on every later transition until
it succeeds. A pending source suspends every live cell until it settles.

## Selectors

`createSelector source` is `createLookup` with `affected = fun prev next -> [ prev; next ]`: `Get key` is
`true` for the selected key and `false` for every other. A selection change wakes only the readers of
the previous and the next key.
*)

let selected = createSignal 1
let isSelected = createSelector (fun () -> selected.Value)

(**

*)

selected.Value <- 3
[ for k in 1..4 -> k, isSelected.Get k ]

(**

```text
[(1, false); (2, false); (3, true); (4, false)]
```

Pinned by `createSelector reports membership and wakes only the two ends` and
`only the affected keys are recomputed`
([Lookups.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Lookups.fs)).

## Deep updates

`Signal.update s f` writes `f s.Peek` to `s`. The write passes through the signal's equality cutoff, so an `f` that
returns its argument wakes no reader and starts no projection pass.

### Nested copy-and-update

F# 8's nested `with` writes a field deep inside a record. It rebuilds one record per level of the path, and every
record off the path keeps its reference. A path segment that also names a record type in scope resolves as the
type, so a field `User` of type `User` fails to compile as `{ m with User.Name = ... }`. Qualify the path with the
outer type (`{ m with Model.User.Name = ... }`) or rename the field.
*)

type Address = { City: string; Zip: string }
type Person = { Name: string; Home: Address }

type Store =
    {
        Owner: Person
        Theme: string
        Items: Todo list
    }

let store =
    createSignal
        {
            Owner =
                {
                    Name = "Ada"
                    Home = { City = "Bergen"; Zip = "5003" }
                }
            Theme = "dark"
            Items = [ { Id = 1; Title = "Write the guide" }; { Id = 2; Title = "Review it" }; { Id = 3; Title = "Publish" } ]
        }

(**

### Focused reads

A `createMemo` over a path is the fine-grained read. Under the default policy each memo compares records by
reference, so a reader of an unchanged branch stays asleep.
*)

let owner = createMemo (fun () -> store.Value.Owner)
let home = createMemo (fun () -> owner.Value.Home)
let city = createMemo (fun () -> home.Value.City)
let zip = createMemo (fun () -> home.Value.Zip)
let theme = createMemo (fun () -> store.Value.Theme)

(**

*)

Signal.update store (fun s -> { s with Owner.Home.City = "Oslo" })
city.Value, zip.Value

(**

```text
("Oslo", "5003")
```

The write re-runs every memo on the path, each direct child of a memo on the path (`zip`), and every memo that
reads the root (`theme`). Only readers whose value changed wake: here the reader of `city`. Several updates inside
`batch` give one flush; to allocate the path once, compose them into one `Signal.update`.

### Keyed updates

`List.updateBy keyOf key f xs` replaces the first element whose key equals `key` with `f element`. Keys compare
under `HashIdentity.Structural`, the comparer of projection row identity. The result is `xs` itself when no key
matches or `f` returns its argument, so a reference test decides whether the parent needs rebuilding.
`Array.updateBy` is the same over arrays.
*)

let rename id title =
    Signal.update store (fun s ->
        let items = s.Items |> List.updateBy _.Id id (fun t -> { t with Title = title })
        if obj.ReferenceEquals (items, s.Items) then s else { s with Items = items })

let storeTitles = createProjection _.Id _.Title (fun () -> store.Value.Items)

(**

*)

rename 2 "Review it twice"
storeTitles.Get 2

(**

```text
"Review it twice"
```

Only the row whose item changed wakes. The write still costs one projection pass over the whole list, and the list
form rebuilds the cells before the match and shares the tail after it. Keep keys unique: `updateBy` rewrites the first
match only, and a projection over a list with a duplicate key raises.

### Selecting one element

`createOptionMemo select` holds `select ()`. While the inner value stays equal under the graph's equality policy,
it keeps its previous `Some` instance and its readers stay asleep.
*)

let second = createOptionMemo (fun () -> store.Value.Items |> List.tryFind (fun t -> t.Id = 2))
let wrapped = createMemo (fun () -> store.Value.Items |> List.tryFind (fun t -> t.Id = 2))

(*** hide ***)
let secondRuns = ref 0
let wrappedRuns = ref 0
createEffect (fun () -> second.Value |> ignore; secondRuns.Value <- secondRuns.Value + 1)
createEffect (fun () -> wrapped.Value |> ignore; wrappedRuns.Value <- wrappedRuns.Value + 1)

(**

*)

Signal.update store (fun s -> { s with Theme = "light" })
secondRuns.Value, wrappedRuns.Value

(**

```text
(1, 2)
```

<div class="alert alert-warning">

**A `createMemo` returning `Some` wakes on every run on .NET.** Each run allocates a new `Some`, and the default
policy compares it by reference, so the theme write above wakes the readers of `wrapped`. Under Fable, `Some x` is
`x` itself for a non-nested option and the same memo stays asleep. `createOptionMemo` behaves the same on both.
</div>

<div class="alert alert-warning">

**`createOptionMemo` creates a memo.** Inside a `createMemo` body or a projection's `map` it raises
`InvalidOperationException`, as any owned node does. Create it at setup, under an owner, or in a
`createProjectionWith` factory.
</div>

Pinned by `a record-path write re-runs only the readers on the path`, `a keyed write wakes only the written
projection row` and `createOptionMemo: an unrelated root write wakes no dependent and calls no Equals`
([Lenses.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Lenses.fs)).

## Combinator views

`Projection.filter`, `Projection.choose`, `Projection.map`, `Projection.mapWith`, `Projection.sortBy` and `Projection.groupBy` derive a projection from another. Each view keeps a row per key, and its
function re-runs for a key only when the upstream row for that key changes. A reader of the view wakes as a reader of
any projection does: `Keys` on a membership or order change, `Get key` on a changed value.
*)

let catalogue = createSignal [ { Id = 1; Title = "a" }; { Id = 2; Title = "bb" }; { Id = 3; Title = "ccc" } ]
let upstreamTitles = createProjection _.Id _.Title (fun () -> catalogue.Value)
let longTitles = upstreamTitles |> Projection.filter (fun title -> title.Length > 1)
let lengths = upstreamTitles |> Projection.map String.length
longTitles.Keys

(**

```text
[|2; 3|]
```
*)

lengths.Get 3

(**

```text
3
```

A view's pass still walks the upstream `Keys`, so a membership or order change costs O(N) per view, as it does for
the source projection. The combinators remove the per-key user calls for unchanged rows, not that walk.

`Projection.sortBy` orders the keys ascending by a sort key under `compare`. Keys with equal sort keys keep their
upstream order, including after the upstream reorders. A `float` or `float32` NaN sort key comes after every other key
and `None` comes before `Some`, on .NET and under Fable; a NaN nested in a tuple, record or option orders as `compare`
orders it.
*)

let catalogueOrder = createSignal [ { Id = 1; Title = "bb" }; { Id = 2; Title = "a" }; { Id = 3; Title = "cc" } ]
let orderedTitles = createProjection _.Id _.Title (fun () -> catalogueOrder.Value)
let byLength = orderedTitles |> Projection.sortBy String.length
byLength.Keys

(**

```text
[|2; 1; 3|]
```

When every sort key, membership and the upstream order are unchanged, a `sortBy` pass costs O(N) and publishes no new
`Keys`; otherwise it re-sorts in O(N log N).

`Projection.choose` keeps the keys whose chooser returns `Some`, each with the value inside it, in one view: the chooser
runs once per key per upstream row change. A change from one
`Some` value to another wakes only readers of the key's row. A pending or raising chooser follows the predicate rules
below.

A predicate or sort key that raises leaves the key out of `Keys`, and `Get` of the key raises the exception. A mapping
that raises keeps the key, with the same `Get` behaviour. A pending predicate keeps the key's last membership; a
pending sort key keeps its last settled sort key. A key whose predicate or sort key has never settled appears in
`PendingKeys` and not in `Keys`; `AnyPending` counts only rows of keys in `Keys`.

In a chain, such a key is also in the `PendingKeys` of every view built on the one that holds it out, so the end of
`filter >> map` still reports it. A key excluded by a failed predicate or sort key is absent from every view built on
the excluding view, and only `Get` and `TryGet` of the excluding view raise its error. A view's `Status` and `Error`
describe its pass, not its rows.

Pinned by `a throwing predicate excludes the key, and Get and TryGet raise its error`, `a pending predicate keeps
membership; a never-settled key is only in PendingKeys`, `an effect reading Keys and Get runs exactly once per write`,
`sorts ascending, and equal sort keys keep upstream order`, `ties keep upstream order at 40 keys, across reorder and
removal`, `NaN sort keys sort after every other key, across updates`, `None sorts before Some, across updates`, `a pass whose sort keys and upstream order are unchanged does not re-sort` and
`a pending sort key keeps its last settled sort key; a never-settled key is only in PendingKeys`, `a pending chooser
keeps membership; a never-settled key is only in PendingKeys`, `a throwing chooser excludes the key, and Get and TryGet
raise its error`, and the
`chained pending` tests
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).

`Projection.mapWith` is the factory form of `map`: its mapping runs once per key with the key and a tracked read of the
upstream value, and returns the key's reader. Nodes the mapping creates belong to the key and are disposed with it.
*)

let labels =
    orderedTitles
    |> Projection.mapWith (fun id title ->
        let length = createMemo (fun () -> String.length (title ()))
        fun () -> $"{id}:{length.Value}")

labels.Get 3

(**

```text
"3:2"
```

`Projection.groupBy` groups the keys by a group key. The groups follow the upstream position of each group's first
member, and each group is an inner view of its keys in upstream order. A key whose group key changes leaves its old
group and joins its new one in the same pass. `groupBy` returns a `Grouping`, which is a
`Projection<'G, Projection<'K, 'V>>`; branches that must unify with a plain projection need an upcast.

A pending group key keeps the key's last settled group. A key whose group key raises or has never settled is in no
group: `UngroupedKeys` lists it, in upstream order, followed by the pending keys the upstream view holds out.
`GroupOf` returns a key's group, re-raises its group key's exception, or raises `NotReadyException` while its group
key has never settled.
*)

let byLengthGroup = orderedTitles |> Projection.groupBy String.length
[ for length in byLengthGroup.Keys -> length, (byLengthGroup.Get length).Keys ]

(**

```text
[(2, [|1; 3|]); (1, [|2|])]
```

An inner view is disposed once its group is empty: a reader holding it sees empty `Keys`, and `Get` raises
`ObjectDisposedException`. A group that returns later has a new inner view.

Pinned by the `Projection.mapWith` and `Projection.groupBy` tests, including `a never-settled key is in UngroupedKeys,
in upstream order, until it settles and joins its group` and `filter then groupBy lists the keys the filter holds out
in UngroupedKeys`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).

`Projection.take`, `Projection.skip` and `Projection.sub` select keys by upstream position. Their counts are functions
read tracked in the pass, so a window driven by a signal follows it. A negative offset is 0, a negative `take` count or
an offset past the last key gives an empty window, and a count past the last key ends the window at the last key.
*)

let page = createSignal 0
let pageOfTitles = orderedTitles |> Projection.sub (fun () -> page.Value * 2) (fun () -> 2)
pageOfTitles.Keys

(**

```text
[|1; 2|]
```

A slice pass costs O(window). A key that stays in the window keeps its row, so shifting the window by d positions
creates and disposes at most d rows, and a write to a row outside the window wakes no reader of the slice.

Pinned by `take, skip and sub select positions and clamp counts as List.truncate and a clamped skip do`, `a key that
stays in a shifted window keeps its row` and `a write outside the window wakes no reader`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).

To fold the values of a projection into one value, such as a total or a count, see [Aggregates](aggregates.fsx).

## Planned and out of scope

Reusable lens
and prism values for deep writes wait on a need for reusable focus paths over collections. Record-shaped stores
remain out of scope.

## Key types

- `Projection<'K, 'V>`: the key set and its rows.
- `Lookup<'K, 'V>`: a pointwise derived value per key.
- `Api`: `createProjection`, `createProjectionWith`,
  `createIndexProjection`, `createIndexProjectionWith`, `createLookup`, `createSelector` and `createOptionMemo`.
- `Signal`, `List`
  and `Array`: `Signal.update`, `List.updateBy` and
  `Array.updateBy`, the deep and keyed writes.
*)

(*** hide ***)
active.Dispose ()
graph.Dispose ()
