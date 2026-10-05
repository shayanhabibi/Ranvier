---
title: Roots and owners
---

Every memo and effect belongs to an owner. Disposing that owner runs its cleanups and disposes
its children. Disposing it again is safe.

The owner can be the graph's `Root`, a scope created by `createRoot`, or the current run of an
enclosing effect, owning memo or boundary.

## Create a scope

- `createRoot body` creates a scope, runs `body` with its `Owner`, and returns `body`'s result.
- `owner.Dispose ()` disposes everything created inside that scope.
- `graph.Dispose ()` disposes every node the graph owns. Use `use graph = new Graph ()` to dispose
  the graph automatically when the enclosing scope ends.

In this replay, disposing the root removes its effect's dependency. The signal remains available,
but later writes no longer run that effect.

```fsharp map replay code=collapsed
let count = createSignal 0
let mutable runs = 0
let owner =
    createRoot (fun owner ->
        createEffect (fun () ->
            runs <- runs + 1
            printfn "count = %d" count.Value)
        owner)

controls [
    button "Write 1" (fun () -> count.Value <- 1)
    |> describe "The root's effect responds while its owner is alive."
    |> expect "The effect responds before disposal." (fun () -> runs = 2)
    button "Dispose root" (fun () -> owner.Dispose ())
    |> describe "Disposal removes the effect and its dependency edge."
    |> expect "Disposal does not run the effect again." (fun () -> runs = 2)
    button "Write 2" (fun () -> count.Value <- 2)
    |> describe "The signal changes, but the disposed effect stays silent."
    |> expect "The disposed effect stays silent." (fun () -> runs = 2 && count.Peek = 2)
]
```

## Disposal

:::details Disposal order and nested roots

An owner runs its cleanups first, then disposes its children. Each group runs in reverse creation
order.

A root created inside an effect belongs to that effect's current run. It is disposed before the
effect's next run.
:::

:::warning Leaving a root or graph undisposed

A memo retains its source dependencies, and an effect keeps responding to changes, until its owner
is disposed. Dispose each `createRoot` owner when its work ends. Dispose the graph with
`use graph = new Graph ()` or `graph.Dispose ()`.
:::

Register subscriptions and other resources with [Cleanup](cleanup.md).
