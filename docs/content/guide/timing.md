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

Capture runs eagerly in a pure scope. Put expensive work downstream of `admitted.Value`.
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
