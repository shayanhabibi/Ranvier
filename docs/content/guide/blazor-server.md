---
title: Blazor Server
order: 12
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

A Blazor Server circuit runs its work one item at a time on the renderer's synchronisation context, and
each item may run on a different pool thread. A graph for a circuit is built with
`ThreadAffinity.Serialised`. It accepts an entry from any thread on the context captured at construction,
and raises when a second thread enters while one is inside. [Contracts](../concepts/contracts.md#serialised-hosts)
lists the rules.

Blazor WebAssembly runs on one thread and works under the default `Guarded` affinity.

## One graph per circuit

Register the graph as a scoped service, so each circuit gets its own. The container constructs it the first
time a component asks for it, during that component's activation on the renderer's context, and the
graph's default dispatcher posts to that context.

A component creates its nodes in a root scope and disposes the scope with itself. An effect asks for a
render once per settled change. Rendering reads the memos directly.

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

Call `addCart builder.Services` at startup. `graph.Run` activates the graph around `createRoot`, which the
`create*` functions need; without it they raise `No ambient graph on this thread`.

Rendered with `HtmlRenderer` at start, after the first quote settles, after six `cart.Add` calls on the
renderer's dispatcher, and after the second quote settles, the component shows:

```text
0 items, 0, shipping: calculating
0 items, 0, shipping 4.99
6 items, 59.94, shipping: calculating
6 items, 59.94, shipping 0
```

## Where each piece runs

- **Event handlers** run on the circuit's context. `cart.Add` enters the graph, writes the signal, runs the
  effects it woke and leaves.
- **The shipping quote** completes on a pool thread. Its settle is queued, and the graph posts a drain to the
  circuit's context, where it runs serialised with rendering. The effect then requests a render.
- **Rendering** reads `summary.Value` and `quote.TryValue`. A stale memo brings itself current inside the
  graph; a render that starts inside the effect's flush reads mid-flush values, which are glitch-free.
- **Disposal.** The renderer disposes the component on the circuit's context, and the root scope's disposal
  enters the graph. `CircuitGraph` hands the graph's own disposal to `Dispatch`, which runs it on the context.

## What raises

- **An entry off the circuit's context.** Code after `ConfigureAwait(false)` or inside `Task.Run` runs
  without the context. A write, stale read or node creation there raises
  `ran on thread N outside the synchronisation context this Serialised graph was constructed on`. Hand the
  work to `graph.Dispatch` or the component's `InvokeAsync`.
- **Two threads at once.** An entry while another thread is inside the graph raises
  `ran on thread N while thread M was inside this Serialised graph`, and the graph is left unchanged.
  [aspnetcore#69323](https://github.com/dotnet/aspnetcore/issues/69323) describes one way two threads
  can run on a circuit's context at once.

## `Dispatch` and C# bindings

Under `Serialised`, `Dispatch` runs work inline only on the thread inside the graph. From an event handler,
which runs outside the graph, it queues the work for the next drain. A C# `BoundSignal` setter goes through
`Dispatch`, so a read right after the set returns the previous value until that drain. Write signals
directly from event handlers when the new value must be visible at once.
