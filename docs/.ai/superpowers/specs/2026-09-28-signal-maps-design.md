# Signal maps: live, animated graph maps for docs examples

**Status.** Design approved in conversation on 2026-09-28. Layout chosen from mockups in the brainstorming
companion: layout A (split card) by default, with layout B's timeline scrubber behind a flag.

## 1. Goal

A docs example written as ordinary Ranvier code renders beside a live map of its graph. Readers press the
example's controls and watch the write travel through the graph: nodes run, values change, flights start, settle
or drop. The styling matches the Partas.Solid example cards (`p-demo`) in Ranvier's brand tokens.

Success:

1. An author writes a plain F# fence with the `map` token and gets a working map, with no Solid code in the fence.
2. The same example with `replay` plays back a build-time recording, for scenarios the browser cannot run.
3. A demo page at `docs/content/design/signal-maps.md` shows the design for review.

## 2. Decisions

| Question | Decision |
| --- | --- |
| What drives the maps | Live Ranvier compiled by Fable with `RANVIER_TRACE`; build-time recorded dumps as the fallback |
| Authoring | A `map` fence flavour as sugar over a `SignalMap` Solid component; `SignalMap` stays public for bespoke demos |
| Layout | A: code left, map right, controls and event log beneath. `timeline` adds B's scrubber |
| Getting Ranvier into the docs build | Extend Partas.Nacara.Plugins.Solid with project references and defines |
| Why a fence flavour, not a directive | The Solid plugin scans raw markdown for fences before the markdown pass, and directives render during it; a directive's output is never compiled |

## 3. Components

| Unit | Location | Role |
| --- | --- | --- |
| Plugin options | `../Partas.Nacara.Plugins/src/Partas.Nacara.Plugins.Solid` | `project`, `property`, `targetFramework`, `transform` (§4) |
| `MapModel` | `docs/maps/model/MapModel.fs` | Pure F#. Trace events and a starting snapshot to a graph and a frame list (§6) |
| `Layout` | `docs/maps/model/Layout.fs` | Pure F#. Deterministic layered placement (§6.4) |
| `Helpers` | `docs/maps/model/Helpers.fs` | `controls` and the `Desk<'T>` request desk, shared by the Fable build and replay scripts |
| `Replay` | `docs/maps/model/Replay.fs` | Trace events to an F# array literal |
| `SignalMap` | `docs/maps/SignalMap.fs` | Solid component: SVG stage, animejs player, controls, log, optional timeline |
| Map transform | `docs/maps/authoring` (`MapFence`, `ReplayRunner`), `docs/Maps.fs`, registered in `docs/Site.fs` | Rewrites a `map` fence into a `SignalMap` call; runs replay scripts |
| Engine tests | `docs/maps/tests` (Expecto) | `MapModel` and `Layout` under .NET |
| Demo page | `docs/content/design/signal-maps.md` | Out of the guide nav until approved |

`docs/maps/model/Ranvier.Docs.MapModel.fsproj` holds `Helpers`, `MapModel`, `Layout` and `Replay`, compiled by .NET
and Fable, and references `src/Ranvier/Ranvier.fsproj`. `docs/maps/Ranvier.Docs.Maps.fsproj` (Fable only) holds
`SignalMap` and references the model and Partas.Solid. The generated `Docs.fsproj` references it through
`SolidExamples.project`, and `SolidExamples.property "RanvierTrace" "true"` traces the Fable build of Ranvier.

## 4. Plugin changes (Partas.Nacara.Plugins.Solid)

- `SolidExamples.project (path: string)` adds a `ProjectReference` to the generated `Docs.fsproj`. A relative path
  resolves against the site root.
- `SolidExamples.property (name: string) (value: string)` sets an MSBuild property for every project Fable cracks,
  through the environment of the Fable process. `RanvierTrace` gates `Compile Include`s, so a define alone is not
  enough.
- `SolidExamples.targetFramework (tfm: string)` sets the generated project's target framework (default `net9.0`).
- The build fingerprint covers the sources of referenced projects and the properties.
- `SolidExamples.transform (token: string) (f: SolidTransformInput -> SolidTransformOutput)` registers a fence
  token. The scanner treats ```` ```fsharp <token> [flags] ```` as a solid cell: the page shows the author's code
  verbatim, and the compiled code is the transform's output.
  - `SolidTransformInput`: the code, the flags (bare words and `key=value` pairs from the info string), the page
    key and the cell id.
  - `SolidTransformOutput`: the generated code, the line spans mapping it back to the author's lines, and the
    cell's `Show` and `Kind`, or a list of diagnostics.
- Tests cover the generated `Docs.fsproj` for `project` and `define`, and the scan of a transformed fence,
  including diagnostic line mapping.
- The plugin nupkg is rebuilt into `docs/feed` and its version is bumped in `docs/docs.fsproj`.

## 5. Authoring

````
```fsharp map
let lines = createSignal [ { Sku = "tea"; Price = 4m; Qty = 1 } ]
let subtotal = createMemo (fun () -> lines.Value |> List.sumBy (fun l -> l.Price * decimal l.Qty))
let shipping = createAsync (fun _ -> quote subtotal.Value)
let total = createMemo (fun () -> subtotal.Value + shipping.Value)
createEffect (fun () -> printfn "total %M" total.Value)

controls [
    "Add tea", fun () -> lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ]
    "Settle quote", fun () -> settle 5m
    "Fail quote", fun () -> fail "quote down"
]
```
````

- The page shows the fence exactly as written; the scenario and its controls are on the page.
- The compiled copy follows each top-level `let x = create…` binding with `Trace.label (graph, x, "x")`.
  `createEffect` returns no node and keeps its kind as its name.
- The compiled copy is a `SignalMap` call whose scenario activates a graph and runs the body. `controls` is a
  function in `Helpers` that returns the control list.
- Helpers come from `Helpers`: `controls`, and `Desk<'T>`, whose `Quote` returns a pending task that `Settle` and
  `Fail` complete. They act on the newest pending request.
- A fence is self-contained: a replayed fence runs outside the page, so it declares no page-level types.
- Flags:

| Flag | Effect |
| --- | --- |
| `timeline` | Adds the scrubber (layout B) |
| `replay` | Plays a build-time recording (§7); implies `timeline` |
| `id=`, `show=` | As for solid fences |

**Amended:** controls are built with `button` and the inputs of [the map inputs design](2026-09-28-map-inputs-design.md), which also adds `policy=` and queued desks.

A bespoke demo calls `SignalMap` directly in a plain `solid` fence.

## 6. Engine

### 6.1 Sources

```fsharp
type MapSource =
    | Live of scenario: (Graph -> Control list)
    | Recorded of events: TraceEvent[]
```

- `Live`: the scenario runs once on mount. After mount and after each control action, the engine compares
  `Trace.events graph` with the last length it read, once per animation frame, and queues new events. Async
  settles arriving after the action are picked up the same way.
- `Recorded`: the events from graph creation, embedded as an F# literal (`TraceModel.parseDump` is .NET-only).
  Controls are replaced by the timeline's play, step and scrub.

The graph's nodes, names and kinds come from `TraceModel.snapshot`; edges come from `TraceModel.sources`. Both are
re-read when an event names a node absent from the current graph.

### 6.2 Frames

`MapModel.frames : Scene -> TraceEvent[] -> Frame[]` yields one frame per event. A `Scene` is the folded snapshot
plus flights in progress, suspensions and error text. Events outside the table are quiet frames, applied without
animation:

| Event | Frame |
| --- | --- |
| Write | Signal flashes contour, value badge swaps |
| Mark | Faint pulse along the edge to the observer, observer marked dirty |
| RunStart / RunEnd | Violet ring on the node; the node's binding line is highlighted in the code pane |
| Moved | Value badge updates; bright pulse travels downstream |
| FlightStart | Amber dashed ring spins |
| FlightDrop | Ring fades with a strike |
| Settle / Fail | Green with the value, or red with the error text |
| Suspend | Node dims amber; tooltip reads "waiting on <path>" |

A frame carries the node state it leaves behind, so `MapModel.stateAt` can rebuild the scene at any event for
scrubbing without replaying animations.

### 6.3 Player and interaction

- Frames play about 180 ms apart; the gap shrinks as the queue grows, so a burst stays near the click.
- Under `prefers-reduced-motion`, state changes apply instantly and the log still updates.
- Hover on a node shows its path, kind, value and run count. Click shows the rendered `Trace.why` in the log panel.
- The log shows the most recent rendered events.
- The timeline has one tick per animated frame, coloured by kind, with play, step and scrub.

### 6.4 Layout and visuals

- Nodes are layered by longest path from the sources, left to right; within a layer, nodes are ordered by the
  barycentre of their sources to reduce crossings. The same graph always yields the same placement.
- Shape carries kind: circle for a signal, rounded rectangle for a memo, dashed ring for an async node, diamond
  for an effect. Stroke colour repeats it.
- Colours come from the `--rv-*` tokens only, so both themes follow the site. The stage has a radial-gradient
  surface; labels and values are Geist Mono.
- Every state has a text form in the tooltip and the log.
- The card stacks the code above the map below 720 px.

## 7. Replay

- For a `replay` fence, the transform writes a script to the Solid workspace. The script references the traced
  Debug `src/Ranvier/bin/Debug/net10.0/Ranvier.dll`, `#load`s `Helpers.fs` and `Replay.fs`, activates a graph,
  runs the code, presses each control in order, and prints `Replay.literal (Trace.events graph)`.
- The transform runs it with `dotnet fsi` and embeds the output as `Recorded events`.
- Recordings are cached under `docs/.nacara/maps-replay`, keyed by a hash of the script, `Helpers.fs`, `Replay.fs`
  and the DLL.

**Amended:** replay now runs in the browser. A `replay` fence compiles like a live one and renders
`Replayed scenario`: the map runs the scenario, presses each control once, in order, one task apart, and plays the
frames. `ReplayRunner`, `Replay.fs` and the recording cache are gone.

**Setup baseline:** in every map, the frames recorded while the scenario builds the graph are setup. The first
paint draws the scene after setup in its final layout, the log lists the setup events, and the scrubber, ticks, Play,
Step and Reset cover only the frames after it. Setup frames stay in the history.

## 8. Errors

- Compile errors in a `map` fence point to the author's lines through the transform's line spans.
- An exception thrown inside the graph by a control is recorded by Ranvier and shows as a red node.
- An exception in the engine shows as an error strip inside the card; other cards and the page keep working.
- A failing replay script is a build diagnostic on the fence, carrying the `fsi` output.

## 9. Testing

- Expecto tests for `MapModel` over real traced scenarios under .NET: frame order for a write (the pulse to
  `subtotal` precedes the run of `total`), a flight drop on two quick writes, a failure, and `stateAt` agreeing
  with the frame fold.
- Expecto tests for `Layout`: layers, stable ordering, and determinism.
- Plugin tests as in §4.
- The demo page is built and checked in a browser before it is handed over for review.

## 10. Demo page

`docs/content/design/signal-maps.md`:

1. The cart as a live `map` fence.
2. The same cart with `replay` and the timeline.
3. Flight cancellation: two quick writes, the first flight dropped.
4. A bespoke `SignalMap` in a plain `solid` fence.
5. An authoring reference for the `map` fence flags.

## 11. Out of scope

- Editing example code in the browser.
- Adding maps to existing guide pages; that follows approval of the demo page.
