(**
---
title: Deep and keyed updates
---
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()
(**


The examples run inside an [active graph](graph.fsx).

`Signal.update s f` writes `f s.Peek` to `s`. The write passes through the signal's equality cutoff, so an `f` that
returns its argument wakes no reader and starts no projection pass.

### Nested copy-and-update

F# 8's nested `with` updates a field deep inside a record. Each record on the path is rebuilt;
records outside it keep their references.

:::details When a field name also names a type
A field `User` of type `User` can make `{ m with User.Name = ... }` fail because the path resolves
as the type. Qualify it with the outer type (`{ m with Model.User.Name = ... }`) or rename the field.
:::


*)
type Todo = { Id: int; Title: string }

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
            Items =
                [
                    { Id = 1; Title = "Write the guide" }
                    { Id = 2; Title = "Review it" }
                    { Id = 3; Title = "Publish" }
                ]
        }
(**

### Focused reads

A `createMemo` over a path is the fine-grained read. Under the default policy each memo compares records by
reference, so a reader of an unchanged branch stays asleep.


*)
let owner = createMemo (fun _ -> store.Value.Owner)
let home = createMemo (fun _ -> owner.Value.Home)
let city = createMemo (fun _ -> home.Value.City)
let zip = createMemo (fun _ -> home.Value.Zip)
let theme = createMemo (fun _ -> store.Value.Theme)
(**


*)
Signal.update store (fun s -> { s with Owner.Home.City = "Oslo" })
city.Value, zip.Value
(**

```text
("Oslo", "5003")
```

:::details Which memos recompute after a deep write

The write re-runs every memo on the path, each direct child of a memo on the path (`zip`), and every memo that
reads the root (`theme`). Only readers whose value changed wake: here the reader of `city`. Several updates inside
`batch` give one flush; to allocate the path once, compose them into one `Signal.update`.

:::

### Keyed updates

`List.updateBy keyOf key f xs` replaces the first element whose key equals `key` with `f element`. Keys compare
under `HashIdentity.Structural`, the comparer of projection row identity. The result is `xs` itself when no key
matches or `f` returns its argument, so a reference test decides whether the parent needs rebuilding.
`Array.updateBy` is the same over arrays.


*)
let rename id title =
    Signal.update store (fun s ->
        let items =
            s.Items
            |> List.updateBy _.Id id (fun t -> { t with Title = title })

        if obj.ReferenceEquals (items, s.Items) then
            s
        else
            { s with Items = items })

let storeTitles = createProjection _.Id _.Title (fun () -> store.Value.Items)
(**


*)
rename 2 "Review it twice"
storeTitles.Get 2
(**

```text
"Review it twice"
```

:::details Keyed update costs and duplicate keys

Only the row whose item changed wakes. The write still costs one projection pass over the whole list, and the list
form rebuilds the cells before the match and shares the tail after it. Keep keys unique: `updateBy` rewrites the first
match only, and a projection over a list with a duplicate key raises.

:::

### Direct edits

`createKeyedCollection keyOf` starts an empty editable source. Its `Rows` property supports the
collection view operators. Updating an existing key writes that row directly, avoiding the
whole-input pass required by immutable list replacement. New keys append; removing and
re-adding a key creates a new row and moves it to the end.

*)
let directTodos = createKeyedCollection (fun (todo: Todo) -> todo.Id)
directTodos.Edit(fun edit ->
    edit.AddOrUpdate { Id = 1; Title = "Write" }
    edit.AddOrUpdate { Id = 2; Title = "Test" })
let directTitles = directTodos.Rows |> Projection.map _.Title
let titleChanges = directTitles.NewValueReader ()
titleChanges.Read () |> ignore
directTodos.AddOrUpdate { Id = 2; Title = "Retest" }
let changedTitles = titleChanges.Read ()
(**

`changedTitles.Changes` contains `Changed` for key 2. `NewKeyReader()` reports membership and order
only; `NewValueReader()` adds settled-value changes. The first read and a reader that falls behind
report a reset. Changes are coalesced key hints: read current values from the projection and
handle pending/error states there. Pending and failed rows retain their last settled value for
value observation; state changes alone do not report `Changed`.

Readers are owned by their creating scope and can be disposed early. Value readers share row
observation, which ends when the last value reader is disposed. Initial observation evaluates
all visible rows. Subsequent value-only pulls evaluate suspect rows.

`Edit` batches synchronous changes and keeps applied changes if its callback throws. Direct
updates preserve insertion order; removals search it in O(N), and membership passes copy key
order. Map views consume key deltas. Filter, sort and grouping membership processing still
scans keys.

### Selecting one element

`createOptionMemo select` holds `select ()`. While the inner value stays equal under the graph's equality policy,
it keeps its previous `Some` instance and its readers stay asleep.


*)
let second =
    createOptionMemo (fun () ->
        store.Value.Items
        |> List.tryFind (fun t -> t.Id = 2))

let wrapped =
    createMemo (fun _ ->
        store.Value.Items
        |> List.tryFind (fun t -> t.Id = 2))
(**


*)
let secondRuns = ref 0
let wrappedRuns = ref 0

createEffect (fun () ->
    second.Value |> ignore
    secondRuns.Value <- secondRuns.Value + 1)

createEffect (fun () ->
    wrapped.Value |> ignore
    wrappedRuns.Value <- wrappedRuns.Value + 1)
(**


*)
Signal.update store (fun s -> { s with Theme = "light" })
secondRuns.Value, wrappedRuns.Value
(**

```text
(1, 2)
```

:::warning A `createMemo` returning `Some` wakes on every run on .NET

Each run allocates a new `Some`, and the default
policy compares it by reference, so the theme write above wakes the readers of `wrapped`. Under Fable, `Some x` is
`x` itself for a non-nested option and the same memo stays asleep. `createOptionMemo` behaves the same on both.
:::

:::warning `createOptionMemo` creates a memo

Inside a `createMemo` body or a projection's `map` it raises
`InvalidOperationException`, as any owned node does. Create it at setup, under an owner, or in a
`createProjectionWith` factory.
:::

:::details Tests covering this behaviour

Pinned by `a record-path write re-runs only the readers on the path`, `a keyed write wakes only the written
projection row` and `createOptionMemo: an unrelated root write wakes no dependent and calls no Equals`
([Lenses.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Lenses.fs)).
:::

## Planned and out of scope

Reusable lens
and prism values for deep writes wait on a need for reusable focus paths over collections. Record-shaped stores
remain out of scope.

*)
(*** hide ***)
active.Dispose ()
graph.Dispose ()
