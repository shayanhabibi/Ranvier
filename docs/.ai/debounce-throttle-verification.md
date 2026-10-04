# Debounce and throttle verification

Feature branch: `feat/debounce-throttle`. Product baseline: `506c8d29c57e29d92cdce7164d89886a6d0b5618`. Build/test checks ran on 2026-10-03; hardware-counter evidence at `878f294` was collected on 2026-10-04. Commands use `rtk proxy`.

## Passing checks

- Complete Release F# tests, both .NET 8 and 10: 975 passed per framework without tracing; 1,055 passed and five expected allocation-test skips per framework with tracing.
- Complete Release C# tests, both frameworks: 84 passed per framework without tracing; 92 passed per framework with tracing.
- Core Release builds with tracing off and on, including netstandard2.1.
- Package compatibility consumer checks pass. Windows NativeAOT publish with trimming/warning gates passes; the executable passes all smoke checks, including all four zero-duration factories. The host RID is `win-x64`; this shell requires `-p:OS=Windows_NT` for the NativeAOT MSBuild OS check.
- Actual Fable Release CLI builds and compatibility runner pass with tracing off and on. All 31 untraced and 38 traced timed tests pass in both inline and promise delivery. Existing classified runtime differences remain: overall inline 797/815 and 876/898; promise 753/815 and 827/898. No new unclassified failure.
- Fresh `fslangmcp check` of `src/Ranvier/Ranvier.fsproj`: zero errors and warnings.
- Fantomas check of all 17 changed F# production/test/benchmark/Fable files; `git diff --check`.
- Compiler/XML audit of the new timed files and changed trace/C# files: zero findings. Comment-hygiene check of core source: no FOR-REVIEW comments.
- Full trace verifier's existing-method IL comparison: all 1,195 existing method bodies unchanged against the merge-base.
- Same-feature IL comparison with timed hook statements removed: exact equality. Trace-only predicate evaluation resides inside conditional hooks.
- Trace verifier: no untraced .NET trace residue, positive traced control, assembly metadata, untraced packed assembly, traced-pack rejection, named/label erasure on .NET and Fable, and no untraced Fable trace imports.
- Packed public API is exactly additive against the actual merge-base: the 52 entries in `debounce-throttle-api-additions.txt`, no existing removals and no unexpected additions.
- Five existing sample dumps are byte-identical; all 29 counter/live-edge reconciliation cases pass, including eight timed scenarios. Timed tests additionally replay schema 2, arbitrary per-clock origins, held/pending/failed states and disposal.
- Three-repeat allocation/graph counter comparison: all 40 existing .NET scenario rows unchanged. See [performance evidence](debounce-throttle-performance.md) for timing results and allocation limits.
- Hardware counters collected at `878f294`: five runs each for .NET and Fable/Node, including retired instructions, cycles and branch mispredictions. .NET calibration reports no divergences. All eight timed capture/admission scenarios have measurements; .NET allocation figures confirm zero-byte capture and 24 bytes/node for timer-backed admission. See the [report](benchmarks/counters/878f294.md) and [raw data](benchmarks/counters/878f294.json).

## Baseline and host limitations

The full trace verifier run on 2026-10-03 exited nonzero for existing `Projections.fs` hook-placement/Appendix A lint violations, the stale legacy public-API baseline, and ETW processor counters unavailable in that non-elevated process. The 2026-10-04 `878f294` report resolves the hardware-counter collection blocker. It does not record a rerun of the full verifier or a matched merge-base retired-instruction comparison. The two existing lint/API-baseline issues remain; the new merge-base API gate provides a precise compatibility check without rewriting that legacy baseline.

`dotnet build Ranvier.slnx -c Release -p:RanvierTrace=false` fails in the existing Fable test project's .NET projection: missing `IcedTasks`/`cancellableTask`, `Expect.isNotNull`, `testSequenced` and `Expect.hasLength`. These correspond to the pre-existing semantic projection diagnostics recorded during planning; the actual Fable compilation and runner are the portability checks. New timed source introduces no diagnostic in that projection.

Auditing all of `Combinators.fs` reports two existing long XML-doc lines, now at lines 612 and 933, outside the added timed module. New timed factory documentation has no audit finding.

Hardware retired-instruction collection is complete. Native timer policy throughput, separate callback/dispatch costs and real deadline latency distributions remain outstanding. A matched merge-base hardware-counter regression verdict is not established by the supplied HEAD report alone. The branch is available for review; the report does not establish that all remaining performance and repository gates pass.
