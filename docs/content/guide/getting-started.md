---
title: Getting started
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Create a graph, store state in signals, derive values with memos, and use effects to connect
that state to your application.

## A first graph

```fsharp
open Ranvier

let graph = new Graph()
```

A signal holds a value `let count = createSignal 1`{fsharp}.

A memo derives a value `let doubled = createMemo (fun _ -> count.Value * 2)`{fsharp}.

And an effect runs when what it read changes:

```fsharp {4}
graph.Run (fun () ->
    let count = createSignal 1
    let doubled = createMemo (fun _ -> count.Value * 2)
    createEffect (fun () -> printfn "doubled = %d" doubled.Value)
    count.Value <- 5
    doubled)
```

```fsharp map replay show=output
let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)

createEffect (fun () -> printfn $"doubled = %d{doubled.Value}")

controls [
    button "Set" (fun () -> count.Value <- 5)
    |> describe "The write refreshes doubled from 2 to 10."
    |> expect "The write refreshes doubled from 2 to 10." (fun () -> doubled.Peek = 10)
]
```

## Make the graph active

The `create*` functions use the active graph. `graph.Run` activates it for the body and restores
the previous graph afterward. For a longer scope, keep the handle returned by `graph.Activate ()`:

```fsharp
open Ranvier

use graph = new Graph ()
use active = graph.Activate ()

let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)
createEffect (fun () -> printfn "doubled = %d" doubled.Value)

count.Value <- 5
```

Disposing the graph releases its computations and runs their cleanups. In an application, keep
the graph alive for as long as its state is needed.

## Choose the next concept

- [Graphs](graph.fsx): activation, explicit construction and graph configuration.
- [Signals](signals.fsx): tracked reads, writes and equality.
- [Memos](memos.md): lazy derived values, previous values and dynamic dependencies.
- [Effects](effects.md): external work, scheduling and failures.
- [Roots and owners](roots.md) and [Cleanup](cleanup.md): lifetimes and resource release.
- [Untrack](untrack.md): read current values without subscribing.
- [Batch](batch.md): combine writes into one effect flush.
- [Editable values](editable.md) and [Drafts](drafts.md): local edits over upstream state.

Continue with [Async and pending](async-and-pending.md) for loading and errors, or
[Collections](collections.fsx) for per-row reactivity and aggregates.
