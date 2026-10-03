# Ranvier.CSharp

C# entry points for [Ranvier](https://github.com/shayanhabibi/Ranvier): the `Reactive` factories take
delegates, and extension methods cover `Graph.Run`, `Signal.Update` and the projection operators.

## Per-node equality

Signals, memos (including owning and seeded forms), split effects and boundaries accept an
optional typed comparer through additional overloads:

```csharp
var name = Reactive.Signal("Ada", StringComparer.OrdinalIgnoreCase);
var label = Reactive.Memo(() => name.Value.Trim(), StringComparer.OrdinalIgnoreCase);
Reactive.EffectOn(() => label.Value, Console.WriteLine, StringComparer.OrdinalIgnoreCase);
```

Call these factories inside `graph.Run` or an active graph scope. Omit the comparer to use the
graph policy. A supplied comparer applies only to that node; pending/error state transitions
and ownership rules still apply. Null comparers throw `ArgumentNullException`.

## Basic use

```csharp
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;

var graph = new Graph();

graph.Run(() =>
{
    var count = Signal(1);
    var doubled = Memo(() => count.Value * 2);
    Effect(() => Console.WriteLine($"doubled {doubled.Value}"));

    count.Value = 2;
});
```

## Async values

A derived value over an async source, with loading and failure surfaced by a boundary instead of a hand-written
busy flag and `try`/`catch`:

```csharp
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

`ISearchService` declares one method, `Task<IReadOnlyList<string>> SearchAsync(string query, CancellationToken token)`.
Construct the view model inside `graph.Run`. The first read of `Summary` after a write to `Query` starts a new search
and cancels the token of the one in progress; `IsSearching` and `Error` follow the boundary.

## Binding to XAML

`ReactiveBindings` raises `INotifyPropertyChanged` and `INotifyDataErrorInfo` for view-model properties backed by
memos and signals, inside any existing view model; `ReactiveObject` is a base class over it. See the
[C# guide](https://github.com/shayanhabibi/Ranvier/blob/master/docs/content/guide/csharp.md#binding-to-xaml).
`ReactiveCommand` is an `ICommand` whose `CanExecute` and busy state come from graph nodes; see
[Commands](https://github.com/shayanhabibi/Ranvier/blob/master/docs/content/guide/csharp.md#commands).

## Editable collections

Create an editable source inside `graph.Run`, then derive views from its `Rows`:

```csharp
var items = Reactive.KeyedCollection<(int Id, string Title), int>(item => item.Id);
items.Edit(edit =>
{
    edit.AddOrUpdate((1, "Write"));
    edit.AddOrUpdate((2, "Test"));
});
var titles = items.Rows.Select(item => item.Title);
using var reader = titles.NewValueReader();
reader.Read();
items.AddOrUpdate((2, "Retest"));
var delta = reader.Read();
```

An existing-key write preserves its position and updates its row directly. New keys append;
`Remove` and `Clear` change membership. `Edit` batches synchronous writes and retains applied
edits if the callback throws.

`NewKeyReader` reports membership and order; `NewValueReader` also reports `KeyChange.Changed`
for unequal settled values under the graph policy. First reads and overflow report a reset.
Deltas contain coalesced key hints: read current values through the projection and handle its
pending/error states there. Dispose readers to release shared observation. See the
[C# collection guide](https://shayanhabibi.github.io/Ranvier/guide/csharp/#collections).

## Tracing

`Tracing.Named` and `Tracing.Label` compile in every build. The queries (`Origin`, `Why`, `WhyDepth`, `WhyNot`,
`History`, `WaitingOn`, `Snapshot`, `Resolve`, `Reconcile`, `Events`, `DumpText`, `Dump`) return text, and exist only
in the traced package, `Ranvier.CSharp.Traced`. It holds the same assembly, namespaces and version as
`Ranvier.CSharp`, and depends on `Ranvier.Traced`. Switch between the two and define `RANVIER_TRACE` with them:

```xml
<PropertyGroup Condition="'$(RanvierTrace)' == 'true'">
    <DefineConstants>$(DefineConstants);RANVIER_TRACE</DefineConstants>
</PropertyGroup>
<ItemGroup>
    <PackageReference Include="Ranvier.CSharp" Condition="'$(RanvierTrace)' != 'true'" />
    <PackageReference Include="Ranvier.CSharp.Traced" Condition="'$(RanvierTrace)' == 'true'" />
</ItemGroup>
```

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

Each per-node query takes the node, or its identity path. `Tracing.Label` is `[Conditional("RANVIER_TRACE")]`: the
call stays only in a project that defines `RANVIER_TRACE` itself.
