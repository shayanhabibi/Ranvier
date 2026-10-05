---
title: Memos
---

`createMemo compute` returns a `Memo<'T>`, a derived value that recomputes when something it read
has changed. A memo is lazy: it runs on its first read, and its `Status` is `Uninitialized` until
then. `compute` receives the memo's [previous value](#the-previous-value); the examples before this section ignore it
with `fun _ ->`.

`Peek` returns the last computed value untracked, as stored, even when the memo is stale.
Use `untrack (fun () -> memo.Value)` for a current value without recording a dependency;
see [Untrack](untrack.md).

Writes leave this unobserved memo stale. **Peek** prints its stored value; **Read** refreshes it.
Watch the memo's run count as you step through the replay.

```fsharp map replay code=collapsed
let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)
doubled.Value |> ignore

controls [
    button "Write 5" (fun () -> count.Value <- 5)
    |> describe "The write leaves doubled stale; its stored value is still 2."
    |> expect "The write leaves doubled stale; its stored value is still 2." (fun () -> doubled.Peek = 2)
    button "Peek" (fun () -> printfn "stored = %d" doubled.Peek)
    |> describe "Peek returns the stored 2 without running the memo."
    |> expect "Peek returns the stored 2 without running the memo." (fun () -> doubled.Peek = 2)
    button "Read" (fun () -> printfn "current = %d" doubled.Value)
    |> describe "Reading doubled refreshes it to 10."
    |> expect "Reading doubled refreshes it to 10." (fun () -> doubled.Peek = 10)
]
```

### Glitch-free

Propagation is glitch-free. In a diamond, where two memos read one signal and an effect reads both
memos, the effect runs once per write and always sees both memos at the same write.

::::details Test your understanding

```fsharp
let a = createSignal 1
let plusOne = createMemo (fun _ -> a.Value + 1)
let timesTen = createMemo (fun _ -> a.Value * 10)
let seen = ResizeArray ()
createEffect (fun () -> seen.Add (plusOne.Value, timesTen.Value))
// How many values does seen have? What are the values?
a.Value <- 2
// How about now?
a.Value <- 3
// And now?
```

:::details Answers

1. `[(2, 10)]`
2. `[(2, 10); (3, 20)]`
3. `[(2, 10); (3, 20); (4, 30)]`

:::
::::

A memo read by several effects recomputes once per write.

::::details Test your understanding

```fsharp
let a = createSignal 1

let shared = createMemo (fun _ -> a.Value * 100)
// 1. How many times has shared run?
createEffect (fun () -> shared.Value |> ignore)
// 2. What about now?
createEffect (fun () -> shared.Value |> ignore)
// 3. and now?
a.Value <- 2
// 4. and now?
a.Value <- 3
// 5. and now?
```
:::details Answers
1. `0`
2. `1`
3. `1`
4. `2`
5. `3`
:::
::::

:::tip Visualise the tests above

```fsharp map replay code=collapsed
let value = createSignal 1
let plusOne = createMemo (fun _ -> value.Value + 1)
let timesTen = createMemo (fun _ -> value.Value * 10)
let seen = createMemo(fun _ -> plusOne.Value, timesTen.Value)
createEffect (fun () -> seen.Value |> ignore) // pull

let shared = createMemo (fun _ -> value.Value * 100)
createEffect (fun () -> shared.Value |> ignore)
createEffect (fun () -> shared.Value |> ignore)

controls [
    button "Set2" (fun _ -> value.Value <- 2)
    |> describe "Both branches settle before the shared reader observes the new pair."
    |> expect "Both branches settle before the shared reader observes the new pair." (fun () -> seen.Peek = (3, 20))
    button "Set3" (fun _ -> value.Value <- 3)
    |> describe "The shared memo runs once even though two effects read it."
    |> expect "The shared memo runs once even though two effects read it." (fun () -> seen.Peek = (4, 30) && shared.Peek = 300)
]
```
:::

### Pure and owning memos

:::info Pure

`createMemo` is a pure derivation. Its body may read anything and create signals, **but creating an
owned node** (a memo, effect, async value, boundary, root, projection, lookup or `onCleanup`) **fails**
the run with `InvalidOperationException`.

`untrack` blocks are included, and the run fails even when the body catches the exception.
:::

:::tip Owning
`createMemoWith` is the owning memo. The **nodes and cleanups its body creates belong to the run
that created them**: they are disposed before the next run and when the memo is disposed.

A __read__ of the memo from one of those cleanups returns the __previous__ value.
:::

::::details Test your understanding

How many times does a string with `"release"` print?

```fsharp
let owningLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let user = createSignal "ada"
    let log = ResizeArray ()

    let greeting =
        createMemoWith (fun _ ->
            let name = user.Value
            onCleanup (fun () -> log.Add $"release {name}")
            $"hello {name}")

    log.Add greeting.Value
    user.Value <- "grace"
    log.Add greeting.Value
    greeting.Dispose ()
    List.ofSeq log

owningLog |> List.iter (printfn "%s")
```

:::details Answer

`2`

```text
hello ada
release ada
hello grace
release grace
```

:::
::::

Construct `Memo (graph, compute)` directly for a pure memo, and `Memo (graph, compute, true)` for
an owning one. `createAsync` and `createAsyncWith` split the same way, and a boundary always owns
the nodes its body creates.

An owning computation replaces the nodes its body creates on every re-run. An async value created and
read in the same body restarts its flight each time it settles, and never settles. Create it outside
and read it inside; see
[Troubleshooting](troubleshooting.md#a-boundary-shows-its-fallback-forever-and-starts-a-flight-on-every-settle).

An owning memo's cleanups run untracked, whichever computation read the memo: a signal read by a
cleanup never becomes a dependency of the reader. A node created by a cleanup belongs to the memo
and is disposed before its next run.

The body runs once per discharge. A cleanup that writes one of the memo's sources and then reads
the memo runs the body at that read, and that run is the re-run: the memo holds one run's nodes.

### The previous value

`compute` receives the value the memo last published. Use it to accumulate a total, keep a running
maximum, or reuse part of a previous result.

Its type is `'T voption -> 'T`:

- `ValueNone` on the first run.
- `ValueSome previous` after a value has been published. This is the same value `Peek` returns.

A total advances **once per run**, using the inputs read in that run. Several writes in a batch,
or before an unobserved memo is read, contribute only their final values.

Compare a single write with two writes in a batch. The total adds `10`, then adds only the
batch's final `2`.

```fsharp map replay code=collapsed
let amount = createSignal 5
let total = createMemo (fun previous -> ValueOption.defaultValue 0 previous + amount.Value)
createEffect (fun () -> printfn "total = %d" total.Value)

controls [
    button "Add 10" (fun () -> amount.Value <- 10)
    |> describe "The memo adds 10 to its previous total of 5."
    |> expect "The memo adds 10 to its previous total of 5." (fun () -> total.Peek = 15)
    button "Batch 1 then 2" (fun () -> batch (fun () -> amount.Value <- 1; amount.Value <- 2))
    |> describe "Only the batch's final 2 is added to the previous total of 15."
    |> expect "Only the batch's final 2 is added to the previous total of 15." (fun () -> total.Peek = 17)
]
```

::::details Test your understanding

What totals does the effect see? How many times does the memo run?

```fsharp
let totals, totalRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let amount = createSignal 5
    let total = createMemo (fun prev -> ValueOption.defaultValue 0 prev + amount.Value)
    let seen = ResizeArray ()
    createEffect (fun () -> seen.Add total.Value)

    amount.Value <- 10

    batch (fun () ->
        amount.Value <- 1
        amount.Value <- 2)

    List.ofSeq seen, total.Runs

printfn "totals seen: %A, runs: %d" totals totalRuns
```

:::details Answer

```text
totals seen: [5; 15; 17], runs: 3
```

The first run adds `5`. The next adds `10`. The batch contributes `2`, so the total becomes `17`;
the intermediate `1` is never added.

:::
::::

Returning the previous value unchanged triggers an [equality cutoff](#equality-cutoff): readers
do not re-run just because the memo ran.

::::details Test your understanding

The memo below keeps the highest reading so far. Which values does the effect see? Does the memo
run for readings that leave the maximum unchanged?

```fsharp
let highs, highestRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let reading = createSignal 20

    let highest =
        createMemo (fun prev ->
            match prev with
            | ValueSome best when best >= reading.Value -> best
            | _ -> reading.Value)

    let seen = ResizeArray ()
    createEffect (fun () -> seen.Add highest.Value)

    for r in [ 18; 25; 22; 19 ] do
        reading.Value <- r

    List.ofSeq seen, highest.Runs

printfn "highs seen: %A, memo runs: %d" highs highestRuns
```

:::details Answer

```text
highs seen: [20; 25], memo runs: 5
```

The memo runs initially and for all four writes. The effect sees only `20` and `25`, because the
other runs return the previous maximum.

:::
::::

:::details Reusing part of a previous result

A new value that reuses parts of the previous one wakes the memo's readers. A memo that selects
a reused part can cut off propagation there.
:::

:::details Pending sources, failures and owned nodes

A run that suspends on a pending source, or fails, publishes nothing. The next run receives the
same previous value. Writes made while a source is pending are folded together once, by the run
that completes.

With `createMemoWith`, the previous run's nodes are disposed before `compute` runs, including any
nodes held in the previous value.
:::

:::details Cost of passing the previous value

Passing the previous value allocates nothing. In the [counter bench](../benchmarks/counters.md) it
adds about 10 instructions to a memo run under .NET and about 40 under Node.js: 2 % and 4.5 % of a
write through a chain of four memos.
:::

:::tip Accumulate in a memo
Effects take `unit -> unit`. If an effect needs an accumulated value, keep it in a memo and read
that memo from the effect.
:::

## Equality cutoff

A write equal to the current value stops there: it does not schedule its readers.

A memo that recomputes to an equal value stops downstream bodies from running. Unlike an equal
signal write, the upstream change has already marked its readers for **check**. Those checks
bring their dependencies current and resolve clean when no dependency publishes a change.
This can happen at any level of a chain. [Equality](equality.md) describes the graph-wide policy.

::::details Test your understanding

The count starts at `2`. We write `2`, then `4`. How many times do the effect and `isEven` run?

```fsharp
let cutoffRuns, parityRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 2
    let isEven = createMemo (fun _ -> count.Value % 2 = 0)
    let label = createMemo (fun _ -> if isEven.Value then "even" else "odd")
    let mutable effectRuns = 0
    createEffect (fun () -> label.Value |> ignore; effectRuns <- effectRuns + 1)

    count.Value <- 2 // equal write
    count.Value <- 4 // isEven recomputes to true again
    effectRuns, isEven.Runs

printfn "effect runs: %d, isEven runs: %d" cutoffRuns parityRuns
```

:::details Answer

```text
effect runs: 1, isEven runs: 2
```

The equal write of `2` stops at `count`. Writing `4` runs `isEven`, but its result is still `true`,
so `label` and the effect do not run again.

:::
::::

In the map, an equal write stops at `count`. A write that keeps the parity re-runs `isEven`, which recomputes to the same value and stops there. A write that flips the parity reaches the effect.

```fsharp map replay code=collapsed
let count = createSignal 2
let isEven = createMemo (fun _ -> count.Value % 2 = 0)
let label = createMemo (fun _ -> if isEven.Value then "even" else "odd")
let mutable effectRuns = 0
createEffect (fun () ->
    effectRuns <- effectRuns + 1
    printfn "%s" label.Value)

controls [
    button "Write 2 (equal)" (fun () -> count.Value <- 2)
    |> describe "An equal write leaves the effect at its initial run."
    |> expect "An equal write leaves the effect at its initial run." (fun () -> effectRuns = 1)
    button "Write 4" (fun () -> count.Value <- 4)
    |> describe "isEven remains true, so the label and effect stay unchanged."
    |> expect "isEven remains true, so the label and effect stay unchanged." (fun () -> effectRuns = 1 && label.Peek = "even")
    button "Write 3" (fun () -> count.Value <- 3)
    |> describe "isEven changes to false and the effect prints odd."
    |> expect "isEven changes to false and the effect prints odd." (fun () -> effectRuns = 2 && label.Peek = "odd")
]
```



## Dynamic dependencies

A memo or effect tracks its reads afresh on every run. If a branch stops reading a source, that
source stops triggering the computation. Taking the branch again restores the dependency.

Reading the same source twice in one run records one dependency.

::::details Test your understanding

The effect initially reads `first`. Which writes add an entry to the log after it switches to
`second`? What happens when it switches back?

```fsharp
let branchLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let useFirst = createSignal true
    let first = createSignal "a"
    let second = createSignal "x"
    let log = ResizeArray ()
    createEffect (fun () -> log.Add (if useFirst.Value then first.Value else second.Value))

    useFirst.Value <- false // now reads `second`
    first.Value <- "b" // not read by the last run: no re-run
    second.Value <- "y"
    useFirst.Value <- true // reads `first` again
    first.Value <- "c"
    List.ofSeq log

printfn "%A" branchLog
```

:::details Answer

```text
["a"; "x"; "y"; "b"; "c"]
```

Writing `"b"` to `first` adds nothing while the effect reads `second`. Switching back reads that
stored `"b"` and restores the dependency on `first`.

:::
::::

Toggle the branch to move the effect's edge between `first` and `second`. A write to the source off the branch wakes nothing.

```fsharp map replay code=collapsed
let useFirst = createSignal true
let first = createSignal "a"
let second = createSignal "x"
let mutable effectRuns = 0
createEffect (fun () ->
    effectRuns <- effectRuns + 1
    printfn "%s" (if useFirst.Value then first.Value else second.Value))

controls [
    button "Toggle branch" (fun () -> useFirst.Value <- not useFirst.Value)
    |> describe "The effect switches its dependency from first to second."
    |> expect "The effect switches its dependency from first to second." (fun () -> effectRuns = 2)
    button "Write first" (fun () -> first.Value <- first.Value + "!")
    |> describe "first is no longer a dependency, so its write leaves the effect alone."
    |> expect "first is no longer a dependency, so its write leaves the effect alone." (fun () -> effectRuns = 2)
    button "Write second" (fun () -> second.Value <- second.Value + "!")
    |> describe "second is now a dependency, so its write runs the effect."
    |> expect "second is now a dependency, so its write runs the effect." (fun () -> effectRuns = 3)
]
```

### Read a disposed memo

A disposed memo keeps its last computed value. It stops recomputing and stops waking its readers,
even if it was stale when disposed.

::::details Test your understanding

The source changes from `1` to `2`, but the memo is disposed before another read. What value does
it keep? How many times has it run?

```fsharp
let disposedValue, disposedRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let source = createSignal 1
    let tenfold = createMemo (fun _ -> source.Value * 10)
    tenfold.Value |> ignore
    source.Value <- 2
    tenfold.Dispose ()
    tenfold.Value, tenfold.Runs

printfn "value after disposal: %d, runs: %d" disposedValue disposedRuns
```

:::details Answer

```text
value after disposal: 10, runs: 1
```

Disposal keeps the stored `10`; it does not refresh the memo to `20`.

:::
::::
