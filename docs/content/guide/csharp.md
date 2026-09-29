---
title: C#
order: 10
description: Using Ranvier from C# through the Ranvier.CSharp package.
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

`Ranvier.CSharp` puts the F# `Api` behind C# names: `Reactive` holds one static factory per `Api` function, each
taking `Func` and `Action` delegates, and extension methods cover `Graph.Run`, `Signal.Update` and the projection
operators. The nodes it returns are the engine's own `Signal<T>`, `Memo<T>`, `AsyncMemo<T>` and `Projection<K, V>`.

## Setup

Reference `Ranvier.CSharp`; it brings `Ranvier` with it:

```bash
dotnet add package Ranvier.CSharp --prerelease
```

Or, built from source:

```xml
<ProjectReference Include="path/to/Ranvier/src/Ranvier.CSharp/Ranvier.CSharp.fsproj" />
```

Import the factories statically:

```csharp
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;
```

## A first graph

```csharp
var graph = new Graph();

graph.Run(() =>
{
    var count = Signal(1);
    var doubled = Memo(() => count.Value * 2);
    Effect(() => Console.WriteLine($"doubled {doubled.Value}"));

    count.Value = 2;
});
```

```text
doubled 2
doubled 4
```

Every factory resolves the active graph, as the F# functions do. Call them inside `graph.Run`, or while a
`using (graph.Activate ())` block is open.

## The factories

| C# | F# |
| --- | --- |
| `Signal(initial)` | `createSignal` |
| `Memo(() => …)` | `createMemo` |
| `Memo(previous => …, seed)` | `createMemo`, with the previous value or `seed` |
| `OwningMemo(() => …)` | `createMemoWith` |
| `Effect(() => …)` | `createEffect`, returning the `Effect`: dispose it to stop the effect early |
| `EffectOn(() => …, value => …)` | `createEffectOn` |
| `Async(token => …)`, `Async((previous, token) => …)` | `createAsync` |
| `OwningAsync(token => …)` | `createAsyncWith` |
| `AsyncSource<T>()` | `createAsyncSource` |
| `Suspense(body, fallback)` | `createSuspense` |
| `ErrorBoundary(body, error => …)` | `createErrorBoundary` |
| `Boundary(body, fallback, error => …)` | `createBoundary` |
| `Batch`, `Untrack`, `OnCleanup`, `Flush` | `batch`, `untrack`, `onCleanup`, `flush` |
| `Root(owner => …)` | `createRoot` |
| `CurrentOwner`, `RunWithOwner(owner, …)` | `getOwner`, `runWithOwner` |
| `Projection(source, keyOf, map)` | `createProjection` |
| `IndexProjection(source, map)` | `createIndexProjection` |
| `Lookup(source, f, affected)`, `Selector(source)` | `createLookup`, `createSelector` |

A boundary's `fallback` and `recover` receive no previous value in C#. Where the previous value matters, call
`Boundary<T>.Suspense`, `Errors` or `Catching` with the graph.

## Async values

`Async` takes a task factory. The token is cancelled when a newer flight supersedes the one it was given.

A search view model: the results are derived from the query, and a boundary turns loading and failure into
the text to show. `IsSearching` and `Error` read the boundary, so the view model holds no busy flag and no
`try`/`catch`:

```csharp
public interface ISearchService
{
    Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken token);
}

public sealed class SearchViewModel
{
    public SearchViewModel(ISearchService service)
    {
        Query = Signal("");
        Results = Async(token => service.SearchAsync(Query.Value, token));
        Summary = Boundary(
            () => Results.Value.Count == 0 ? "No matches" : string.Join(", ", Results.Value),
            () => "Searching…",
            error => $"Search failed: {error.Message}");
    }

    public Signal<string> Query { get; }
    public AsyncMemo<IReadOnlyList<string>> Results { get; }
    public Boundary<string> Summary { get; }
    public bool IsSearching => Summary.IsWaiting;
    public Exception? Error => Summary.Caught;
}
```

Construct it inside `graph.Run`, and bind the view with an effect:

```csharp
var graph = new Graph();
var search = graph.Run(() => new SearchViewModel(service));
graph.Run(() => Effect(() => Console.WriteLine(search.Summary.Value)));

search.Query.Value = "ada";
```

With the effect reading `Summary`, each write to `Query` starts a new search and cancels the token of the one in
progress. While a search is in flight the effect prints `Searching…` and `IsSearching` is `true`; a failed search
prints `Search failed: …` and sets `Error`; the next search that succeeds prints its results and clears `Error`.
A flight that completes on the thread pool reaches the graph through its dispatcher, as described in
[Async and pending](async-and-pending.md#threading-and-dispatch).

Read every input before the first `await`: a read after it is not tracked. The overload taking `Previous<T>`
awaits the value last published through `previous.Settled`, whose result is a `ValueOption`: test `IsSome`,
then read `Value`.

`TryValue` reads a node without raising. `TryGetValue` and `TryGetError` take it apart:

```csharp
if (price.TryValue.TryGetValue(out var value)) Console.WriteLine(value);
else if (price.TryValue.TryGetError(out var error)) Console.WriteLine(error.Message);
else Console.WriteLine("pending");
```

## Collections

The projection operators carry LINQ names and return live nodes. Each updates per changed row.

```csharp
var rows = Projection(() => todos.Value, t => t.Id, t => t);

var open = rows.Where(t => !t.Done).OrderBy(t => t.Title);
var hours = rows.Sum(t => t.Hours);
var remaining = rows.Count(t => !t.Done);
var firstPage = open.Take(() => pageSize.Value);
```

| C# | F# |
| --- | --- |
| `Where`, `Select`, `OrderBy`, `GroupBy` | `Projection.filter`, `map`, `sortBy`, `groupBy` |
| `Take`, `Skip`, `Slice` | `Projection.take`, `skip`, `sub` |
| `Sum` for `int`, `long`, `decimal` and `double` | `Projection.sumBy` |
| `Count`, `Any`, `All` | `Projection.countBy`, `exists`, `forall` |
| `Aggregate(seed, folder)` | `Projection.fold` |
| `Aggregate(zero, add, subtract)` | `Projection.foldGroup` |

`rows.TryGetValue(key, out var row)` reads a row that may be absent; `Lookup` has the same method.
`AsObservableCollection` binds a projection to a WPF, Avalonia or MAUI list.

## Options and threads

`GraphOptions` is built with `With` methods:

```csharp
var graph = new Graph(GraphOptions.Default
    .WithFlightPolicy(FlightPolicy.Queue)
    .WithDispatcher(new ManualDispatcher()));
```

`graph.Dispatch(() => …)` marshals a write from another thread, as described in
[Async and pending](async-and-pending.md#threading-and-dispatch).

## Tracing

`Tracing` wraps the [trace log](tracing.md) for C#. `Tracing.Named` and `Tracing.Label` compile in every
build. The queries exist in a traced build only, from the `Ranvier.CSharp.Traced` package or a source build
with `-p:RanvierTrace=true`, and return text:

```csharp
var graph = new Graph();

graph.Run(() =>
{
    var count = Tracing.Named("count", () => Signal(1));
    var log = Effect(() => Console.WriteLine(count.Value));
    Tracing.Named("changes", () => EffectOn(() => count.Value, value => { }));
    Flush();

    count.Value = 2;
    Flush();
#if RANVIER_TRACE
    Console.WriteLine(Tracing.Why(graph, log));
    Console.WriteLine(Tracing.Why(graph, "/changes"));
#endif
});
```

| C# | F# |
| --- | --- |
| `Named(label, () => …)` | `Trace.named` |
| `Label(graph, node, text)` | `Trace.label` |
| `Origin`, `Why`, `WhyDepth`, `WhyNot`, `History`, `WaitingOn` | the same queries, passed to `Trace.render` |
| `Snapshot(graph)`, `Snapshot(graph, seq)` | `Trace.snapshot`, `Trace.snapshotAt`, rendered |
| `Resolve`, `Reconcile`, `Events`, `DumpText`, `Dump` | `Trace.resolve`, `reconcile`, `events`, `dumpText`, `dump` |

Each per-node query takes the node, or its identity path such as `"/changes"`. The path form reaches nodes
without a handle, such as an `EffectOn`. `Tracing.Label` is `[Conditional("RANVIER_TRACE")]`: the call
stays only in a project that defines `RANVIER_TRACE` itself.

## Limits

- **`Graph.Current` everywhere.** The factories read the thread's active graph. The constructors, such as
  `new Memo<int>(graph, previous => …)`, take the graph explicitly.
