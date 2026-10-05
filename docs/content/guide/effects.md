---
title: Effects
---

`createEffect body` runs the body and tracks what it reads. When a tracked value changes, the
effect runs again.

Use effects to connect reactive state to external work, such as logging, subscriptions or UI updates.
Use a [memo](memos.md) when you need a derived value that other computations can read.

```fsharp map replay code=collapsed
let count = createSignal 0
let mutable messages = []
createEffect (fun () ->
    let value = count.Value
    messages <- messages @ [value]
    printfn "count = %d" value)

controls [
    button "Write 1" (fun () -> count.Value <- 1)
    |> describe "The effect runs synchronously for the changed value."
    |> expect "The effect sees its initial value and the write." (fun () -> messages = [0; 1])
    button "Write 1 again" (fun () -> count.Value <- 1)
    |> describe "The equal write does not run the effect."
    |> expect "The equal write adds no message." (fun () -> messages = [0; 1])
]
```

Outside a batch or an existing flush, the first run is immediate, and effects triggered by a write
run before that write returns. Inside a [batch](batch.md), effects wait until the batch ends.

::::details Test your understanding

The first effect writes `fahrenheit`; the second reads it. In what order are the messages logged
when `celsius` changes to `100`? Does the Fahrenheit effect run before the write returns?

```fsharp
let effectLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let log = ResizeArray ()
    let celsius = createSignal 0
    let fahrenheit = createSignal 32

    createEffect (fun () ->
        log.Add $"celsius effect: {celsius.Value}"
        fahrenheit.Value <- celsius.Value * 9 / 5 + 32)

    createEffect (fun () -> log.Add $"fahrenheit effect: {fahrenheit.Value}")

    log.Add "-- write celsius <- 100"
    celsius.Value <- 100
    log.Add "-- write returned"
    List.ofSeq log

effectLog |> List.iter (printfn "%s")
```

:::details Answer

```text
celsius effect: 0
fahrenheit effect: 32
-- write celsius <- 100
celsius effect: 100
fahrenheit effect: 212
-- write returned
```

The Celsius effect writes `fahrenheit`, which schedules the Fahrenheit effect in the same flush.
Both run before the outer write returns.

:::
::::

:::details Creating or writing from inside a flush

An effect created inside a batch or a running flush first runs when that batch or flush reaches it.
A write inside an effect joins the flush already running; the effects it wakes run before the
outer write returns.
:::

`createEffect` returns `unit`. Its lifetime is managed by the enclosing scope; see
[Scopes and disposal](roots.md).

:::tip Keep a handle when you need one
Construct `new Effect (graph, body)` to inspect its `Status`, `Runs` or `Error`, or to dispose it
individually.
:::

::::details Test your understanding

How many times does this effect run? Does the write after disposal run it again?

```fsharp
let handleRuns =
    use graph = new Graph ()
    let count = Signal (graph, 0)
    let effect = new Effect (graph, fun () -> count.Value |> ignore)
    count.Value <- 1
    effect.Dispose ()
    count.Value <- 2
    effect.Runs

printfn "runs: %d" handleRuns
```

:::details Answer

```text
runs: 2
```

It runs on construction and on the first write. Disposal stops it responding to later writes.

:::
::::

:::details Changes made during a flush

- An effect that disposes itself, or disposes an effect queued after it, stops that effect running.
- An effect that writes a signal it reads re-runs until the value stops changing.
- `flush ()` called from inside an effect body joins the flush already running.

```fsharp
let convergedAt, convergeRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 0
    let mutable runs = 0

    createEffect (fun () ->
        runs <- runs + 1
        if count.Value < 3 then count.Value <- count.Value + 1)

    count.Value, runs

printfn "converged at %d after %d runs" convergedAt convergeRuns
```

```text
converged at 3 after 4 runs
```

:::

## Failures

:::warning Missing effect errors

An exception in an effect body is stored in `Effect.Error`. It does not escape the write or flush,
and later effects still run.

`createEffect` returns `unit`. Use `new Effect (graph, body)` when you need to inspect errors.
:::

::::details Test your understanding

When the first effect throws, does the write return? Does the second effect run? Where can you
find the exception?

```fsharp
let writeReturned, effectError, laterEffectRan =
    use graph = new Graph ()
    let count = Signal (graph, 0)

    let failing =
        new Effect (graph, fun () -> if count.Value > 0 then failwith "boom")

    let mutable laterEffectRan = false
    new Effect (graph, fun () -> if count.Value > 0 then laterEffectRan <- true) |> ignore

    count.Value <- 1
    true, failing.Error.Message, laterEffectRan

printfn "write returned: %b, Effect.Error: %s, later effect ran: %b" writeReturned effectError laterEffectRan
```

:::details Answer

```text
write returned: true, Effect.Error: boom, later effect ran: true
```

The write returns and the later effect runs. The failing effect keeps the exception in its `Error`
property.

:::
::::
