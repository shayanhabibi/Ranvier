# Binding integration and measured overhead — 2026-10-11

> Superseded by the [text/boundary repair](2026-10-11-text-boundary-repair.md). This document records the rejected per-binding outcome experiment. Its policies, extra roots and HTML snapshot selector have been removed.

Continues `272b82b` and the [binding policy proof](2026-10-11-binding-policies.md). Tree/Program now carry `BindWith` policy bindings through Backend, and `Typed.reactiveText` supplies the direct typed helper. `Mounting.mountWith` selects synchronous or deferred commits; a top-level deferred mount owns its queue and nested keyed rows share it. `Mounted.Flush` settles work and `CommitErrors` exposes the latest queue errors. The original `Bind` is retained as the legacy synchronous comparison operation; it does not adopt deferred scheduling from `mountWith`.

Views retain readers and outcome policies, not a graph-specific queue. Reusing a view across mounts gives independent binding roots/committed caches and queues. Removed keyed rows cancel their slots; disposing one mount does not stop another mount on the same graph. Binding policies remain explicit and introduce no automatic memo.

## Setup and server behavior

New view bindings use `Binding.startChecked`. Initial policy callback or immediate setter failures are reported, escape effect isolation, and unwind the containing mount. A failed reading handled by the selected fallback/retention policy is not a setup exception. A scheduled first setter runs after construction, so its failure reaches the reporter/queue and permits later recovery. The lower-level `Binding.start` preserves its earlier effect-isolated setup behavior. Per-reader-run setter cleanup semantics are unchanged.

`Html.renderBindings` requires an explicit server outcome selector returning optional text, then escapes that text. It converts thrown reader exceptions to failed readings before selection. It does not execute client equality, status, reporting or fallback callbacks. Existing `Html.render` rejects these new bindings, including under browser `Omit`; it does not infer a pending/error policy. Server request ownership, asynchronous streaming and hydration remain later tasks.

Foundation row text in the playground now uses policy bindings on an owned microtask queue. A separate load-result panel demonstrates pending fallback, failed fallback with explicit reporting, recovery, and retained element identity. This remains an experimental source consumer, not a published renderer package.

## Measurement

[Raw samples and summaries](binding-policies/browser-results.json) contain 1,650 measurements: three independent Chromium processes, three warmups and eleven recorded rounds per case. Runs rotate legacy/direct/helper modes. Environment: Fable 5.18.0 Release output, Chromium 153.0.8010.12, Node 26.7.0, Windows 10.0.26200, Ryzen 9 9900X, isolated high-resolution timers. Source base is `272b82b` with this integration uncommitted; the results are a checkpoint, not a stable release budget.

The legacy comparator is Foundation's old synchronous `Bind`, using the same mount/range/graph and text workload. The new runtime has stronger status, reporting and cancellation contracts; this is the cost of those contracts against the earlier narrow path, not a behavioral equivalence or a Ripple/Solid comparison. Queued measurements include **manual flush**, not microtask latency. All modes validate final output and disposal after samples. Timings exclude view construction from mount/update measurements; construction is measured separately. The 100-write workload uses `Api.batch` in every mode, so it measures graph-batched updates plus commit cost, not queue coalescing across separately flushed source updates.

Median microseconds per operation (one operation updates all bindings in the row):

| Bindings | Phase | Legacy sync | Policy direct sync | Policy direct queued |
| --- | --- | ---: | ---: | ---: |
| 1 | Mount | 6.48 | 6.72 | 6.95 |
| 1 | Update | 0.293 | 0.610 | 1.277 |
| 1 | 100-write batch | 0.975 | 1.650 | 2.325 |
| 1 | Dispose | 1.95 | 2.27 | 2.73 |
| 64 | Mount | 50.86 | 80.78 | 83.83 |
| 64 | Update | 13.08 | 33.00 | 32.90 |
| 64 | 100-write batch | 25.92 | 47.53 | 44.68 |
| 64 | Dispose | 18.91 | 46.80 | 53.12 |

The direct synchronous ready-update path is about **2.08× slower for one binding and 2.52× for 64**. This is a material regression against the desired performance target. The typed helper's synchronous update medians are 1.06× and 0.99× the direct API; queued update ratios are 0.99× and 1.02×. These small differences do not establish a dependable helper penalty. Across other phases, ratios vary more with run noise and GC; all medians, p95 values and replicate medians are retained rather than choosing a favorable sample.

The helper creates the same representation instruction and adds no runtime effect/owner or scheduler layer. Resource costs are still identified from the code rather than measured retained heap: each policy binding adds a disposable root, effect, committed cache/version and callback closures; queued bindings also reserve a slot. Every queue flush materializes a snapshot. Cheap setters make these costs visible. No allocation, heap-retention, layout/rendering or microtask-latency conclusion follows from this timing harness.

The playground bundle increased from 71.70 to 78.05 kB JavaScript, and 20.16 to 21.93 kB gzip. This includes the new example UI and integrated runtime; it does not isolate runtime bundle contribution.

## Verification and next work

Initial setup-unwind tests failed for Tree and Program before the checked binding path was added, then passed. The integration checks cover both representations/schedulers, keyed component sharing/removal, independent mounts, setup unwind, immediate/deferred setter failure and explicit escaped HTML snapshots. CLI portable tests cover the server selector independently of client callbacks. The original forty pure binding cases remain covered on .NET and Fable.

Final verification:

- Complete playground acceptance: 187 Node cases and 18 Chromium tests passed. The binding runner executes the original 43 cases plus 14 integration cases; Chromium's range harness also executes the synchronous integration cases, and the application test covers the microtask consumer.
- Release `DomTests.fsproj` build: zero warnings/errors. Fresh scoped FCS checks for DomTests, Playground and RepresentationChecks: zero errors/warnings.
- Portable .NET executable: all eight groups passed, including explicit outcome snapshots for Tree/Program. The refreshed SageFs worktree session passed all 45 pure binding/portable integration cases. Successful results were followed by the MCP client's existing SSE `BrokenResourceError`/`ClosedResourceError` during connection teardown; the daemon was not restarted.
- Canonical .NET runner: 938 F# and 81 C# tests passed on each of net8.0/net10.0, plus AOT smoke (`rtk proxy dotnet fsi build.fsx -- test --quick --configuration Release`).
- Production playground bundle passed. XML documentation audit of Binding, Representation and Mounting: zero findings. Staged formatting/comment-hygiene gates run during commit.
- `rtk npm --prefix examples/Fable.Ranvier.Playground run bench:binding`: all three independent Chromium replicates completed and saved 1,650 samples. Benchmark verification checked output and disposal for all workload/mode combinations.

A fresh reviewer found no important correctness/lifetime/API defects and identified the batched-workload interpretation limit recorded above. The whole-solution Release build still reports the same five baseline Fable-test errors (`IcedTasks`, `cancellableTask`, `Expect.isNotNull`, `testSequenced`, `Expect.hasLength`), with existing docs dependency warnings. Whole-solution acceptance remains open.

The next performance task is to profile the ready synchronous path and disposal, then reduce proven overhead while preserving these contracts. Check the cost of nested untracking/owner restoration, per-run closures and the extra binding owner before changing the representation or adding ergonomic sugar. Queue throughput, allocations after settled disposal, unbatched queue coalescing, several independent mounts and real form/property workloads also need measurement. Do not freeze this API or claim the intended extreme performance yet. Generated typed DOM breadth remains separate from this runtime work.
