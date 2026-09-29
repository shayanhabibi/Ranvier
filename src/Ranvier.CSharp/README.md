# Ranvier.CSharp

C# entry points for [Ranvier](https://github.com/shayanhabibi/Ranvier): the `Reactive` factories take
delegates, and extension methods cover `Graph.Run`, `Signal.Update` and the projection operators.

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
