---
title: Batch
---

`batch body` groups writes and defers effects until the outermost batch ends. Several writes
produce one run per effect, using the final values. `batch` returns the body's result.

**Memos still refresh when read inside a batch**, using the writes made so far.

::::details Test your understanding

After both names change, what does `full.Value` return inside the batch? When does the effect log
the new name, and when is `"batch result"` logged?

```fsharp
let batchLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let first = createSignal "Ada"
    let last = createSignal "Lovelace"
    let full = createMemo (fun _ -> $"{first.Value} {last.Value}")
    let log = ResizeArray ()
    createEffect (fun () -> log.Add $"effect: {full.Value}")

    let result =
        batch (fun () ->
            first.Value <- "Grace"
            last.Value <- "Hopper"
            log.Add $"memo inside the batch: {full.Value}"
            "batch result")

    log.Add result
    List.ofSeq log

batchLog |> List.iter (printfn "%s")
```

:::details Answer

```text
effect: Ada Lovelace
memo inside the batch: Grace Hopper
effect: Grace Hopper
batch result
```

Reading `full.Value` refreshes the memo inside the batch. The effect runs when the batch ends,
before `batch` returns its result to the caller.

:::
::::

In the map, two separate writes run the effect twice. The same two writes in a batch run it once.

```fsharp map replay show=output
let a = createSignal 0
let b = createSignal 0
let sum = createMemo (fun _ -> a.Value + b.Value)
let mutable effectRuns = 0
createEffect (fun () ->
    effectRuns <- effectRuns + 1
    printfn $"effect: {sum.Value}")

controls [
    button "Two writes" (fun () ->
        a.Value <- a.Value + 1
        b.Value <- b.Value + 1)
    |> describe "Separate writes run the effect twice after setup."
    |> expect "Separate writes run the effect twice after setup." (fun () -> effectRuns = 3 && sum.Peek = 2)
    button "Two writes in a batch" (fun () ->
        batch (fun () ->
            a.Value <- a.Value + 1
            b.Value <- b.Value + 1))
    |> describe "The batch runs the effect once with the final sum of 4."
    |> expect "The batch runs the effect once with the final sum of 4." (fun () -> effectRuns = 4 && sum.Peek = 4)
]
```

:::details Flushing early and handling exceptions

`flush ()` runs queued effects immediately, including inside a batch.

A batch that throws still ends. Later writes flush normally, and effects queued by the failed
batch's writes run at the next flush.
:::

## Reading effect output

:::warning Reading an effect's side effect inside a batch

Effects wait until the outermost batch ends. Inside the batch, a value written by an effect still
has the state left by the previous run. Read a memo when you need a current derived value.
:::

::::details Test your understanding

The signal changes to `5` inside the batch. What is `mirrored` inside the batch, and after it ends?

```fsharp
let sideEffectInsideBatch, sideEffectAfterBatch =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 0
    let mutable mirrored = 0
    createEffect (fun () -> mirrored <- count.Value)

    let inside =
        batch (fun () ->
            count.Value <- 5
            mirrored)

    inside, mirrored

printfn "inside the batch: %d, after the batch: %d" sideEffectInsideBatch sideEffectAfterBatch
```

:::details Answer

```text
inside the batch: 0, after the batch: 5
```

The effect updates `mirrored` only when the batch ends.

:::
::::
