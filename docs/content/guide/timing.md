---
title: Debounce and throttle
---

Timed values capture stabilized source changes and control when downstream readers receive them.
All four modes publish their first ready value immediately. Later changes follow the selected rule:

- `debounce delay read graph`: publishes the latest changed value after a quiet period. Each changed capture extends the deadline.
- `throttleFirst interval read graph`: admits a leading change and discards changes during its cooldown. Uses a timestamp without a timer.
- `throttleLast interval read graph`: opens a fixed window on the first change and publishes the latest capture when it ends.
- `throttle interval read graph`: admits a leading change and the latest trailing change. Each trailing admission starts another cooldown.

For debounce with a 100 ms delay, changes at 0, 50 and 90 ms publish the last value at or after 190 ms.
For trailing throttle, those changes publish the last value at or after 100 ms.
Durations must be nonnegative. Zero duration uses normal graph propagation without creating a timer.

```fsharp
open System
open Ranvier

let graph = new Graph()
let query, admitted =
    graph.Run(fun () ->
        let query = createSignal ""
        let admitted = debounce (TimeSpan.FromMilliseconds 250.) (fun () -> query.Value) graph
        createEffect(fun () -> printfn "search: %s" admitted.Value)
        query, admitted)
```

:::warning Capture runs eagerly
Debounce and throttle delay publication, not evaluation of the capture callback. Put expensive work downstream of the timed value.
:::

The node tracks conditional dependencies on each capture, even while its publication is held.
An equal capture leaves the deadline unchanged; an equal admitted value does not notify readers.

`CancelPrevious` cancels superseded async flights. `KeepLatest` leaves them running but publishes
only the latest flight's result. Debounce and throttle govern admission before that work starts.
Compose them by reading the timed value before starting an async request:

```fsharp
let results =
    graph.Run(fun () ->
        createAsync(fun _ cancellation ->
            let term = admitted.Value
            search term cancellation))
```

Here `search` is the application's function returning a task. Choose the async memo's flight policy
separately; timing does not create artificial flights or cancel a request already admitted.

Pending capture cancels a held candidate and keeps the published state. Before any ready value,
the output becomes pending. Source or comparer failure publishes immediately and preserves its
error origin. Recovery follows the selected timing rule. Dispose the node or its owner to cancel
timing and detach dependencies. Reads after disposal retain the last published state.

Timer callbacks post a wake to the graph owner. A queued expiry respects batching and processes
new source changes before admission. Native callbacks may be late; throttle anchors its next
cooldown to actual admission time and avoids catch-up bursts. A console graph without a captured
synchronization context uses manual dispatch: call `graph.Pump()` on its owning thread.
See [Threading and dispatch](threading.md).

The `With` factories accept `TimedOptions<'T>` with an injected monotonic `TimedClock` and optional
typed comparer. `TimedClock.system` uses `Stopwatch` on .NET and `performance.now()` under Fable.
.NET 8 and 10 also expose `TimedClock.ofTimeProvider`. Custom timers start disarmed, replace a
one-shot wait on `Arm`, and never invoke callbacks synchronously from `Arm`. Callbacks can race cancellation;
the timed node checks its current deadline and disposal state.

```csharp
using var graph = new Graph();
graph.Run(() =>
{
    var input = Ranvier.CSharp.Reactive.Signal(0);
    var latest = Ranvier.CSharp.Reactive.Debounce(
        TimeSpan.FromMilliseconds(100), () => input.Value);
});
```

Debounce extends an armed deadline without rescheduling the backend on every input. Trailing
throttle arms once per window; leading throttle creates no timer. Warmed primitive captures
allocate zero managed bytes in the tested .NET path. Cross-thread wake/dispatch and construction
have separate costs.

## Watch admission on the signal map

These replays use a manual clock so each recorded control step has a deterministic result.
The value above a timed node is published output. Its captured input and timing window are separate;
a timing window does not draw an async flight ring. **Advance time** moves this example's clock,
not wall-clock time. Timeline **Speed** changes playback animation only.

### Debounce waits for quiet

Press **Play** or **Step** to follow the recorded controls: capture 1 at 0 ms, capture 2 at 50 ms, then reach 100 ms.
The output is still 0 because the second capture extended its deadline to 150 ms.

```fsharp map replay code=open code-max-height=24rem
let graph = Graph.Current
let clock = MapClock()
let input = createSignal 0
let admitted =
    debounceWith { Clock = clock; Comparer = None }
        (System.TimeSpan.FromMilliseconds 100.) (fun () -> input.Value) graph
createEffect (fun () -> printfn "admitted %d" admitted.Value)

controls [
    button "Capture 1" (fun () -> input.Value <- 1)
    |> expect "initial publication is held" (fun () -> admitted.Peek = 0)
    button "50 ms, capture 2" (fun () -> clock.Advance 50.; input.Value <- 2)
    |> expect "latest capture is held" (fun () -> admitted.Peek = 0)
    button "Advance time to 100 ms" (fun () -> clock.Advance 50.)
    |> expect "extended deadline has not expired" (fun () -> admitted.Peek = 0)
    button "Advance time to 150 ms" (fun () -> clock.Advance 50.)
    |> expect "latest capture publishes" (fun () -> admitted.Peek = 2)
]
```

### Compare the three throttle modes

Leading throttle publishes 1 and discards 2 during its cooldown. Trailing throttle holds its
initial value until the fixed deadline, then publishes 2. Combined throttle publishes 1 immediately
and 2 at the deadline; that trailing admission starts its next cooldown.

```fsharp map replay
let graph = Graph.Current
let clock = MapClock()
let input = createSignal 0
let options = { Clock = clock; Comparer = None }
let interval = System.TimeSpan.FromMilliseconds 100.
let first = throttleFirstWith options interval (fun () -> input.Value) graph
let last = throttleLastWith options interval (fun () -> input.Value) graph
let both = throttleWith options interval (fun () -> input.Value) graph
createEffect (fun () -> printfn "%d / %d / %d" first.Value last.Value both.Value)

controls [
    button "Capture 1" (fun () -> input.Value <- 1)
    |> expect "leading modes publish immediately" (fun () -> (first.Peek, last.Peek, both.Peek) = (1, 0, 1))
    button "50 ms, capture 2" (fun () -> clock.Advance 50.; input.Value <- 2)
    |> expect "trailing modes hold the candidate" (fun () -> (first.Peek, last.Peek, both.Peek) = (1, 0, 1))
    button "Advance time to 100 ms" (fun () -> clock.Advance 50.)
    |> expect "trailing modes publish the latest capture" (fun () -> (first.Peek, last.Peek, both.Peek) = (1, 2, 2))
    button "Capture 3 at 100 ms" (fun () -> input.Value <- 3)
    |> expect "combined mode starts a new cooldown" (fun () -> (first.Peek, last.Peek, both.Peek) = (3, 2, 2))
    button "Advance time to 200 ms" (fun () -> clock.Advance 100.)
    |> expect "next trailing admissions publish" (fun () -> (first.Peek, last.Peek, both.Peek) = (3, 3, 3))
]
```

See [Signal maps](signal-maps.md) for the map marks, controls and replay diagnostics.
