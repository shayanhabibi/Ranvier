# Portable view representation evidence

The experiment supports continuing with an **opaque instruction tree as the provisional core**. Both candidates can support ordinary typed functions and small sugar wrappers. The executable candidate has no consistent browser advantage here, while its .NET definition allocations are about 13–16% higher. This does not establish extreme performance or justify freezing the API: keyed branches, production status semantics, instance/retained heap measurement and larger component workloads remain gates.

## What was implemented

`experiments/Ranvier.View.Foundation/Representation.fs` contains two reusable definitions against one backend contract: a private instruction union, and a private backend build function. The latter's wrapper is erased in Fable, so the JS comparison does not penalize it for an unnecessary wrapper object. Both share the HTML interpreter and `Browser.fs` interpreter; no production renderer signature changed.

`Typed.fs` ties modifiers to Xantham native element types. Generic id/title modifiers infer the chosen type; input values require HTMLInputElement. SageFs accepted the positive div/id/input example and rejected `Typed.div Tree.ops [Typed.value "bad"] []` with the expected HTMLDivElement/HTMLInputElement mismatch. These are compiler probes, not a generated complete HTML catalog or typed event map. Low-level backend tokens are intentionally available inside this experiment.

Definitions allocate no nodes, owners, effects or listeners. Component setup executes untracked once per mount and receives cleanup registration, without native element access. Two mounts have independent instances, owners, effects and listeners. Browser hooks receive their typed native element only during interpretation and return cleanup. Arbitrary user closures can still capture browser globals; the experiment does not claim a compiler-enforced portable closure system or a dependency-isolated server package.

The .NET interpreter escapes text and attributes and snapshots explicit readers. Browser operations require an explicit Reject or Omit policy. The small domain supports div/button/input and id/title/value; it is not production SSR, hydration, sanitization, boolean-attribute coverage or a general arbitrary-tag API. Browser bindings use synchronous ordinary successful readers, explicit equality, and untracked writes. Pending/error adapters, scheduling policies and reactive properties are not implemented by this probe.

## Correctness and sugar

Portable checks cover escaped values, empty/nested fragments, repeated component setup, browser policy, input serialization and cleanup on failure. Browser checks cover reused views, setup once, independent disposal, equality cutoff, unchanged snapshots, scalar write locality, retained listener removal and static content with no effects. Existing lifetime traces continue to run through the playground suite.

The function probe delegates to ordinary operations. The builder probe uses Yield, YieldFrom, Combine and Delay to construct a fragment. They produce identical HTML and identical mounted counts in both representations across 64-child static, 256-child wide, 20-level nested fragments and one-binding reactive fixtures. Each fixture deliberately contains one component: two owners including the mount, one setup, zero effects for static fixtures and one for reactive fixtures. The single reactive update adds exactly one write. Timed runs disable the optional resource counters. These probes do not represent complete Oxpecker or Feliz libraries or CE control-flow coverage.

## Measurements

Raw timing samples, process medians and p95 values are in [browser-results.json](view-representation/browser-results.json). Host: AMD Ryzen 9 9900X, Chromium 153.0.8010.12, Node 26.7.0, Fable 5.18.0, Release F# output. Three independent headless browser processes, three warmups and eleven samples per cell; rotating representation/sugar order. Each sample times 500 definitions, 64 independent mounts/disposals of one reused definition, or 10,000 synchronous source updates. Values are batch elapsed time divided by operation count; p95 is a percentile of batch averages, not individual latency.

The initial run used ordinary timers and eight mounts per batch; many disposal results quantized to zero. It is retained as [browser-results-low-resolution.json](view-representation/browser-results-low-resolution.json) and is **not used to select a candidate**. The final run requires cross-origin isolation and larger batches. Both runs served unminified Release-generated modules through Vite; this is not a minified production bundle comparison. Construction includes fixture dispatch/list/string creation and a JS sink assignment; those costs are identical in both candidates. Mount includes insertion into an attached host and retaining disposal handles. Update excludes mount/disposal and runs one graph context around the source loop. Disposal excludes construction/mount. Timers exclude paint and forced layout, include incidental GC, and do not control CPU scheduling.

Direct static mount medians were 57.7 microseconds for tree and 70.3 for program; wide mounts were 257.5 and 254.5; nested fragments 58.3 and 55.3; reactive mounts 60.0 and 58.0. The relative winner changes with workload. Direct scalar updates were 0.206 and 0.198 microseconds; both use the same already-mounted binding code, so this difference provides no representation advantage. Process medians and tails in the raw data show why tiny differences should not be treated as stable rankings.

Tree direct/function/builder construction medians were 11.11/9.75/10.76 microseconds for static fixtures and 40.85/38.41/41.96 for wide fixtures. Reactive update medians were 0.206/0.211/0.202. Wrapper work occurs at construction; no wrapper is consulted by the mounted binding. Do not interpret the function probe's lower median as a promise that adding a wrapper improves performance.

Release .NET allocation measurements are in [dotnet-allocations.json](view-representation/dotnet-allocations.json): GetAllocatedBytesForCurrentThread, three warmups, median of seven batches of 100 definitions. Direct/function/builder allocate the same measured bytes for each workload. Static definitions allocate 13,640 bytes for tree and 15,752 for program; wide 53,576/61,832; fragments 14,920/17,352; reactive 15,760/17,888. These are **.NET definition bytes**, not JS allocations, instance allocations or retained heap. The program wrapper exists on .NET while being erased in Fable.

## Generated code and outstanding budgets

The tree emits a tagged union and mount-time switch. The executable candidate emits closures and calls the backend directly; its View wrapper disappears. Both retain F# lists and list enumerators, plus callbacks at element/component traversal boundaries. The typed Modifier wrapper disappears, but normalizing modifier lists still maps a list during definition creation. The function wrapper remains a call in unminified output. The builder retains its object, Delay closures and helper calls there; .NET's measured allocation equality does not prove these disappear in JS. Neither candidate is specialized static-template code.

The measured resource budgets are exact for these fixtures: no effects on static content; one effect per explicit binding; one setup and one child owner per explicit component; no setup rerun or unrelated write for a scalar update. Numerical timing baselines and tails are stored per workload, but enforceable runtime regression margins await stable broader baselines. We have not measured JS allocation bytes, instance bytes, retained heap, minified wrapper contribution, keyed movement, status transitions, native event-map typing, concurrency or cross-browser performance. Framework Task 2 remains open for these gates.

The lifetime prototype still has recursive collection, per-scope arrays, disposal arrays and linear child unlinking. No claim about production memory efficiency follows from successful resource release. The playground production build remains 48.90 kB JS / 14.42 kB gzip because the experiment is only in the test project; that is not the new renderer's bundle size.

## Verification and tool limits

Commands from the worktree root:

```powershell
rtk proxy dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj --configuration Release
rtk proxy dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj --configuration Release -- --allocations
rtk proxy npm --prefix examples/Fable.Ranvier.Playground test
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run build
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run bench:view
rtk proxy dotnet fsi build.fsx -- test --quick --configuration Release
rtk proxy dotnet build Ranvier.slnx --configuration Release
```

Portable checks pass. Playground acceptance passes 63 Node checks and 13 Chromium tests, with each representation browser case executed within the Chromium aggregate. Canonical .NET acceptance passes F# 938 and C# 81 tests on each of net8/net10 plus AOT smoke. The whole solution still fails on the baseline five FS0039 errors: IcedTasks, cancellableTask, Expect.isNotNull, testSequenced and Expect.hasLength in the Fable core-test project. Existing NU1608/xUnit warnings and the playground's two npm audit critical entries are unchanged.

SageFs session fdf01aa5 was created for this worktree after its Debug build. Pure and compiler probes succeeded; hard reset/rebuild then loaded the persisted portable checks successfully. Source-file declarations initially required file-mode wrapping. The Python MCP client sometimes reported anyio.BrokenResourceError/ClosedResourceError while closing SSE after a successful structured evaluation; this did not invalidate the result. No daemon restart was performed.

## Independent review and fixes

A fresh-context read-only review found two Important portability defects and no Critical or deferred Minor findings. Repeated id/title/input-value modifiers now serialize their final effective value once, matching native DOM assignment. A component that fails during setup/render and cleanup now reports an aggregate containing the original failure followed by cleanup errors, while all cleanup callbacks still run; a setup-only failure is rethrown as the original object. Both findings were reproduced RED in the portable checks, fixed in SageFs, persisted and verified GREEN. Parsed HTML versus actual native input/id values is covered in both browser representations. An earlier expanded fragment test also caught and fixed clearing the root list inside the removal loop; multiple top-level nodes now release completely.

Session e5ee1e46 was created for the fix probes and reset to verify persisted checks. Initial optimized-project health warnings concerned hot reload; the probes defined fresh pure code, and reset rebuilt the session without optimizations. Owned sessions were stopped through their exact dashboard session endpoints when the one-shot MCP client could not satisfy connection ownership; the user's daemon remained healthy.

Timing data was collected before these correctness fixes. The HTML changes are absent from the timed browser path, and the root-release change preserves the measured one-root-per-mount fixture operations. These timings remain candidate evidence, not final production performance acceptance.

Review rulings: retain the plan's deferrals for keyed branches, status/scheduling/reactive properties, JS allocation/instance/retained heap, strict timing budgets and minified contribution, wider HTML/sanitization/hydration/native event maps/package isolation, portable closure enforcement, CE/control-flow/full sugar, other browsers/larger components, lifetime scaling and direct DOM/old-PoC baselines. Those are explicit future gates rather than claims proved by this experiment. Cost if wrong: larger workloads or production semantics may change the representation decision or expose additional unsupported cases.

## Decision and follow-up

Keep ordinary typed functions as the authoring core and use the tree provisionally for the next scopes/ranges proof. Its introspectable portable tokens and lower .NET construction allocations are useful; browser results do not pay for opaque executable closures with a clear general win. Preserve the backend seam so a specialized build path can be tried on demonstrated hot paths later. Finish structural/status/heap gates before selecting a public representation or promising extremely performant complete sugar libraries.
