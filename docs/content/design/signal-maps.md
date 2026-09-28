---
title: Signal maps
description: Live, animated maps of the graph behind a docs example.
---

> **Design review.** This page demonstrates signal maps before they reach the guide. It is not in the navigation.

A signal map draws the graph an example builds and animates each event the graph records: writes flash, marks travel along edges, runs light up the binding that ran, and flights start, settle, fail or drop. The map runs the real engine, compiled to JavaScript with tracing on.

## A live cart

A cart's subtotal feeds a shipping quote, which the example answers by hand with the buttons. Press **Add tea**, then **Settle quote**. Hover a node for its state; click it for the cause chain of its last run.

```fsharp map
let desk = Desk<decimal>()
let lines = createSignal [ "tea", 4m, 1 ]

let subtotal =
    createMemo (fun () ->
        lines.Value |> List.sumBy (fun (_, price, qty) -> price * decimal qty))

let shipping = createAsync (fun _ -> desk.Quote subtotal.Value)
let total = createMemo (fun () -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    "Add tea", fun () -> lines.Value <- lines.Value @ [ "tea", 4m, 1 ]
    "Settle quote", fun () -> desk.Settle 5m
    "Fail quote", fun () -> desk.Fail "quote down"
]
```

## The same cart, replayed

With `replay`, the build runs the scenario under .NET, presses every button once in order, and embeds the recording. The timeline plays, steps and scrubs through it; each tick marks an animated event.

```fsharp map replay
let desk = Desk<decimal>()
let lines = createSignal [ "tea", 4m, 1 ]

let subtotal =
    createMemo (fun () ->
        lines.Value |> List.sumBy (fun (_, price, qty) -> price * decimal qty))

let shipping = createAsync (fun _ -> desk.Quote subtotal.Value)
let total = createMemo (fun () -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    "Add tea", fun () -> lines.Value <- lines.Value @ [ "tea", 4m, 1 ]
    "Settle quote", fun () -> desk.Settle 5m
]
```

## Flight cancellation

Two writes in quick succession start two flights. The first is superseded: when its request is cancelled, its ring fades and the log reads `drop shipping (superseded)`. Only the newest flight can settle.

```fsharp map timeline
let desk = Desk<decimal>()
let qty = createSignal 1
let subtotal = createMemo (fun () -> 4m * decimal qty.Value)
let shipping = createAsync (fun _ -> desk.Quote subtotal.Value)
createEffect (fun () -> printfn $"shipping {shipping.Value}")

controls [
    "Two quick writes", fun () ->
        qty.Value <- qty.Value + 1
        qty.Value <- qty.Value + 1
    "Settle quote", fun () -> desk.Settle 5m
]
```

## A bespoke map

`SignalMap` is an ordinary component. A `solid` fence can call it with any scenario: here the names come from `Trace.named` rather than from the `map` fence's labels.

```fsharp solid render=Thermo.Thermometer
module Thermo =
    open Ranvier
    open Ranvier.Docs.Maps

    let scenario (graph: Graph) =
        use _ = graph.Activate ()
        let celsius = Trace.named "celsius" (fun () -> createSignal 20.0)
        let fahrenheit = Trace.named "fahrenheit" (fun () -> createMemo (fun () -> celsius.Value * 9.0 / 5.0 + 32.0))
        let warm = Trace.named "warm" (fun () -> createMemo (fun () -> celsius.Value >= 25.0))
        createEffect (fun () -> printfn $"{fahrenheit.Value}°F, warm: {warm.Value}")

        controls [
            "Warmer", fun () -> celsius.Value <- celsius.Value + 5.0
            "Cooler", fun () -> celsius.Value <- celsius.Value - 5.0
        ]

    let Thermometer () = SignalMap (Live scenario) [||] false
```

## Authoring a map

A `map` fence holds plain Ranvier code and ends with `controls`. The fence shows exactly what is written; the compiled copy labels each top-level `let x = create…` binding with its name, and runs the code against a fresh traced graph.

| Flag | Effect |
| --- | --- |
| `timeline` | Adds the play, step and scrub bar. |
| `replay` | Plays a recording made when the site builds; implies `timeline`. |
| `id=`, `show=` | As on `solid` fences. |

The helpers in scope:

- `controls [ label, action; … ]` lists the map's buttons, in order.
- `Desk<'T>()` stands in for a remote service. `desk.Quote x` returns a request that stays pending; a newer request cancels it. `desk.Settle value` and `desk.Fail message` answer the pending request.

A replayed fence runs outside the page, so it declares no page-level types. A fence that does not end with `controls` is reported at its last line.
