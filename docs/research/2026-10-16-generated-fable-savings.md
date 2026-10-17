# Generated Fable savings

Date: 2026-10-16. Baseline: `d2ee0cb` (`2ef3876` has identical production source).
Companion investigation: [historic instruction erosions](2026-10-16-instruction-erosions.md).

## Implemented

All production changes are selected by `FABLE_COMPILER`; .NET keeps its existing executable code.

- **Graph affinity and inbox:** omit thread checks, the per-graph inbox allocation, unreachable dispatch/enqueue/drain paths, and serialised stale-read bookkeeping. Fable dispatch remains synchronous; `PendingWork` and `Pump()` remain zero. The dispatcher property and ambient hosting/activation state are preserved.
- **Observer and dependency slots:** erase the single-case slot representations under Fable. Generated arrays now store observer/source references directly. .NET retains the structs that avoid covariant array-store checks. Occupied ranges, whole-slot replacement, swap removal, index maintenance and source trimming are unchanged.
- **Async continuation bookkeeping:** call the flight body directly under Fable, omitting the .NET continuation-frame callbacks, fields and methods. Inline the Fable exception-capture identity operation. Keep all .NET execution-context handling.
- **Async locking:** inline the existing Fable lock behavior, executing the action once. Four async lock sites no longer allocate callbacks or call `Operators_Lock`; .NET still uses its existing locks.
- **Scheduler queue writes:** emit a direct JavaScript store after the existing `queueCount < queue.Count` check; .NET keeps its original indexer.
- **DOM queue snapshots:** use native array mapping instead of a sequence pipeline. The output array and each slot/version/action tuple remain: they preserve cancellation and reentrant commit semantics.
- **DOM region identity tests:** replace nine Fable option-to-array/sequence predicates with direct option matching and reference comparison. Preserve absent options and `Some null` behavior. .NET retains its original `Option.exists` operations.

## What was examined and retained

The pass examined generated Core, Platform, Api, Bindings, collection/combinator/projection/delta/positional/value-reader modules, plus renderer scheduling, lifetime, mounting and template output. It distinguished emitted module size from retained production output.

- Reflection functions and unused template catalogue declarations are often tree-shaken. Their source-file size is not a shipped saving.
- Structural dictionary/set fallback cannot be replaced by native JavaScript identity collections: record/array keys still require structural equality. Lazy fallback construction was considered but generic iteration would still materialize it; no unmeasured representation change was added.
- Range iteration may call user code that mutates the range. Index-loop rewrites were left out pending a separate semantic/performance case.
- Generic option values, comparer calls and captured failure provenance remain where they carry observable semantics.
- The previously tested unconditional `RunHosted` restoration variant had mixed performance and was not adopted.

## Evidence and reproduction

The complete core-module output shrank from **426,906 to 417,911 bytes** (8,995 bytes); Core.fs.js accounts for 8,151 bytes. This is emitted output, including unused exports and comments. The production measurements below count minified, retained code instead:

- Synchronous API probe: **46,248 → 42,035 bytes**, saving 4,213 bytes (921 bytes gzip).
- Collection API probe: **111,629 → 107,526 bytes**, saving 4,103 bytes (916 bytes gzip).
- Async API probe: **58,170 → 51,907 bytes**, saving 6,263 bytes (1,394 bytes gzip).
- Actual playground application: **157,846 → 154,881 bytes**, saving **2,965 bytes** (860 bytes gzip). Both builds use the same application source and Vite production settings. The gzip numbers use the respective measurement script's compressor settings; they are paired within each measurement.

These probe entries overlap; their savings must not be added together. The playground result includes the renderer changes as well as the core changes.

Across three fresh processes for each version/scenario, the median Ranvier elapsed times were:

- Equal writes: **3.016 → 1.926 ns/op**, 36% lower.
- Updates: **76.58 → 68.18 µs/op**, 11% lower.
- Four-memo chain: **1.074 → 1.050 µs/op**, 2% lower; treated as noise.
- Dispose 1,000 rows: **179.28 → 132.54 µs/op**, 26% lower.
- Create 1,000 rows: **1.688 → 1.117 ms/op**, 34% lower.

Normalizing against the Ripple control gives after/before ratios of 0.638, 0.906, 0.978, 0.704 and 0.674 respectively. These measure the combined patch against the original pre-affinity-fix code, not individual attribution. Creation/disposal results remain sensitive to warmup and JIT work; they do not establish new retired-instruction counts or overturn the historical counter caveats.

`instruction-erosions/audit-output.mjs` bundles three controlled public-API entry sets using Vite 8.3.2, minification and an ES2022 target. It also checks dispatch/inbox behavior under all three affinity policies in both variants. The inputs are generated trees, not edited JavaScript. Its baseline for the deeper pass already includes the initial affinity fix; total performance comparisons use the original pre-fix generated tree.

`instruction-erosions/compare-output.py` uses the same complete scenario harness as the historic investigation: three fresh processes per version/scenario, forty warmup rounds, twenty-one measured rounds, alternating Ranvier/Ripple order, forced GC outside timing, and Node `--expose-gc --single-threaded`. Version order reverses in the middle process. These are elapsed-time measurements, not new retired-instruction counts. Creation/disposal still carry the warmup caveats documented in the historic investigation.

The .NET invariant was checked from freshly built Release assemblies using `PEReader`: compare every method body's IL bytes in metadata order, including methods without bodies. All **1,638 core methods** and **16,312 renderer methods** match their original baseline. Assembly/PDB identity is not asserted: source line information changes. The first cached renderer comparison was rejected as stale and replaced with a fresh build of the exported baseline.

Independent source and generated-output review found no concrete correctness issues. Existing observer/source tests exercise duplicate observers, threshold transitions, swap removal, reordering and trimming; scheduled DOM tests exercise reentrant commits and disposal during a commit.

Measured sizes, timing samples and final verification are recorded in the sibling JSON artifacts. Generated output remains build output; only its F# source is changed.

- [API bundle and emitted-module sizes](instruction-erosions/generated-output-sizes.json)
- [Playground bundle sizes](instruction-erosions/generated-output-playground-size.json)
- [Timing samples and summaries](instruction-erosions/generated-output-performance.json)
- [.NET IL comparison](instruction-erosions/generated-output-dotnet-il.json)
- [Verification commands and outcomes](instruction-erosions/generated-output-verification.json)

Final acceptance: all 1,104 F# and 84 C# tests on each of net8.0/net10.0, AOT smoke, the traced/untraced Fable compatibility gate, 366 DOM tests and 23 Playwright browser tests. Fable's documented differences remain: untraced inline 923/941 and promise 879/941; traced inline 1,011/1,033 and promise 962/1,033. No new untriaged difference was introduced. Formatting and source-comment checks pass. XML auditing reports the existing nonstandard `<b>` tag in Core.fs, with no new finding.

Fantomas initially failed to merge four XML comment blocks whose declarations were conditional but comments were not. Moving the directives above their comments fixed formatting without changing executable code. The final compiler regeneration succeeded. Owned SageFs sessions were stopped; the user's existing sessions were left running.
