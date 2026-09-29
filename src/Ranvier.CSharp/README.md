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

## Binding to XAML

`ReactiveBindings` raises `INotifyPropertyChanged` and `INotifyDataErrorInfo` for view-model properties backed by
memos and signals, inside any existing view model; `ReactiveObject` is a base class over it. See the
[C# guide](https://github.com/shayanhabibi/Ranvier/blob/master/docs/content/guide/csharp.md#binding-to-xaml).

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
