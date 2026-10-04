# Debounce and Throttle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` if the user selects delegated execution, or `superpowers:executing-plans` for inline execution. Use `superpowers:test-driven-development` for the implementation and `superpowers:verification-before-completion` before completion claims.

**Goal:** Add portable debounce and three throttle modes with correct graph ownership, causal tracing, and measured performance.

**Architecture:** A single specialized timed node captures stabilized input through the existing graph queue and publishes independently through a monotonic deadline. Timer callbacks post wakes to the graph owner. Keep graph options, flight policies, and existing hot paths unchanged.

**Tech Stack:** F#, .NET 10/8/netstandard2.1, Fable, Expecto, C# facade/xUnit, BenchmarkDotNet, existing trace/counters/AOT tooling.

**Spec:** [Debounce and throttle design](../specs/2026-10-03-debounce-throttle-design.md). Research: [prior art](../../.ai/debounce-throttle-prior-art.md).

Implementation is available on `feat/debounce-throttle`, based on `506c8d29c57e29d92cdce7164d89886a6d0b5618`. The task lists below preserve the original execution requirements; this status records what was actually delivered and what remains outstanding.

## Execution status (updated 2026-10-04)

Tasks 1–5 are implemented: portable clocks, all four timed modes, causal tracing and recorded timing/schema 2, C# factories, shared Fable tests and Windows NativeAOT smoke. Timed tests live together in `tests/Ranvier.Tests/Timed.fs`; the manual clock has its own `TimedTestClock.fs` rather than enlarging general test support. Clock and node slices were combined into a green implementation commit. Counter scenarios exercise the timed trace path; existing sample dumps remain unchanged.

Task 6 has allocation/counter baselines and 30 BenchmarkDotNet Short cases, plus a deterministic JavaScript policy harness. The [878f294 counter report](../../.ai/benchmarks/counters/878f294.md) now supplies five hardware-counter runs for both .NET and Fable/Node, including retired instructions, cycles and branch mispredictions; hardware collection is no longer blocked. Production retains lazy deadline extension. Native timer policy throughput, separate callback/dispatch costs, construction/equal-write measurements and real deadline latency distributions remain unmeasured. The supplied HEAD report alone does not establish a matched merge-base retired-instruction regression verdict. See [performance evidence](../../.ai/debounce-throttle-performance.md) for measured results and their limits.

Task 7 includes the timing guide, schema documentation, full traced/untraced .NET and C# tests, actual Fable runs, format/XML/comment checks, packed API comparison and AOT smoke. New API additions are explicitly enumerated; existing signatures are retained. Review found and fixed per-clock origin normalization and admission-failure history attribution, including a regression with an equal capture after the winning input. The final trace verifier retains pre-existing projection lint and stale legacy API-baseline failures rather than waiving them. Performance acceptance remains pending the measurements above.

## Global constraints

- Prefix every shell command with `rtk`; use `rtk proxy` for tools without a dedicated filter. Do not chain dependent mutations.
- Respect unrelated working-tree content, including `examples/Fable.Ranvier.Playground/`. Arrange an isolated execution workspace before implementation if required by `superpowers:using-git-worktrees`.
- Read the repository SageFs skill before F# edits. Run `fslangmcp check` before `find`; use explicit project paths. Run `fcs_refactor_impact` before any public signature change and capture `fcs_public_api` before/after. Do not infer missing symbols from a failed workspace check.
- Obtain the repository-required setup permission for missing comment-hygiene tooling before checking new source comments. Read the XML documentation skill before writing `///` comments. No user daemon is stopped or restarted.
- New timed-node and clock files carry their own state; add no fields/branches to existing graph or memo hot paths. Retain all supported TFMs and Fable without new runtime dependencies.
- Run a failing behavioral test before each implementation slice, then make it pass. Tests assert observable timing/graph behavior rather than private field arrangements.
- No sleeping tests, fake async flights, blanket API rebaseline, trace-gate waiver, or unsupported claim of zero expiry allocations.

## Review focus

1. Dirty computed inputs losing later changes: the sliding-deadline regression and conditional-edge tests must pass.
2. Timer/owner races bypassing batching: queued expiry plus batched source writes, stale/disposed wakes, and wake coalescing must pass.
3. Published and captured states being confused: pending/failure/recovery, equal admission, and reentrant observer tests must pass.
4. Trace cause pointing at a superseded input: deterministic event folding, causal explanation, and counter reconciliation must pass.
5. Hidden cost to existing users: merge-base IL, hook-erasure IL, API compatibility, allocation counters, and retired instructions must pass or report a concrete blocker.

## Task 1: Record baselines and introduce portable clocks

Files: add `src/Ranvier/TimedClock.fs`; edit `src/Ranvier/Ranvier.fsproj`; add `tests/Ranvier.Tests/Timed.fs`; edit `tests/Ranvier.Tests/Support.fs` and `tests/Ranvier.Tests/TestFiles.props`. Place the new shared test file after `Combinators.fs` in the props file.

- [ ] Record HEAD, workspace status, library public API, and the existing trace verifier result. Run full actual Fable CLI tests to classify the projection diagnostics recorded in the spec. Preserve baseline output under `docs/.ai/` without machine secrets or oversized build logs.
- [ ] Read the required repo skills and establish semantic/REPL tooling. A missing tool is a prerequisite to resolve, not a reason to substitute text search for F# semantics.
- [ ] Add test-only manual monotonic clock and counting timer adapter. Specify one-shot replace/disarm/dispose behavior, equal-deadline arm ordering, and no inline callback from `Arm`.
- [ ] Write failing clock tests: no callback before due; rearm replaces prior due; disarm/dispose prevent effective work; long-duration chunks; negative-duration rejection; zero-duration operator contract reserved for Task 2.
- [ ] Implement abstract `TimedClock`/`TimedTimer`, .NET reusable timer adapter, Fable monotonic/setTimeout adapter, and conditional `ofTimeProvider`. Compile the new clock file after platform support and before `Core.fs`.
- [ ] Ensure callback races are allowed by the adapter contract and covered at the node boundary, rather than asserting cancellation can retract an already-running callback.
- [ ] Run the new clock tests on net8.0/net10.0 and through the Fable test project. Build netstandard2.1 explicitly. Commit this slice only after its checks pass.

## Task 2: Implement captured versus published state and debounce

Files: add `src/Ranvier/Timed.fs`; edit library compile order; edit `src/Ranvier/Combinators.fs`, `tests/Ranvier.Tests/Timed.fs`, and test registration. Add only the debounce factories at this stage; leave the rest for Task 3.

- [ ] Use `fcs_refactor_impact` for affected factory modules, then write failing tests against `debounceWith` with injected manual time.
- [ ] Test initial immediate value, first ready after initial pending, zero delay, negative delay, direct inputs at 0/50/90 ms admitting only at 190 ms for delay 100 ms, and no admission one tick before a deadline.
- [ ] Add the computed-source regression: read a memo through debounce, change its dependency twice before expiry, and prove the last change extends the deadline. Add a source check that resolves equal and a batch returning to its initial value; neither resets the deadline.
- [ ] Implement owned `Timed<'T>` using existing source-list/check-walk/pure-host patterns. Separate captured and published states; a value read tracks the source and returns only published state. Source capture runs at graph flush boundaries and immediately reconciles conditional dependencies.
- [ ] Implement `TimedOptions<'T>`, default options, equality comparisons, failure-origin preservation, pending cancellation, and recovery exactly as the spec describes. Add tests for comparer failure and upstream failure.
- [ ] Implement lazy deadline extension and a lazy reusable timer. Timer callbacks request a coalesced `Graph.Post` wake; the wake schedules graph execution. Do not mutate graph state from the timer thread.
- [ ] Test expiry queued before a new write, expiry inside a batch, a callback racing with disposal, dependency switching while unpublished, and callbacks from a worker thread dispatched to the owner. Use a controllable dispatcher and synchronization barriers, not time-based sleeps.
- [ ] Test reentrant downstream writes during admission. Snapshot/clear pending state before notification and prove the new candidate survives.
- [ ] Include conditional trace hook seams while writing this node; keep all new trace data under `RANVIER_TRACE`. Do not modify old hot paths to host timer state.
- [ ] Run focused tests red/green, then full .NET untraced tests and netstandard build before committing.

Example behavioral test sequence (use the suite's actual graph/factory helpers): construct with initial 0; set input 1 at time 0; advance to 50 and set 2; advance to 90 and set 3; assert output 0 at 189; advance to 190 and flush the controlled dispatcher; assert output 3 and one changed admission. Repeat with a memo as the source.

## Task 3: Add the three throttle modes

Files: edit `src/Ranvier/Timed.fs`, `src/Ranvier/Combinators.fs`, and `tests/Ranvier.Tests/Timed.fs`.

- [ ] Write failing leading-only tests: first eligible input admits, inputs in cooldown are discarded, and time advancement alone never replays a discarded value. Assert the counting clock created zero timers.
- [ ] Implement `throttleFirst`/`throttleFirstWith` with a timestamp-only admission gate.
- [ ] Write failing trailing-only tests: first changed input opens a fixed window; later inputs replace the candidate without sliding the deadline; idle periods create no additional callbacks.
- [ ] Implement `throttleLast`/`throttleLastWith` with one fixed deadline and one candidate slot.
- [ ] Write failing leading-plus-trailing tests for a solitary leading input, a burst with one trailing admission, continuous input across multiple windows, equality suppressing a trailing notification, and owner dispatch delayed beyond several windows.
- [ ] Implement `throttle`/`throttleWith`. Anchor cooldowns to actual admission time; do not emit catch-up bursts after owner stalls.
- [ ] Run the failure, pending, batching, disposal, and reentrancy matrix for every mode. Make the mode-specific assertions part of the shared .NET/Fable suite.
- [ ] Run complete .NET tests and the Fable suite before committing this slice.

## Task 4: Complete causal tracing and event folding

Files: edit `src/Ranvier/TraceEvents.fs`, `Trace.fs`, `TraceModel.fs`, `TraceApi.fs`, and conditional hooks in `Timed.fs`; edit `tests/Ranvier.Tests/Tracing.fs` and `TraceModelTests.fs`; add timed cases to `samples/trace-sample.fsx`, `tools/verify-trace.fsx`, and `bench/Ranvier.Counters/Reconcile.fs`.

- [ ] Write failing traced tests proving that the final admitted value points to its winning captured input, not the input that first opened the window. Include debounce extension and throttle replacement.
- [ ] Append the timed node kind and event kinds without renumbering existing entries. Record capture, arm/extend, suppression, cancellation, and publication. Capture executions count as runs; admissions do not fabricate extra runs or flights.
- [ ] Add `Trace.timing` with the state/query contract in the spec. Keep existing record shapes, `whyNot` cases, and `waitingOn` flight semantics intact.
- [ ] Fold captured versus published state independently. Attribute `Moved` and downstream causes to the winning capture; match live and historical snapshots before capture, during a window, after admission, after failure, and after disposal.
- [ ] Add schema 2 encoding/parsing for timed logs and schema 1 compatibility for existing logs. Keep non-timed dumps on schema 1. Normalize timer metadata to relative time.
- [ ] Replay identical manual-clock scripts five times and compare bytes. Include pending, equal admission, cancelled windows, conditional edges, and disposal.
- [ ] Extend counter reconciliation: actual capture runs, changed publications, observer notifications, edges, and flushes must agree with folded logs. Assert live edges equal folded edges after switches/disposal.
- [ ] Extend `tools/verify-trace.fsx` to inspect new methods for hook erasure using a hook-free build of the same feature, retain the existing merge-base comparison, and permit only the enumerated new public API additions. Do not regenerate a broad baseline.
- [ ] Run complete traced tests plus the trace verifier's non-counter gates before committing. Reserve the full hardware-counter run for the final gate.

## Task 5: Complete C# and portability coverage

Files: edit `src/Ranvier.CSharp/Reactive.fs`; add `tests/Ranvier.CSharp.Tests/TimedTests.cs`; edit `tests/Ranvier.AotSmoke/Program.fs` and shared Fable tests only where needed.

- [ ] Write C# tests for every factory, `Func<T>` capture, custom comparer, injected clock, read-only value tracking, failure propagation, and owner disposal.
- [ ] Add facade factories following the existing graph argument convention. Expose optional clock/comparer arguments; return the core `Timed<T>`. Avoid introducing an extra wrapper computation.
- [ ] Run library builds for net10.0/net8.0/netstandard2.1, C# tests, and shared Fable tests with tracing off and on. Check actual emitted JS for timer cancellation and zero-delay behavior.
- [ ] Add an AOT/trimming smoke use of debounce and throttle; require publish with the repo's warning-as-error policy and run the resulting executable on its supported host.
- [ ] Read the XML-doc skill before documenting new signatures, run its audit, and run the required comment-hygiene check once available.
- [ ] Compare semantic and packed public APIs against baseline. Review the precise additions and prove existing signatures remain unchanged before committing.

## Task 6: Measure and choose the timer policy

Files: add `bench/Ranvier.Benchmarks/Timed.fs` and its compile item; edit `bench/Ranvier.Counters/Workloads.fs`, `Scenarios.fs`, and `fable/Ranvier.Counters/Scenarios.fs` using existing registrations; add `docs/.ai/debounce-throttle-performance.md`.

- [ ] Build a benchmark-only alternative using reusable per-input rearm, sharing the exact admission semantics of the production lazy-extension implementation. Keep benchmark instrumentation out of production hot paths.
- [ ] Measure construction, changed capture, unchanged check, timer callback, graph-owner dispatch, and downstream admission separately. Use primitive values and preallocated delegates for allocation targets.
- [ ] Exercise sparse input, dense bursts, continuous input, sustained throttling, and 1/64/4096 timed nodes. Compare lazy extension versus rearm on .NET and Fable, with trace configurations kept equal.
- [ ] Report allocations, backend arms, callbacks, posts, admissions, throughput, and latency distribution. State runtime/version/environment and distinguish deterministic simulated deadlines from real timer latency.
- [ ] Run existing non-timed counters against the baseline. Require unchanged allocations; investigate reproducible retired-instruction changes around 5% or more, and retain the stricter merge-base IL check.
- [ ] Select the production strategy based on evidence. If changing the proposed lazy strategy, preserve the semantics and rerun affected timing/race tests. Record runtime-specific tradeoffs and expiry allocations candidly.
- [x] Run both allocated-bytes and elevated retired-instruction measurements. Completed for .NET and Fable/Node in the five-run `878f294` counter report; matched baseline regression acceptance is a separate check above.

## Task 7: Document behavior and run final gates

Files: edit `docs/content/guide/tracing.md` and the appropriate combinator guide/API docs; update the design status and performance note with actual results.

- [ ] Explain timing versus flight policy, initial immediate value, mode naming, pending/error behavior, batching, owner dispatch, and why expensive work belongs downstream. Show composing debounce before async work.
- [ ] Document trace schema compatibility, timed diagnostics, and the deterministic-input/callback-order boundary. State measured performance rather than a universal fastest claim.
- [ ] Run the full repo test/build workflow for all TFMs plus traced/untraced F# and C# tests. Run actual Fable traced/untraced suites, format verification, XML/comment checks, API compatibility, and AOT smoke.
- [ ] Run the full trace verifier including counters; report all partial gates separately if the host cannot supply hardware measurements. No acceptance claim until the required measurements have been obtained on a suitable host.
- [ ] Inspect diffs and public surface, confirm no unrelated changes, and request code review with emphasis on the five failure classes above.
- [ ] Record commands/results, unresolved limitations, and the selected timer strategy in the final feature report. Keep the branch available for review; do not merge or publish merely because the implementation is complete.

## Verification commands

Use these known entry points. The current CI uses direct commands because the build CLI's timing summary can fail when its output is redirected. AOT publish/run below belongs on the Linux CI host; local Windows smoke runs must target Windows instead.

```powershell
rtk proxy dotnet build src/Ranvier/Ranvier.fsproj -c Release
rtk proxy dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=false
rtk proxy dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=true
rtk proxy dotnet test tests/Ranvier.CSharp.Tests -c Release
rtk proxy pwsh -NoProfile -File tests/check-package-compatibility.ps1
rtk proxy dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release
rtk proxy pwsh -NoProfile -Command '$env:RanvierTrace = "true"; rtk proxy dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release'
rtk proxy node fable/Ranvier.Tests.Fable/Report.mjs
rtk proxy pwsh -NoProfile -Command '$timedPlanFiles = @(rtk proxy git ls-files "*.fs"); rtk proxy dotnet fantomas --check $timedPlanFiles'
rtk proxy dotnet fsi tools/verify-trace.fsx
rtk proxy pwsh -NoProfile -File counters.ps1 -Repeat 5
rtk git diff --check
```

Linux CI AOT gate:

```bash
rtk proxy dotnet publish tests/Ranvier.AotSmoke -c Release -r linux-x64 -o artifacts/aot
rtk proxy ./artifacts/aot/Ranvier.AotSmoke
```

Use clean/separate build output for traced versus untraced verification as the existing tooling does. A stale binary, focused test run, or Fable semantic projection is not a substitute for the complete corresponding gate.
