(**
---
title: Aggregates
order: 6
---

:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

How to keep a total, a count or any other fold over a [projection](collections.md) current as its rows change. An
aggregate is a `Memo<'S>`: read it, track it and dispose it as any memo.
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

open Ranvier

let graph = new Graph ()
let active = graph.Activate ()

(**
The examples on this page run inside an active graph, as in [Getting started](getting-started.md), over one list of
tasks:
*)

type Task = { Id: int; Estimate: int; Done: bool }

let tasks =
    createSignal [
        { Id = 1; Estimate = 3; Done = false }
        { Id = 2; Estimate = 5; Done = true }
        { Id = 3; Estimate = 2; Done = false }
    ]

let rows = createProjection _.Id id (fun () -> tasks.Value)

(**
## Choosing an aggregate

| Function | Result | Cost of a row edit |
|----------|--------|--------------------|
| `Projection.foldGroup add subtract zero` | the fold of every settled value with `add` | O(1) calls: `subtract` of the old value, `add` of the new |
| `Projection.fold folder state` | the fold of every settled value in `Keys` order | O(N) calls of `folder` over the cached values |
| `Projection.sumBy projection` | the sum of `projection` over the values | as `foldGroup` |
| `Projection.countBy predicate` | the count of values satisfying `predicate` | as `foldGroup` |
| `Projection.exists predicate` | whether `countBy predicate` is above zero | as `foldGroup` |
| `Projection.forall predicate` | whether `countBy` of the misses is zero | as `foldGroup` |

A key added to or removed from `Keys` costs an O(N) key diff, then one `add` or `subtract` per changed key. Each
aggregate reads only the rows that changed, so an edit to one row of a thousand re-runs the row's function alone.

## Folds with an inverse

`Projection.foldGroup` takes `subtract` as the inverse of `add`: `subtract (add s v) v` must equal `s`. An added key
adds its value, a removed key subtracts its last value, and a changed value subtracts the old and adds the new.
*)

let totalEstimate = rows |> Projection.foldGroup (fun s t -> s + t.Estimate) (fun s t -> s - t.Estimate) 0

totalEstimate.Value // 10

tasks.Value <- tasks.Value |> List.map (fun t -> if t.Id = 1 then { t with Estimate = 8 } else t)
totalEstimate.Value // 15

(**
The fold reads settled values. A pending row keeps its last settled value in the state, as in `Snapshot`, and
contributes from its first settled value; the upstream's `AnyPending` reports it. While a row is failed, reading the
aggregate raises the row's exception, and the fold recovers once the row settles.

The state returns to exactly `zero` when the last row value leaves it.

## The shorthands

`sumBy`, `countBy`, `exists` and `forall` are `foldGroup` over a `Projection.map` view. Their function re-runs for a
key when the key's upstream row changes, and a pending or raising function behaves as a pending or failed row.
*)

let remaining = rows |> Projection.sumBy (fun t -> if t.Done then 0 else t.Estimate)
let doneCount = rows |> Projection.countBy _.Done
let anyDone = rows |> Projection.exists _.Done
let allDone = rows |> Projection.forall _.Done

(remaining.Value, doneCount.Value, anyDone.Value, allDone.Value) // (10, 1, true, false)

tasks.Value <- tasks.Value |> List.map (fun t -> { t with Done = true })
(remaining.Value, doneCount.Value, anyDone.Value, allDone.Value) // (0, 3, true, true)

(**
An empty projection gives `exists` as `false` and `forall` as `true`.

`sumBy` over `float` or `float32` subtracts in floating point, so a long run of edits can leave a rounding error in
the last digits. While the sum is infinite or NaN, each row change re-adds every cached value in O(N), so removing
an infinite or NaN row restores a finite sum.

## Folds with no inverse

`Projection.fold` takes a folder alone. Any key or value change re-folds the cached values in `Keys` order, which
costs O(N) folder calls but re-reads only the changed rows. Use it for folds with no inverse, such as a maximum or
an order-dependent result.
*)

let longest = rows |> Projection.fold (fun best t -> max best t.Estimate) 0
let order = rows |> Projection.sortBy _.Estimate |> Projection.fold (fun ids t -> ids @ [ t.Id ]) []

(longest.Value, order.Value) // (8, [ 3; 2; 1 ])

(**
## Reading other nodes

A signal or memo read inside `add`, `subtract` or `folder` is tracked. When it changes, the aggregate re-folds every
cached value with the new reading.
*)

let rate = createSignal 2
let cost = rows |> Projection.foldGroup (fun s t -> s + t.Estimate * rate.Value) (fun s t -> s - t.Estimate * rate.Value) 0

cost.Value // 30
rate.Value <- 3
cost.Value // 45

(**
These functions re-run on row changes, outside the calling scope. A node created inside one of them raises
`InvalidOperationException`; create it outside the aggregate and read it inside.

## Lifetime

The aggregate belongs to the scope that created it and is disposed with it. After disposal it stops observing the
rows, and reads return the last state.

Pinned by the `Projection.foldGroup` tests, including `an edit at N=1000 makes one add and one subtract`, `sumBy over
floats recovers from infinity and NaN` and `sumBy over floats reads zero once every row leaves`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
*)

(*** hide ***)
active.Dispose ()
graph.Dispose ()
