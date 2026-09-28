---
title: Getting started
order: 3
---

:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

How to create a graph, read and write signals, derive values and run effects, and control their
lifetime. For in-flight values and the pending flag, see [Async and pending](async-and-pending.md);
for keyed collections, see [Collections](collections.fsx).

The sections follow the order in which the concepts build on each other. Every example on this page
was evaluated in F# Interactive against the Partas.Signals source (commit `915f139`) that Ranvier is
built from, and the output shown under it is the real output.

## Install

Ranvier is not on NuGet yet; [Installation](installation.md) covers building it from source. Once
published, reference the package from a project that targets `net10.0`:

```xml
<PackageReference Include="Ranvier" />
```

`open Ranvier` brings the `Api` module (`createSignal`, `createMemo`, `createEffect`, ...)
and the `GraphExtensions` module (`Graph.Run`) into scope. Both are `AutoOpen`.

## A first graph

A signal holds a value, a memo derives one, and an effect runs when what it read changes. The write
of `5` re-runs the memo and then the effect, before the write returns.

```fsharp
open Ranvier

let graph = new Graph ()

let doubled =
    graph.Run (fun () ->
        let count = createSignal 1
        let doubled = createMemo (fun () -> count.Value * 2)
        createEffect (fun () -> printfn "doubled = %d" doubled.Value)
        count.Value <- 5
        doubled)
```

```text
doubled = 2
doubled = 10
```

## The graph

Every node belongs to a `Graph`. `new Graph ()` uses `GraphOptions.Default`. The `Api` functions
resolve the graph through `Graph.Current`, so a graph has to be active on the calling thread before
any of them run. `graph.Activate ()` makes the graph active until the returned handle is disposed.
`graph.Run body` activates the graph, runs `body`, and restores the previous graph.

```fsharp
let fromActivate =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 1
    count.Value

let fromRun =
    use graph = new Graph ()
    graph.Run (fun () -> (createSignal 2).Value)

printfn "Activate: %d, Run: %d" fromActivate fromRun
```

```text
Activate: 1, Run: 2
```

The `Api` functions require an active graph and throw `InvalidOperationException` without one.

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

Activation is a stack: disposing a nested activation restores the graph that was active before it.
The active graph is per thread, so a graph activated on one thread is not active on another.

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

`Graph.Run` is an F# extension member. From C#, write `using (graph.Activate()) { ... }`.

## Signals

`createSignal initial` returns the `Signal<'T>` itself.

| Member | Meaning |
| --- | --- |
| `.Value` | Reads the value and records a dependency when a memo or effect is running |
| `.Value <- x` | Writes the value |
| `.Peek` | Reads the value without recording a dependency |
| `.TryValue` | Reads the value as a `Reading<'T>` (`Ready`, `Pending` or `Failed`) and records a dependency |

```fsharp
let signalReads =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 1
    count.Value <- 2
    count.Value, count.Peek, count.TryValue

printfn "%A" signalReads
```

```text
(2, 2, Ready 2)
```

## Memos

`createMemo compute` returns a `Memo<'T>`, a derived value that recomputes when something it read
has changed. A memo is lazy: it runs on its first read, and its `Status` is `Uninitialized` until
then.

```fsharp
let lazyRuns, statusBefore, firstRead, runsAfterRead =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 1
    let doubled = createMemo (fun () -> count.Value * 2)

    count.Value <- 2
    count.Value <- 3
    let lazyRuns = doubled.Runs
    let statusBefore = doubled.Status
    let firstRead = doubled.Value
    lazyRuns, statusBefore, firstRead, doubled.Runs

printfn "runs before a read: %d, status: %A" lazyRuns statusBefore
printfn "first read: %d, runs after: %d" firstRead runsAfterRead
```

```text
runs before a read: 0, status: Uninitialized
first read: 6, runs after: 1
```

`Peek` returns the last computed value untracked, as stored, even when the memo is stale.

```fsharp
let peekStale, readFresh =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 1
    let doubled = createMemo (fun () -> count.Value * 2)

    doubled.Value |> ignore
    count.Value <- 5
    let peekStale = doubled.Peek
    peekStale, doubled.Value

printfn "Peek after the write: %d, Value after the write: %d" peekStale readFresh
```

```text
Peek after the write: 2, Value after the write: 10
```

Propagation is glitch-free. In a diamond, where two memos read one signal and an effect reads both
memos, the effect runs once per write and always sees both memos at the same write. A memo read by
several effects recomputes once per write.

```fsharp
let diamondSeen, sharedRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let a = createSignal 1
    let plusOne = createMemo (fun () -> a.Value + 1)
    let timesTen = createMemo (fun () -> a.Value * 10)
    let seen = ResizeArray ()
    createEffect (fun () -> seen.Add (plusOne.Value, timesTen.Value))

    let shared = createMemo (fun () -> a.Value * 100)
    createEffect (fun () -> shared.Value |> ignore)
    createEffect (fun () -> shared.Value |> ignore)

    a.Value <- 2
    a.Value <- 3
    List.ofSeq seen, shared.Runs

printfn "diamond saw: %A" diamondSeen
printfn "shared memo runs for three states: %d" sharedRuns
```

```text
diamond saw: [(2, 10); (3, 20); (4, 30)]
shared memo runs for three states: 3
```

### Pure and owning memos

`createMemo` is a pure derivation. Its body may read anything and create signals, but creating an
owned node (a memo, effect, async value, boundary, root, projection, lookup or `onCleanup`) fails
the run with `InvalidOperationException`. `untrack` blocks are included, and the run fails even
when the body catches the exception.

`createMemoWith` is the owning memo. The nodes and cleanups its body creates belong to the run
that created them: they are disposed before the next run and when the memo is disposed. A read of
the memo from one of those cleanups returns the previous value.

```fsharp
let pureFailure =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 1
    let creating = createMemo (fun () -> (createMemo (fun () -> count.Value * 2)).Value)

    try
        creating.Value |> ignore
        "no error"
    with :? System.InvalidOperationException as ex ->
        ex.Message.Substring (0, ex.Message.IndexOf '.')

printfn "%s" pureFailure
```

```text
A memo created by createMemo created an owned node in its body: a memo, effect, async value, boundary, root, projection, lookup, selector or onCleanup
```

```fsharp
let owningLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let user = createSignal "ada"
    let log = ResizeArray ()

    let greeting =
        createMemoWith (fun () ->
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

```text
hello ada
release ada
hello grace
release grace
```

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

## Effects

`createEffect body` runs `body` once on construction, at the current flush: immediately outside a
batch or flush, otherwise when the batch or the running flush reaches it. A write to anything the
body read re-runs it on the flush at the end of that write, or at the end of the enclosing batch
(see [Batch](#batch)). A write made from inside an effect body joins the flush already running, so the effects it wakes run
before the outer write returns.

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

```text
celsius effect: 0
fahrenheit effect: 32
-- write celsius <- 100
celsius effect: 100
fahrenheit effect: 212
-- write returned
```

`createEffect` returns `unit`; the effect belongs to the enclosing scope (see
[Scopes and disposal](#scopes-and-disposal)). To read an effect's `Status`, `Runs` or `Error`, or to
dispose it on its own, construct it directly with `new Effect (graph, body)`.

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

```text
runs: 2
```

## Equality cutoff

A write equal to the current value is cut off: its readers keep their values and stay unscheduled.
A memo that recomputes to a value equal to its previous one is cut off the same way, for the nodes
below it. The cutoff applies at every level of a chain.

```fsharp
let cutoffRuns, parityRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let count = createSignal 2
    let isEven = createMemo (fun () -> count.Value % 2 = 0)
    let label = createMemo (fun () -> if isEven.Value then "even" else "odd")
    let mutable effectRuns = 0
    createEffect (fun () -> label.Value |> ignore; effectRuns <- effectRuns + 1)

    count.Value <- 2 // equal write
    count.Value <- 4 // isEven recomputes to true again
    effectRuns, isEven.Runs

printfn "effect runs: %d, isEven runs: %d" cutoffRuns parityRuns
```

```text
effect runs: 1, isEven runs: 2
```

`GraphOptions.Equality` chooses the comparison. The default, `JsIdentityPolicy`, follows JavaScript
`===`. `StructuralPolicy` compares with `EqualityComparer<'T>.Default`.

| Value kind | `JsIdentityPolicy` (default) | `StructuralPolicy` |
| --- | --- | --- |
| Primitives, strings, structs, struct tuples | By value | By value |
| Records, unions, `Some x`, other reference types | By reference: an equal copy propagates | By value: an equal copy is cut off |
| `None` over `None` | Cut off (`None` is `null`) | Cut off |
| `nan` over `nan` | Propagates (IEEE: `nan` equals nothing) | Cut off |
| `0.0` over `-0.0` | Cut off | Cut off |

Under `JsIdentityPolicy`, mutating a referenced object in place and writing the same reference back
is cut off. Write a new value to propagate a change.

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

## Dynamic dependencies

A memo or effect collects its dependencies again on every run. A source read by a branch that is no
longer taken stops waking the node, and the dependency returns when the branch is taken again.
Reading a source twice in one run records one dependency.

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

```text
["a"; "x"; "y"; "b"; "c"]
```

## Untrack

`untrack body` runs `body` and records no dependencies for the reads inside it. Calls nest, and
tracking resumes when the outermost `untrack` returns. An untracked read of a stale memo still
recomputes it, so the value is current.

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
    let doubled = createMemo (fun () -> source.Value * 2)
    doubled.Value |> ignore
    source.Value <- 21
    runs, untrack (fun () -> doubled.Value)

printfn "effect runs: %d, untracked stale memo read: %d" untrackRuns untrackedMemoValue
```

```text
effect runs: 2, untracked stale memo read: 42
```

## Batch

`batch body` defers effects until the outermost batch ends and returns `body`'s result. Several
writes inside a batch produce one run per effect, at the last values. A memo read inside the batch
recomputes on the spot and sees the writes made so far. A batch that throws still ends: later writes
flush normally, and the effects queued by its writes run at the next flush. `flush ()` runs the queued effects immediately, including inside a batch.

```fsharp
let batchLog =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let first = createSignal "Ada"
    let last = createSignal "Lovelace"
    let full = createMemo (fun () -> $"{first.Value} {last.Value}")
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

```text
effect: Ada Lovelace
memo inside the batch: Grace Hopper
effect: Grace Hopper
batch result
```

## Scopes and disposal

Every memo and effect belongs to an owner: the graph's `Root`, a scope created by `createRoot`, or
the current run of an enclosing effect, owning memo or boundary. Disposing an owner runs its
cleanups and then disposes its children, each in reverse creation order. Disposal is idempotent.

- `createRoot body` creates a scope, runs `body` with its `Owner`, and returns `body`'s result.
  `owner.Dispose ()` disposes everything created inside it.
- `onCleanup f` registers `f` with the innermost scope. Inside an effect, owning memo or
  boundary, `f` runs before the computation's next run and when it is disposed. Inside a
  `createMemo` body it raises `InvalidOperationException`.
- `graph.Dispose ()` disposes every node the graph owns.
- A root created inside an effect belongs to that effect's current run and is disposed before the
  next run.

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

```text
subscribe ada
unsubscribe ada
subscribe grace
unsubscribe grace
```

A cleanup that throws is recorded, and the remaining cleanups still run. A `createRoot` scope keeps
the error in its own `Errors`; the scope of an effect or memo records it in `graph.Root.Errors`. A
write made inside a cleanup is visible to the effect's next run and does not start another run.

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

A disposed memo keeps its last computed value, including when it was stale at disposal. It stops
recomputing and stops waking its readers. An async memo disposed while pending reads as `Failed`
with an `ObjectDisposedException`, and its readers are woken once to see it.

```fsharp
let disposedValue, disposedRuns =
    use graph = new Graph ()
    use _ = graph.Activate ()
    let source = createSignal 1
    let tenfold = createMemo (fun () -> source.Value * 10)
    tenfold.Value |> ignore
    source.Value <- 2
    tenfold.Dispose ()
    tenfold.Value, tenfold.Runs

printfn "value after disposal: %d, runs: %d" disposedValue disposedRuns
```

```text
value after disposal: 10, runs: 1
```

Re-entrant changes during a flush converge:

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

## Common mistakes

> **Caution: calling an `Api` function with no active graph.** `createSignal`, `createMemo` and the
> other `Api` functions throw `InvalidOperationException` ("No ambient graph on this thread...")
> outside `graph.Activate ()` or `graph.Run`. The active graph is per thread, so a callback on a
> thread-pool thread also has no active graph. See [Troubleshooting](troubleshooting.md).

> **Caution: reading an effect's side effect inside a batch.** Effects run when the outermost batch
> ends. Code inside the batch sees the state the effects left before the batch began. Read a memo
> instead: memos recompute on read inside a batch.

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

```text
inside the batch: 0, after the batch: 5
```

> **Caution: leaving a root or graph undisposed.** A memo keeps a dependency on every source it
> read, and an effect keeps running, until its owner is disposed. Dispose each `createRoot` owner
> when its work ends, and dispose the graph with `use graph = new Graph ()` or `graph.Dispose ()`.

> **Caution: expecting `Peek` to refresh a stale memo.** `Peek` returns the last computed value. Use
> `untrack (fun () -> memo.Value)` for an up-to-date value with no dependency.

> **Caution: effect exceptions are invisible through `createEffect`.** An exception thrown by an
> effect body is stored in `Effect.Error` and never escapes the write or the flush. The effects
> queued after it still run. `createEffect` returns `unit`, so construct the effect with
> `new Effect (graph, body)` when the error has to be observable.

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

```text
write returned: true, Effect.Error: boom, later effect ran: true
```

## Key types

- `Graph`
- `Signal<'T>`
- `Memo<'T>`
- `Effect`
- `Owner`
- `GraphOptions`
- `Api` module
