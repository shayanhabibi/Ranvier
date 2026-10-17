# Direct bindings in template rows

`T.from` slots were designed to bind directly to their signal or memo with the core fixed-dependency binding (`Api.bindSignal`, `Api.bindMemo`) instead of being read in the instance's render effect. A measurement of the benchmark row's shape shows that switching them would make mount, allocation and update slower, so `T.from` slots stay in the render effect.

## Measurement

The benchmark row has three `T.from` slots (two on one signal, one on a memo) and one slot read in the render effect. The two shapes compared, per row:

- **Render effect**: one effect reading all four slots.
- **Effect plus bindings**: one effect reading the remaining slot, plus three bindings.

Recorded 2026-10-14 with BenchmarkDotNet on .NET 10, AMD Ryzen 9 9900X, one run, at commit `9bec88d` with the benchmark below added to `bench/Ranvier.Benchmarks/Bindings.fs` and not committed. Raw report: `docs/.ai/benchmarks/results/Ranvier.Benchmarks.Bindings.TemplateShapeBenchmarks-report-github.md`.

| Shape | Mount, 128 rows | Allocated | Update (one write, 128 rows) |
| --- | ---: | ---: | ---: |
| Render effect | 24.2 µs | 68.9 KB | 3.44 µs |
| Effect plus bindings | 52.2 µs | 129.3 KB | 5.44 µs |
| Ratio | 2.16× | 1.88× | 1.58× |

A binding costs about 0.7× an effect when one consumer replaces another, as the core benchmark showed. Here three bindings replace three tracked reads inside an effect that exists anyway, and a tracked read costs far less than a binding. Updates are slower too: one write reaches two bindings per row instead of one effect run.

## Consequence

`T.from` keeps its typed contract (the accessor names one source) but evaluates in the render effect. A direct binding pays off only where it replaces a whole effect: a template whose single dynamic slot is a `T.from` slot. That case is left for when a template shape needs it.

## Benchmark

```fsharp
/// <summary>SPIKE (uncommitted): a template row's render effect against one effect plus direct bindings.</summary>
[<MemoryDiagnoser; BenchmarkCategory "TemplateShape">]
type TemplateShapeBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, "a")
    let memo = Memo (graph, fun _ -> source.Value = "blocked")
    let mutable sink = 0
    let mutable counter = 0
    let writeString = fun (_: obj) (value: string) -> sink <- sink + value.Length
    let writeBool = fun (_: obj) (value: bool) -> sink <- sink + (if value then 1 else 0)
    let mutable owner: Owner = Unchecked.defaultof<_>

    [<Params(128)>]
    member val Rows = 128 with get, set

    [<Params("render-effect", "effect-plus-bindings")>]
    member val Shape = "render-effect" with get, set

    member this.Build() =
        graph.CreateRoot (fun owner ->
            for _ in 1 .. this.Rows do
                let local = Signal (graph, 0)

                match this.Shape with
                | "render-effect" ->
                    new Effect (graph, fun () ->
                        sink <- sink + source.Value.Length + source.Value.Length + (if memo.Value then 1 else 0) + local.Value)
                    |> ignore
                | _ ->
                    new Effect (graph, fun () -> sink <- sink + local.Value) |> ignore
                    Binding<obj, string>.OfSignal(graph, source, null, writeString) |> ignore
                    Binding<obj, string>.OfSignal(graph, source, null, writeString) |> ignore
                    Binding<obj, bool>.OfMemo(graph, memo, null, writeBool) |> ignore

            owner)

    /// <summary>Create and dispose <c>Rows</c> rows.</summary>
    [<Benchmark>]
    member this.Mount() = (this.Build ()).Dispose ()

    [<GlobalSetup(Target = "Update")>]
    member this.SetupUpdate() = owner <- this.Build ()

    [<GlobalCleanup(Target = "Update")>]
    member _.CleanupUpdate() = owner.Dispose ()

    /// <summary>One write to the shared source, flushed through every row.</summary>
    [<Benchmark>]
    member _.Update() =
        counter <- counter + 1
        source.Value <- string counter
```
