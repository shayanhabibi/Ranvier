---
title: Signal maps
order: 8
description: Live, animated maps of the graph behind a docs example.
---

:::info
Preview — signal maps follow the trace log, which may change before the first release.
:::

A signal map shows the graph behind an example. Writes flash, dependency marks travel along
edges, computations light up, and async flights start, settle, fail or drop.

Maps run the real engine compiled to JavaScript with tracing enabled. Their animations follow
the [trace log](tracing.md).

Maps appear throughout these docs beneath the examples they draw. This page explains how to read one
and how to write one.

## Reading a map

Press **Add tea**, then **Settle quote**. A cart's subtotal feeds a shipping quote, which the buttons
answer by hand.

```fsharp map
let desk = Desk<int>()
let lines = createSignal [ "tea", 4, 1 ]

let subtotal =
    createMemo (fun _ ->
        lines.Value |> List.sumBy (fun (_, price, qty) -> price * qty))

let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
let total = createMemo (fun _ -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    button "Add tea" (fun () -> lines.Value <- lines.Value @ [ "tea", 4, 1 ])
    button "Settle quote" (fun () -> desk.Settle 5)
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

`timeline` adds **Play**, **Step** and a scrub bar. Each tick represents an event; the bar starts
after setup, and the log initially shows setup's latest events.

Try **Two quick writes** below. The first flight is superseded: its ring fades and the log shows
`drop shipping (superseded)`. **Settle quote** answers the newest flight.

```fsharp map timeline
let desk = Desk<int>()
let qty = createSignal 1
let subtotal = createMemo (fun _ -> 4 * qty.Value)
let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
createEffect (fun () -> printfn $"shipping {shipping.Value}")

controls [
    button "Two quick writes" (fun () ->
        qty.Value <- qty.Value + 1
        qty.Value <- qty.Value + 1)
    button "Settle quote" (fun () -> desk.Settle 5)
]
```

Inputs write as you change them: the slider on every movement, the toggle on each flip.

```fsharp map timeline
let qty = createSignal 1
let gift = createSignal false
let subtotal = createMemo (fun _ -> 4 * qty.Value)
let total = createMemo (fun _ -> subtotal.Value + (if gift.Value then 2 else 0))
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    slider "Qty" (1, 10) 1 [ 3 ] (fun v -> qty.Value <- v)
    toggle "Gift wrap" false [ true ] (fun on -> gift.Value <- on)
]
```

`replay` records the scenario and each control in order: each button once, and each input once
per replay value. The map opens at the end of setup. Press **Play** or drag the scrub bar to watch
the recorded actions.

```fsharp map replay
let desk = Desk<int>()
let lines = createSignal [ "tea", 4, 1 ]

let subtotal =
    createMemo (fun _ ->
        lines.Value |> List.sumBy (fun (_, price, qty) -> price * qty))

let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
let total = createMemo (fun _ -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    button "Add tea" (fun () -> lines.Value <- lines.Value @ [ "tea", 4, 1 ])
    button "Settle quote" (fun () -> desk.Settle 5)
]
```

### Replay queued flights

Under `policy=queue`, flights apply in start order. Setup and two writes start three quotes.
Watch the newest answer wait until the older two are answered; then all three apply in order.

```fsharp map replay policy=queue
let desk = Desk<int>(queued = true)
let qty = createSignal 1
let subtotal = createMemo (fun _ -> 4 * qty.Value)
let shipping = createAsync (fun _ _ -> desk.Quote subtotal.Value)
createEffect (fun () -> printfn $"shipping {shipping.Value}")

controls [
    slider "Qty" (1, 10) 1 [ 2; 3 ] (fun v -> qty.Value <- v)
    button "Answer the newest" (fun () -> desk.SettleNewest 12)
    button "Answer the older two" (fun () ->
        desk.Settle 4
        desk.Settle 8)
]
```

## Writing a map

A `map` fence holds plain Ranvier code and ends with `controls`. The page shows the code as written.
The compiled copy labels each top-level `let x = create…` binding with its name and runs the code
against a fresh traced graph.

:::details Map fence options

| Flag | Effect |
| --- | --- |
| `timeline` | Adds the play, step and scrub bar. |
| `replay` | Runs every control in order, for the timeline to play back; implies `timeline`. |
| `policy=` | The graph's flight policy: `cancel-previous` (default), `keep-latest`, `queue` or `finish-current`. |
| `groups=` | How collections draw: `expand` (default), a box with a row per key, or `collapse`, one node. |
| `id=`, `show=` | As on `solid` fences. |

:::

### Controls

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

### Explain and check a replay

Use `describe` to give an action a caption. It stays visible while that action's events play,
and scrubbing backwards restores the caption for the earlier action. This lets an output-only
map explain what to watch without displaying its source.

Use `expect` to check the state after an action. `dotnet fsi build.fsx -- docs` runs maps containing
expectations under Fable and Node.js; a failed check stops the build and reports the page, fence line, control
and your message. Replays also report failures in the map itself.

```fsharp map replay show=output
let count = createSignal 1
let doubled = createMemo (fun _ -> count.Value * 2)
createEffect (fun () -> doubled.Value |> ignore)

controls [
    button "Write 5" (fun () -> count.Value <- 5)
    |> describe "The write refreshes doubled from 2 to 10."
    |> expect "doubled settles at 10" (fun () -> doubled.Peek = 10)
]
```

Author the action like this:

```fsharp
button "Write 5" (fun () -> count.Value <- 5)
|> describe "The write refreshes doubled from 2 to 10."
|> expect "doubled settles at 10" (fun () -> doubled.Peek = 10)
```

An input's caption and checks apply to every replay value. Chain `expect` calls to check several
properties; they run in the order written. Use `Peek`, counters or captured output so a check
does not force a lazy memo and change the behaviour you are demonstrating.

Checks run after one turn of queued continuations, not after an arbitrary remote request.
Use a `Desk` settlement or `AsyncSource.Settle` action to control async examples. Captions and
expectations apply to replay actions; live controls keep their normal interactive behaviour.

:::warning End the fence with controls
A map fence must compile to JavaScript through Fable and end with `controls`. A missing
`controls` is reported at the fence's last line.
:::

## Collections

A projection appears as a box with its node on top and one row per key below. An edge from a row
means the reader reads that key; an edge from the projection means it reads `Keys`.

Move **Tea**, then add an egg. The tea row updates for its new quantity, and a row is added for
the egg. The item computation runs inside the projection node and appears as `rows[tea] item`
in the log.

```fsharp map timeline
let lines = createSignal [ "tea", 1; "jam", 2 ]
let rows = createProjection fst (fun (_, qty) -> 4 * qty) (fun () -> lines.Value)
let total = createMemo (fun _ -> rows.Keys |> Array.sumBy rows.Get)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    slider "Tea" (1, 5) 1 [ 3 ] (fun qty ->
        lines.Value <- lines.Value |> List.map (fun (sku, q) -> sku, (if sku = "tea" then qty else q)))
    toggle "Egg" false [ true ] (fun on ->
        lines.Value <-
            if on then lines.Value @ [ "egg", 1 ]
            else lines.Value |> List.filter (fun (sku, _) -> sku <> "egg"))
]
```

### Per-row async values

A node created by a `createProjectionWith` factory belongs to its key and sits to the left of
that row. Each row below requests its own price. Settle the quotes one at a time to watch the
rows clear; the projection remains pending while any row is pending.

```fsharp map timeline
let desk = Desk<int>(queued = true)
let lines = createSignal [ "tea"; "jam" ]
let prices =
    createProjectionWith id (fun sku ->
        let quote = Trace.named "quote" (fun () -> createAsync (fun _ _ -> desk.Quote (sku ())))
        fun () -> quote.Value) (fun () -> lines.Value)
createEffect (fun () -> printfn "tea %d" (prices.Get "tea"))
createEffect (fun () -> printfn "jam %d" (prices.Get "jam"))

controls [
    button "Settle the oldest" (fun () -> desk.Settle 4)
    button "Fail the oldest" (fun () -> desk.Fail "no stock")
]
```

### Lookups and selection

A lookup is drawn the same way: a row per key read, beneath the memo of its state. Moving the selection marks only the
rows of the two keys whose answer changed; the third row stays quiet.

```fsharp map timeline
let selected = createSignal 1
let isSelected = createSelector (fun () -> selected.Value)
let first = createMemo (fun _ -> isSelected.Get 1)
let second = createMemo (fun _ -> isSelected.Get 2)
let third = createMemo (fun _ -> isSelected.Get 3)
createEffect (fun () -> printfn $"{first.Value} {second.Value} {third.Value}")

controls [
    slider "Selected" (1, 3) 1 [ 2; 3 ] (fun v -> selected.Value <- v)
]
```

:::details Give a lookup its name on the map

A `map` fence wraps `createLookup`, `createSelector`, `createEditable` and `createDraft` bindings
in `Trace.named`. Both single-line and multiline bindings receive their variable's name.

:::

### Collapse a collection

`groups=collapse` draws each collection as one node containing its rows and their nodes.
Here is the first collection map with its rows collapsed:

```fsharp map timeline groups=collapse
let lines = createSignal [ "tea", 1; "jam", 2 ]
let rows = createProjection fst (fun (_, qty) -> 4 * qty) (fun () -> lines.Value)
let total = createMemo (fun _ -> rows.Keys |> Array.sumBy rows.Get)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    slider "Tea" (1, 5) 1 [ 3 ] (fun qty ->
        lines.Value <- lines.Value |> List.map (fun (sku, q) -> sku, (if sku = "tea" then qty else q)))
]
```

## A bespoke map

:::details Build a SignalMap component directly

`SignalMap` is an ordinary component. A `solid` fence can call it with any scenario: here the names
come from `Trace.named` rather than from the `map` fence's labels. `SignalMap` takes the scenario, the graph's flight
policy, the code bindings, whether to show the timeline and how to draw collections.

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

    let Thermometer () = SignalMap (Live scenario) FlightPolicy.CancelPrevious [||] false Grouping.Expand
```

:::

## Edit a map

This map runs in your browser: change the code and press **Run** to compile it again and redraw the
graph. It is the example from the [home page](../index.md#watch-the-graph-think). Try adding a memo
or a button.

```fsharp live
open Ranvier
open Ranvier.Docs.Maps
open Ranvier.Docs.Maps.SignalMapComponent

let scenario (graph: Graph) =
    use _ = graph.Activate ()
    let price = Trace.named "price" (fun () -> createAsyncSource<int> ())
    let total = Trace.named "total" (fun () -> createMemo (fun _ -> price.Value * 3))

    let view =
        Trace.named "view" (fun () ->
            createBoundary
                (fun _ -> "Loading…")
                (fun ex _ -> "Unavailable: " + ex.Message)
                (fun () -> sprintf "Total %d" total.Value))

    createEffect (fun () -> printfn "%s" view.Value)

    let offline = exn "feed offline"

    controls [
        button "Settle 4" (fun () -> price.Settle 4)
        button "Fail" (fun () -> price.Fail offline)
        button "Settle 5" (fun () -> price.Settle 5)
    ]

let map = SignalMap (Live scenario) FlightPolicy.CancelPrevious [||] true Grouping.Expand
Browser.Dom.document.body.appendChild (unbox map) |> ignore
```

An editable map spells out what a `map` fence adds when the site is built: `Trace.named` names each node, and
the last two lines build the map and put it on the page. The compiler loads the first time you press **Run**,
which takes a few seconds.

## Limits

Keep maps small: beyond about ten nodes, edges cross and text shrinks.

:::details Layout, values and tracing limits

- **Small graphs.** Nodes are layered by longest path, with no crossing minimisation. Beyond about ten
  nodes, edges cross and text shrinks.
- **Initial values are not drawn.** The log records a node's value when it moves, not when it is
  created. A signal shows its value from its first write.
- **Boundaries and projections share the memo mark.** A boundary's fallback or recovered state shows
  as its value. Projection internals such as row watches are hidden.
- **JavaScript equality.** Maps run under Fable, where `decimal`, `DateTime` and structs compare by
  reference. A memo returning one moves on every run, so its readers always run again. See
  [Fable](../fable/index.md).
- **The dependency graph, not the owner tree.** A disposed node leaves the map.
- **Values are text.** A value longer than 16 characters is cut short; the hover label has it whole.
  An error shows its message without its exception type.
- **Last run only.** A click explains the most recent run; `Trace.history` and `Trace.whyNot` are
  not in the map.

:::

## Next

- [Tracing](tracing.md): the log a map draws, and the queries that answer why a node ran.
- [Async and pending](async-and-pending.md): flights, boundaries and the pending channel, each with
  its map.
