---
title: C#
order: 10
description: Using Ranvier from C# through the Ranvier.CSharp package.
---

:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change.
:::

`Ranvier.CSharp` puts the F# `Api` behind C# names: `Reactive` holds one static factory per `Api` function, each
taking `Func` and `Action` delegates, and extension methods cover `Graph.Run`, `Signal.Update` and the projection
operators. The nodes it returns are the engine's own `Signal<T>`, `Memo<T>`, `AsyncMemo<T>` and `Projection<K, V>`.

## Setup

Reference `Ranvier.CSharp`; it brings `Ranvier` with it. Built from source:

```xml
<ProjectReference Include="path/to/Ranvier/src/Ranvier.CSharp/Ranvier.CSharp.fsproj" />
```

Import the factories statically:

```text
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;
```

## A first graph

```text
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
| `Effect(() => …)` | `createEffect` |
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

```text
var userId = Signal(1);
var user = Async(token => api.GetUserAsync(userId.Value, token));
var shown = Suspense(() => user.Value.Name, () => "Loading…");
```

Read every input before the first `await`: a read after it is not tracked. The overload taking `Previous<T>`
awaits the value last published through `previous.Settled`, whose result is a `ValueOption`: test `IsSome`,
then read `Value`.

`TryValue` reads a node without raising. `TryGetValue` and `TryGetError` take it apart:

```text
if (price.TryValue.TryGetValue(out var value)) Console.WriteLine(value);
else if (price.TryValue.TryGetError(out var error)) Console.WriteLine(error.Message);
else Console.WriteLine("pending");
```

## Collections

The projection operators carry LINQ names and return live nodes. Each updates per changed row.

```text
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

```text
var graph = new Graph(GraphOptions.Default
    .WithFlightPolicy(FlightPolicy.Queue)
    .WithDispatcher(new ManualDispatcher()));
```

`graph.Dispatch(() => …)` marshals a write from another thread, as described in
[Async and pending](async-and-pending.md#threading-and-dispatch).

## Limits

- **No tracing.** The trace log and its queries are F# only.
- **`Graph.Current` everywhere.** The factories read the thread's active graph. The constructors, such as
  `new Memo<int>(graph, previous => …)`, take the graph explicitly.
