---
title: Pending and failures
---

Examples using tasks import the .NET types and construct a graph with a manual dispatcher:

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

Every node reports a `Status`, which is a `[<Flags>]` enum:

| Flag | Meaning |
|------|---------|
| `Status.None` | The node holds a settled value. |
| `Status.Pending` | The node is waiting on a value that has not arrived. |
| `Status.Error` | The node's last run failed. Reads report the exception. |
| `Status.Uninitialized` | The node has not produced a value yet. |

The flags combine: an `AsyncSource` that has not settled reports `Pending ||| Uninitialized`.

Pending is independent of dirtiness. A dirty node is out of date and recomputes on its next read. A
pending node has run and is waiting on a source. Pending propagates: a memo, effect or boundary that
reads a pending node becomes pending itself, until a [boundary](boundaries.md) stops it.

There are two ways to read a node that may be pending:

- `TryValue` returns a `Reading<'T>`: `Ready value`, `Pending`, or `Failed error`.
- `.Value` returns the value. On a pending node it raises `NotReadyException`, which carries the
  source that is not ready. On a failed node it raises the stored exception.

Both reads link the same dependency edge, so a reader that saw `Pending` is woken when the source
settles.

Settle the source, fail it, then settle it again. The state travels through the memo;
the boundary supplies a display value for the effect.

```fsharp map replay show=output
let price = createAsyncSource<int> ()
let total = createMemo (fun _ -> price.Value * 3)
let view = createBoundary (fun _ -> "Loading") (fun ex _ -> "Error: " + ex.Message) (fun () -> sprintf "Total %d" total.Value)
createEffect (fun () -> printfn "%s" view.Value)

controls [
    button "Settle 4" (fun () -> price.Settle 4)
    |> describe "The source settles at 4; the boundary displays Total 12."
    |> expect "The source settles at 4; the boundary displays Total 12." (fun () -> view.Peek = "Total 12")
    button "Fail" (fun () -> price.Fail (exn "offline"))
    |> describe "The boundary catches the failed source and displays the error."
    |> expect "The boundary catches the failed source and displays the error." (fun () -> view.Peek = "Error: offline")
    button "Recover with 5" (fun () -> price.Settle 5)
    |> describe "A new settlement clears the error and displays Total 15."
    |> expect "A new settlement clears the error and displays Total 15." (fun () -> view.Peek = "Total 15")
]
```

:::details Catching pending reads and using untrack

A body that catches `NotReadyException` in its own `try/with` is still pending, whatever it returns.
A read inside `untrack` is the exception: the body's result stands. A pending read inside `untrack`
that the body does not catch leaves the body pending with no edge to the source, so settling the
source does not re-run it. Read the source tracked, or catch the exception.

:::

```fsharp
/// Renders a reading with the exception message only.
let show (reading: Reading<'T>) =
    match reading with
    | Ready v -> sprintf "Ready %A" v
    | Pending -> "Pending"
    | Failed e -> sprintf "Failed %s: %s" (e.GetType().Name) e.Message

let graph = newGraph ()

let user, greeting =
    graph.Run (fun () ->
        let user = createAsyncSource<string> ()
        let greeting = createMemo (fun _ -> "Hello, " + user.Value)
        user, greeting)

greeting.TryValue
```

```text
Pending
```

Settling the source wakes the memo:

```fsharp
user.Settle "Ada"
greeting.TryValue
```

```text
Ready "Hello, Ada"
```

A pending read stops an effect's body at that read. The body runs again from the start when the
source settles:

::::details Test your understanding

Does this effect add a message before the name settles? What is in the log afterward?

```fsharp
let log = ResizeArray<string> ()

let name =
    graph.Run (fun () ->
        let name = createAsyncSource<string> ()
        createEffect (fun () -> log.Add ("saw " + name.Value))
        name)

let beforeSettle = log.Count
name.Settle "Grace"
beforeSettle, List.ofSeq log
```

:::details Answer

```text
(0, ["saw Grace"])
```

:::
::::

A reader that suspended re-runs from the start of its body when the source settles, not from the
read that suspended. See
[Re-running versus resuming](../concepts/suspension.md#re-running-versus-resuming) for the reasoning.

## Failures

A failure is a settled outcome, not a slow success. A memo whose source fails settles as `Failed`,
and its readers see the error, not `Pending`. The exception survives unchanged through every memo
between the source and the reader.

A throwing effect does not stop the flush. Every effect queued behind it still runs. `createEffect`
returns `unit`, so an effect's error is readable only on an `Effect` constructed directly with
`new Effect (graph, body)`, through its `Status` and `Error`:

::::details Test your understanding

If the first effect throws, does the second still run? Where is the exception stored?

```fsharp
let effectGraph = newGraph ()
let count = effectGraph.Run (fun () -> createSignal 1)
let seen = ResizeArray<int> ()
let failing = new Effect (effectGraph, (fun () -> failwithf "boom %d" count.Value))
effectGraph.Run (fun () -> createEffect (fun () -> seen.Add count.Value))

count.Value <- 2
failing.Status, failing.Error.Message, List.ofSeq seen
```

:::details Answer

```text
(Error, "boom 2", [1; 2])
```

:::
::::
