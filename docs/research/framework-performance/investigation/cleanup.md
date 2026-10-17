# Array and modifier-function cleanup

The DOM foundation now uses arrays for modifiers, children and fragments. Modifiers are backend functions; the `Token` union and its interpreters are removed. Fable reuses the erased typed modifier array, avoiding the previous list-unwrapping copy. .NET unwraps the typed modifiers into a function array. Authoring arrays are retained, so callers should treat them as immutable after creating a view. Keyed snapshot copying remains in place.

`Program.View` is a nominal backend delegate. Fable emits it as a plain function, with no view object. A transparent function alias caused F# value-restriction errors for ordinary saved views: the compiler generalized their backend argument to a constrained subtype. The delegate preserves the intended type without requiring annotations at authoring sites. All 112 generated bound element helpers inline into the direct Program construction path. Generic operation sets and portable HTML rendering remain available.

Static property writes apply directly instead of registering a no-op cleanup. Reactive property adapters capture their mounted target, and browser hooks keep their cleanup ownership. A regression test caught component-owner disposal inside a static setter; the modifier runner now checks setup liveness after each recipe, preventing dead component output from attaching. The test failed before the guard and passes afterward.

Name validation uses a character loop instead of splitting strings and creating sequence iterators. A SageFs equivalence check covered every UTF-16 code unit for both accepted character sets. Synchronous binding equality uses an explicit option match rather than a sequence helper. Core reactive ownership, boundaries, scheduling, rollback and DOM release behavior remain unchanged.

## Paired performance checkpoint

The [raw results](cleanup-results.json) compare the saved counter-free Release bundle from `ca3a5d5` against the cleanup bundle. Both are minified production builds. Three independent Chromium processes alternate variant order, discard three warmups and collect eleven rounds at widths 1, 16 and 128. Bundle hashes, all samples, correctness checks and retained heap readings are archived. Host setup and paint/layout are excluded; timings include GC and use the existing root-batch sizes.

At 128 rows, pooled median microseconds per operation were:

- Mount: **1102 → 911**, approximately **17% faster**. Every process improved, with process-median reductions of approximately 15–23%.
- Update: **152 → 131**, approximately **14% faster**. Every process improved.
- Disposal: **422 → 447**. Process ordering was mixed; this cleanup does not demonstrate a disposal improvement. Individual descendant removal remains the next separate target.

Retained JavaScript heap increased by approximately **5%** in this workload. Function-based modifiers simplify construction and improve these timings, but this change does not establish a memory improvement. Retained heap is distinct from transient allocation and native DOM memory. The shared development machine had no background-load or CPU-frequency controls; use paired results rather than comparing absolute times across investigation runs. No individual change's contribution is isolated here.

At 16 rows, pooled mounting improves by about 23%. One-row mounting has mixed process ordering and a roughly 5% higher pooled median; the improvement is strongest in wider views.

## Comparison with Ripple and Solid

The subsequent [same-run framework comparison](cleanup-framework-results.json) rebuilds all adapters and measures the cleaned-up Ranvier alongside Fable.Ripple and Solid in the same production bundle. At 128 rows, pooled median microseconds are listed in **Ranvier / Fable.Ripple / Solid** order:

- Mount: **980.31 / 533.44 / 313.44**. Ranvier costs **1.84× Ripple** and **3.13× Solid**.
- Update: **133.90 / 125.87 / 126.85**. Ranvier costs **1.06× Ripple** and **1.06× Solid**.
- Disposal: **366.72 / 50.16 / 47.34**. Ranvier costs **7.31× Ripple** and **7.75× Solid**.
- Retained JavaScript heap: **473 / 163 / 130 KiB**, approximately **2.91× Ripple** and **3.65× Solid**. Native DOM memory is excluded.

These comparisons show the remaining gap directly. The cleanup brings updates close to the two fine-grained libraries; mounting and disposal still need substantial work. Ratios use values from this comparison run, rather than mixing its absolute timings with the paired before/after run. Solid's normal compiled template path remains enabled. The same run also includes React; all widths, process medians and heap readings are retained in the raw file.

## Verification and reproduction

The full playground suite passed **197 Node cases and 22 Chromium tests**. All **25 compiler fixtures** passed, including unannotated saved Program views, native element constraints and rejection of list arguments. The generator's determinism, coverage, policy and corrupt-catalogue tests passed. The portable .NET representation checks passed, and the foundation Release build had zero warnings/errors. The XML documentation audit returned zero findings.

For a paired rerun, build the framework benchmark at `ca3a5d5` in a separate checkout using `rtk npm run bench:frameworks:verify`. Copy its `generated-framework-dist` directory to this playground's ignored `generated-framework-before` directory. Then compile the current playground tests in Release with counters disabled and run:

```powershell
rtk npm run compile:tests
rtk node benchmarks/frameworks/compare-cleanup.mjs
rtk node benchmarks/frameworks/run.mjs --build --cleanup-comparison
```

The comparison script rebuilds the current minified bundle and serves both bundles. The saved baseline must precede compilation of the changed adapters. The original framework-ranking results and earlier investigation data are preserved.
