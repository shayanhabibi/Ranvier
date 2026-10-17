# Instruction-count erosion investigation — 2026-10-16

The Fable thread check is an avoidable regression. Previous-value memos and reverse-acquisition owner teardown have useful, tested contracts and evidence supporting their retention. The historical creation and disposal instruction deltas do not by themselves establish their steady-state cost: the Node counter protocol includes VM compilation and optimisation transitions.

The initial investigation below preceded production changes; its JavaScript no-guard variant is an experiment. The implementation follow-up records the subsequent F# fix. The additional implemented savings and final verification are in [the generated-output pass](2026-10-16-generated-fable-savings.md). No fresh hardware-counter acceptance run is claimed.

## Evidence and limits

- Repository HEAD: `d2ee0cb`; latest stored counter run: `2ef3876`.
- Historical reports: `d87920f`, `cdef427`, `dd533de`, `b693a42`, `e13f159`, `878f294`, and `2ef3876`, under [`../.ai/benchmarks/counters/`](../.ai/benchmarks/counters/).
- Node 26.7.0, Fable 5.18.0 / fable-library-js 5.18.0, Ripple 1.0.0-beta.5. The historical Node reports use these same versions and `--expose-gc --single-threaded`. The .NET runtime changed from 10.0.12 to 10.0.13 by the latest report.
- `fslangmcp` freshly checked `src/Ranvier/Ranvier.fsproj`: clean, zero warnings/errors. Semantic definitions were queried after that check. The MCP connection was made through the installed Python MCP SDK because the server's tools were not exposed in this conversation.
- Four source snapshots were exported with `git archive` and freshly compiled: `59da758^`, `59da758`, `878f294`, `2ef3876`. The last snapshot's generated `Core.fs.js` is byte-identical to the checkout's existing plain counter output.
- The no-guard experiment copies that generated plain output and replaces only `Graph__AssertOnGraphThread_Z721C83C5` with an empty function. It does not change graph ambient-context handling or disposal semantics.
- Each variant/scenario was measured in three independent Node processes, 40 warmups at 2N and 21 samples at 4N, with two explicit collections immediately before each timed run. Variant order reverses for the second repetition. There were no concurrent builds or benchmark processes during these 75 runs. Initial exploratory samples collected during the snapshot build are excluded.
- These new measurements are elapsed time, not retired instructions. Each scenario runs in a separate process. They are deliberately distinct from the published full-suite, ten-warmup, N/2N counter protocol and must not replace its numbers or be presented as a framework ranking.
- Hardware counters require an elevated Windows process. This session is not elevated, so no fresh retired-instruction result is claimed.

The raw samples, summary, historical region analysis and reproduction scripts are in [`instruction-erosions/`](instruction-erosions/).

To repeat the timing and V8 region audit, run from the repository root, with no build or other benchmark alongside:

```powershell
rtk proxy python docs/research/instruction-erosions/reproduce.py --trace
```

This exports snapshots into a new temporary directory, builds all four before starting measurements, and writes results there. It needs the installed .NET/Fable tools and cached or restorable dependencies. Use a fresh `--work <absolute-directory>` for another run. It does not collect hardware counters or change production source.

## 1. Fable equal-write cutoff: reject the accidental cost

Stored counter checkpoints move from about 41 instructions at `cdef427`/`dd533de` to about 69 at `b693a42` and later. The source-level cause is `59da758` ("Trim per-write and per-run overhead on the memo hot path").

Before that change, `AssertOnGraphThread` used inline `Platform.isOffThread`; under Fable that helper is the constant `false`. The emitted condition was `this$.guarded && ((void this$.ownerThread, false))`. The commit replaced it with an ambient-cell reference comparison, useful on .NET but emitted as real work under Fable. Every signal write, including an equal one, calls it before testing equality.

In the Fable implementation, `AmbientSlot.cell` is the module's singleton cell. Guarded construction retains that cell as `ownerAmbient`. JavaScript graph objects are not shared between workers. The comparison protects no additional thread-affinity behavior in this target. This conclusion concerns the affinity assertion; the ambient state that tracks active graphs remains necessary.

Fresh cutoff results, median of the three process medians, ns per write:

- `59da758^`: 2.495 (process medians 2.640, 2.490, 2.495).
- `59da758`: 3.955 (3.955, 3.721, 4.147).
- `2ef3876`: 3.871 (3.871, 3.853, 4.480).
- `2ef3876`, generated-JS no-guard experiment: 2.615 (2.767, 2.603, 2.615).

The exact parent/commit pair reproduces the slowdown. Removing the check from current emitted JS reduces the median by about 32%. This is a causal timing result; it does not prove that a source fix will restore precisely 41 retired instructions. The broader no-guard timings are mixed/noisy, so no universal speedup is claimed.

**Verdict:** keep the .NET affinity protection and its optimisation; restore the Fable affinity assertion to a no-op. Do not move the .NET assertion after equality: off-thread equal writes must still be rejected. A production patch should be followed by Fable correctness checks and matched instruction measurements.

## 2. Previous-value memos: keep

The feature's own before/after reports are `cdef427` and `dd533de`, both recorded on September 28 and committed with `f8c8b39`. The source diff introduces the published-value flag and the previous-value argument, alongside the corresponding API, projection, and async changes.

- .NET four-memo chain: 1,889.31 → 1,928.01 instructions, +2.0%.
- Node four-memo chain: 3,404.65 → 3,565.96 instructions, +4.7%.
- Equal-write cutoff remains about 41 instructions under Node and 49 under .NET.
- The chain still performs four recomputations, with no meaningful per-operation allocation increase.

These recorded differences are below the reports' 5% noise threshold. They do not demonstrate a zero-cost implementation, but they also do not justify assigning the entire later chain increase to this feature.

The feature provides folds without external mutable state, keeps the last successful value through pending/failure, permits returning the previous value to stop propagation, and chains queued async predecessors. Its contract is represented by `tests/Ranvier.Tests/PreviousValues.fs` and the [design note](../.ai/designs/previous-value-computes.md).

**Verdict:** retain. There is a direct capability gain and the feature's recorded A/B cost is within the measurement noise budget.

## 3. Wider graph guards and failure handling: retain correctness, qualify the remaining cost

The later chain rise is larger than the previous-value pair: the September 27 baseline to latest increases are about 8.7% on .NET and 14.6% under Node. The intervening changes include:

- `6736ae3`: checks node creation, stale reads, batches, roots, cleanup registration and disposal, and fixes async activation flow. Before this, an off-thread stale read could recompute and rewire a guarded graph without raising. The commit adds explicit threading/async-activation regression tests.
- `2247fbe`: treats throwing comparers as computation failures and restores the previous successful value. This changes memo/boundary/split-effect behavior, rather than the signal equal-write path.
- `460b781`: records failure origin, preserves the original exception stack through downstream reads, and clears retained failure state after the outermost pull/flush.
- `c996711`: supports serialized entry on a captured synchronization context for Blazor Server and rejects simultaneous entry; Fable does not need the .NET locking path.

These are useful correctness and hosting capabilities. However, the broad `b693a42..e13f159` comparison contains several of them and cannot allocate its instruction difference to a single feature. The old design notes label the failure-provenance and serialized-affinity benchmark gates as pending, and [Wave B decisions](../.ai/wave-b/decisions.md) require matched A/B evidence before accepting hot-path regressions beyond noise. A feature being useful is not evidence that every incidental hot-path instruction was required.

**Verdict:** retain the contracts; recover the redundant Fable assertion first. Do not claim the whole remaining .NET/Node chain increase has passed the original per-feature gate. Fresh matched hardware-counter runs are still required to attribute and accept the residual cost.

## 4. Reverse-acquisition teardown: keep; the recent aggregate does not show an out-of-budget core regression

`15aa153` records acquisition order across children and cleanups, making teardown follow `use` order. It adds `Acquired` and owner bookkeeping and replaces the old "all cleanups, then all children" passes with a combined reverse walk. It also simplifies link removal by folding the linked state into the generation value.

This gives a renderer the ability to register DOM release before component construction and have that release run after component cleanup. [Owner lifetime](2026-10-13-owner-lifetime.md) records removing the renderer's duplicate per-component scope: the recorded 128-row disposal fell from 359 to 168 microseconds and retained heap from 427 to 397 KiB. Those are separate historical sessions, so they support the direction and practical benefit, not an exact isolated speedup for the core commit. Owner tests pin the interleaving of cleanups and child disposal.

Across the October 4 and latest .NET counter reports:

- Create: 2,179,026 → 2,175,579 instructions, -0.2%.
- Dispose: 429,403 → 438,663, +2.2%.
- Update/chain/cutoff each differ by under 0.4%.

That aggregate is within the stated instruction noise budget. It is not a single-commit attribution. The controlled Node timing protocol also shows no recent broad regression: create is about 2.08 → 2.14 ms/root, and disposal about 0.324 → 0.284 ms/root, with process variation. These timings do not invalidate the stored Node instruction counts; the protocols measure different VM states.

**Verdict:** retain. The ownership contract enabled the removal of a second lifetime system, and the recent .NET core aggregate remains within noise.

## 5. Creation/disposal headlines: remeasure before assigning a tradeoff

The published Node creation medians suggest +18.5% from October 4 to latest, but their raw N/2N regions are strongly nonlinear. For Ranvier creation, `instructions(2N) - 2 * instructions(N)` is approximately -88 million instructions per process at `878f294`, versus -25 to -26 million at `2ef3876`. An intercept can contribute to this diagnostic, so it is not itself a count of compilation instructions. The magnitude and movement make subtraction of these two regions unsuitable as an isolated steady-state regression verdict.

An additional `--trace-opt --trace-deopt` experiment used the same scenario's Run between marked boundaries, with preparation, collections and teardown outside. At the original scale and ten warmups:

- `878f294`, Ranvier: 0 compilations in N, 15 in 2N, with 3 deoptimisation bailouts in 2N.
- `2ef3876`, Ranvier: 6 compilations in N, 16 in 2N, with 2 and 1 bailouts respectively.
- Ripple also compiles in both measured regions.

Even scale 8 and forty warmups do not eliminate every compilation event. More warmup alone is not an adequate acceptance criterion. These traces run the isolated scenario rather than replaying the historical full-suite process; they demonstrate the protocol's vulnerability, not the exact compilation cost of the stored historical runs.

Disposal also needs care. The latest Node 1.409M-instruction result is tightly clustered, so it should not be dismissed. But older disposal regions have positive nonlinear work; one September process contains an approximately 529M-instruction nonlinear excess. The fresh timing protocol's current disposal ratio to Ripple is around 0.93, whereas the historical instruction protocol reports 2.93. This mismatch is a measurement-state finding, not proof that either metric is universally representative.

**Verdict:** withdraw the claim that the historical +18.5% creation figure is a demonstrated steady-state source regression. Keep the disposal gap as a real observation under the published protocol, but require a compilation-audited, scale-checked matched rerun before explaining it as the price of a feature.

## Follow-through

The highest-confidence implementation opportunity is Fable-only affinity-assertion elimination, preserving .NET checks. The most important measurement repair is to record VM compilation/deoptimization alongside the Node counter regions and verify results at several scales. The remaining core contracts should not be rolled back on the aggregate reports alone.

No production change, feature rollback, public API change, or fresh hardware-counter pass is included in this investigation.

## Implementation follow-up: conditional affinity assertion

The subsequent user instruction approved target-specific conditional compilation. `Core.fs` now makes `Graph.AssertOnGraphThread` a no-op under `FABLE_COMPILER`, while the existing .NET implementation stays in the other branch. The public signature is unchanged.

Fresh verification of this source change:

- All 1,638 untraced net10.0 method IL bodies match a freshly built pre-change assembly. Four compiler-generated method names differ in source-line suffixes; their IL bytes match. The affinity assertion itself remains 55 bytes of identical IL.
- The original Fable output fails a timing probe against the no-guard reference (1.492×; maximum 1.2×). Rebuilt source passes against that reference (0.972×).
- A separate paired comparison against copied pre-change output measures a median 3.105 → 2.164 ns per equal write, ratio 0.697, about 30% lower elapsed time. Three processes per variant, reversed order on the middle repetition, with no concurrent build. These are timing results, not fresh retired-instruction measurements.
- Complete .NET gate passes: 1,104 F# and 84 C# tests on each of net8.0/net10.0, plus the AOT smoke stage.
- The complete Fable compatibility gate passes with no untriaged inline failures: untraced inline 923/941 and promise 879/941; traced inline 1,011/1,033 and promise 962/1,033. The gate retains the project's documented platform differences.
- Playground passes all 366 Node tests and 23 browser tests.
- Formatting and the vendored comment-hygiene check pass. The XML-doc compiler audit exits zero and reports the existing nonstandard `<b>` tag at `Core.fs:68`; the changed documentation introduces no finding.
- A separate code review found no concrete issue in the patch.

Direct field restoration in the two `RunHosted` overloads was also compared against conditional restoration, with the guard removed in both variants. Results were mixed across chain/update and normalized process ratios, so that change was not added.

The follow-up's samples, verification commands, and IL comparison are recorded in [`instruction-erosions/affinity-conditionals.json`](instruction-erosions/affinity-conditionals.json).
