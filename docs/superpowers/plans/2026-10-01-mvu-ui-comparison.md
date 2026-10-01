# FuncUI MVU Workload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native execution, or superpowers:subagent-driven-development if selected. Implement task by task; steps below track verification.

**Goal:** Build and measure an equivalent Avalonia.FuncUI task dashboard using ordinary Elmish, cached Elmish and Ranvier.Elmish.

**Architecture:** A shared immutable model and deterministic message traces feed three disposable runners. Shared view functions produce the same dashboard; only state propagation and caching differ. A headless harness verifies rendered equivalence and measures completed dispatcher work, while a desktop entry point makes the workload interactive.

**Tech Stack:** F#, .NET 10, Avalonia 12.1.0, the sibling Avalonia.FuncUI checkout, Elmish 5.0.2, local Ranvier.Elmish with tracing disabled, Avalonia.Headless 12.1.0.

**Spec:** `docs/superpowers/specs/2026-10-01-mvu-ui-comparison-design.md`

## Global Constraints

- Dataset sizes: 100 and 1,000 tasks; page size: 50.
- Same model, update logic, controls, message ordering and final rendered output for all three runners.
- Release measurements; Ranvier tracing disabled; all UI mutation on the Avalonia dispatcher.
- No network, filesystem or sleeping inside measured intervals.
- Warmup and setup excluded; drain deferred UI work before ending a sample.
- Raw JSON, repeatable commands, environment/source identity and measurement limitations accompany the report.
- No Ranvier public API changes or publication.
- Use fslangmcp for F# semantics and SageFs for supported experiments; record Avalonia session failures and use CLI verification for those scenarios.

## Review Focus

- Search yielding no tasks: empty list and a consistent detail state.
- Selected task disappearing from the current page: details continue to represent the selected ID.
- Offscreen task updates: model and aggregate counts change correctly without corrupting visible rows.
- Repeat messages and invalid IDs: consistent no-op behavior and no stale callbacks.
- Repeated creation/disposal: independent windows and no retained timers, subscriptions or effects.

## File Map

Create `examples/Ranvier.FuncUI.Workload/Workload.fsproj`, `Model.fs`, `Views.fs`, `Runners.fs`, `Verification.fs`, `Measurement.fs`, `Program.fs` and `README.md`.

Create `examples/Ranvier.FuncUI.Workload/results/` for recorded JSON and a Markdown report. Create a results canvas in the managed workspace canvas directory after real measurements exist. Keep the example outside the shipping libraries and avoid changing their project files.

### Task 1: Deterministic model and message traces

**Files:** `Workload.fsproj`, `Model.fs`, `Verification.fs`.

**Interfaces:** Define `TaskItem` (ID, title, owner, status, progress), `Model` (task map, query, page, selected ID), and `Msg` (search, page, select, progress and status changes). Export `Model.init : int -> Model`, `Model.update : Msg -> Model -> Model`, `Model.visibleIds : Model -> int array`, `Model.summary : Model -> struct (int * int * int)` and `Workloads.trace : string -> int -> Msg array`.

- [ ] Create the non-packable project with explicit compile order and a configurable `FuncUIRoot` MSBuild property defaulting to the sibling checkout. Disable tracing explicitly for the example's project references.
- [ ] Write checks for search, bounds, missing IDs, stable IDs, selection and deterministic replay before implementing the corresponding logic. A missing-ID check has the form:

```fsharp
let initial = Model.init 100
let unchanged = Model.update (SetProgress(-1, 10)) initial
if not (obj.ReferenceEquals(initial, unchanged)) then failwith "missing ID must be a no-op"
```

- [ ] Implement immutable record/map updates, clamped progress and page indices, case-insensitive search, and seeded traces. Preserve unchanged task record references.
- [ ] Verify pure cases in a SageFs session where possible; run the complete example verification command through the CLI for acceptance.

### Task 2: Equivalent views and three runners

**Files:** `Views.fs`, `Runners.fs`, extend `Verification.fs`.

**Interfaces:** Export `Views.row : TaskItem -> (Msg -> unit) -> IView`, plus shared header, summary, paging and details views. Export `Runners.create : string -> Model -> bool -> Runner`, where `Runner` exposes `Host : HostControl`, `Dispatch : Msg -> unit`, `CurrentModel : unit -> Model`, render counters and `Dispose : unit -> unit`. The boolean enables diagnostic counters, which remain disabled during timing.

- [ ] Write rendered-equivalence checks for the initial model and all four traces, including blank search results, repeated updates, offscreen updates and selected-task details.
- [ ] Implement ordinary Elmish with its actual `Program.mkSimple` loop and conventional whole-view reconstruction.
- [ ] Implement cached Elmish with the same loop and explicit caches keyed by stable row ID and unchanged task references. Cache summary and detail sections using their actual inputs. Count both root/host work and row construction so caching does not hide virtual DOM traversal costs.
- [ ] Implement Ranvier with an Avalonia dispatcher, `Mvu.create`, section selectors and row selectors. Create/dispose visible row ownership when page membership changes; dispose the graph with the runner.
- [ ] Use one shared host/layout structure for cached Elmish and Ranvier. Read the actual FuncUI host and DSL APIs through fslangmcp; type-check before drawing semantic conclusions.
- [ ] Verify the rendered task IDs/text and summaries against the shared model after every message, rather than checking only that no exception occurred. Compare final models across variants.

### Task 3: Measurement harness

**Files:** `Measurement.fs`, extend `Verification.fs`.

**Interfaces:** Export `Measurement.run : string -> int -> int -> int -> Trial array`, taking workload, dataset size, trial count and messages per trial. Trial records include implementation, elapsed ticks, message count, allocated bytes and GC deltas. Export diagnostic render/selector counters separately, and environment metadata.

- [ ] Check that repeated trials begin from the same state and that every submitted message is applied before sampling ends.
- [ ] Warm each implementation, rotate implementation order by trial, and run background, selected-edit, search/page and mixed traces at both dataset sizes.
- [ ] Measure replay duration using `Stopwatch.GetTimestamp`, UI-thread allocation using `GC.GetAllocatedBytesForCurrentThread`, and process GC counts using `GC.CollectionCount`. Record the UI thread ID and reject a trial that changes execution thread.
- [ ] Drain Avalonia dispatcher work before recording the final timestamp and allocation count. Keep generation, setup, JSON writing and teardown outside the interval.
- [ ] Run an untimed instrumented replay for view construction, host update and selector counts. Include a pure-update-only baseline for each trace.
- [ ] Write all trials to JSON. Summarize median microseconds/message, dispersion, bytes/message and GC deltas without claiming desktop frame or input-to-screen latency.

### Task 4: Desktop example and executable checks

**Files:** `Program.fs`, `README.md`, extend `Verification.fs`.

**Interfaces:** Commands: `--verify`, `--measure --output <path>`, and `--desktop --variant <name> --items <count>`; valid variants are `elmish`, `elmish-cached` and `ranvier`.

- [ ] Wire headless initialization for verification/measurement and classic desktop lifetime for the interactive dashboard.
- [ ] Add a fake background feed whose deterministic messages use the UI dispatcher; stop it on close. Supply search, paging, status and progress interactions.
- [ ] Verify button callbacks, input synchronization, stable controls for unchanged rows, two independent runners and repeated disposal. Ensure creation/teardown does not grow live runner counts.
- [ ] Build Release, run `--verify`, and run the applicable unfiltered Ranvier test suites. Perform a desktop smoke launch and report any platform limitation explicitly.
- [ ] Document dependency paths and the exact reproduction commands, including the `FuncUIRoot` override and tracing setting.

### Task 5: Measured comparison and report

**Files:** raw results JSON, `results/report.md`, managed `.canvas.tsx` artifact.

- [ ] Run the full comparison with warmup and repeated trials. Preserve all raw samples and the exact command/environment/source metadata.
- [ ] Inspect variability and equivalence verdicts before reporting ratios. Repeat only cases with a concrete correctness or measurement concern.
- [ ] Write a report explaining ordinary versus cached Elmish, Ranvier's selector overhead, view construction/patch work, offscreen updates and workload-dependent wins/losses.
- [ ] Use the canvas skill to embed the actual results in a standalone interactive comparison, with metric units, sample counts and headless limitations. Keep data inline.
- [ ] Review the final implementation against the approved spec, run whitespace/comment hygiene checks, and deliver the example, raw results, report, canvas and a concise factual conclusion.
