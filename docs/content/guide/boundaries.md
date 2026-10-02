---
title: Boundaries
---

Examples using tasks import the .NET types and construct a graph with a manual dispatcher:

```fsharp
open System
open System.Threading
open System.Threading.Tasks
open Ranvier

let newGraph () =
    new Graph ({ GraphOptions.Default with Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher) })
```

When a completion arrives from another thread, pump the graph on its owning thread. See
[Threading and dispatch](threading.md) and the `pumpUntil` helper in [Testing async state](testing.md).

A boundary supplies a value when its body is pending or fails. Its readers see that fallback or
recovered value instead of the state it handles. Choose the value to suit your UI or computation.

The body always re-runs from the start. The suspended read linked its edge before throwing, so the
source settling wakes the boundary.

| Constructor | Pending body | Failed body |
|-------------|--------------|-------------|
| `createSuspense fallback body` | shows `fallback ()` | propagates as `Failed` |
| `createErrorBoundary recover body` | propagates as `Pending` | shows `recover ex` |
| `createBoundary fallback recover body` | shows `fallback ()` | shows `recover ex` |

:::details Which failures a boundary catches

A boundary catches what its body reads, directly or through memos. A node the body creates and does
not read is outside its reach: an effect created in the body that suspends or fails leaves the
boundary showing the body's value.

:::

:::warning Create async values outside the boundary

A boundary owns the nodes its body creates and replaces them on every re-run. An async value created
and read in the body restarts its flight on every settle, and the boundary shows the fallback
forever. Create the async value outside the boundary and read it in the body; see
[Troubleshooting](troubleshooting.md#a-boundary-shows-its-fallback-forever-and-starts-a-flight-on-every-settle).

:::

All three return a `Boundary<'T>`. `IsWaiting` is `true` while a fallback stands in for the body, and
`Caught` holds the exception a `recover` handled on the current run, or `null`. Both are tracked reads that bring the
boundary current, so an effect reading only `IsWaiting` wakes when the body settles.

### Stale while refreshing, and empty versus not yet known

A fallback receives the boundary's last value, `ValueNone` before the first. A fallback that returns it
keeps the last result on screen while a new flight is in progress, and `IsWaiting` reports the refresh.
Choose a value type that separates a result not known yet from a result that is empty:

::::details Test your understanding

What distinguishes an unknown result from an empty result? Which value remains visible during a refresh?

```fsharp
type Results =
    | NotYetKnown
    | Loaded of string list

let searchGraph = newGraph ()
let query = searchGraph.Run (fun () -> createSignal "a")
let replies = ResizeArray<TaskCompletionSource<string list>> ()

let hits =
    searchGraph.Run (fun () ->
        createAsync (fun _ _ ->
            query.Value |> ignore // tracked: read before the first await
            let reply = TaskCompletionSource<string list> ()
            replies.Add reply
            reply.Task))

let results =
    searchGraph.Run (fun () ->
        createSuspense (fun last -> ValueOption.defaultValue NotYetKnown last) (fun () -> Loaded hits.Value))

let snapshot () = results.Value, results.IsWaiting

let unknown = snapshot ()
replies[0].SetResult []
let empty = snapshot ()
query.Value <- "ab"
let refreshing = snapshot ()
replies[1].SetResult [ "abc" ]
unknown, empty, refreshing, snapshot ()
```

:::details Answer

```text
((NotYetKnown, true), (Loaded [], false), (Loaded [], true), (Loaded ["abc"], false))
```

:::
::::

:::details Other ways to read a previous result

The three states of Uno MVUX's `Option<T>` map to `NotYetKnown`, `Loaded []` and `Loaded items`, and its
progress axis maps to `IsWaiting`. `createBoundary`'s `recover` receives the last value too, so a failed
refresh can keep the stale list beside the error in `Caught`. Outside a boundary, `AsyncMemo.Peek` reads
the last settled value, or the default before the first, untracked and without starting a flight.

:::

### createSuspense

The boundary shows the fallback while the body is pending, and shows the body's value once every
source the body waited on has settled. A failure passes through as `Failed`.

::::details Test your understanding

While data is pending, is the boundary itself pending? What value does it publish?

```fsharp
let viewGraph = newGraph ()
let data = viewGraph.Run (fun () -> createAsyncSource<string> ())

let view =
    viewGraph.Run (fun () -> createSuspense (fun () -> "loading") (fun () -> "loaded " + data.Value))

let waiting = view.TryValue, view.IsWaiting, view.Status
data.Settle "report"
waiting, (view.TryValue, view.IsWaiting)
```

:::details Answer

```text
((Ready "loading", true, None), (Ready "loaded report", false))
```

:::
::::

In the map, the effect reads `view` and runs with the fallback while `data` is pending. **Settle** runs the body again with the value; **Fail** passes the failure through the boundary to the effect.

```fsharp map replay show=output
let data = createAsyncSource<string> ()
let view = createSuspense (fun _ -> "loading") (fun () -> "loaded " + data.Value)
createEffect (fun () -> printfn "%s" view.Value)
let offline = exn "offline"

controls [
    button "Settle" (fun () -> data.Settle "report")
    button "Fail" (fun () -> data.Fail offline)
]
```

:::details Pending or failed fallback computations

A fallback may read reactive values itself. A fallback that suspends leaves the boundary `Pending`,
and a fallback that throws leaves it `Failed`.

:::

### createErrorBoundary

The boundary shows `recover ex` when the body throws, and shows the body's value again when a source
the body read changes and the re-run succeeds. A pending body passes through as `Pending`.

Enter an invalid integer, then a valid one. The boundary displays its recovery value and
later returns to the parsed value without rebuilding the graph.

```fsharp map replay show=output
let input = createSignal "42"
let parsed = createMemo (fun _ -> int input.Value)
let view = createErrorBoundary (fun _ _ -> -1) (fun () -> parsed.Value)
createEffect (fun () -> printfn "parsed = %d" view.Value)

controls [
    textSignal "Input" input [ "forty-two"; "7" ]
    |> describe "Invalid input displays -1; entering 7 recovers without rebuilding the graph."
    |> expect "Invalid input displays -1; entering 7 recovers without rebuilding the graph." (fun () -> view.Peek = (if input.Peek = "7" then 7 else -1))
]
```

::::details Test your understanding

Which value replaces the invalid input? What happens to Caught when the input becomes valid?

```fsharp
let parseGraph = newGraph ()
let input = parseGraph.Run (fun () -> createSignal "42")

let parsed =
    parseGraph.Run (fun () -> createErrorBoundary (fun _ -> -1) (fun () -> int input.Value))

input.Value <- "forty-two"
let recovered = parsed.TryValue, parsed.Caught.GetType().Name
input.Value <- "7"
recovered, (parsed.TryValue, isNull parsed.Caught)
```

:::details Answer

```text
((Ready -1, "FormatException"), (Ready 7, true))
```

:::
::::

:::details Handle selected error types

A `recover` that throws leaves the boundary `Failed` with the exception `recover` threw, so
re-raising narrows the boundary to the errors it handles.

:::

### createBoundary

The boundary catches both channels: `fallback ()` while the body is pending, `recover ex` when it
throws, and the body's value once it succeeds.

::::details Test your understanding

What does the panel show while loading, after a failure, and after the source settles?

```fsharp
let bothGraph = newGraph ()
let feed = bothGraph.Run (fun () -> createAsyncSource<int> ())

let panel =
    bothGraph.Run (fun () -> createBoundary (fun () -> 0) (fun _ -> -1) (fun () -> feed.Value))

let loading = panel.TryValue
feed.Fail (exn "offline")
let broken = panel.TryValue
feed.Settle 9
loading, broken, panel.TryValue
```

:::details Answer

```text
(Ready 0, Ready -1, Ready 9)
```

:::
::::
