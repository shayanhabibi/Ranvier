---
title: Troubleshooting
order: 9
---

:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

Each heading is an exception message or symptom as it appears at runtime; search this page for the
text you see. For the underlying contracts, see the guide pages linked from each entry.

Every exception entry was reproduced in F# Interactive, and its heading checked against the message
the library throws.

The examples open these namespaces:

```fsharp
open System
open System.Threading.Tasks
open Ranvier
```

## Exceptions

### No ambient graph on this thread. Activate one with `use _ = graph.Activate ()`, or construct nodes against an explicit graph.

`InvalidOperationException`, from `Graph.Current`.

**Cause.** An `Api` function (`createSignal`, `createMemo`, `batch`, `untrack` and the rest) ran on a
thread with no active graph. The functions resolve the graph per thread and require it to be active.

**Fix.** Activate a graph around the calls, or pass the graph to a constructor such as
`Signal (graph, 0)`. `Activate` returns a handle that restores the previous graph when disposed.
C# callers write `using (graph.Activate ())`.

```fsharp
let appGraph = new Graph ()

let doubled =
    use _ = appGraph.Activate ()
    let count = createSignal 2
    createMemo (fun _ -> count.Value * 2)

doubled.Value
```

```text
4
```

`appGraph.Run (fun () -> ...)` activates, runs the body and restores the previous graph in one call.
The graph outlives the call, so effects created inside it keep running.

The message also arrives as a row's error or a memo's error. A projection's factory runs at the first
read of the projection, and a memo's body at the first read of the memo. When that code calls an `Api`
creator, the first read needs an active graph too. A factory that raised this way fails its row until
the key is removed.

See [Getting started](getting-started.md#the-graph).

### &lt;operation&gt; ran on thread N, but this graph is owned by thread M. Marshal through Graph.Dispatch, or set GraphOptions.ThreadAffinity to Unchecked if affinity is guaranteed some other way.

`InvalidOperationException`. The operation is `A signal write` or `Pump`.

**Cause.** A signal was written, or `Graph.Pump` was called, on a thread other than the one that
constructed the graph. The check applies to writes and `Pump`, and equal writes raise too. A graph
built with `Dispatcher = Some (ImmediateDispatcher ())` calls `Pump` on the posting thread, so an
off-thread `AsyncSource.Settle`, `Fail` or `AsyncMemo` flight completion raises `Pump ran on thread`
on that thread and leaves the work queued.

**Fix.** Hand the write to `Graph.Dispatch` from the other thread, and call `Pump` on the owning
thread. On a thread with a `SynchronizationContext` (a UI thread), the graph posts the drain to that
context and `Pump` is unnecessary.

```fsharp
let ownerGraph = new Graph ()
let temperature = Signal (ownerGraph, 20)

// On a worker thread:
Task.Run(fun () -> ownerGraph.Dispatch (fun () -> temperature.Value <- 25)).Wait ()

// Back on the owning thread:
ownerGraph.Pump () |> ignore
temperature.Value
```

```text
25
```

`GraphOptions.ThreadAffinity = Unchecked` removes the check. Use it only when every write is already
guaranteed to arrive on one thread.

See [Async and pending](async-and-pending.md#threading-and-dispatch).

### NotReadyException

`NotReadyException`, whose message is `NotReadyException` followed by the pending node's type, for
example ``NotReadyException Ranvier.Memo`1[System.Int32]``.

**Cause.** Top-level code read `.Value` of a pending node (an async value, an unsettled
`AsyncSource`, or a memo that reads one) outside a memo, effect or boundary. Inside those, the
exception suspends the reader and the reader re-runs when the source settles. At the top level it
escapes to the caller.

**Fix.** Read `TryValue` or `Status` at the top level, or wrap the read in a suspense boundary.

```fsharp
let shopGraph = new Graph ()

let price, total, label =
    shopGraph.Run (fun () ->
        let price = createAsyncSource<int> ()
        let total = createMemo (fun _ -> price.Value * 3)
        let label = createSuspense (fun () -> "loading") (fun () -> string total.Value)
        price, total, label)

let before = total.TryValue, label.Value
price.Settle 5
let after = total.TryValue, label.Value
before, after
```

```text
((Pending, "loading"), (Ready 15, "15"))
```

See [Async and pending](async-and-pending.md#boundaries).

### A memo created by createMemo created an owned node in its body: a memo, effect, async value, boundary, root, projection, lookup, selector or onCleanup.

`InvalidOperationException`, raised as the memo's error by `Value` and every other read of the memo.
The full message continues: *createMemo is a pure derivation. Use createMemoWith for a memo that owns
the nodes its body creates: they are disposed before each re-run and with the memo.* An async value
built by `createAsync` raises the same message naming `createAsync` and `createAsyncWith`. A
lookup's `f` raises *A lookup's f created an owned node* for its key, and a lookup's `affected`
raises *A lookup's affected created an owned node* for every live key.

**Cause.** The body of a `createMemo`, or of `Memo (graph, compute)`, created an owned node,
directly or inside `untrack`. The run fails even when the body catches the creator's exception,
and it fails with this message even when the body then throws an exception of its own.

**Fix.** Use `createMemoWith`, or `Memo (graph, compute, true)`, when the memo creates nodes on
purpose. Its nodes and cleanups are disposed before each re-run and with the memo. Otherwise create
the node outside the memo and read it inside.

```fsharp
let owningGraph = new Graph ()

let subscriptions =
    owningGraph.Run (fun () ->
        let topic = createSignal "news"

        createMemoWith (fun _ ->
            let name = topic.Value
            // Owned by this run: released before the next run and with the memo.
            onCleanup ignore
            $"subscribed to {name}"))

owningGraph.Run (fun () -> subscriptions.Value)
```

```text
"subscribed to news"
```

See [Getting started](getting-started.md#pure-and-owning-memos).

### A projection row's reader created an owned node: a memo, effect, async value, boundary, root, projection, lookup, selector or onCleanup.

### A projection's map created an owned node: a memo, effect, async value, boundary, root, projection, lookup, selector or onCleanup.

`InvalidOperationException`, raised as the row's error by `Get`, `TryGet` or any other read of the row.
The first message comes from the reader returned by `createProjectionWith`'s factory, the second from
`createProjection`'s `map`. The same messages apply to the index forms.

**Cause.** The reader or `map` called an `Api` creator (`createMemo`, `createEffect`, `createAsync`,
`createSuspense`, `createRoot`, `createProjection`, `createLookup`, `createSelector` or `onCleanup`).
The reader re-runs whenever its key's item or a value it read changes, so each run would create
another node. The row fails even when the reader catches the creator's exception.

**Fix.** Use the factory form and create the node in the factory body, which runs once per key and
whose nodes are disposed with the key. Return a reader that only reads.

```fsharp
let labelGraph = new Graph ()

let labels =
    labelGraph.Run (fun () ->
        let items = createSignal [ 1; 2 ]

        createProjectionWith
            id
            (fun item ->
                // Runs once per key; the memo is disposed with the key.
                let label = createMemo (fun _ -> sprintf "#%d" (item ()))
                fun () -> label.Value)
            (fun () -> items.Value))

labelGraph.Run (fun () -> labels.Get 2)
```

```text
"#2"
```

The check covers the constructors as well as the `Api` creators: a reader that builds
`Memo (graph, ...)` fails the same way.

A node created inside a memo that a row's reader pulls belongs to that memo. An owning memo keeps
it until the memo's next run or disposal; a pure memo fails with the `createMemo` message above.

See [Collections](collections.fsx#the-factory-form).

### The projection produced the key &lt;k&gt; twice in one pass. Keys must be unique; check the keyOf function.

`InvalidOperationException`, raised from `Keys`, `Get` and every other read of the projection, and
caught by an enclosing error boundary. `Projection.Error` holds it when a scheduled pass fails.

**Cause.** `keyOf` returned the same key for two items of the source.

**Fix.** Key by a value unique to each item, such as an id. Key by position with
`createIndexProjection` when the items carry no identity.

```fsharp
type Todo = { Id: int; Text: string }

let todoGraph = new Graph ()

let todoTexts =
    todoGraph.Run (fun () ->
        let todos = createSignal [ { Id = 1; Text = "a" }; { Id = 2; Text = "b" } ]
        createProjection (fun t -> t.Id) (fun t -> t.Text) (fun () -> todos.Value))

todoTexts.Keys
```

```text
[|1; 2|]
```

See [Collections](collections.fsx#identity).

### The projection's factory for key &lt;k&gt; read a pending source.

`InvalidOperationException`, raised as the row's error. The full message continues: *The factory runs
once per key, untracked, and cannot wait for a source to settle. Read the source inside the reader the
factory returns.* The `NotReadyException` from the pending read is the inner exception.

**Cause.** The body of a `createProjectionWith` factory read `.Value` of a pending source. The row
stays failed until its key is removed.

**Fix.** Move the read into the reader the factory returns. The reader suspends while the source is
pending and re-runs when it settles.

```fsharp
let rateGraph = new Graph ()

let rate, priced =
    rateGraph.Run (fun () ->
        let rate = createAsyncSource<int> ()
        let items = createSignal [ 1; 2 ]

        rate,
        createProjectionWith
            id
            (fun item -> fun () -> item () * rate.Value) // read inside the reader
            (fun () -> items.Value))

rate.Settle 10
priced.Get 2
```

```text
20
```

See [Collections](collections.fsx#the-factory-form).

### The projection has no key &lt;k&gt;.

`KeyNotFoundException`, from `Projection.Get`.

**Cause.** `Get` asked for a key absent from the projection's current key set. `Get` on a disposed
projection raises this for every key, and `TryGet` returns `None`.

**Fix.** Use `TryGet`, which returns `None` for an absent key, or iterate `Keys`.

```fsharp
let lookupGraph = new Graph ()
let squares = lookupGraph.Run (fun () -> createProjection id (fun x -> x * x) (fun () -> [ 1; 2; 3 ]))

squares.TryGet 3, squares.TryGet 9
```

```text
(Some 9, None)
```

See [Collections](collections.fsx#reading-a-projection).

### Cannot access a disposed object. Object name: 'LookupOf`3'.

`ObjectDisposedException`. The object name is ``LookupOf`3`` for `Lookup.Get` on a disposed lookup or
selector, and ``KeyedProjection`3`` or ``IndexProjection`2`` for `AsObservableCollection` on a
disposed projection.

**Cause.** The lookup or projection was disposed, directly or with the scope that created it, before
the read.

**Fix.** Read it only while its owner is alive: create it in the scope that reads it, or dispose that
scope after the last read.

See [Collections](collections.fsx#lookups).

## Symptoms

### An async value stays Pending forever

**Cause.** The value settles on another thread, such as an `AsyncMemo` task continuing on the thread
pool or `AsyncSource.Settle` called from a worker. On a thread without a `SynchronizationContext` the
graph uses `ManualDispatcher`, and off-thread settles wait in the graph's inbox until `Graph.Pump`
runs on the owning thread.

**Fix.** Call `Pump` on the owning thread from the application's loop, or construct the graph on a
thread with a `SynchronizationContext`.

```fsharp
let pumpGraph = new Graph ()
let reply = AsyncSource<string> (pumpGraph)

Task.Run(fun () -> reply.Settle "done").Wait ()
let beforePump = reply.TryValue

pumpGraph.Pump () |> ignore
let afterPump = reply.TryValue

beforePump, afterPump
```

```text
(Pending, Ready "done")
```

Under the default `ThreadAffinity = Guarded`, a graph built with `ImmediateDispatcher` turns an
off-thread settle into the `Pump ran on thread` exception and leaves the value `Pending` until the
owning thread calls `Pump`. It drains on the settling thread only under `ThreadAffinity = Unchecked`,
which is safe only when every write already arrives on one thread.

See [Async and pending](async-and-pending.md#threading-and-dispatch).

### An exception inside an effect disappears

**Cause.** An effect records the exception from its body in `Effect.Error`, and the flush continues
with the next effect. `createEffect` returns `unit`, so its effect's error is unreachable.

**Fix.** Construct the effect with `new Effect (graph, body)` and read `Error` and `Status`. The
effect is still owned by the enclosing scope.

```fsharp
let effectGraph = new Graph ()
let input = Signal (effectGraph, 1)

let watcher =
    new Effect (effectGraph, fun () -> if input.Value > 1 then failwith "too large")

input.Value <- 2
watcher.Status, watcher.Error.Message
```

```text
(Error, "too large")
```

See [Getting started](getting-started.md#effects).

### A lookup returns an old value

**Cause.** `createLookup`'s `affected prev next` omitted a key whose value changed between `prev` and
`next`. An observed cell for that key keeps its previous value until a later transition names it.

**Fix.** Return every key whose value can differ between the two states. An extra key costs one
recomputation.

```fsharp
let changedKeys (prev: Map<string, int>) (next: Map<string, int>) =
    Seq.append prev.Keys next.Keys
    |> Seq.distinct
    |> Seq.filter (fun k -> Map.tryFind k prev <> Map.tryFind k next)

let scoreGraph = new Graph ()
let seen = ResizeArray<int> ()

let scores =
    scoreGraph.Run (fun () ->
        let scores = createSignal (Map.ofList [ "a", 1; "b", 1 ])

        let byName =
            createLookup
                (fun s k -> s |> Map.tryFind k |> Option.defaultValue 0)
                changedKeys
                (fun () -> scores.Value)

        createEffect (fun () -> seen.Add (byName.Get "b"))
        scores)

scores.Value <- Map.ofList [ "a", 1; "b", 7 ]
List.ofSeq seen
```

```text
[1; 7]
```

See [Collections](collections.fsx#lookups).

### An async memo does not re-run when a value read after await changes

**Cause.** An `AsyncMemo` (`createAsync`) tracks the reads made while its body runs synchronously, up
to the first `await` that suspends. An `await` on an already-completed task does not suspend. A read
inside a continuation is untracked.

**Fix.** Read every reactive value before the first `await` and bind it to a local.

`pumpUntil` is the helper defined in [Async and pending](async-and-pending.md#async-memos).

```fsharp
let queryGraph = new Graph ()
let userId = Signal (queryGraph, 1)

let profile =
    new AsyncMemo<string> (
        queryGraph,
        fun _ ->
            task {
                let id = userId.Value // read before the first await: tracked
                do! Task.Yield ()
                return sprintf "user %d" id
            }
    )

pumpUntil queryGraph (fun () -> profile.TryValue = Ready "user 1") (TimeSpan.FromSeconds 5.0)
userId.Value <- 2
pumpUntil queryGraph (fun () -> profile.TryValue = Ready "user 2") (TimeSpan.FromSeconds 5.0)
profile.TryValue
```

```text
Ready "user 2"
```

The continuation after `Task.Yield` settles on the thread pool, so the example pumps the graph; see
"An async value stays Pending forever" above.

See [Async and pending](async-and-pending.md#async-memos).

### A boundary shows its fallback forever and starts a flight on every settle

**Cause.** The async value is created inside the body of a boundary, an owning memo, an effect or
`createAsyncWith`, and read in the same body. The owner replaces the nodes its body creates on every
re-run. The settle wakes the owner, and the re-run disposes the settled value and creates a new one
with a new flight.

**Fix.** Create the async value outside the owner and read it inside the body.

```fsharp
let fetchGraph = new Graph ()
let reportFlight = TaskCompletionSource<string> ()

// Created once, outside the boundary.
let report = new AsyncMemo<string> (fetchGraph, fun _ -> reportFlight.Task)

let reportView =
    Boundary<string>.Suspense (fetchGraph, (fun () -> "report: " + report.Value), (fun () -> "loading"))

let beforeReport = reportView.TryValue
reportFlight.SetResult "ready"
beforeReport, reportView.TryValue
```

```text
(Ready "loading", Ready "report: ready")
```

See [Getting started](getting-started.md#pure-and-owning-memos).

### An effect did not run inside batch

**Cause.** Effects queued by writes inside `batch` run once, when the outermost batch returns. A memo
read inside the batch is recomputed at the read.

**Fix.** Read the effect's result after `batch` returns. Call `flush ()` inside the batch to run the
queued effects early, which ends the batching of the writes made before it.

```fsharp
let batchGraph = new Graph ()
let runsInside = ref 0

let effectRuns =
    batchGraph.Run (fun () ->
        let count = createSignal 0
        let runs = ResizeArray<int> ()
        createEffect (fun () -> runs.Add count.Value)

        batch (fun () ->
            count.Value <- 1
            count.Value <- 2
            runsInside.Value <- runs.Count)

        List.ofSeq runs)

runsInside.Value, effectRuns
```

```text
(1, [0; 2])
```

See [Getting started](getting-started.md#batch).
