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

Tracing is available from F# only.
