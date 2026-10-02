---
title: Async sources
---

`createAsyncSource<'T> ()` creates a source that starts pending and is completed by hand.
Use it to publish outcomes from callbacks, an external producer, or deterministic tests.

Construct a graph with a manual dispatcher for the examples below:

```fsharp
open System
open System.Threading
open System.Threading.Tasks
open Ranvier

let newGraph () =
    new Graph ({ GraphOptions.Default with Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher) })
```

When a completion arrives from another thread, pump the graph on its owning thread. See
[Threading and dispatch](threading.md) and the `pumpUntil` helper in [Testing async state](testing.md).

`Settle value` publishes a value, and `Fail exn` publishes a failure. Both can be called more than
once. Neither returns the source to pending; use an [async memo](async-memos.md) for requests
that start again when tracked inputs change.

- `Settle` applies no equality cutoff. Settling twice publishes the second value, and settling with the
  value already held still wakes every reader.
- A failed source can be settled later. The settle clears the `Error` flag.
- A source settled before anything reads it is a plain value to its first reader, which is not
  pending at any point.

```fsharp
let graph = newGraph ()
let price = graph.Run (fun () -> createAsyncSource<int> ())
let shown = graph.Run (fun () -> createMemo (fun _ -> price.Value))

price.Settle 10
shown.Value |> ignore
price.Settle 10
shown.Value |> ignore
shown.Runs
```

```text
2
```

```fsharp
price.Fail (exn "offline")
let whileFailed = shown.TryValue
price.Settle 12
whileFailed, shown.TryValue, price.Status
```

```text
(Failed ..., Ready 12, None)
```

## Equality and check marks

An async source has **no equality cutoff**. Every settlement marks direct readers dirty,
and memos forward check marks to their own readers before recomputing. A memo that returns
an equal value can stop the downstream bodies from running, but those readers still perform
their checks. This differs from an equal [signal write](signals.fsx#equality), which marks nobody.

The source below is already settled when the observers are created. Settle the same value again
to see the memo re-run and the effect check its dependency without running its body.

```fsharp map replay show=output
let sourceGraph = Graph.Current
let source = createAsyncSource<int> ()
source.Settle 10
let shown = createMemo (fun _ -> source.Value)
let mutable effectRuns = 0
createEffect (fun () ->
    effectRuns <- effectRuns + 1
    printfn "shown = %d" shown.Value)
let mutable settlementMarks = [||]

controls [
    button "Settle 10 again" (fun () ->
        let offset = (Trace.events sourceGraph).Length
        source.Settle 10
        settlementMarks <- Trace.events sourceGraph |> Array.skip offset)
    |> describe "The memo runs again; its equal result lets the effect's check resolve clean."
    |> expect "Equal settlement dirties the memo and marks downstream readers for checking."
        (fun () -> shown.Runs = 2 && effectRuns = 1
                   && (settlementMarks |> Array.exists (fun event -> event.Kind = TraceEventKind.Mark && event.Arg = 2))
                   && (settlementMarks |> Array.exists (fun event -> event.Kind = TraceEventKind.Mark && event.Arg = 1))
                   && (settlementMarks |> Array.exists (fun event -> event.Kind = TraceEventKind.CheckResolved && event.Flag = 0)))
    button "Settle 12" (fun () -> source.Settle 12)
    |> describe "The changed memo value reaches the effect."
    |> expect "Changed settlement reaches the effect."
        (fun () -> shown.Runs = 3 && effectRuns = 2 && shown.Peek = 12)
]
```

Changing from pending or failed to ready also changes what readers observe, even if the ready
value matches an earlier value. State transitions are part of the memo's cutoff decision.
