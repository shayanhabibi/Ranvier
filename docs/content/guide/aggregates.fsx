(**
---
title: Aggregates
order: 6
---
*)
(**
:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Keep totals, counts and other folds current as [projection](collections.fsx) rows change.
An aggregate is a `Memo<'S>`: read, track and dispose it like any other memo.
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

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

- For totals and counts, use `sumBy` and `countBy`.
- For any/all checks, use `exists` and `forall`.
- For a fold with an inverse, use `foldGroup` to update changed contributions.
- For a maximum or an order-dependent result, use `fold` to recompute from cached values.

:::details Aggregate functions and row-edit costs

| Function | Result | Cost of a row edit |
|----------|--------|--------------------|
| `Projection.foldGroup add subtract zero` | the fold of every settled value with `add` | O(1) calls: `subtract` of the old value, `add` of the new |
| `Projection.fold folder state` | the fold of every settled value in `Keys` order | O(N) calls of `folder` over the cached values |
| `Projection.sumBy projection` | the sum of `projection` over the values | as `foldGroup` |
| `Projection.countBy predicate` | the count of values satisfying `predicate` | as `foldGroup` |
| `Projection.exists predicate` | whether `countBy predicate` is above zero | as `foldGroup` |
| `Projection.forall predicate` | whether `countBy` of the misses is zero | as `foldGroup` |

:::

:::details Membership changes and row reads

A key added to or removed from `Keys` costs an O(N) key diff, then one `add` or `subtract` per changed key. Each
aggregate reads only the rows that changed, so an edit to one row of a thousand re-runs the row's function alone.

:::

Change one row's estimate, then remove another row. The total updates its contributions,
and the count changes only when membership changes.

*)
(*** map replay show=output ***)
let estimates = createSignal [ 1, 3; 2, 5; 3, 2 ]
let estimateRows = createProjection fst snd (fun () -> estimates.Value)
let estimateTotal = estimateRows |> Projection.sumBy id
let estimateCount = estimateRows |> Projection.countBy (fun _ -> true)
createEffect (fun () -> printfn "estimate = %d, tasks = %d" estimateTotal.Value estimateCount.Value)

controls [
    button "Estimate task 1 at 8" (fun () -> estimates.Value <- estimates.Value |> List.map (fun (key, estimate) -> key, (if key = 1 then 8 else estimate)))
    |> describe "One contribution grows by 5; membership stays at three tasks."
    |> expect "One contribution grows by 5; membership stays at three tasks." (fun () -> estimateTotal.Peek = 15 && estimateCount.Peek = 3)
    button "Remove task 2" (fun () -> estimates.Value <- estimates.Value |> List.filter (fun (key, _) -> key <> 2))
    |> describe "Removing the estimate of 5 leaves a total of 10 across two tasks."
    |> expect "Removing the estimate of 5 leaves a total of 10 across two tasks." (fun () -> estimateTotal.Peek = 10 && estimateCount.Peek = 2)
]

(**
## Folds with an inverse

`Projection.foldGroup` updates a total by removing old contributions and adding new ones:

- An added key adds its value.
- A removed key subtracts its last value.
- A changed row subtracts its old value and adds its new value.

`subtract` must undo `add`: `subtract (add s v) v` must equal `s`.
*)

let totalEstimate = rows |> Projection.foldGroup (fun s t -> s + t.Estimate) (fun s t -> s - t.Estimate) 0

totalEstimate.Value // 10

tasks.Value <- tasks.Value |> List.map (fun t -> if t.Id = 1 then { t with Estimate = 8 } else t)
totalEstimate.Value // 15

(**
:::details Pending and failed rows

The fold reads settled values. A pending row keeps its last settled value in the state, as in `Snapshot`, and
contributes from its first settled value; the upstream's `AnyPending` reports it. While a row is failed, reading the
aggregate raises the row's exception, and the fold recovers once the row settles.

:::

The state returns to exactly `zero` when the last row value leaves it.

## The shorthands

Use these shorthands for common totals and predicates. Their functions re-run for changed rows.
A pending or throwing function behaves like a pending or failed row.

:::details How the shorthands are built
They combine `Projection.map` with `foldGroup`.
:::
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

:::details Floating-point rounding, infinity and NaN

`sumBy` over `float` or `float32` subtracts in floating point, so a long run of edits can leave a rounding error in
the last digits. While the sum is infinite or NaN, each row change re-adds every cached value in O(N), so removing
an infinite or NaN row restores a finite sum.

:::

## Folds with no inverse

Use `Projection.fold` for a maximum or another result whose old contribution cannot be subtracted.
It folds cached values in `Keys` order after any key or value change. This costs O(N) folder calls,
but reads only the changed rows again.
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
:::warning Create nodes outside the aggregate

These functions re-run on row changes, outside the calling scope. A node created inside one of them raises
`InvalidOperationException`; create it outside the aggregate and read it inside.

:::

## Lifetime

The aggregate belongs to the scope that created it and is disposed with it. After disposal it stops observing the
rows, and reads return the last state.

:::details Tests covering this behaviour

Pinned by the `Projection.foldGroup` tests, including `an edit at N=1000 makes one add and one subtract`, `sumBy over
floats recovers from infinity and NaN` and `sumBy over floats reads zero once every row leaves`
([Combinators.fs](https://github.com/shayanhabibi/Ranvier/blob/master/tests/Ranvier.Tests/Combinators.fs)).
:::
*)

(*** hide ***)
active.Dispose ()
graph.Dispose ()
