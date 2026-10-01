---
title: Getting started
order: 3
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

How to create a graph, read and write signals, derive values and run effects, and control their lifetime.

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
]
```

## The graph

Every node belongs to a `Graph`. `new Graph ()` uses `GraphOptions.Default`.

The `Api` functions resolve the graph through `Graph.Current`, so a graph has to be active on the
calling thread before any of them run.
`graph.Run body` activates the graph, runs `body`, and restores the previous graph.

```fsharp
let fromRun =
    use graph = new Graph()
    // Graph is active in action
    graph.Run (fun () -> (createSignal 2).Value)
    // Restore previous graph (if there was one)
```

`graph.Activate ()` makes the graph active until the returned handle is disposed.

```fsharp
let fromActivate =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 1
    count.Value
```
```fsharp
printfn "Activate: %d, Run: %d" fromActivate fromRun
(* Activate: 1, Run: 2 *)
```

:::details The `Api` functions require an active graph and throw `InvalidOperationException` without one.

```fsharp
let outsideMessage =
    try
        createSignal 0 |> ignore
        "no exception"
    with :? InvalidOperationException as e ->
        e.Message

printfn "%s" outsideMessage
```

```text
No ambient graph on this thread. Activate one with `use _ = graph.Activate ()`, or construct nodes against an explicit graph.
```
:::

::: details Activation is a stack: disposing a nested activation restores the graph that was active before it. The active graph is per thread, so a graph activated on one thread is not active on another.

```fsharp
let inner, restored, onOtherThread =
    use outerGraph = new Graph ()
    use innerGraph = new Graph ()
    use _ = outerGraph.Activate ()

    let inner =
        use _ = innerGraph.Activate ()
        obj.ReferenceEquals (Graph.Current, innerGraph)

    let restored = obj.ReferenceEquals (Graph.Current, outerGraph)

    let mutable onOtherThread = true
    let thread = Threading.Thread (fun () -> onOtherThread <- Graph.TryCurrent.IsSome)
    thread.Start ()
    thread.Join ()

    inner, restored, onOtherThread

printfn "inner active: %b, outer restored: %b, visible on another thread: %b" inner restored onOtherThread
```

```text
inner active: true, outer restored: true, visible on another thread: false
```
:::

`Graph.Run` is an F# extension member. From C#, `Ranvier.CSharp` provides it as an extension method; see [C#](csharp.md).

## Signals

`createSignal initial` returns the `Signal<'T>` itself.

| Member | Meaning |
| --- | --- |
| `.Value` | Reads the value and records a dependency when a memo or effect is running |
| `.Value <- x` | Writes the value |
| `.Peek` | Reads the value without recording a dependency |
| `.TryValue` | Reads the value as a `Reading<'T>` (`Ready`, `Pending` or `Failed`) and records a dependency |

```fsharp
use graph = new Graph ()
use _ = graph.Activate ()

let count = createSignal 1
count.Value // 1

count.Value <- 2
count.Value // 2
count.Peek // 2
count.TryValue // Ready 2
```

## Memos

`createMemo compute` returns a `Memo<'T>`, a derived value that recomputes when something it read
has changed. A memo is lazy: it runs on its first read, and its `Status` is `Uninitialized` until
then. `compute` receives the memo's [previous value](#the-previous-value); the examples before this section ignore it
with `fun _ ->`.

`Peek` returns the last computed value untracked, as stored, even when the memo is stale.

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

```fsharp map replay show=output
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
    button "Set3" (fun _ -> value.Value <- 3)
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

## Effects

`createEffect body` runs the body and tracks what it reads. When a tracked value changes, the
effect runs again.

Outside a batch or an existing flush, the first run is immediate, and effects triggered by a write
run before that write returns. Inside a [batch](#batch), effects wait until the batch ends.

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
[Scopes and disposal](#scopes-and-disposal).

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

## Equality cutoff

A write equal to the current value stops there: it does not schedule its readers.

A memo that recomputes to an equal value stops propagation in the same way. This can happen at
any level of a chain.

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

```fsharp map
let count = createSignal 2
let isEven = createMemo (fun _ -> count.Value % 2 = 0)
let label = createMemo (fun _ -> if isEven.Value then "even" else "odd")
createEffect (fun () -> printfn "%s" label.Value)

controls [
    button "Write 2 (equal)" (fun () -> count.Value <- 2)
    button "Write 4" (fun () -> count.Value <- 4)
    button "Write 3" (fun () -> count.Value <- 3)
]
```

### Choosing equality

`GraphOptions.Equality` sets the comparison for every signal and memo in the graph.

- `JsIdentityPolicy`, the default, follows JavaScript `===`.
- `StructuralPolicy` uses `EqualityComparer<'T>.Default`.

:::details How the policies compare values on .NET

| Value kind | `JsIdentityPolicy` (default) | `StructuralPolicy` |
| --- | --- | --- |
| Primitives, strings, structs, struct tuples | By value | By value |
| Records, unions, `Some x`, other reference types | By reference: an equal copy propagates | By value: an equal copy is cut off |
| `None` over `None` | Cut off (`None` is `null`) | Cut off |
| `nan` over `nan` | Propagates (IEEE: `nan` equals nothing) | Cut off |
| `0.0` over `-0.0` | Cut off | Cut off |

:::

:::tip Write a new object to propagate a change
Under `JsIdentityPolicy`, mutating a referenced object in place and writing the same reference back
is cut off. Write a new value to propagate a change.
:::

:::details Compare the policies on .NET

This example counts the effect runs caused by each write, excluding the initial run.

```fsharp
type Point = { X: int; Y: int }

let countWakes (options: GraphOptions) (first: 'T) (second: 'T) =
    use graph = new Graph (options)
    use _ = graph.Activate ()
    let source = createSignal first
    let mutable runs = 0
    createEffect (fun () -> source.Value |> ignore; runs <- runs + 1)
    source.Value <- second
    runs - 1

let structural = { GraphOptions.Default with Equality = StructuralPolicy () }

let equalityRows =
    [ "equal record", countWakes GraphOptions.Default { X = 1; Y = 2 } { X = 1; Y = 2 },
                      countWakes structural { X = 1; Y = 2 } { X = 1; Y = 2 }
      "Some 1 over Some 1", countWakes GraphOptions.Default (Some 1) (Some 1),
                            countWakes structural (Some 1) (Some 1)
      "None over None", countWakes GraphOptions.Default (None: int option) None,
                        countWakes structural (None: int option) None
      "nan over nan", countWakes GraphOptions.Default nan nan, countWakes structural nan nan
      "0.0 over -0.0", countWakes GraphOptions.Default 0.0 -0.0, countWakes structural 0.0 -0.0 ]

for name, js, st in equalityRows do
    printfn "%-20s default wakes: %d, structural wakes: %d" name js st
```

```text
equal record         default wakes: 1, structural wakes: 0
Some 1 over Some 1   default wakes: 1, structural wakes: 0
None over None       default wakes: 0, structural wakes: 0
nan over nan         default wakes: 1, structural wakes: 0
0.0 over -0.0        default wakes: 0, structural wakes: 0
```

:::

:::details Differences between .NET and Fable

The table below shows whether writing an equal but separately built value is cut off. Some value
types, including `DateTime` and `decimal`, compile to objects under Fable, so the default comparison
can differ between targets.

Writing the same object instance back is cut off on both targets.

| Type | Default on .NET | Default under Fable | `StructuralPolicy`, both targets |
| --- | --- | --- | --- |
| `int`, `float`, `string` | Cut off | Cut off | Cut off |
| `nan` | Propagates | Propagates | Cut off on .NET, propagates under Fable |
| `DateTime`, `DateTimeOffset`, `decimal` | Cut off | Propagates | Cut off |
| Struct records, struct tuples | Cut off | Propagates | Cut off |
| `Some 1` | Propagates | Cut off | Cut off |
| `None` | Cut off | Cut off | Cut off |
| Records, tuples, lists, `Some` of a record | Propagates | Propagates | Cut off |
| Class without custom equality | Propagates | Propagates | Propagates |

:::

:::details Define a custom equality policy

Pass an `IEqualityPolicy` as `GraphOptions.Equality`. Its `Comparer<'T>` supplies the comparer for
each value type and is called once when a node is created. Individual nodes cannot take their own
comparers.

For example, this graph treats strings that differ only in case as equal:

```fsharp
type CaseInsensitivePolicy() =
    interface IEqualityPolicy with
        member _.Comparer<'T>() =
            { new System.Collections.Generic.IEqualityComparer<'T> with
                member _.Equals(a, b) =
                    System.String.Equals (string (box a), string (box b), System.StringComparison.OrdinalIgnoreCase)
                member _.GetHashCode a = (string (box a)).ToLowerInvariant().GetHashCode () }

let caseInsensitiveWakes =
    countWakes { GraphOptions.Default with Equality = CaseInsensitivePolicy () } "abc" "ABC"

printfn "case-insensitive wakes: %d" caseInsensitiveWakes
```

```text
case-insensitive wakes: 0
```

:::

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

```fsharp map
let useFirst = createSignal true
let first = createSignal "a"
let second = createSignal "x"
createEffect (fun () -> printfn "%s" (if useFirst.Value then first.Value else second.Value))

controls [
    button "Toggle branch" (fun () -> useFirst.Value <- not useFirst.Value)
    button "Write first" (fun () -> first.Value <- first.Value + "!")
    button "Write second" (fun () -> second.Value <- second.Value + "!")
]
```

## Untrack

`untrack body` reads values without making them dependencies of the surrounding computation.
Use it when you need a value but do not want changes to that value to trigger another run.

Unlike `Peek`, an untracked read of `memo.Value` refreshes a stale memo.

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

## Batch

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

```fsharp map
let a = createSignal 0
let b = createSignal 0
let sum = createMemo (fun _ -> a.Value + b.Value)
createEffect (fun () -> printfn $"effect: {sum.Value}")

controls [
    button "Two writes" (fun () ->
        a.Value <- a.Value + 1
        b.Value <- b.Value + 1)
    button "Two writes in a batch" (fun () ->
        batch (fun () ->
            a.Value <- a.Value + 1
            b.Value <- b.Value + 1))
]
```

:::details Flushing early and handling exceptions

`flush ()` runs queued effects immediately, including inside a batch.

A batch that throws still ends. Later writes flush normally, and effects queued by the failed
batch's writes run at the next flush.
:::

## Scopes and disposal

Every memo and effect belongs to an owner. Disposing that owner runs its cleanups and disposes
its children. Disposing it again is safe.

The owner can be the graph's `Root`, a scope created by `createRoot`, or the current run of an
enclosing effect, owning memo or boundary.

### Create a scope

- `createRoot body` creates a scope, runs `body` with its `Owner`, and returns `body`'s result.
- `owner.Dispose ()` disposes everything created inside that scope.
- `graph.Dispose ()` disposes every node the graph owns. Use `use graph = new Graph ()` to dispose
  the graph automatically when the enclosing scope ends.

### Register cleanup

`onCleanup f` registers cleanup with the innermost scope. Inside an effect, owning memo or boundary,
it runs **before the next run** and **on disposal**.

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

:::details Disposal order and nested roots

An owner runs its cleanups first, then disposes its children. Each group runs in reverse creation
order.

A root created inside an effect belongs to that effect's current run. It is disposed before the
effect's next run.
:::

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

:::details Dispose an async memo while pending

It reads as `Failed` with an `ObjectDisposedException`. Its readers are woken once to see the
failure.
:::

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

## Common mistakes

:::warning No active graph

Call `Api` functions such as `createSignal` inside `graph.Run` or while a `graph.Activate ()`
handle is in scope. Without an active graph, they throw `InvalidOperationException`.

Activation is per thread: a thread-pool callback needs its own activation. See
[Troubleshooting](troubleshooting.md).
:::

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

:::warning Leaving a root or graph undisposed

A memo retains its source dependencies, and an effect keeps responding to changes, until its owner
is disposed. Dispose each `createRoot` owner when its work ends. Dispose the graph with
`use graph = new Graph ()` or `graph.Dispose ()`.
:::

:::warning Expecting Peek to refresh a stale memo

`Peek` returns the last computed value. Use `untrack (fun () -> memo.Value)` for a current value
without recording a dependency.
:::

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

## Key types

- `Graph` — contains the reactive nodes and manages their lifetime.
- `Signal<'T>` — a value you can read and write.
- `Memo<'T>` — a value derived from tracked reads.
- `Effect` — a computation that reacts to changes in tracked values.
- `Owner` — a scope that owns nodes and cleanups.
- `GraphOptions` — graph configuration, including the equality policy.
- `Api` module — functions such as `createSignal`, `createMemo` and `createEffect` that use the active graph.
