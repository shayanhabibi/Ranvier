# Ranvier.Elmish

An Elmish-style model-view-update bridge for [Ranvier](https://github.com/shayanhabibi/Ranvier). The model lives in a
signal: `Dispatch` applies `update`, and `Select` reads a part of the model through a memo that wakes its readers only
when that part changes.

> **Preview.** Ranvier is pre-release. Its APIs may change before the first release.

```fsharp
open Ranvier
open Ranvier.Elmish

type Msg = Increment

use graph = new Graph ()

graph.Run (fun () ->
    let app = Mvu.create {| Count = 0 |} (fun Increment model -> {| model with Count = model.Count + 1 |})
    let count = app.Select _.Count
    createEffect (fun () -> printfn "count = %d" count.Value)
    app.Dispatch Increment)
```

`Mvu.withCmd` takes an `update` that also returns commands. A command list has the shape of Elmish's `Cmd`, so
Elmish commands pass unchanged; the package does not depend on Elmish.

## Targets

`net10.0`, `net8.0` and `netstandard2.1`.

## Tracing

`Ranvier.Elmish.Traced` is the same assembly built against `Ranvier.Traced`. Reference it together with
`Ranvier.Traced`; see [Tracing](https://shayanhabibi.github.io/Ranvier/guide/tracing/).

## Links

- [Elmish guide](https://shayanhabibi.github.io/Ranvier/guide/elmish/)
- [Source](https://github.com/shayanhabibi/Ranvier)
- [MIT License](https://github.com/shayanhabibi/Ranvier/blob/master/LICENSE)
