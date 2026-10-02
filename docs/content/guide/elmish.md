---
title: Migrating from Elmish
order: 12
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Keep your Elmish `init` and `update`, and move views to Ranvier one at a time.

`Mvu` holds the model in a signal. Each view reads the parts it needs through selectors: memos
that trigger their readers only when the selected value changes. The rest of the application can
keep its Elmish loop.

## Setup

`Mvu` ships in its own package, `Ranvier.Elmish`, which depends on `Ranvier`:

```bash
dotnet add package Ranvier.Elmish --prerelease
```

:::info Existing commands work unchanged
`Mvu` has no Elmish dependency. Its commands have the same shape as Elmish's `Cmd<'Msg>`: a list
of functions with type `('Msg -> unit) -> unit`.
:::

## A model and its update

Create an app with `Mvu.create init update`. Read a field with `app.Select`, and send messages
with `app.Dispatch`.

:::details A counter with name and address fields
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

:::

This map isolates selector behaviour: incrementing changes the count, while renaming changes
the model but leaves the count effect alone.

```fsharp map replay show=output
let model = createSignal {| Count = 0; Name = "Ada" |}
let count = createMemo (fun _ -> model.Value.Count)
let mutable effectRuns = 0
createEffect (fun () ->
    effectRuns <- effectRuns + 1
    printfn "count = %d" count.Value)

controls [
    button "Increment" (fun () -> model.Value <- {| model.Value with Count = model.Value.Count + 1 |})
    |> describe "The count selector publishes 1 and runs its effect."
    |> expect "The count selector publishes 1 and runs its effect." (fun () -> count.Peek = 1 && effectRuns = 2)
    button "Rename Grace" (fun () -> model.Value <- {| model.Value with Name = "Grace" |})
    |> describe "The model changes, but count remains 1 and its effect stays quiet."
    |> expect "The model changes, but count remains 1 and its effect stays quiet." (fun () -> count.Peek = 1 && effectRuns = 2)
]
```

`Dispatch msg` applies `update msg model` and writes the resulting model to the signal.

- `app.Model` tracks the whole model. An effect reading it runs on every model change, like an
  Elmish `view`.
- `app.Select f` tracks the selected value. The selector recomputes on model writes while observed,
  but triggers its readers only when its result changes.

:::tip Keep unchanged values unchanged
An `update` that returns the original model triggers nothing: the signal's
[equality cutoff](signals.fsx#equality) stops the write.
:::

::::details Test your understanding

In the counter example, does renaming Ada to Grace run the count effect again? Would an effect
reading `app.Model` run?

:::details Answer

The count selector recomputes, but still returns `1`, so its effect does not run again. An effect
reading `app.Model` runs because the model changed.
:::
::::

## Commands

Use `Mvu.withCmd` when `init` and `update` return a model and a command list, as with Elmish's
`Program.mkProgram`:

```fsharp
let update msg model =
    match msg with
    | Load -> { model with Loading = true }, [ fun dispatch -> fetch (fun items -> dispatch (Loaded items)) ]
    | Loaded items -> { model with Loading = false; Items = items }, []

let app = Mvu.withCmd (initial, [ fun dispatch -> dispatch Load ]) update
```

Each command receives `Dispatch` and runs in list order after the accompanying model write.
Initial commands run before `withCmd` returns.

## Dispatch and threads

Under the default affinity, `Dispatch` runs inline on the graph's thread. Calls from other threads
are queued and applied on that thread, through
[`Graph.Dispatch`](threading.md).

:::tip Dispatch an async result directly
A command that completes on the thread pool can call `dispatch` directly. `Mvu` handles sending
the update to the graph's thread.
:::

:::details Dispatch from an effect
A dispatch joins the flush already running. Neither `update` nor its commands become tracked
parts of the effect.
:::

:::details Serialised graphs

Under `ThreadAffinity.Serialised`, dispatch runs inline only on the thread already inside the
graph. Other calls, including calls on the construction context, queue for the next drain.

Without a captured `SynchronizationContext`, the graph drains only when `graph.Pump ()` runs.
Until then, the message stays queued. See
[Serialised hosts](../concepts/contracts.md#serialised-hosts).
:::

## Selectors and their cost

Each model write recomputes every observed selector directly over the model. Unobserved selectors
do not run. A selector compares its result; it does not diff a view.

Nest selectors to reduce this work. A memo over another selector runs only when that selector's
result changes:

```fsharp
let address = app.Select _.Address
let city = createMemo (fun _ -> address.Value.City)
```

::::details Test your understanding

After a write to `Count`, does `address` recompute? Does `city` recompute?

:::details Answer

`address` recomputes if observed, but returns the same `Address` record. Propagation stops there,
so `city` does not recompute.

Under the default equality policy, records compare by reference. A nested copy-and-update keeps
records outside the changed path, allowing their selectors to cut off propagation. See
[Deep updates](collection-updates.fsx).
:::
::::

:::tip Frequently changing forms
If a view reads many fields of a frequently changing record, consider the record-of-signals pattern
in [Editable values and forms](forms.md). Updating one field then costs one signal write.
:::

:::details Selector costs and benchmark results

Like Elmish's `lazy`, selectors compare values to avoid downstream work, but at memo granularity.
They still run to make that comparison.

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

In this benchmark, dispatch re-runs all 64 observed selectors and costs about 80 times a field
signal write. Nesting selectors reduces downstream runs to the changed path. See
[Instruction counts](../benchmarks/counters.md) and the
[full report](https://github.com/shayanhabibi/Ranvier/blob/master/docs/.ai/benchmarks/counters/e13f159.md).

:::

## An adoption path

1. **Keep the whole-model view.** Keep `init` and `update`, replace `Program.mkProgram` with
   `Mvu.withCmd`, and run the existing `view` from an effect reading `app.Model`. Pass it `app.Dispatch`.
2. **Migrate one view.** Read `app.Select` memos in that view's effects instead of `app.Model`.
3. **Repeat.** Move the next view, nesting selectors when it displays part of a sub-model.

## Owners instead of hooks

Selectors and effects belong to the scope active when they are created. Create a view inside
`createRoot` or an owning memo to dispose its nodes, including selectors, with that scope.

:::info Branches and loops are allowed
Ownership depends on scope, not call position. A view can create selectors inside a branch or a
loop. See [Owners instead of hooks](../concepts/ecosystem.md#owners-instead-of-hooks).
:::
