(**
---
title: Lookups
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

`createLookup f affected source` computes `f state key` for each key you read with `Get`.
Unlike a [projection](projections.fsx), a lookup has no fixed key set: it creates cells for
requested keys. For the common case of selection membership, use a [selector](selectors.fsx).
When the source changes, it recomputes live cells for the keys returned by `affected prev next`.

:::details Cell lifetime
An unobserved cell is evicted on the next source transition or read of another key.
:::


*)
let stock = createSignal (Map [ "apples", 3; "pears", 0 ])

let changedKeys (prev: Map<string, int>) (next: Map<string, int>) =
    Seq.append prev.Keys next.Keys
    |> Seq.distinct
    |> Seq.filter (fun k -> prev.TryFind k <> next.TryFind k)

let onHand =
    createLookup (fun (m: Map<string, int>) k -> m.TryFind k |> Option.defaultValue 0) changedKeys (fun () -> stock.Value)

onHand.Get "apples", onHand.Get "plums"
(**

```text
(3, 0)
```

:::warning The `affected` function is a contract

`affected prev next` must name every key whose value can differ between
the two states. A key left out keeps its stale value. An extra key costs one recomputation.
:::

Change the apple count while the pear reader stays quiet. The lookup's `affected` function
names only the key whose count changed.

```fsharp map replay show=output
let stock = createSignal (Map [ "apples", 3; "pears", 0 ])
let changedKeys (previous: Map<string, int>) (next: Map<string, int>) =
    Seq.append previous.Keys next.Keys
    |> Seq.distinct
    |> Seq.filter (fun key -> previous.TryFind key <> next.TryFind key)
let onHand = createLookup (fun (state: Map<string, int>) key -> state.TryFind key |> Option.defaultValue 0) changedKeys (fun () -> stock.Value)
let mutable appleRuns = 0
let mutable pearRuns = 0
createEffect (fun () ->
    appleRuns <- appleRuns + 1
    printfn "apples = %d" (onHand.Get "apples"))
createEffect (fun () ->
    pearRuns <- pearRuns + 1
    printfn "pears = %d" (onHand.Get "pears"))

controls [
    button "Add an apple" (fun () -> stock.Value <- stock.Value.Add ("apples", 4))
    |> describe "Only the apple cell and its reader update."
    |> expect "The pear reader stays quiet." (fun () -> appleRuns = 2 && pearRuns = 1)
    button "Add a pear" (fun () -> stock.Value <- stock.Value.Add ("pears", 1))
    |> describe "Only the pear cell and its reader update."
    |> expect "The apple reader stays quiet." (fun () -> appleRuns = 2 && pearRuns = 2)
]
```

:::details Failed and pending lookup cells

A key whose computation throws holds the exception, and is recomputed on every later transition until
it succeeds. A pending source suspends every live cell until it settles.

:::

*)
(*** hide ***)
active.Dispose ()
graph.Dispose ()
