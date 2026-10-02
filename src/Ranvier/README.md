# Ranvier

Fine-grained reactive computation for .NET.

> **Preview.** Ranvier is pre-release. Its APIs may change before the first release.

Ranvier builds a graph of signals (settable sources), memos (derived values, recomputed on read once something they
read has changed) and effects (side effects the scheduler runs after a change). Async sources carry an explicit Pending
state, and boundaries decide where a pending or failed read stops.

```fsharp
open Ranvier

use graph = new Graph ()

graph.Run (fun () ->
    let count = createSignal 1
    let doubled = createMemo (fun _ -> count.Value * 2)
    createEffect (fun () -> printfn "doubled = %d" doubled.Value)
    count.Value <- 5)
```

C# projects reference [Ranvier.CSharp](https://www.nuget.org/packages/Ranvier.CSharp), which adds delegate-based
factories and extension methods.

## Targets

`net10.0`, `net8.0` and `netstandard2.1`.

## Per-node equality

Ordinary factories use `GraphOptions.Equality`. To override a node's value cutoff, supply a
typed `IEqualityComparer<T>`:

```fsharp
let name = createSignalWithComparer System.StringComparer.OrdinalIgnoreCase "Ada"
let label = createMemoWithComparer System.StringComparer.OrdinalIgnoreCase (fun _ -> name.Value.Trim())
```

The override applies to that node. Other nodes continue using the graph policy. A signal keeps
its current value when the comparer treats a write as equal. A memo caches its computed result
but suppresses downstream work when it compares equal. Pending, failed, waiting and caught-error
state changes still propagate.

The opt-in factories are `createSignalWithComparer`, `createMemoWithComparer`,
`createOwningMemoWithComparer`, `createEffectOnWithComparer`, `createSuspenseWithComparer`,
`createErrorBoundaryWithComparer` and `createBoundaryWithComparer`. Their ownership, purity and
previous-value rules match the ordinary factories. Null comparers are rejected at construction.
A comparer exception leaves a signal unchanged and reaches its writer; on a computed node it
fails the node using its existing error handling. A boundary's recovery function handles body
errors, so a comparer error fails the boundary directly.

Comparers are selected once at construction and stored in the existing typed field. The default
memo, split-effect and boundary constructors add a construction-time option check; update and
read methods retain their existing IL and instance fields. Async sources and async memos retain
their completion/state notifications, and collection APIs retain their graph-policy cutoffs.

The C# facade adds comparer overloads, for example `Reactive.Signal("Ada", StringComparer.OrdinalIgnoreCase)`
and `Reactive.Memo(() => name.Value.Trim(), StringComparer.OrdinalIgnoreCase)`.

## Editable collections and change readers

```fsharp
let items = createKeyedCollection (fun (id, _) -> id)
items.Edit(fun edit ->
    edit.AddOrUpdate(1, "write")
    edit.AddOrUpdate(2, "test"))
let titles = items.Rows |> Projection.map snd
use reader = titles.NewValueReader ()
reader.Read () |> ignore // initial reset
items.AddOrUpdate(2, "retest")
let delta = reader.Read () // Changed for key 2
```

`Reactive.KeyedCollection<T, K>(keyOf)` is the C# factory. `Rows` supports the existing
map/filter/sort/group/aggregate operators. Updating a live key writes its value directly;
it preserves the key array and avoids scanning an input collection. New keys append.
Removing and re-adding a key creates a new row, reported as `Replaced` to a reader that saw
the old row. Removal searches insertion order in O(N); membership passes copy key order.
`Edit` batches synchronous writes and retains applied edits if its callback throws.

`NewKeyReader()` reports membership and order. `NewValueReader()` also reports `Changed`
for unequal settled row values under the graph policy. Each reader has an independent
bounded cursor; the first read and overflow return a reset. Deltas are coalesced refresh
hints containing keys, rather than value snapshots or a history of every intermediate write.
Read current values through the projection and handle pending/error states there.

Value readers share observation and accepted values while any value reader lives. Initial
observation evaluates every visible row; later value-only pulls evaluate suspect rows.
Pending or failed rows retain their last settled value, and status changes alone do not
produce `Changed`. Dispose readers to release observation. The .NET observable collection
adapter uses this cache to update changed rows without polling every row.

Map membership consumes upstream key deltas. Filters, sorts, grouping and some aggregate
membership paths still scan keys; this is a foundation for incremental collections,
with explicit costs for membership and order.

## Tracing

`Ranvier.Traced` is the same assembly, namespaces and version built with the per-graph event log and its queries.
Reference it in place of `Ranvier` for development builds; see
[Tracing](https://shayanhabibi.github.io/Ranvier/guide/tracing/).

## Links

- [Documentation](https://shayanhabibi.github.io/Ranvier/)
- [Source](https://github.com/shayanhabibi/Ranvier)
- [MIT License](https://github.com/shayanhabibi/Ranvier/blob/master/LICENSE)
