---
title: Cleanup
---

Use [roots and owners](roots.md) to control the lifetime of reactive nodes.

`onCleanup f` registers cleanup with the innermost scope. Inside an effect, owning memo or boundary,
it runs **before the next run** and **on disposal**.

In this replay, changing the user releases the old subscription before creating the next.
Disposing the root releases the final subscription.

```fsharp map replay code=collapsed
let user = createSignal "Ada"
let mutable released = []
let owner =
    createRoot (fun owner ->
        createEffect (fun () ->
            let name = user.Value
            printfn "subscribe %s" name
            onCleanup (fun () ->
                released <- released @ [name]
                printfn "unsubscribe %s" name))
        owner)

controls [
    button "Switch to Grace" (fun () -> user.Value <- "Grace")
    |> describe "The previous run's cleanup releases Ada before the new subscription."
    |> expect "The previous subscription is released." (fun () -> released = ["Ada"])
    button "Dispose" (fun () -> owner.Dispose ())
    |> describe "Disposal releases Grace and removes the effect."
    |> expect "Disposal releases the final subscription." (fun () -> released = ["Ada"; "Grace"])
]
```

:::info Pure memos
`onCleanup` inside a `createMemo` body raises `InvalidOperationException`. Use `createMemoWith`
when the computation needs to own nodes or cleanups.
:::

::::details Test your understanding

When does each subscription get cleaned up? Does writing `"hopper"` after disposal create a
new subscription?

```fsharp
let scopeLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let log = ResizeArray ()
    let user = createSignal "ada"

    let owner =
        createRoot (fun owner ->
            createEffect (fun () ->
                let name = user.Value
                log.Add $"subscribe {name}"
                onCleanup (fun () -> log.Add $"unsubscribe {name}"))

            owner)

    user.Value <- "grace"
    owner.Dispose ()
    user.Value <- "hopper" // the effect is gone
    List.ofSeq log

scopeLog |> List.iter (printfn "%s")
```

:::details Answer

```text
subscribe ada
unsubscribe ada
subscribe grace
unsubscribe grace
```

Changing the user cleans up the Ada subscription before subscribing to Grace. Disposing the owner
cleans up Grace and removes the effect, so the last write does nothing.

:::
::::

:::details Cleanup failures

A cleanup that throws has its error recorded; the remaining cleanups still run. A `createRoot`
scope stores the error in its own `Errors`. An effect or memo's scope records it in
`graph.Root.Errors`.
:::

:::details Writes made during cleanup

A write inside a cleanup is visible to the effect's next run and does not start another run.

```fsharp
let cleanupWriteSeen =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let trigger = createSignal 0
    let generation = createSignal 0
    let seen = ResizeArray ()

    createEffect (fun () ->
        seen.Add (trigger.Value, generation.Value)
        onCleanup (fun () -> generation.Value <- generation.Value + 1))

    trigger.Value <- 1
    List.ofSeq seen

printfn "%A" cleanupWriteSeen
```

```text
[(0, 0); (1, 1)]
```

:::
