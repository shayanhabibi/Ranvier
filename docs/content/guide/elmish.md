---
title: Migrating from Elmish
order: 12
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

`Mvu` holds an Elmish-style model in a signal. `init` and `update` stay as they are, and each view reads the parts of
the model it shows through selectors: memos that wake their readers only when their part changes. An application can
move one view at a time, while the rest keeps its Elmish loop.

`Mvu` ships in its own package, `Ranvier.Elmish`, which depends on `Ranvier`:

```bash
dotnet add package Ranvier.Elmish --prerelease
```

`Mvu` has no Elmish dependency. A command is a plain function of type `('Msg -> unit) -> unit`, and a command list has
the shape of Elmish's `Cmd<'Msg>`, so Elmish commands pass through unchanged.

## A model and its update

```fsharp
open Ranvier
open Ranvier.Elmish

type Model = { Count: int; Name: string; Address: Address }
and Address = { City: string; Zip: string }

type Msg =
    | Increment
    | Rename of string
    | Move of string

let init = { Count = 0; Name = "Ada"; Address = { City = "Bergen"; Zip = "5003" } }

let update msg model =
    match msg with
    | Increment -> { model with Count = model.Count + 1 }
    | Rename name -> { model with Name = name }
    | Move city -> { model with Model.Address.City = city }

let graph = new Graph ()

graph.Run (fun () ->
    let app = Mvu.create init update
    let count = app.Select _.Count
    createEffect (fun () -> printfn "count = %d" count.Value)

    app.Dispatch Increment      // count = 1
    app.Dispatch (Rename "Grace") // the count effect stays asleep
)
```

- `Mvu.create init update` holds `init` in a signal. `Dispatch msg` writes `update msg model` to it.
- `Model` is a tracked read of the whole model. An effect that reads it runs on every change, as an Elmish `view` does.
- `Select f` is a memo over `f model`. It re-runs on each model write while something reads it, and wakes its readers
  only when its result changes under the graph's [equality cutoff](getting-started.md#equality-cutoff).
- An `update` that returns its argument wakes nothing: the write stops at the signal's cutoff.

## Commands

`Mvu.withCmd` takes an `init` and an `update` that return a model and a command list, as Elmish's `Program.mkProgram`
does:

```fsharp
let update msg model =
    match msg with
    | Load -> { model with Loading = true }, [ fun dispatch -> fetch (fun items -> dispatch (Loaded items)) ]
    | Loaded items -> { model with Loading = false; Items = items }, []

let app = Mvu.withCmd (initial, [ fun dispatch -> dispatch Load ]) update
```

Each command runs after the write that came with it, in order, with `Dispatch` as its argument. The initial commands
run before `withCmd` returns.

## Dispatch and threads

- A `Dispatch` on the graph's thread runs inline. A dispatch from an effect joins the flush that is running, as any
  write from an effect does, and neither `update` nor the commands are tracked by the effect.
- A `Dispatch` from another thread is queued and applied on the graph's thread, as
  [`Graph.Dispatch`](async-and-pending.md#threading-and-dispatch) queues work. A command that completes on the thread pool can call
  `dispatch` directly.
- Under `ThreadAffinity.Serialised`, a `Dispatch` runs inline only on the thread inside the graph. A `Dispatch` from
  anywhere else, including a thread on the construction context, is queued and applied at the next drain. A graph
  constructed with no `SynchronizationContext` drains only when `graph.Pump ()` runs, so the message stays queued until
  then. See [Serialised hosts](../concepts/contracts.md#serialised-hosts).

## Selectors and their cost

Each write re-runs every selector over the model that something reads. That is the cost profile of Elmish's `lazy`
at memo granularity: a selector is a comparison, not a view diff. A selector nothing reads does not run.

Nest selectors to cut the re-run set. A selector over another selector's memo re-runs only when that memo changes:

```fsharp
let address = app.Select _.Address
let city = createMemo (fun _ -> address.Value.City)
```

A write to `Count` re-runs `address`, which returns the same `Address` record and stops there; `city` stays asleep.
Under the default policy records compare by reference, so a nested copy-and-update keeps every record off the written
path, and the selectors over those records stay asleep. See [Deep updates](collections.fsx#deep-updates).

| Per write, one field changed, one reader per field | Cost |
| --- | --- |
| A signal per field (`FieldSignalWrite`) | One signal write; flat in the number of fields. |
| A root signal and a selector per field (`SelectorMemoWrite`, `MvuDispatch`) | A model copy, plus one selector run per field. |
| The model copy alone (`ModelCopyOnly`) | The allocation of the new model. |

`FieldWriteBenchmarks` in `bench/Ranvier.Benchmarks` measures these at 8, 64 and 256 fields. The counter scenario
`mvu-dispatch` measures the first two at 64 fields, in retired instructions per write at commit `e13f159`, .NET 10 with
tiered compilation and PGO off:

| Per write, 64 fields | Instructions | Bytes | Selector runs |
| --- | ---: | ---: | ---: |
| A signal per field | 607 | 0 | 0 |
| `Mvu.Dispatch` with a `Select` per field | 48,133 | 400 | 64 |

A dispatch re-runs all 64 observed selectors and costs about 80 times a field signal write. Nested selectors cut that
set to the selectors on the written path. When a view reads many fields of a record that changes often, the
record-of-signals pattern in [Editable values and forms](forms.md) costs one signal write per field instead. See
[Instruction counts](../benchmarks/counters.md) and the [full report](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

## An adoption path

1. Keep `init` and `update`. Replace `Program.mkProgram` with `Mvu.withCmd`, and run the existing `view` from an
   effect that reads `app.Model` and passes `app.Dispatch`.
2. Move one view to selectors: read `app.Select` memos in its effects, not `app.Model`.
3. Split the next view, and nest selectors where a view shows part of a sub-model.

## Owners instead of hooks

Selectors and effects belong to the scope that is current when they are created, not to a call position. A view
created inside `createRoot` or an owning memo is disposed with it, selectors included. There are no rules of hooks:
a view may create selectors in a branch or a loop. See [Owners instead of hooks](../concepts/ecosystem.md#owners-instead-of-hooks).
