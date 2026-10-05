---
title: Blazor Server
order: 12
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Give each Blazor Server circuit its own graph. Use `ThreadAffinity.Serialised` so the graph can
follow the renderer's synchronisation context across pool threads.

The circuit normally runs one item at a time, but successive items may run on different threads.
Construct the graph on the renderer's context and keep graph work on that context.

:::info Blazor WebAssembly
Blazor WebAssembly runs on one thread and works under the default `Guarded` affinity.
:::

## One graph per circuit

Register the graph and shared state as scoped services. The container constructs the graph when
a component first requests it, capturing the renderer's context for the default dispatcher.

A component then follows three steps:

1. Create its nodes inside `graph.Run` and `createRoot` during initialisation.
2. Use an effect to request rendering when the values it displays change.
3. Dispose the root scope when the component is disposed.

Rendering reads the memos directly. Circuit-wide state lives outside the component's root, so
other components can share it.

:::details Complete cart component

`CartStore` owns the shared cart signal. `CartSummary` derives a total and an async shipping quote,
and requests a render when either changes. Constructor injection requires .NET 9 or later.

```fsharp
module Shop.Cart

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Rendering
open Microsoft.Extensions.DependencyInjection
open Ranvier

/// One graph per circuit, constructed on the renderer's context the first time a component asks for it.
type CircuitGraph() =
    let graph = new Graph (GraphOptions.Default.WithThreadAffinity Serialised)
    member _.Graph = graph

    // The container may dispose the circuit's services off the renderer's context; Dispatch runs the
    // disposal there.
    interface IDisposable with
        member _.Dispose() = graph.Dispatch (fun () -> graph.Dispose ())

/// Circuit-wide state, shared by every component on the circuit.
type CartStore(circuit: CircuitGraph) =
    let lines = circuit.Graph.Run (fun () -> createSignal List.empty<decimal>)
    member _.Lines = lines
    member _.Add(price: decimal) = lines.Value <- price :: lines.Value

let addCart (services: IServiceCollection) =
    services.AddScoped<CircuitGraph>().AddScoped<CartStore> ()

type CartView = { Total: decimal; Count: int }

/// Graph and store arrive by constructor injection (.NET 9 and later).
type CartSummary(circuit: CircuitGraph, cart: CartStore) =
    inherit ComponentBase()

    let graph = circuit.Graph

    let mutable scope: Owner option = None
    let mutable view: Memo<CartView> option = None
    let mutable shipping: AsyncMemo<decimal> option = None

    // F# calls protected members only from a member body, never from a lambda: hence the two wrappers.
    member private this.Rerender() = this.StateHasChanged ()

    member private this.RequestRender() =
        this.InvokeAsync (Action this.Rerender) |> ignore

    override this.OnInitialized() =
        graph.Run (fun () ->
            createRoot (fun owner ->
                let summary =
                    createMemo (fun _ ->
                        let lines = cart.Lines.Value
                        { Total = List.sum lines; Count = lines.Length })

                // Completes on a pool thread; the graph queues the settle to the circuit's context.
                let quote =
                    createAsync (fun _ token ->
                        let total = summary.Value.Total

                        task {
                            do! Task.Delay (200, token)
                            return if total >= 50m then 0m else 4.99m
                        })

                // One render request per settled change.
                createEffectOn (fun () -> struct (summary.TryValue, quote.TryValue)) (fun _ -> this.RequestRender ())

                scope <- Some owner
                view <- Some summary
                shipping <- Some quote))

    override this.BuildRenderTree(builder: RenderTreeBuilder) =
        match view, shipping with
        | Some summary, Some quote ->
            let cartView = summary.Value

            let delivery =
                match quote.TryValue with
                | Ready cost -> $"shipping %M{cost}"
                | Pending -> "shipping: calculating"
                | Failed ex -> $"shipping unavailable: %s{ex.Message}"

            builder.OpenElement (0, "p")
            builder.AddContent (1, $"%d{cartView.Count} items, %M{cartView.Total}, %s{delivery}")
            builder.CloseElement ()
            builder.OpenElement (2, "button")
            builder.AddAttribute (3, "onclick", EventCallback.Factory.Create (this, Action (fun () -> cart.Add 9.99m)))
            builder.AddContent (4, "Add")
            builder.CloseElement ()
        | _ -> ()

    interface IDisposable with
        member _.Dispose() = scope |> Option.iter _.Dispose()
```

:::

Call `addCart builder.Services` at startup to register the services.

This map illustrates the component's dependencies and loading state. Add items while the
shipping quote is pending, then answer it. The circuit's threading rules are covered below.

```fsharp map replay code=collapsed
let desk = Desk<decimal>()
let lines = createSignal [ 9.99m ]
let total = createMemo (fun _ -> List.sum lines.Value)
let shipping = createAsync (fun _ _ -> desk.Quote total.Value)
let view = createSuspense (fun _ -> "shipping: calculating") (fun () -> sprintf "shipping %M" shipping.Value)
createEffect (fun () -> printfn "total %M, %s" total.Value view.Value)

controls [
    button "Add five items" (fun () -> lines.Value <- lines.Value @ List.replicate 5 9.99m)
    |> describe "The new total starts another shipping quote and shows the loading fallback."
    |> expect "The new total starts another shipping quote and shows the loading fallback." (fun () -> desk.Pending = 1 && view.Peek = "shipping: calculating")
    button "Answer quote" (fun () -> desk.Settle (if total.Value >= 50m then 0m else 4.99m))
    |> describe "The total exceeds 50, so the settled shipping quote is free."
    |> expect "The total exceeds 50, so the settled shipping quote is free." (fun () -> shipping.Peek = 0m)
]
```

:::warning Activate the graph before creating nodes
`graph.Run` activates the graph around `createRoot`. Without activation, the `create*` functions
raise `No ambient graph on this thread`.
:::

::::details Test your understanding

The shipping quote costs `4.99` below a total of `50`, and is free at or above it. What does the
component show before and after the first quote settles? What changes after six additions of
`9.99` and the next quote settles?

:::details Answer

Rendered with `HtmlRenderer`, with the additions made on the renderer's dispatcher:

```text
0 items, 0, shipping: calculating
0 items, 0, shipping 4.99
6 items, 59.94, shipping: calculating
6 items, 59.94, shipping 0
```

The total updates while the new quote is pending. When that quote settles, the component renders
again with free shipping.

:::
::::

## Where each piece runs

### Event handlers

Handlers run on the circuit's context. `cart.Add` enters the graph, writes the signal and runs the
effects triggered by the write before leaving.

### Async completion

The shipping quote completes on a pool thread. The graph queues its result, then posts a drain
to the circuit's context. Applying the result there triggers the effect that requests rendering.

### Rendering

The renderer reads `summary.Value` and `quote.TryValue`. A stale memo refreshes itself inside the
graph. `TryValue` lets the component display ready, pending and failed shipping states.

:::details Rendering during a flush
A render that starts inside an effect's flush reads mid-flush values. Those values are glitch-free.
:::

### Disposal

The renderer disposes the component on the circuit's context, which disposes the component's root
scope. The circuit service uses `graph.Dispatch` for the graph's own disposal, because the
container may dispose services off that context.

## What raises

:::warning Work outside the circuit's context
Code inside `Task.Run` or after `ConfigureAwait(false)` may run without the renderer's context.
A write, stale read or node creation there raises. Hand the work to `graph.Dispatch` or the
component's `InvokeAsync`.
:::

:::warning Concurrent graph entry
`Serialised` rejects a second thread entering while another thread is inside the graph. The
rejected entry leaves the graph unchanged.
:::

:::details Recognise the error messages

- Outside the captured context: `ran on thread N outside the synchronisation context this Serialised graph was constructed on`.
- Concurrent entry: `ran on thread N while thread M was inside this Serialised graph`.

[Contracts](../concepts/contracts.md#serialised-hosts) lists the rules.
[aspnetcore#69323](https://github.com/dotnet/aspnetcore/issues/69323) describes one way two threads
can run on a circuit's context at once.
:::

## `Dispatch` and C# bindings

Under `Serialised`, `Dispatch` runs inline only on the thread already inside the graph. An event
handler runs on the circuit's context but outside the graph, so its dispatched work queues for
the next drain.

:::tip Need the new value immediately?
Write a signal directly from the event handler. A C# `BoundSignal` setter uses `Dispatch`, so a
read immediately after setting it still returns the previous value until the queue drains.
:::

::::details Test your understanding

An event handler sets a `BoundSignal`, then immediately reads it. Which value does the read return?
Would `Mvu.Dispatch` or `ReactiveCommand.Execute` apply their work immediately from that handler?

:::details Answer

The read returns the previous value. All three queue their work for the next drain when called
from an event handler outside the graph.

See [Migrating from Elmish](elmish.md#dispatch-and-threads) for `Mvu.Dispatch` and
[C# commands](csharp.md#commands) for `ReactiveCommand.Execute`.
:::
::::
