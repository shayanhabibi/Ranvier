---
title: C#
order: 10
description: Using Ranvier from C# through the Ranvier.CSharp package.
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Create signals, derive values and react to changes from C# with `Ranvier.CSharp`.

The `Reactive` factories take familiar `Func` and `Action` delegates. They return the engine's own
nodes, including `Signal<T>`, `Memo<T>`, `AsyncMemo<T>` and `Projection<K, V>`. Extension methods
provide `Graph.Run`, `Signal.Update` and the collection operators.

## At a glance

A city drives an async temperature request; a computed property formats the result for a XAML view.
`ReactiveObject` handles property notifications, loading and errors.

:::details A complete weather view model

```csharp
public interface IWeatherService
{
    Task<int> TemperatureAsync(string city, CancellationToken token);
}

public sealed class WeatherViewModel : ReactiveObject
{
    readonly BoundSignal<string> city;
    readonly Signal<int> attempt;
    readonly BoundValue<string> forecast;

    public WeatherViewModel(Graph graph, IWeatherService weather) : base(graph)
    {
        city = Bindings.Writable(nameof(City), "Oslo");
        attempt = Bindings.Run(() => Signal(0));
        var celsius = Bindings.Run(() => Async(token =>
        {
            _ = attempt.Value;
            return weather.TemperatureAsync(city.Value, token);
        }));
        forecast = Bindings.Computed(nameof(Forecast), () => $"{City}: {celsius.Value} °C");
    }

    public string City { get => city.Value; set => city.Value = value; }
    public string Forecast => forecast.Value;
    public void Retry() => attempt.Value++;
}
```

:::

Bound to a view, the example behaves as follows:

- **Loading.** While the temperature is in flight, `IsLoading` is true and `Forecast` keeps its last value, or `null` before the first result. Setting
  `City` starts a new request.
- **Value.** When the request completes, `Forecast` raises `PropertyChanged`, then `IsLoading` turns false.
- **Error.** When the request fails, `HasErrors` turns true, `ErrorsChanged` is raised for `Forecast`, and
  `GetErrors("Forecast")` returns the exception's message. `Forecast` still shows the last value.
- **Retry.** `Retry` writes the signal the request reads, so the request runs again for the current city. The error
  clears while it loads, and the next result replaces it.

`ReactiveObject` implements `INotifyPropertyChanged`, `INotifyDataErrorInfo` and `IDisposable`,
and exposes `IsLoading` and `HasErrors`. See [Binding to XAML](#binding-to-xaml) for the details.

:::details Runnable sample
The sample runs as a test in `tests/Ranvier.CSharp.Tests/HeadlineSampleTests.cs`.
:::

## Setup

Reference `Ranvier.CSharp`; it brings `Ranvier` with it:

```bash
dotnet add package Ranvier.CSharp --prerelease
```

:::details Reference a source build

```xml
<ProjectReference Include="path/to/Ranvier/src/Ranvier.CSharp/Ranvier.CSharp.fsproj" />
```

:::

Import the factories statically:

```csharp
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;
```

## A first graph

A signal holds a value, a memo derives one, and an effect reacts to changes in what it reads.

::::details Test your understanding

What does the effect print on construction? What does it print when `count` changes to `2`?

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

:::details Answer

```text
doubled 2
doubled 4
```

The first read computes `doubled` from `1`. The write refreshes it from `2` and runs the effect
again before the write returns.

:::
::::

:::info Activate the graph
Every factory uses the active graph. Call factories inside `graph.Run`, or while a
`using (graph.Activate ())` block is open.
:::

The map runs the same engine behaviour as the C# example. Set the count to `2` and watch
the derived value update before the effect prints it.

```fsharp map replay show=output
let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)
createEffect (fun () -> printfn "doubled %d" doubled.Value)

controls [
    button "Set count to 2" (fun () -> count.Value <- 2)
    |> describe "The count write refreshes doubled to 4."
    |> expect "The count write refreshes doubled to 4." (fun () -> doubled.Peek = 4)
]
```

## The factories

Use `Signal` for writable state, `Memo` for derived state, and `Effect` for side effects.
The remaining factories cover async values, boundaries, scopes and collections.

:::details C# factories and their F# equivalents

| C# | F# |
| --- | --- |
| `Signal(initial)` | `createSignal` |
| `Memo(() => …)` | `createMemo` |
| `Memo(previous => …, seed)` | `createMemo`, with the previous value or `seed` |
| `OwningMemo(() => …)` | `createMemoWith` |
| `Editable(() => …)`, `Draft(() => …)` | `createEditable`, `createDraft` |
| `Effect(() => …)` | `createEffect`, returning the `Effect`: dispose it to stop the effect early |
| `EffectOn(() => …, value => …)` | `createEffectOn` |
| `Async(token => …)`, `Async((previous, token) => …)` | `createAsync` |
| `OwningAsync(token => …)` | `createAsyncWith` |
| `AsyncSource<T>()` | `createAsyncSource` |
| `Suspense(body, fallback)`, `Suspense(body, previous => …, seed)` | `createSuspense` |
| `ErrorBoundary(body, error => …)`, `ErrorBoundary(body, (error, previous) => …, seed)` | `createErrorBoundary` |
| `Boundary(body, fallback, error => …)`, `Boundary(body, previous => …, (error, previous) => …, seed)` | `createBoundary` |
| `Batch`, `Untrack`, `OnCleanup`, `Flush` | `batch`, `untrack`, `onCleanup`, `flush` |
| `Root(owner => …)` | `createRoot` |
| `CurrentOwner`, `RunWithOwner(owner, …)` | `getOwner`, `runWithOwner` |
| `Projection(source, keyOf, map)` | `createProjection` |
| `IndexProjection(source, map)` | `createIndexProjection` |
| `Lookup(source, f, affected)`, `Selector(source)` | `createLookup`, `createSelector` |

:::

:::tip Keep the previous value while loading
Boundary overloads that take a `seed` pass the last published value to their fallback and recovery
handlers. Before the first value, they pass the seed.

A fallback that returns its argument keeps the previous value visible while the body reloads:

```csharp
var shown = Suspense(() => name.Value, previous => previous, "Loading");
```

:::

:::details Construct a boundary with an explicit graph
`Boundary<T>.Suspense`, `Errors` and `Catching` take the graph explicitly, with the seed before the handlers.
:::

## Async values

`Async` takes a task factory. A new request cancels the token of the request it supersedes.

A boundary turns loading, success and failure into values the view can display.

:::details A search view model

`Query` drives the request. `Summary` displays its results, a loading message or an error message.
`IsSearching` and `Error` read the boundary's state.

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

:::

Construct it inside `graph.Run`, and bind the view with an effect:

```csharp
var graph = new Graph();
var search = graph.Run(() => new SearchViewModel(service));
graph.Run(() => Effect(() => Console.WriteLine(search.Summary.Value)));

search.Query.Value = "ada";
```

With the effect observing `Summary`:

- A write to `Query` starts a search and cancels the previous request's token.
- While loading, the effect prints `Searching…` and `IsSearching` is `true`.
- A failure prints `Search failed: …` and sets `Error`.
- A later success prints the results and clears `Error`.

:::warning Read inputs before awaiting
Reads after the first `await` are not tracked. Read every reactive input before it so that changes
can start a new request.
:::

:::details Use the previous async value

The overload taking `Previous<T>` receives the value last published:

- `previous.SettledOr(seed)` returns it, or the seed before the first value.
- `previous.TrySettled()` returns `(HasValue, Value)`.

```csharp
var total = Async<int>(async (previous, token) =>
{
    var by = step.Value;
    return await previous.SettledOr(0) + by;
});
```

Both return a `ValueTask` that is already complete under `CancelPrevious` and `KeepLatest`. Under `Queue` it
completes once the flight started before this one is applied.

:::

:::details Async completion and threads
A request that completes on the thread pool reaches the graph through its dispatcher. See
[Async and pending](async-and-pending.md#threading-and-dispatch).
:::

### Read without throwing

`TryValue` lets you handle ready, failed and pending states explicitly. Use `TryGetValue` and
`TryGetError` to take the result apart:

```csharp
if (price.TryValue.TryGetValue(out var value)) Console.WriteLine(value);
else if (price.TryValue.TryGetError(out var error)) Console.WriteLine(error.Message);
else Console.WriteLine("pending");
```

## Collections

Projection operators use LINQ names and return live nodes that update per changed row.

```csharp
var rows = Projection(() => todos.Value, t => t.Id, t => t);

var open = rows.Where(t => !t.Done).OrderBy(t => t.Title);
var hours = rows.Sum(t => t.Hours);
var remaining = rows.Count(t => !t.Done);
var firstPage = open.Take(() => pageSize.Value);
```

:::details Collection operators and their F# equivalents

| C# | F# |
| --- | --- |
| `Where`, `Select`, `OrderBy`, `GroupBy` | `Projection.filter`, `map`, `sortBy`, `groupBy` |
| `Take`, `Skip`, `Slice` | `Projection.take`, `skip`, `sub` |
| `Sum` for `int`, `long`, `decimal` and `double` | `Projection.sumBy` |
| `Count`, `Any`, `All` | `Projection.countBy`, `exists`, `forall` |
| `Aggregate(seed, folder)` | `Projection.fold` |
| `Aggregate(zero, add, subtract)` | `Projection.foldGroup` |

:::

Use `rows.TryGetValue(key, out var row)` for a row that may be absent. `Lookup` supports the same
method. Use `AsObservableCollection` to bind a projection to a WPF, Avalonia or MAUI list.

:::details Read collection changes directly

`rows.NewKeyReader()` returns a disposable reader. Each `Read()` reports the keys added, removed
or replaced since the previous read:

```csharp
using var reader = rows.NewKeyReader();
reader.Read(); // the first read reports a reset: rebuild from Keys

var delta = reader.Read();
foreach (var (key, change) in delta.Changes)
{
    switch (change)
    {
        case KeyChange.Added: /* insert key */ break;
        case KeyChange.Removed: /* drop key */ break;
        case KeyChange.Replaced: /* rebuild key */ break;
    }
}
```

`delta.IsReset` asks for a rebuild from `delta.Keys`, and `delta.Positional` lists the index edits from
`PreviousKeys` to `Keys`.

:::

## Binding to XAML

`ReactiveBindings` connects graph nodes to view-model property and error notifications.

- `Writable` registers a two-way property backed by a signal.
- `Computed` registers a read-only property backed by a tracked `Func<T>`.

A property raises `PropertyChanged` once per settled change. An equal derived result raises
nothing, and derived properties track their dependencies automatically.

For a new view model, derive from `ReactiveObject`. Its `Bindings` implement
`INotifyPropertyChanged`, `INotifyDataErrorInfo` and `IDisposable` for you.

:::details Add bindings to an existing view model

Keep its base class. Construct `ReactiveBindings` with the view model as the event sender, then
forward the events:

```csharp
public sealed class OrderViewModel : ObservableObject, INotifyDataErrorInfo, IDisposable
{
    readonly ReactiveBindings bindings;
    readonly BoundSignal<int> quantity;
    readonly BoundValue<decimal> price;
    readonly BoundValue<decimal> total;

    public OrderViewModel(Graph graph, IPriceService prices)
    {
        bindings = new ReactiveBindings(this, graph);
        bindings.PropertyChanged += (_, e) => OnPropertyChanged(e);
        bindings.ErrorsChanged += (_, e) => ErrorsChanged?.Invoke(this, e);

        quantity = bindings.Writable(nameof(Quantity), 1);
        var quote = bindings.Run(() => Async(token => prices.QuoteAsync(quantity.Value, token)));
        price = bindings.Computed(nameof(Price), () => quote.Value);
        total = bindings.Computed(nameof(Total), () => price.Memo.Value * quantity.Value);
    }

    public int Quantity { get => quantity.Value; set => quantity.Value = value; }
    public decimal Price => price.Value;
    public decimal Total => total.Value;
    public bool IsLoading => bindings.IsLoading;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => bindings.HasErrors;
    public IEnumerable GetErrors(string? propertyName) => bindings.GetErrors(propertyName!);
    public void Dispose() => bindings.Dispose();
}
```

:::

### Loading and errors

While a computed property is loading, it keeps its last settled value (`default` before the first)
and its `IsLoading` is true. On failure, `Error` holds the exception and `GetErrors` returns its
message.

The bindings' `IsLoading` and `HasErrors` cover all properties and raise notifications under those
names. Pass `loadingName` to `Computed(name, compute, loadingName)` for a per-property loading
notification too.

:::tip Choose how dependent properties handle loading
On the graph's thread, reading another bound property's `Value` tracks it but keeps using its last
settled value while it loads or fails.

Read its `Memo.Value` instead to make the dependent property load and fail with that input, as
`Total` does in the example.
:::

:::details Threads and notifications

The notifying effect runs on the graph's thread. Each event handler runs on the
`SynchronizationContext` captured when it subscribed; notifications are posted there when raised
elsewhere.

From another thread, a bound property's `Value` returns the value last notified. Setting a
`Writable` property goes through `Graph.Dispatch`.
:::

:::details What disposal owns

The bindings create a root under the scope current at construction. Disposing the bindings or
that scope disposes the property memos, commands and anything created through `bindings.Run`, and
removes all handlers. Signals passed to `Writable` stay usable.
:::

## Commands

`bindings.Command` returns a `ReactiveCommand` that implements `ICommand`. Its eligibility tracks
the state read by its predicate; its busy state is a signal.

:::details An editor with Save and Load commands

Each command is disabled while the other runs. `IsBusy` combines both commands' running state.

```csharp
public sealed class EditorViewModel : ReactiveObject
{
    readonly BoundSignal<string> draft;
    readonly BoundValue<bool> isValid;
    readonly BoundValue<bool> isBusy;

    public EditorViewModel(Graph graph, IRepository repo) : base(graph)
    {
        draft = Bindings.Writable(nameof(Draft), "");
        isValid = Bindings.Computed(nameof(IsValid), () => Draft.Length > 0);
        Save = Bindings.Command((_, token) => repo.SaveAsync(draft.Value, token), () => IsValid && Load is { IsRunning: false });
        Load = Bindings.Command((_, token) => repo.LoadAsync(token), () => !Save.IsRunning);
        isBusy = Bindings.Computed(nameof(IsBusy), () => Save.IsRunning || Load.IsRunning);
    }

    public string Draft { get => draft.Value; set => draft.Value = value; }
    public bool IsValid => isValid.Value;
    public bool IsBusy => isBusy.Value;
    public ReactiveCommand Save { get; }
    public ReactiveCommand Load { get; }
}
```

:::

### Eligibility and state

`CanExecute` is true when the predicate permits execution. A pending or failed read makes it
false. Under the default `CommandPolicy.Disable`, a running execution also makes it false.

`CanRun`, `IsRunning` and `Error` raise `PropertyChanged`. On the graph's thread, reading them in a
computed property or another command's predicate tracks them. `Enabled` is the memo behind
`CanRun`.

:::details Predicates that refer to a later command

In the example, `Save` reads `Load`, which is constructed after it. A command first evaluates its
predicate on the first `CanExecute` call, event subscription or execution, after this constructor
has returned. The pattern `Load is { IsRunning: false }` satisfies C# null analysis.
:::

### Execution and cancellation

`ExecuteAsync(parameter)` marshals to the graph's thread and starts an enabled command. Its task
completes after the execution finishes and `IsRunning` and `Error` have been updated.

- `CommandPolicy.Disable` disables the command before its body starts, so a second click does nothing.
- `CommandPolicy.CancelPrevious` keeps it enabled. A new execution cancels the previous token;
  only the latest execution sets `Error`.

The body runs untracked. `Cancel`, `Dispose` and `CancelPrevious` cancel its token.

:::warning Inspect command errors
A failed execution sets `Error`. A later success clears it, as does an `OperationCanceledException`
after the command cancels its token.

A button calls `ICommand.Execute`, which discards the task. Its failures reach `Error`, rather
than escaping to the `SynchronizationContext`.
:::

:::details Commands on serialised graphs

Under `ThreadAffinity.Serialised`, `Execute` and `ExecuteAsync` start inline only on the thread
inside the graph. Other calls, including button handlers on the construction context, queue for
the next drain.

Without a captured `SynchronizationContext`, the graph drains only when `graph.Pump()` runs.
The execution and its returned task wait until then. See
[Serialised hosts](../concepts/contracts.md#serialised-hosts).
:::

:::details Command notifications and lifetime

`CanExecute` returns the value last notified, so any thread can call it. `CanExecuteChanged` and
`PropertyChanged` handlers run on the `SynchronizationContext` captured when they subscribed.

The bindings dispose their commands. Use `command.Dispose()` to stop one earlier and cancel its
executions.
:::

:::details Synchronous and standalone commands

`bindings.Command(parameter => …)` takes a synchronous `Action<object>` and batches its writes.
Outside `ReactiveBindings`, `Reactive.Command` creates a command on `Graph.Current` with an
effect of its own.
:::

:::tip Combine pending states
`AnyPending(quote, stock, shipping)` is true while any of those nodes is pending, even when their
value types differ.
:::

:::details Track status without throwing
`AnyPending` uses `graph.TrackStatus(node)`: a tracked read of `Status` that returns pending or
failed states without throwing.
:::

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

`Tracing` exposes the [trace log](tracing.md) through C# methods. Give nodes names, then query why
they ran or what they are waiting on.

:::info Use a traced build for queries
Queries return text and require `Ranvier.CSharp.Traced` or a source build with
`-p:RanvierTrace=true`. `Tracing.Named` and `Tracing.Label` compile in every build.
:::

:::details Name nodes and inspect their causes

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

:::

:::details Tracing methods and their F# equivalents

| C# | F# |
| --- | --- |
| `Named(label, () => …)` | `Trace.named` |
| `Label(graph, node, text)` | `Trace.label` |
| `Origin`, `Why`, `WhyDepth`, `WhyNot`, `History`, `WaitingOn` | the same queries, passed to `Trace.render` |
| `Snapshot(graph)`, `Snapshot(graph, seq)` | `Trace.snapshot`, `Trace.snapshotAt`, rendered |
| `Resolve`, `Reconcile`, `Events`, `DumpText`, `Dump` | `Trace.resolve`, `reconcile`, `events`, `dumpText`, `dump` |

:::

Each per-node query accepts a node or its identity path, such as `"/changes"`. Use a path to reach a
node without a handle, such as an `EffectOn`.

:::details Conditional labels
`Tracing.Label` is `[Conditional("RANVIER_TRACE")]`. Its call is retained only when the calling
project defines `RANVIER_TRACE`.
:::

## Limits

:::warning Factories need an active graph
The factories use the calling thread's active graph. `Graph.TryGetCurrent(out var graph)` tells
you whether one is active.
:::

:::details Construct a memo with an explicit graph
Use `new Memo<int>(graph, _ => …)`, or `new Memo<int>(graph, seed, previous => …)` for a memo that
receives its previous value. The seed comes before the compute function; `owning` is the last
argument in both constructors.
:::
