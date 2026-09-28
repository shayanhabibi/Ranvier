---
title: Signal maps
order: 8
description: Live, animated maps of the graph behind a docs example.
---

:::info
Preview — signal maps follow the trace log, which may change before the first release.
:::

A signal map draws the graph an example builds and animates each event the [trace log](tracing.md)
records: writes flash, marks travel along edges, runs light up, and flights start, settle, fail or
drop. The map runs the real engine, compiled to JavaScript with tracing on, so what it shows is what
the graph did.

Maps appear throughout these docs beneath the examples they draw. This page explains how to read one
and how to write one.

## Reading a map

Press **Add tea**, then **Settle quote**. A cart's subtotal feeds a shipping quote, which the buttons
answer by hand.

```fsharp map
let desk = Desk<decimal>()
let lines = createSignal [ "tea", 4m, 1 ]

let subtotal =
    createMemo (fun _ ->
        lines.Value |> List.sumBy (fun (_, price, qty) -> price * decimal qty))

let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
let total = createMemo (fun _ -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    button "Add tea" (fun () -> lines.Value <- lines.Value @ [ "tea", 4m, 1 ])
    button "Settle quote" (fun () -> desk.Settle 5m)
    button "Fail quote" (fun () -> desk.Fail "quote down")
]
```

Sources sit on the left and observers on the right; an edge runs from each node to every node that
read it. The value above a node is its latest value.

| Mark | Node |
| --- | --- |
| Solid circle | Signal |
| Dashed circle | Async source or async memo |
| Rounded rectangle | Memo, boundary or projection |
| Diamond | Effect |

| Look | State |
| --- | --- |
| Faded | Created and not yet run |
| Filled | Running |
| Spinning ring | A flight is in progress |
| Dotted outline, dimmed value | Waiting on a pending source |
| Red outline and value | Failed; the value is the error |

A dot travelling an edge is a mark: the source tells its observer it may be stale. A bright dot means
the source's value moved. A ring that fades is a dropped flight.

Hover or focus a node for its path, state, value and run count. Click it for the cause chain of its
last run, as `Trace.why` renders it. The log beneath the buttons lists the latest events.

## Timelines and replays

With `timeline`, the map records every event and adds play, step and a scrub bar; each tick on the
bar is an animated event. The bar starts where the scenario's setup ends; the log opens with the setup's
latest events. Two writes in quick succession start two flights here. The first is
superseded: its ring fades and the log reads `drop shipping (superseded)`. Only the newest flight can
settle.

```fsharp map timeline
let desk = Desk<decimal>()
let qty = createSignal 1
let subtotal = createMemo (fun _ -> 4m * decimal qty.Value)
let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
createEffect (fun () -> printfn $"shipping {shipping.Value}")

controls [
    button "Two quick writes" (fun () ->
        qty.Value <- qty.Value + 1
        qty.Value <- qty.Value + 1)
    button "Settle quote" (fun () -> desk.Settle 5m)
]
```

Inputs write as you change them: the slider on every movement, the toggle on each flip.

```fsharp map timeline
let qty = createSignal 1
let gift = createSignal false
let subtotal = createMemo (fun _ -> 4m * decimal qty.Value)
let total = createMemo (fun _ -> subtotal.Value + (if gift.Value then 2m else 0m))
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    slider "Qty" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)
    toggle "Gift wrap" false [ true ] (fun on -> gift.Value <- on)
]
```

With `replay`, the page runs the scenario, then every control in order: a button once, an input once per
replay value. The map opens with the graph as the scenario built it; press **Play** or drag the bar to watch
them run.

```fsharp map replay
let desk = Desk<decimal>()
let lines = createSignal [ "tea", 4m, 1 ]

let subtotal =
    createMemo (fun _ ->
        lines.Value |> List.sumBy (fun (_, price, qty) -> price * decimal qty))

let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
let total = createMemo (fun _ -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    button "Add tea" (fun () -> lines.Value <- lines.Value @ [ "tea", 4m, 1 ])
    button "Settle quote" (fun () -> desk.Settle 5m)
]
```

Under `policy=queue`, flights apply in the order they started. Setup and the two writes start three quotes. The
newest is answered first, and waits; answering the older two applies all three, in order.

```fsharp map replay policy=queue
let desk = Desk<decimal>(queued = true)
let qty = createSignal 1
let subtotal = createMemo (fun _ -> 4m * decimal qty.Value)
let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
createEffect (fun () -> printfn $"shipping {shipping.Value}")

controls [
    slider "Qty" (1, 10) 1 [ 2; 3 ] (fun v -> qty.Value <- v)
    button "Answer the newest" (fun () -> desk.SettleNewest 12m)
    button "Answer the older two" (fun () ->
        desk.Settle 4m
        desk.Settle 8m)
]
```

## Writing a map

A `map` fence holds plain Ranvier code and ends with `controls`. The page shows the code as written.
The compiled copy labels each top-level `let x = create…` binding with its name and runs the code
against a fresh traced graph.

| Flag | Effect |
| --- | --- |
| `timeline` | Adds the play, step and scrub bar. |
| `replay` | Runs every control in order, for the timeline to play back; implies `timeline`. |
| `policy=` | The graph's flight policy: `cancel-previous` (default), `keep-latest` or `queue`. |
| `id=`, `show=` | As on `solid` fences. |

The helpers in scope:

- `controls [ … ]` lists the map's controls, in order. Each action runs with the graph active, so `batch` and the
  other `Api` functions work inside it. A replay runs every control in this order.
- `button label action` runs `action` when pressed, and once in a replay.
- `slider label (min, max) start replay set` writes each integer it moves to. `number label start replay set` and
  `text label start replay set` write when the value is committed, with Enter or by leaving the field.
  `toggle label start replay set` writes on each flip. `start` sets the widget only: keep it equal to the signal's
  initial value. A replay writes each value in `replay`, in order, and logs `set <label> = <value>` before it.
- `Desk<'T>()` stands in for a remote service. `desk.Quote x` returns a request that stays pending; a newer request
  cancels it. `desk.Settle value` and `desk.Fail message` answer the pending request.
- `Desk<'T>(queued = true)` keeps every request, in order. `Settle` and `Fail` answer the oldest; `SettleNewest`
  and `FailNewest` the newest. `desk.Pending` counts the requests waiting.

A fence compiles with Fable, so its code must compile to JavaScript. A fence that does not end with
`controls` is reported at its last line.

## A bespoke map

`SignalMap` is an ordinary component. A `solid` fence can call it with any scenario: here the names
come from `Trace.named` rather than from the `map` fence's labels. `SignalMap` takes the scenario, the graph's flight
policy, the code bindings and whether to show the timeline.

```fsharp solid render=Thermo.Thermometer
module Thermo =
    open Ranvier
    open Ranvier.Docs.Maps

    let scenario (graph: Graph) =
        use _ = graph.Activate ()
        let celsius = Trace.named "celsius" (fun () -> createSignal 20.0)
        let fahrenheit = Trace.named "fahrenheit" (fun () -> createMemo (fun _ -> celsius.Value * 9.0 / 5.0 + 32.0))
        let warm = Trace.named "warm" (fun () -> createMemo (fun _ -> celsius.Value >= 25.0))
        createEffect (fun () -> printfn $"{fahrenheit.Value}°F, warm: {warm.Value}")

        controls [
            button "Warmer" (fun () -> celsius.Value <- celsius.Value + 5.0)
            button "Cooler" (fun () -> celsius.Value <- celsius.Value - 5.0)
        ]

    let Thermometer () = SignalMap (Live scenario) FlightPolicy.CancelPrevious [||] false
```

## Limits

- **Small graphs.** Nodes are layered by longest path, with no crossing minimisation. Beyond about ten
  nodes, edges cross and text shrinks.
- **Initial values are not drawn.** The log records a node's value when it moves, not when it is
  created. A signal shows its value from its first write.
- **Boundaries and projections share the memo mark.** A boundary's fallback or recovered state shows
  as its value. Projection internals such as row watches are hidden.
- **The dependency graph, not the owner tree.** A disposed node leaves the map.
- **Values are text.** A value longer than 16 characters is cut short; the hover label has it whole.
  An error shows its message without its exception type.
- **Last run only.** A click explains the most recent run; `Trace.history` and `Trace.whyNot` are
  not in the map.

## Next

- [Tracing](tracing.md): the log a map draws, and the queries that answer why a node ran.
- [Async and pending](async-and-pending.md): flights, boundaries and the pending channel, each with
  its map.
