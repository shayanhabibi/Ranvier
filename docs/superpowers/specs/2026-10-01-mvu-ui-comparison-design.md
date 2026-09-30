# Avalonia.FuncUI MVU comparison

## Purpose

Create a runnable task dashboard and a reproducible comparison of conventional Elmish and Ranvier.Elmish under a typical immutable model, message loop and UI workload. Report measured tradeoffs, including cases where Ranvier is slower.

## Deliverables

- A self-contained example under `examples/Ranvier.FuncUI.Workload`, with a desktop mode and a headless measurement mode.
- Three interchangeable implementations: ordinary Elmish, Elmish with cached subviews, and Ranvier.Elmish with selectors and effects per UI section and visible row.
- Automated checks of model and rendered output equivalence, message ordering, stable row identity and disposal.
- A reproducible benchmark command, raw JSON results, a Markdown report and an interactive results canvas.

## Dashboard and workload

The dashboard shows a searchable, paginated task list, summary counts and details for the selected task. Each task has a stable ID, title, owner, status and progress. Use 100 and 1,000 tasks, with a fixed page size of 50 and the same controls, dimensions, text and visible rows in every implementation. Pagination prevents the comparison from becoming primarily a test of creating thousands of offscreen controls.

Buttons select a task, change its status, advance progress and move between pages. A fake feed generates progress and status changes on a timer. Search filters by title or owner. Data generation and message traces use a fixed seed; no network, filesystem or sleeps occur inside measured intervals.

Measure separate traces for background updates, selected-task edits, search/page changes and a mixed interaction sequence. Include both visible and offscreen background updates. Each variant receives exactly the same messages in the same order and reaches the same final model and visible output.

## Shared implementation and comparison boundaries

All variants share the model, pure `init`/`update`, task generation, filtering rules and view construction functions. Ordinary Elmish runs the actual Elmish program and builds a root view on each changed model. Cached Elmish runs that same loop and reuses unchanged subviews using explicit reference/value checks. Ranvier.Elmish runs `Mvu.create`, using selectors to notify only the affected UI sections and visible rows.

Cached Elmish and Ranvier use equivalent UI section boundaries and stable row IDs. Any difference in virtual DOM traversal or patch scope is documented. UI updates execute on the Avalonia dispatcher in every variant. Graphs, timers and subscriptions stop when the window closes; opening another window creates independent state.

The package versions and whether Ranvier comes from a project reference are recorded. Use the local Ranvier project for development and measurements, with tracing disabled. Use the local Avalonia.FuncUI checkout when its APIs are needed, and make the dependency location configurable or document it explicitly.

## Measurement

Run headless Avalonia in Release to exercise actual controls and FuncUI patches. Warm up each implementation before measurement, rotate implementation order between repeated trials and retain all raw samples. Exclude setup, initial control construction and teardown from steady-state measurements; report startup separately if measured.

Record elapsed time per replay and per message, trial variability, allocated bytes per message, generation 0/1/2 collection counts, view construction counts, host update counts and selector evaluations. Collect render/selector counters in a separate instrumented replay if their overhead would bias timing. Also measure the shared pure update loop alone to identify the model-copy/filtering cost.

Allocation measurement must cover the executing UI thread and state its scope. Deferred dispatcher work must finish before ending a sample. Wall-clock measurements include that drain. Report medians and dispersion across trials; do not present unstable tail latencies as user-facing interaction guarantees.

Headless measurements cover model updates, view construction, virtual DOM patching and dispatcher work. They do not establish desktop frame rate, GPU rendering performance or input-to-screen latency. A desktop smoke run verifies the example, while the report labels the headless boundary explicitly.

## Verification and interpretation

Before measuring, run deterministic equivalence checks for every trace and dataset size. Verify initial rendering, repeated edits, invalid/no-op messages, search results, pagination, selected-task details and repeated open/close cycles. Confirm the benchmark does not accumulate previous trials' effects or controls.

Run the applicable repository test gates and build the example in Release. Follow the repository's F# semantic tooling and SageFs instructions; document tool failures and use the CLI for unsupported Avalonia scenarios.

The report states hardware/runtime configuration, workload sizes, repetitions, dependencies, source identity and measurement limitations. Explain whether a difference comes from the loop, selectors, caching, view construction or patching. Compare against cached Elmish as well as ordinary Elmish; do not attribute a UI partitioning advantage solely to the reactive engine.

## Exclusions

No changes to Ranvier's public API, no real backend, no production application, no publication or package release, and no claims about other UI libraries without measuring them.
