# Signal Maps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A ```` ```fsharp map ```` fence in the Ranvier docs renders the author's code beside a live, animated map of its traced graph, with a replayed variant and a demo page.

**Architecture:** Partas.Nacara.Plugins.Solid gains `project`, `property`, `targetFramework` and `transform` options. Ranvier's docs add a model project (`Helpers`, `MapModel`, `Layout`, `Replay`; compiled by .NET and Fable), an authoring project (`MapFence`, `ReplayRunner`; .NET only, used by the site at build time), and a Fable-only `SignalMap` Solid component. The site registers a `map` transform that rewrites each fence into a `SignalMap` call.

**Tech Stack:** F# (.NET 10), Fable 5.13, Partas.Solid 3 (local build), Solid 2 rc, animejs 4.5, Expecto 11 alpha, Nacara 2.

**Spec:** `docs/.ai/superpowers/specs/2026-09-28-signal-maps-design.md`

## Global Constraints

- Commit messages: short conventional-commit summaries, no body, no `Co-Authored-By` trailer.
- Shell commands are prefixed with `rtk`.
- Push only when the user asks.
- Comments follow `.claude/rules/comments.md`; no `//FOR-REVIEW` left in commits.
- Colours come from `--rv-*` tokens only.
- Traced Ranvier comes from the MSBuild property `RanvierTrace=true` (it gates `Compile Include`s, so a bare define is not enough). MSBuild reads it from the environment, as `fable/Ranvier.Counters` already relies on for `RanvierCounters`.
- `TraceModel.parseDump` is .NET-only; replayed fences embed an F# `TraceEvent[]` literal.
- Plugin work happens in `../Partas.Nacara.Plugins` on branch `feat/solid-transforms`; Ranvier work on branch `feat/signal-maps`.

## Review Focus

1. **Stale bundles after an engine edit.** Editing `SignalMap.fs` or Ranvier must rebuild the docs bundle. Pinned by the fingerprint test in Task 2.
2. **Map fences whose code does not end in `controls`.** Readers expect a diagnostic on the fence, not a broken page. Pinned by the rejection test in Task 7.
3. **Multi-line bindings.** The inserted `Trace.label` line must follow the whole binding, and diagnostics must still point at the author's line. Pinned in Task 7.
4. **Settling a superseded request.** "Settle quote" after two writes must settle the live flight. Pinned by the `Desk` test in Task 4.
5. **`Trace.named` labels folded one event at a time.** A bespoke demo using `Trace.named` must keep its names. Pinned in Task 5.

---

### Task 1: Probe traced Fable through a ProjectReference

Throwaway; nothing is committed.

- [ ] Create `%TEMP%/rvprobe/Probe.fsproj` (net10.0) with `<ProjectReference Include="C:/Users/shaya/RiderProjects/Ranvier/src/Ranvier/Ranvier.fsproj" />` and `Probe.fs`:
  ```fsharp
  module Probe
  open Ranvier
  let run () =
      let g = new Graph ()
      let s = g.Run (fun () -> createSignal 1)
      Trace.events g |> Array.length
  ```
- [ ] Run `RanvierTrace=true dotnet fable Probe.fsproj -c Release -o out` there.
- [ ] Expected: success, and `out` holds `TraceModel.js`. If it fails for the TFM, the plugin needs `targetFramework` (Task 2 adds it regardless).

### Task 2: Plugin `project`, `property`, `targetFramework`, fingerprint

**Files:** `src/Partas.Nacara.Plugins.Solid/{Types,Solid,Workspace,Compile,Watch}.fs`, `tests/Partas.Nacara.Plugins.Tests/SolidTests.fs`

**Produces:**
- `SolidExamplesOptions.Projects: string list`, `Properties: (string * string) list`, `TargetFramework: string` (default `"net9.0"`).
- `SolidExamples.project path`, `SolidExamples.property name value`, `SolidExamples.targetFramework value`.
- `SolidWorkspace.run` takes `environment: (string * string) list` after `timeout`.
- `SolidCompile.projectInputs (fsproj: string) : string list`: the fsproj text and every `Compile Include` file text, recursing through `ProjectReference`s.

- [ ] Tests: `projectFile` emits a `ProjectReference` per project and the chosen `TargetFramework`; `projectInputs` covers a referenced project's source (temp dir with two fsproj files); the fingerprint changes when a referenced source changes and when a property changes.
- [ ] Implement. `compile` resolves relative projects against the site root before `projectFile` and `fingerprint`. Both `run` and `SolidFableWatcher` set each property as an environment variable.
- [ ] `dotnet test` in the plugin repo passes. Commit `feat(solid): project references, build properties and target framework`.

### Task 3: Plugin `transform` fences

**Produces:**
```fsharp
type SolidTransformInput = { Code: string; Flags: string list; PageKey: string; CellId: string }
type SolidTransformed = { Code: string; Render: string; Spans: SolidLineSpan list }
[<RequireQualifiedAccess>]
type SolidTransformOutput = Compiled of SolidTransformed | Rejected of problems: (int * string) list
// SolidCellKind.Transformed of render: string * spans: SolidLineSpan list
// SolidExamplesOptions.Transforms: (string * (SolidTransformInput -> SolidTransformOutput)) list
// SolidExamples.transform token f
// SolidScan.scanWith transforms fenceToken showJsx pageKey body; scan = scanWith []
```
Transform spans count lines from 1 within the transform's code; `Body` counts from 1 within the fence's code; `Indent = Int32.MaxValue` pins a line to the fence line.

- [ ] Tests: a ```` ```fsharp map timeline ```` fence becomes a `Transformed` cell with `Flags = ["timeline"]`; the body wraps the plain fence and placeholder in `<div class="partas-solid-card partas-solid-card--map">`; `show=code` mounts nothing; a `Rejected` output reports its problems at body lines; `SolidGenerate.fsharp` maps a generated line of the transformed code back to its fence line and pins the `Cell_` wrapper.
- [ ] Implement in Scan and Generate; `jsxNames` returns the wrapper for `Transformed`.
- [ ] Tests pass. Commit `feat(solid): transform fences`.

### Task 4: Pack the plugin into Ranvier's feed

- [ ] `dotnet pack src/Partas.Nacara.Plugins.Solid -c Release -p:Version=0.1.0-local.<sha> -o ../Ranvier/docs/feed`, with `<sha>` the plugin's short HEAD.
- [ ] Remove the old `0.1.0-local.44d0693` nupkg, bump `docs/docs.fsproj`, update the commit named in `docs/nuget.config`.
- [ ] `dotnet build docs/docs.fsproj` passes. Commit (Ranvier) `build(docs): solid plugin with transforms`.

### Task 5: Model project: Helpers, MapModel, Layout, Replay

**Files:** `docs/maps/model/{Ranvier.Docs.MapModel.fsproj,Helpers.fs,MapModel.fs,Layout.fs,Replay.fs}`, `docs/maps/tests/{Ranvier.Docs.Maps.Tests.fsproj,ModelTests.fs,Main.fs}`; add both projects to `Ranvier.slnx`.

**Produces (namespace `Ranvier.Docs.Maps`):**
```fsharp
type Control = { Label: string; Run: unit -> unit }
val controls : (string * (unit -> unit)) list -> Control list
type Desk<'T> = new : unit -> Desk<'T>; member Quote : 'R -> Task<'T>; member Settle : 'T -> unit; member Fail : string -> unit; member Pending : int
type MapSource = Live of scenario: (Graph -> Control list) | Recorded of events: TraceEvent[]
type Cue = Quiet | Flash of int | Pulse of source: int * target: int | Surge of source: int * targets: int list
         | Ring of int | Rest of int | Flight of int | Drop of int | Settled of int | Failed of int | Waits of node: int * source: int
type Scene = { Snapshot: TraceSnapshot; Flights: Set<int>; Waiting: Map<int, int>; Errors: Map<int, string> }
type Frame = { Event: TraceEvent; Cue: Cue; Log: string; After: Scene }
module MapModel: start, name, visible, edges, frames : Scene -> TraceEvent[] -> Frame[], stateAt
module Layout: place : int list -> Map<int, int list> -> Map<int, int * int>   // node -> (layer, row)
module Replay: normalise : TraceEvent[] -> TraceEvent[]; literal : TraceEvent[] -> string
```
`Desk.Settle`/`Fail` act on the newest pending request and drop older ones.

- [ ] Tests (real traced graphs): a write pulses `subtotal` before `total` runs; two quick writes drop the first flight; settle after two writes settles the live flight; a failure lands in `Errors` with its message; the last frame's snapshot agrees with `TraceModel.snapshot` (paths, values, statuses), with `Trace.named` labels kept; `Layout` layers a chain, orders by barycentre, is deterministic; `Replay.normalise` folds to the same snapshot; `literal` escapes strings.
- [ ] Implement; tests pass under `dotnet test docs/maps/tests`. Commit `feat(docs): signal map model`.

### Task 6: SignalMap component and styles

**Files:** `docs/maps/Ranvier.Docs.Maps.fsproj` (Fable-only, `IsExcludedFromRootBuild`), `docs/maps/SignalMap.fs` (namespace `Partas.Solid.Ranvier.Maps`), `docs/theme/maps.css`.

`SignalMap` props: `source: MapSource`, `bindings: (string * int * int)[]`, `timeline: bool`. Stage: SVG with nodes placed by `Layout.place`, shape per kind, animated cues via animejs, paced ~180 ms with shrinking gaps, instant under reduced motion. Hover shows path, kind, value and runs; click puts `TraceModel.renderWhy` of the node's last run in the log. Controls row with reset; timeline (ticks for non-quiet frames, play/step/scrub) when `timeline`. Highlights the running binding's lines in the card's `pre`. An exception in the engine shows an error strip.

- [ ] Built as part of Task 8's docs build.

### Task 7: MapFence generator and ReplayRunner

**Files:** `docs/maps/authoring/{Ranvier.Docs.MapAuthoring.fsproj,MapFence.fs,ReplayRunner.fs}`, `docs/maps/tests/AuthoringTests.fs`.

**Produces:**
```fsharp
type MapFlags = { Timeline: bool; Replay: bool }   // MapFlags.parse : string list -> MapFlags
type MapSpan = { Generated: int; Length: int; Body: int; Indent: int }
type MapFenceOutput = { Code: string; Render: string; Spans: MapSpan list; Bindings: (string * int * int) list }
MapFence.scenario : cellId: string -> code: string -> Result<string * MapSpan list * (string * int * int) list, (int * string) list>
MapFence.generate : cellId: string -> flags: MapFlags -> recorded: string option -> code: string -> Result<MapFenceOutput, (int * string) list>
ReplayRunner.record : ReplaySettings -> scenarioModule: string -> moduleName: string -> Result<string, string>
```
- [ ] Tests: labels follow multi-line bindings; `createEffect` gets none; spans map author lines back; a fence without trailing `controls` is rejected at its last line; `replay` emits `MapSource.Recorded`.
- [ ] Implement; commit `feat(docs): map fence generator and replay runner`.

### Task 8: Site wiring and demo page

**Files:** `docs/Maps.fs`, `docs/Site.fs`, `docs/docs.fsproj`, `docs/content/design/signal-maps.md`.

- [ ] Site: `SolidExamples.project "maps/Ranvier.Docs.Maps.fsproj"`, `property "RanvierTrace" "true"`, `targetFramework "net10.0"`, `transform "map" Maps.transform`; layer `maps.css` after `landing`.
- [ ] Demo page: live cart, replayed cart with timeline, flight cancellation, bespoke `SignalMap` in a `solid` fence, authoring reference.
- [ ] `dotnet fsi build.fsx -- docs` builds; check the page in a browser (both themes, narrow width, reduced motion). Commit `docs: signal maps demo page`.
- [ ] Final gates: `dotnet build Ranvier.slnx`, `dotnet test Ranvier.slnx`, plugin tests.
