---
title: Contracts
order: 4
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

This page states three contracts that users of other reactive libraries tend to learn from bug reports:
which threads may touch a graph, what a failure does to the nodes that read it, and who owns a node created
inside a computation. The test suite covers each statement. The guide pages linked from each section show
the APIs in use.

## Threading

*Implemented.*

A graph belongs to the thread that constructed it, the **owning thread**. A program that follows the table
below runs every body, cleanup and comparer on the owning thread. The exception is the code of an async body
after its first `await` that suspends: it runs wherever its task resumes, outside the graph's tracking.

### Which threads may call what

| Operation | Thread | Called from another thread under `Guarded` |
| --- | --- | --- |
| `signal.Value <- v` | Owning | Raises `InvalidOperationException` before the equality check. The value stays unchanged, and an equal write raises too. |
| `graph.Pump ()` | Owning | Raises `InvalidOperationException`. |
| `graph.Dispatch work` | Any | Queues `work` in the graph's inbox. On the owning thread, `work` runs inline. |
| `AsyncSource.Settle` and `Fail`, flight completions | Any | Queued in the inbox, as `Dispatch` is. On the owning thread, they apply inline. |
| Reads, node creation, `batch`, `flush`, `Dispose` | Owning | Unchecked. Marshal them through `Dispatch`. |

The affinity check compares `Environment.CurrentManagedThreadId` with the id recorded when the graph was
constructed. The check depends on the thread id alone: a second thread that has the owning thread's
`SynchronizationContext` installed still fails the check. The check therefore holds on hosts where a context
can be installed on more than one thread, such as a Blazor Server renderer
([aspnetcore#69323](https://github.com/dotnet/aspnetcore/issues/69323)).

`ThreadAffinity.Unchecked` removes the check. The caller then guarantees that one thread at a time touches
the graph.

### Where work runs

- **Effects** run on the owning thread, in the flush at the end of the write or batch that woke them, or in
  the flush that `Pump` runs after draining the inbox.
- **A write inside an effect body** joins the running flush. The effects it wakes run before the outer write
  returns, and an effect that writes a signal it reads re-runs until the value stops changing.
- **A write inside a memo body** wakes effects that run once the outermost body or read has finished, before
  that read returns.
- **Async settles** from another thread wait in the inbox until the owning thread drains it. The graph's
  dispatcher, chosen at construction, decides when that happens:

| Dispatcher | The inbox drains |
| --- | --- |
| A `SynchronizationContext` was current at construction | On that context, through `Post`. |
| `ManualDispatcher`, the default when `SynchronizationContext.Current` is null at construction | When the owning thread calls `graph.Pump ()`. |
| `ImmediateDispatcher` | On the posting thread. Under `Guarded` that drain raises `Pump ran on thread` there, and the work stays queued. |

[The async graph on .NET](async-graph.md#threads-and-dispatch) covers the dispatcher in detail.

### A write from another thread during a flush

A direct write from another thread raises on that thread, and the running flush completes unaffected.
`Dispatch`, `Settle` and `Fail` return at once, while the owning thread is still inside the flush. Their work
waits in the inbox and applies at the next drain on the owning thread.

### Internal locks

User code runs outside every internal lock. This covers memo, effect and boundary bodies, cleanups,
equality comparers, `fallback` and `recover`, dispatched work and `IGraphDispatcher.Post`.

- The inbox is a lock-free queue, so `Dispatch`, `Settle` and `Fail` return at once.
- Each async memo holds one lock, around its published value and its `Previous.Settled` waiters. The tasks
  it completes resume their awaiters asynchronously.
- A build with tracing compiled in adds a lock around the trace log's bookkeeping.

### Known limitation: hopping synchronisation contexts

A host that serialises work but runs it on different pool threads fails the thread check under `Guarded`.
See [the async graph on .NET](async-graph.md#known-limitation-hopping-synchronisation-contexts).

## Error recovery

*Implemented.*

A failure is a state of a node, and the node stays in the graph with its edges. In Rx, `OnError` ends the
subscription and the pipeline has to be rebuilt. In Ranvier, a failed node recovers when an input changes.

### What a reader sees

A node that has failed reports it on every read:

| Read | Result |
| --- | --- |
| `.Value` | Raises the stored exception. |
| `TryValue` | `Failed ex`. |
| `Status` | Has the `Error` flag. |
| `Peek` | The last value published before the failure. |

A memo that reads a failed node fails with the same exception instance. The instance passes unchanged through
every intermediate memo to the nearest error boundary, which shows `recover ex last` and exposes the
exception as `Caught`. A suspense boundary passes failures through to its readers.

### Recovery

Recovery happens through re-runs alone.

- A re-read of a failed node serves the stored failure and leaves the body un-run.
- A change to a source that the failed run read re-runs the body. A successful run clears the error at every
  level, boundaries included.
- A failed `AsyncSource` recovers at its next `Settle`.
- A failed effect re-runs when a source it read changes, and a successful run clears its `Status` and `Error`.

### Where an exception lands

| Thrown by | Result |
| --- | --- |
| A memo body | The memo fails. |
| An async body, or its task faulting or being cancelled | The async value fails. Under `CancelPrevious` and `KeepLatest`, a superseded flight's outcome is discarded. |
| An effect body | Recorded on the effect's `Status` and `Error`. Every effect queued behind it in the flush still runs. |
| A boundary's `fallback` or `recover` | The boundary fails with that exception. |
| A cleanup | Recorded, and the remaining cleanups and disposals still run. A `createRoot` scope keeps the error in its own `Errors`; a computation's scope records it in `graph.Root.Errors`. |
| Work drained from the inbox | Recorded in `graph.Root.Errors`, and the rest of the inbox still runs. |
| A signal's equality comparer | The write raises to the writer. The value stays unchanged, and readers stay asleep. |
| The equality comparer of a memo, boundary, projection row, fold or lookup cell | The node fails with that exception and keeps its previous value, and its readers wake to see the failure. A boundary's `recover` does not see it. |
| A `createEffectOn` comparer | Recorded on the effect as a failure of `compute`, and `act` does not run. |
| Disposal of a pending async value | The async value fails with `ObjectDisposedException`, and its readers wake once to see it. |
| A flight's task faulting after it was superseded, or after its async value or graph was disposed | Observed under every `FlightPolicy`. `TaskScheduler.UnobservedTaskException` does not receive it. |

`createEffect` returns `unit`, so an effect's failure is visible through tracing or through an `Effect`
constructed directly. See
[Troubleshooting](../guide/troubleshooting.md#an-exception-inside-an-effect-disappears).

### Finding where a failure came from

A failed node reports the node the failure originated in as `ErrorOrigin`, and an error boundary reports the origin
of `Caught` as `CaughtFrom`. Both are null while the node holds no failure. `ErrorOrigin` is an untracked read, like
`Status`; `CaughtFrom` is tracked and brings the boundary current, like `Caught`.

| Node | `ErrorOrigin` |
| --- | --- |
| `Memo`, `Boundary`, `Effect` | The node itself when its body, comparer or purity check raised the exception; otherwise the origin of the failed read it rethrew. |
| `AsyncMemo` | The async memo for a faulted or cancelled flight, a throwing body or disposal while pending; the origin of a failed read rethrown before the first `await` that suspends. A failure read after that `await` can report the async memo instead. |
| `AsyncSource` | The source, after `Fail`. |
| `Projection` | The projection when its source, `keyOf` or a duplicate key failed the pass; otherwise the origin of the failed read the source rethrew. |

A reader that rethrows a failed read keeps its origin, through any number of memos, effects, boundaries and
projections. A boundary's `recover` that rethrows `ex` keeps the origin too. A node that throws a new exception,
including one that wraps the failure as its `InnerException`, is the origin of the new exception.

Rows, lookup cells and fold rows are internal nodes. A failed projection row reports its projection, and a fold over
it reports the row's origin. A lookup is not a node: a failure raised by its `source` or `affected` function reports
the lookup's internal source memo, and one raised by its key function or comparer reports the key's internal cell. In
[traces](../guide/tracing.md) the cells appear as parts of the source memo.

Two limits apply. A body that catches a failed read, reads another failed node and then rethrows the first
exception is the origin of that exception. A failure delivered to a `Queue` flight's turn behind earlier flights
reports the async memo.

`.Value` rethrows the stored exception with its original stack trace. On .NET every reader on the path rethrows the
same capture, so the trace shows the frames of the origin's throw followed by the frames of the last read. Under
Fable the exception is rethrown as it was raised.

Other tools for tracing a failure back:

- a boundary's `Caught` and every failed node on the path hold the same instance
- `PendingSources` on a memo or effect gives the source of its last pending read
- in a build with [tracing](../guide/tracing.md), `Trace.why` gives the writes and settles behind a node's last
  run, and `Trace.origin` gives the site that created the node

## Ownership

*Implemented.*

Every node other than a signal or an async source attaches, when it is constructed, to the **current
owner**:

| Created | Current owner |
| --- | --- |
| At the top level | `graph.Root` |
| Inside `createRoot body` | The new scope |
| Inside `runWithOwner owner body` | `owner` |
| Inside the body of an effect, `createMemoWith`, boundary or `createEffectOn`'s `act` | The current run of that computation |
| Inside `createAsyncWith`'s body, before its first `await` that suspends | The current flight |
| Inside a projection's factory | The key's scope |
| Inside a projection's `source` or `keyOf`, or a lookup's `source` | The current pass |

:::info
**A node belongs to the run of the computation whose body created it.** The next run of that body disposes
it, and so does the disposal of the computation. Ownership is independent of the reader that caused the
run: an owning memo first read by an effect keeps its nodes when the effect re-runs.
:::

### What each API returns, and when it is disposed

| API | Owner | Disposed |
| --- | --- | --- |
| `createSignal`, `createAsyncSource` | Unowned | Collected like any object once unreferenced. A reader drops its edge when it is disposed, or re-runs without reading the source. |
| `createMemo`, `createMemoWith`, `createAsync`, `createAsyncWith`, `createOptionMemo` | Current owner | With the owner, or by `Dispose`. |
| `createEffect`, `createEffectOn` | Current owner | With the owner. Construct `Effect` directly for a handle. |
| `createSuspense`, `createErrorBoundary`, `createBoundary` | Current owner | With the owner, or by `Dispose`. |
| `createProjection`, `createProjectionWith`, the index forms, and the views built on a projection | Current owner | With the owner, or by `Dispose`. Each key's scope is disposed when the key leaves. |
| `AsObservableCollection ()` | Current owner | The collection stops following when the owner re-runs or is disposed, or when the projection is disposed. |
| `createLookup`, `createSelector` | Current owner | With the owner, or by `Dispose`. |
| `createRoot` | Current owner | With the owner, or by `owner.Dispose ()`. |
| `onCleanup f` | Current owner | `f` runs when the owner's run ends or the owner is disposed. |
| `new Owner ()`, `new Graph ()` | The caller | By `Dispose`. `graph.Dispose ()` disposes `graph.Root`. |

### Nodes created inside a computation

| Created in | Result |
| --- | --- |
| A `createMemo` body | The run fails with `InvalidOperationException`: *A memo created by createMemo created an owned node in its body*. The run fails even when the body catches the exception, and `untrack` blocks are included. Use `createMemoWith`. |
| A `createMemoWith` body | Owned by the run. Disposed before the next run and with the memo. |
| An effect or boundary body | Owned by the run. Disposed before the next run and with the effect or boundary. |
| A `createAsync` body | The run fails, and the message names `createAsyncWith`. |
| A `createAsyncWith` body, before the first `await` that suspends | Owned by the flight. Disposed before the next flight starts and with the async value. |
| An async body, after that `await` | On the owning thread, owned by `graph.Root`. To keep it in the flight, capture `getOwner ()` before the `await` and create it inside `runWithOwner`. |
| A projection factory | Owned by the key. Disposed when the key leaves or the projection is disposed. |
| A projection row reader, or `createProjection`'s `map` | The row fails with `InvalidOperationException`. Create the node in the factory. |
| A lookup's `f` or `affected` | The key, or every live key, fails with `InvalidOperationException`. |
| A cleanup | Owned by the scope running the cleanup. Disposed before that computation's next run, or at once when the scope itself is being disposed. |

A signal or async source created in any body is unowned, and each run creates a new one that starts from its
initial value. State that has to survive a re-run belongs outside the body that re-runs.

An async value created and read in the same owning body restarts its flight on every settle. See
[Troubleshooting](../guide/troubleshooting.md#a-boundary-shows-its-fallback-forever-and-starts-a-flight-on-every-settle).

### Teardown order

Disposing a scope, or ending a computation's run, runs its cleanups last-registered-first, then disposes its
children last-created-first. Each child scope does the same, so a scope's own cleanups run before its
children's. Cleanups run untracked. Disposal is idempotent.

- A disposed memo keeps its last value and stops recomputing.
- Disposing an async value cancels its flight's token.
- A pending async value that is disposed fails with `ObjectDisposedException`.

### Deterministic lifetimes

Every edge and every ownership link in the graph is a strong reference. A node's lifetime ends at a defined
point: the end of its owner's run, or a `Dispose` call. The graph behaves identically whenever the garbage
collector runs. A build with tracing holds weak references in its trace log, which affects diagnostics
only.

[Scopes and disposal](../guide/getting-started.md#scopes-and-disposal) and
[Pure and owning memos](../guide/getting-started.md#pure-and-owning-memos) show these rules in code.
