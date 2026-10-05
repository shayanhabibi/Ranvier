---
title: Untrack
---

`untrack body` reads values without making them dependencies of the surrounding computation.
Use it when you need a value but do not want changes to that value to trigger another run.

Unlike `Peek`, an untracked read of `memo.Value` refreshes a stale memo.

Change **Ignored**, then **Tracked**. Only the tracked signal has an edge to the effect, so
only its write runs the effect again. That run still reads the current ignored value.

```fsharp map replay code=collapsed
let tracked = createSignal 0
let ignored = createSignal 0
let mutable effectRuns = 0
createEffect (fun () ->
    effectRuns <- effectRuns + 1

    let current = untrack (fun () -> ignored.Value)
    printfn "tracked = %d, ignored = %d" tracked.Value current)

controls [
    button "Ignored +1" (fun () -> ignored.Value <- ignored.Value + 1)
    |> describe "The untracked read creates no edge; changing ignored does not run the effect."
    |> expect "The untracked read creates no edge; changing ignored does not run the effect." (fun () -> effectRuns = 1)
    button "Tracked +1" (fun () -> tracked.Value <- tracked.Value + 1)
    |> describe "Changing tracked runs the effect, which also sees the current ignored value."
    |> expect "Changing tracked runs the effect, which also sees the current ignored value." (fun () -> effectRuns = 2 && ignored.Peek = 1)
]
```

::::details Test your understanding

Does writing `ignored` run the effect again? Does writing `tracked`? What value does the final
untracked read of `doubled` return?

```fsharp
let untrackRuns, untrackedMemoValue =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let tracked = createSignal 0
    let ignored = createSignal 0
    let mutable runs = 0

    createEffect (fun () ->
        tracked.Value |> ignore
        untrack (fun () -> ignored.Value) |> ignore
        runs <- runs + 1)

    ignored.Value <- 1 // no re-run
    tracked.Value <- 1 // re-run

    let source = createSignal 1
    let doubled = createMemo (fun _ -> source.Value * 2)
    doubled.Value |> ignore
    source.Value <- 21
    runs, untrack (fun () -> doubled.Value)

printfn "effect runs: %d, untracked stale memo read: %d" untrackRuns untrackedMemoValue
```

:::details Answer

```text
effect runs: 2, untracked stale memo read: 42
```

The effect runs initially and when `tracked` changes. The read of `ignored` records no dependency.
The final read recomputes `doubled`, so it returns `42`.

:::
::::

:::details Nested untrack calls
Calls nest. Tracking resumes after the outermost `untrack` returns.
:::
