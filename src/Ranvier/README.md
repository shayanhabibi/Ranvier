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

## Tracing

`Ranvier.Traced` is the same assembly, namespaces and version built with the per-graph event log and its queries.
Reference it in place of `Ranvier` for development builds; see
[Tracing](https://shayanhabibi.github.io/Ranvier/guide/tracing/).

## Links

- [Documentation](https://shayanhabibi.github.io/Ranvier/)
- [Source](https://github.com/shayanhabibi/Ranvier)
- [MIT License](https://github.com/shayanhabibi/Ranvier/blob/master/LICENSE)
