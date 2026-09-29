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

## Tracing

`Ranvier.Traced` is the same assembly, namespaces and version built with the per-graph event log and its queries.
Reference it in place of `Ranvier` for development builds; see
[Tracing](https://shayanhabibi.github.io/Ranvier/guide/tracing/).

## Links

- [Documentation](https://shayanhabibi.github.io/Ranvier/)
- [Source](https://github.com/shayanhabibi/Ranvier)
- [MIT License](https://github.com/shayanhabibi/Ranvier/blob/master/LICENSE)
