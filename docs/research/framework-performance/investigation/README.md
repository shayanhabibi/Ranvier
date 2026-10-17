# Mounting and disposal investigation

> Solid figures in this directory before the [mount cost rationale](mount-rationale.md) are Solid 1.9.17 and are kept as earlier measurements; that rationale and later records measure Solid 2.

This records the baseline investigation at `ca3a5d5`. The subsequent [array and modifier-function cleanup](cleanup.md) removes the intermediate token model and updates the performance checkpoint.

The original comparison identifies a real problem: at 128 rows Ranvier mounts twice as slowly as Ripple and disposes about eight times as slowly. The investigation finds excess intermediate construction, JavaScript allocation and unnecessarily fine DOM removal. It does **not** establish that ownership guarantees intrinsically require this gap, or that replacing the view representation alone will close it. Production renderer code is unchanged in this checkpoint.

## View construction and allocations

The measured production path uses `Program.View`, an erased wrapper around `Backend -> unit`. It allocates element closures, child lists and modifier/token lists. `Operations<'view>` is a shared operations record; it is not instantiated for every element. `Tree.View` is the separate tree representation. Ownership entries describe roots/ranges, while generated descendants are retained as node references. These distinctions matter when deciding what to remove.

The user’s preferred direction is direct construction with modifier arrays or composed functions, with lifetime state held by boundaries. The [direct-backend probe](direct-results.json) bypasses per-element Program views while keeping the same owned backend, graph, reactive primitives and typed tokens. At 128 rows, mounting falls from 1177 to 1001 microseconds in the pooled median, about 15%; the three process medians improve by approximately 6–18%. Updates remain essentially unchanged. Disposal is 407 versus 395 microseconds, with mixed ordering across processes. This probe still uses token lists and changes the child construction/execution shape; it shows that reducing intermediate construction helps, rather than isolating the exact cost of Program wrappers. The retained run includes source hashes and process medians.

The [allocation sample](allocations.json) estimates roughly 1.04 MiB of JavaScript allocations per 128-row Ranvier mount, 0.95 MiB for direct construction, 369 KiB for Ripple and 175 KiB for Solid. This is a single-process CDP sample at a 4096-byte interval, including collected objects. It measures estimated JavaScript allocation, distinct from retained heap and native DOM memory. It does not provide exact object counts or a time budget.

Mapped allocation stacks point to binding setup, token interpretation, component setup, validation, linked lists and view callbacks. Attribute validation alone accounts for approximately 86 KiB in the Ranvier sample. Emitted Fable code implements our `String.forall` validation with `name.split("")` and sequence iteration. The modifier unwrapping map also copies a list even though `Modifier` is erased in JavaScript. These are concrete avoidable allocations; they do not require weakening ownership or reactive guarantees. Allocation stack attribution can move under optimization, so source totals are diagnostic estimates.

## Disposal granularity

The operation-count capture at 128 rows records 385 elements, 128 text nodes, two anchors and one document fragment for Ranvier. Disposal calls `removeEventListener` 128 times, `remove` 515 times and unclaims 518 times. Ripple uses one root `removeChild`; Solid clears its host, and React removes its root subtree. The counter does not intercept every DOM API: in particular, it omits Solid’s host `textContent` setter and native adapters’ `append` calls. Empty count sets do not mean zero DOM work.

Our renderer currently removes every generated descendant individually. The [native lower-bound experiment](native-results.json) compares a handwritten DOM fixture that removes its section once with another that removes each descendant. At 128 rows, individual removal costs roughly 193 microseconds versus 55 microseconds for removing the section. These adapters implement live state, clicks and listener cleanup, but lack Ranvier’s ownership, rollback, boundary and effect semantics. They demonstrate that removal granularity has a cost; their absolute times are not a substitute framework benchmark.

Removing only roots would break a supported case: a generated child may have been moved into another host. It must remain claimed until its original mount is disposed, and disposal must remove it from its new parent. Borrowed native nodes also require restoration. A better release path should detach owned roots once, release claims and metadata, and handle externally moved descendants and borrowed nodes explicitly. Existing cleanup ordering requires callbacks to finish before DOM release.

The [prototype-removal probes](stages.json) disable or replace native prototype methods temporarily. At 128 rows, median disposal was about 385 microseconds at baseline, 318 with root-only removal, 263 with removal disabled and 353 with unclaiming disabled. Detaching first was slower. These experiments deliberately break guarantees and can change JIT dispatch/deoptimization. Their deltas are not additive cost attribution, and none is a production implementation.

## Other hypotheses

The isolated stage fixture reports approximately 39 microseconds of outer definition construction, 899 of DOM mount and 194 of renderer disposal at 128 rows in its unminified run. Nested component definitions are constructed during the DOM-mount stage. It therefore includes more than DOM operations. The stage fixture mounts and disposes roots sequentially, unlike the retained multi-root timing batches; stage measurements cannot be subtracted from the main benchmark or added into an exact budget.

Empty renderer-scope cleanup and core-only effect cleanup are much smaller than complete renderer disposal at 128 rows. Renderer sibling removal has nonlinear scaling at much larger widths: the empty-scope probe rises from roughly 12 microseconds at 128 to 1390 at 8192. Core owners use intrusive links and observer removal has indexed bookkeeping; the core teardown is not a naive linked-list scan. Isolated models have different retention and callback shapes and do not establish a precise percentage spent on guarantees.

Wrapping disposal in `Graph.Run` produced a small improvement in this fixture. This establishes graph context, not the full semantics of `Api.batch`. It is not an explanation of the large gap. A diagnostic transform that shares the backend class also failed to produce a large, consistent improvement. Its [raw results](shared-backend.json) were collected after 44 range proofs warmed each timing page, so its absolute timings have a different warmup history and should not be compared to the main ranking.

CPU profiles complement the allocation and operation counts. Native/runtime `(program)` samples and optimized/inlined stacks are partly unattributed, and Windows samples were coarser than the requested 100-microsecond interval. Exact source percentages would overstate what these profiles establish. The archived source map belongs to the accompanying latest CPU capture; the mapping script rejects mismatched asset names.

## Correctness checkpoint and next changes

Three new cases exercise disposal at 128 and 512 components while the graph and a sibling mount remain live, plus externally moved generated nodes. They check callback order while the host still contains DOM, idempotent disposal, empty hosts, inactive retained buttons, stopped binding reads, continued sibling updates, overlap rejection and subsequent reclamation. Ordinary subtree detachment may retain parent links inside the detached subtree; these tests deliberately avoid requiring every descendant to be individually unlinked. Externally moved nodes must be removed from their new host. These cases complement the existing rollback, borrowed-node, keyed, scheduling and boundary cases. Inactive button clicks prove behavior after disposal; the operation capture additionally verifies listener removal calls, rather than relying exclusively on an adapter’s disposed-graph guard.

The full playground suite passed 196 Node cases and 22 Chromium tests. The Release DomTests build passed with zero warnings/errors. Both new F# files passed the XML documentation audit in SageFs with zero findings. The diagnostic shared-backend transform passed all 44 range cases in both variants across three browser processes; it remains confined to the benchmark build.

The next implementation should use modifier arrays and direct child execution through the owned backend, removing redundant intermediate construction. Remove the identified validation allocation as well. Check emitted JavaScript to ensure function composition actually removes wrappers. Optimize release granularity separately, using the moved-descendant and borrowed-node cases as acceptance criteria. Static template cloning remains another substantial opportunity because Solid already uses it; it is client-side work. Each change needs paired before/after timings with the same guarantees enabled. No additional per-element record layer is needed.

## Reproduction and limits

From `examples/Fable.Ranvier.Playground`, run these sequentially:

```powershell
rtk node benchmarks/frameworks/run.mjs --build --direct-diagnostic
rtk node benchmarks/frameworks/run.mjs --build --native-diagnostic
rtk node benchmarks/frameworks/stages.mjs
rtk node benchmarks/frameworks/allocations.mjs
rtk node benchmarks/frameworks/diagnose.mjs --build
rtk node benchmarks/frameworks/summarize-profiles.mjs
rtk node benchmarks/frameworks/prototype.mjs
rtk npm test
```

The `run --build` step compiles both Fable adapters in Release with counters disabled and no cache. The other diagnostic scripts consume those generated adapters; keep that prerequisite when running them individually. Stages now forces a minified Vite rebuild, allocation/CPU sampling use unminified production bundles, and the shared-class probe has its own output directory. The archived stage runs preceded this forced-rebuild safeguard; their minified/unminified labels reflect the builds deliberately selected during collection, rather than an artifact metadata assertion.

Use process-paired ratios within a run. Absolute times rose during the investigation on this shared development machine, and no CPU-frequency or background-load controls were applied. Allocation sampling, native lower bounds, monkeypatch ablations and isolated stage timings have different measurement contracts. They provide complementary evidence, not interchangeable framework rankings. Existing original `results.json` is retained unchanged.
