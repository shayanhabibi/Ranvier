(**
---
title: Projections
---
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()
(**


A projection gives each collection row its own reactive value. Key by identity to keep rows
when items move, or by position to keep slots when their contents change.

The examples run inside an [active graph](graph.fsx).

## Choosing a form

- Use a **keyed projection** when items have stable identities, such as IDs.
- Use an **index projection** when rows should stay at their positions as items change.
- Use a **factory form** when each row needs its own nodes or cleanup.
- Use a **lookup** for a value per requested key, or a **selector** for selection membership.

:::details Creator signatures and row lifetimes

| Creator | Keyed by | Row value | Per-key nodes |
|---------|----------|-----------|---------------|
| `createProjection keyOf map source` | `keyOf item` | `map item`, re-run when the key's item or a value `map` read changes | none |
| `createProjectionWith keyOf factory source` | `keyOf item` | the reader returned by `factory`; `factory` runs once per key, untracked | created in `factory`, disposed when the key is removed |
| `createIndexProjection map source` | position | as `createProjection`; the row at a slot survives its item changing | none |
| `createIndexProjectionWith factory source` | position | as `createProjectionWith`; the factory runs once per slot | created in `factory`, disposed when the slot is removed |
| `createLookup f affected source` | any key read through `Get` | `f state key`, recomputed for the keys `affected` names | none |
| `createSelector source` | any key read through `Get` | `true` for the selected key, `false` otherwise | none |

:::

The index forms are Solid's `indexArray`. A projection is a key set plus one row per key, and each
of those is observed separately. A lookup has no key set: it holds a cell for each key that has been read.

## Reading a projection

The source returns a sequence, usually from a signal. `Keys` reads its ordered key set;
`Get key` reads one row. This projection exposes each todo's title under its ID.


*)
open Ranvier

type Todo = { Id: int; Title: string }

let todos =
    createSignal
        [
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
titles.Snapshot
|> Seq.map (fun row -> row.Key, row.Value)
|> List.ofSeq
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

A source write recomputes rows whose items changed. Their readers run only when the row values
change. Unchanged items keep their previous row results.

:::details Equal records and row recomputation
The default policy compares records by reference. A fresh record with equal contents recomputes
its row, but the row's readers run only if its result changes.
:::

**Test your understanding:** does changing todo 2's title change `Keys`? Which row value changes?


*)
todos.Value <-
    todos.Value
    |> List.map (fun t -> if t.Id = 2 then { t with Title = "Review it twice" } else t)

titles.Get 2
(**

:::details Answer

```text
"Review it twice"
```

The key set stays the same. Only row 2's value changes.
:::

:::details Tests covering this behaviour

Pinned by `editing one row wakes that row and no other`, `reordering the collection wakes Keys and no row`
([Projections.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Projections.fs))
and `an unchanged survivor is skipped`
([MapSemantics.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/MapSemantics.fs)).
:::

Rename one item, then reverse the collection. The row reader responds to its title change;
the key reader responds to the new order.

```fsharp map replay code=collapsed
let items = createSignal [ 1, "Write"; 2, "Review"; 3, "Publish" ]
let titles = createProjection fst snd (fun () -> items.Value)
createEffect (fun () -> printfn "row 2 = %s" (titles.Get 2))
createEffect (fun () -> printfn "keys = %A" titles.Keys)

controls [
    button "Rename row 2" (fun () -> items.Value <- items.Value |> List.map (fun (key, title) -> key, (if key = 2 then "Review twice" else title)))
    |> describe "The row value changes while the key order stays the same."
    |> expect "The row value changes while the key order stays the same." (fun () -> titles.Keys = [| 1; 2; 3 |])
    button "Reverse order" (fun () -> items.Value <- List.rev items.Value)
    |> describe "The existing keys move into reverse order."
    |> expect "The existing keys move into reverse order." (fun () -> titles.Keys = [| 3; 2; 1 |])
]
```

## Identity

`keyOf` determines whether an edited item keeps its row:

- Key by an ID to keep the existing row when that item changes.
- Key by the whole item (`id`) to create a new row for an edited item. This follows Solid's
  unkeyed semantics.

:::warning Keys must be unique
Records and unions used as keys compare by contents. Two equal items therefore produce the same
key and fail the projection pass.
:::

:::details Key equality is separate from value equality

Keys compare structurally (`HashIdentity.Structural`) regardless of `GraphOptions.Equality`. The
equality policy applies to items and row values only. A key built fresh on every pass, such as a
tuple or an array, matches the previous pass's key when the contents are equal.

:::

A pass that produces the same key twice fails. Every read of the projection raises the failure:


*)
let duplicated =
    createProjection _.Id _.Title (fun () -> [ { Id = 1; Title = "a" }; { Id = 1; Title = "b" } ])
(**

The read raises `InvalidOperationException` with the message
`The projection produced the key 1 twice in one pass. Keys must be unique; check the keyOf function.`
`Projection.Error` holds the same exception for a pass that the scheduler ran with no reader to raise to.

## The factory form

`createProjectionWith` separates row setup from row computation:

1. The **factory** runs once per key, untracked. It receives an accessor for the latest item.
2. The factory returns a **reader**, which computes the row's value from tracked reads.
3. The reader refreshes when stale and read, following changes to the item or its dependencies.

The factory runs inside a scope that lives until the key is removed. Nodes created in the factory
body belong to that scope, and cleanups registered there run when the key is removed.


*)
let removed = ResizeArray<int>()

let rows =
    createProjectionWith
        _.Id
        (fun item ->
            let id = (item ()).Id
            let shout = createMemo (fun _ -> (item ()).Title.ToUpper())
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

:::details When removal cleanups run

An unobserved projection runs its pass at the next read, so the removal and its cleanup follow the read of `rows.Keys`.

A cleanup registered in the factory may write the projection's own source; the removal completes and
the graph settles (`a removed item's cleanup may write the source without hanging`).

:::

:::warning Read pending values in the reader

A factory that reads a pending source fails its row with an `InvalidOperationException` whose message
begins `The projection's factory for key ... read a pending source`. The factory runs once and cannot
wait for the source to settle. Read the source inside the returned reader instead.

:::

:::warning Create owned nodes in the factory body

A reader, or a `map`, that
creates an owned node (a memo, effect, async value, boundary, root, projection, lookup, selector or
`onCleanup`) raises `InvalidOperationException`, and that exception becomes the row's error. The value
form's message begins `A projection's map created an owned node`; the factory form's begins
`A projection row's reader created an owned node`.
:::


*)
let misplaced =
    createProjection _.Id (fun t -> (createMemo (fun _ -> t.Title)).Value) (fun () -> todos.Value)
(**

`misplaced.Get 1` raises the error above. Moving the memo into `createProjectionWith`'s factory, as
`rows` does, creates it once per key.

:::warning The check covers direct constructors too

A reader or `map` that builds a node with
`Memo (graph, ...)`, `new Effect (...)`, `Graph.CreateRoot` or `Graph.OnCleanup` fails with the same
exception.
:::

:::details Nodes owned by a memo read from a row

A node created inside a memo that a projection row pulls belongs to that memo. A `createMemoWith`
memo keeps it until the memo's next run or disposal, whichever key or projection first read it. A
`createMemo` body that creates a node fails with `InvalidOperationException` naming
`createMemoWith`.

:::

## Index projections

An index projection keys rows by position. The row at a slot survives its item changing, and
shortening the source drops the trailing slots.


*)
let letters = createSignal [ "a"; "b"; "c" ]

let slots =
    createIndexProjection (fun (s: string) -> s.ToUpper ()) (fun () -> letters.Value)
(**


*)
letters.Value <- [ "z" ]
slots.Keys, slots.Get 0
(**

```text
([|0|], "Z")
```

## Pending and failed rows

`Keys` can be available while individual rows are still pending. Handle loading at the row or
collection level with a boundary.

A row whose value is pending raises `NotReadyException` from `Get` and `TryGet`, which a suspense
boundary catches. The key set resolves independently of the rows' values, so `Keys` is available while
rows are still in flight. A failed row re-raises its reader's exception from `Get`.

:::details Pending summaries, snapshots and read costs

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

:::

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

:::details UI notifications, cost and lifetime

The collection reconciles membership and order with individual item events, so a bound
list control keeps its unchanged items:

- The first population raises one `Reset`, followed by one `Add` per value.
- A departed row raises `Remove`, and a new row raises `Add` at its position in key order.
- A reorder raises `Move` only for the rows outside the longest run that kept its order. Swapping two
  neighbours raises one `Move`.
- A row whose value changed, under the graph's equality policy, raises one `Replace`.

After initial population, value-only updates consume settled-value reader deltas and replace only
changed accepted values; they do not poll every row. Membership/order reconciliation scans the visible
keys and uses an O(N log N) positional diff. Initial population, reset recovery and recovery after a
notification failure rebuild with `Reset` followed by `Add` events. A row settling for the first time
can also require membership reconciliation. Pending or failed rows keep their last accepted value;
rows that have never settled are omitted.

The updates stop when the calling scope is disposed or re-runs, or when the projection is disposed.

:::

## Reading changes

`NewKeyReader ()` returns a reader of the projection's membership and order, owned by the calling scope.
Each `Read ()` reports the keys added, removed or replaced since the reader's previous read, in time
proportional to the changes.

`NewValueReader ()` returns the same kind of reader, adding `KeyChange.Changed` for unequal
settled row values. It works on source projections and on collection views, including the
`Rows` of an [editable keyed collection](collection-updates.fsx#direct-edits).


*)
let reader = titles.NewKeyReader ()
reader.Read () |> ignore // the first read reports a reset

todos.Value <-
    (todos.Value |> List.filter (fun t -> t.Id <> 1))
    @ [ { Id = 5; Title = "Rest" } ]

let delta = reader.Read ()

[ for change in delta.Changes -> change.Key, change.Value ]
|> List.sortBy fst
(**

```text
[(1, Removed); (5, Added)]
```

:::details Key deltas, resets and read costs

- `Changes` holds one `KeyChange` per key: `Added`, `Removed`, or `Replaced` for a key removed and
  re-added between two reads. A key added and then removed between two reads cancels out. The order is
  unspecified; `Keys` gives the order.
- `Keys` and `PreviousKeys` are the key arrays at this read and at the previous one. `OrderChanged` is
  false when both are the same array.
- `Positional` lists the `RemoveAt`, `InsertAt` and `Move` edits that turn `PreviousKeys` into `Keys`,
  computed on first use in O(N log N).
- `IsReset` is true on the first read, after the projection is disposed, and once more than
  `max(64, N)` changes are unread. `Changes` is then empty: rebuild from `Keys`.
- `Read` is a tracked read of `Keys`, so an effect that reads the reader wakes on every change it
  reports. While the pass is pending or failed, `Read` raises what `Keys` raises and keeps the changes
  for the next read.

A key reader reports membership and order; read row values with `Get` for the keys in `Changes`. Each
reader costs one map update per added or removed key, and a projection without readers pays one null
check.

:::

### Settled row changes

After the first reset, renaming todo 2 reports its key even though membership and order stay the same.

*)
let valueReader = titles.NewValueReader ()
valueReader.Read () |> ignore

todos.Value <-
    todos.Value
    |> List.map (fun t -> if t.Id = 2 then { t with Title = "Final review" } else t)

let valueDelta = valueReader.Read ()
[ for change in valueDelta.Changes -> change.Key, change.Value ]
(**

```text
[(2, Changed)]
```

`Changes` contains keys rather than value snapshots. Read the current row through `Get` or
`TryGet` and handle its pending/error state there. Multiple writes between reads coalesce:
these deltas are refresh hints, not a history of every intermediate value. Membership changes
take precedence over `Changed` for the same key, so an added or replaced row needs one refresh.

:::details Observation, equality and lifetime

- Settled values compare under `GraphOptions.Equality`. Status changes alone do not report
  `Changed`. Pending or failed rows retain their last accepted settled value; their next unequal
  settled value, or their first settled value, reports a change.
- Value readers share row observation and accepted values, while each keeps its own bounded
  cursor. The first value read evaluates all visible rows. Later value-only pulls
  evaluate suspect rows; membership processing can still scan keys.
- `Read` tracks membership and the value observation, so it can drive an effect on row changes.
  If the projection's pass is pending or failed, `Read` raises and preserves its unread changes.
- First reads, overflow and projection disposal report `IsReset` with empty `Changes`;
  rebuild from `delta.Keys`. `Positional` handles membership and order, not row-value refreshes.
- Readers belong to the scope that created them. Dispose a reader to stop it early; disposing
  the last value reader releases shared observation. Reading a disposed reader raises
  `ObjectDisposedException`.

:::

*)
(*** hide ***)
active.Dispose ()
graph.Dispose ()
