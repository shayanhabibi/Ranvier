# Binding policies and scheduling proof — 2026-10-11

> Superseded by the [text/boundary repair](2026-10-11-text-boundary-repair.md). This document records the rejected per-binding outcome experiment. Its policies, extra roots and HTML snapshot selector have been removed.

Continues the DOM worktree from `79b9e96`. Foundation now has an additive binding runtime proof in `Binding.fs`, a portable commit queue in `Scheduling.fs`, and a separate browser `Microtask.fs`. Generated HTML bindings are not required for this work. Existing `Dom.fs`, `Backend.Bind`, view instruction types and public DSL signatures are unchanged.

## Selected contracts

The caller supplies a tracked reader returning `Reading<'T>`, a setter, destination liveness/cleanup capabilities, scheduling and every policy callback. Use a core source/memo's `TryValue` to expose pending and failed outcomes as data. A normal effect observes this reader: the split-effect action over `.Value` can skip pending transitions, so it is not used here. An arbitrary thrown reader exception becomes `Reading.Failed`; this proof does not infer pending from an exception thrown by `.Value`.

`Pending` and `Failed` return an optional replacement value. `None` explicitly selects retention; `Some` selects a fallback subject to the same binding equality as ready values. `Status` observes every reader execution, even when equality suppresses a DOM write. Source equality, explicit memo equality and binding equality are separate decisions. There is no automatic binding memo. The committed cache advances after a successful setter only. An always-false binding comparer explicitly selects always-write when upstream layers actually notify.

Status, fallback, comparer, setter and reporter callbacks are untracked. Failed readings are reported before selecting their fallback. Callback/setter failures are reported and rethrown into the current execution boundary; a throwing reporter produces an aggregate with both error messages. Synchronous effect failures remain isolated by core effect handling, so callers must observe the supplied reporter rather than expect every synchronous error to escape `start`. Queued commit failures are additionally exposed through `Queue.Errors`, representing the latest flush. Reporting does not roll back successful DOM writes.

Synchronous scheduling commits during the effect. A queued binding independently selects an immediate or scheduled first observation; synchronous scheduling has no deferred first write. An immediate pending fallback counts as the initial observation, so a later first ready value follows normal queued timing. A binding has one explicitly disposable root and one ordinary effect. Destination disposal cancels queued work and disposes the root. Liveness is checked again after callbacks; cleanup registration that disposes immediately rejects setup before reading.

Setter `Api.onCleanup` registrations belong to the current reader run. They clean up before the next reader execution, including pending retention or an equal result that skips the next setter. Retention preserves committed output, not these per-run resources. Resources that must survive retained output belong to the supplied destination/mount cleanup scope. This follows the current core effect lifecycle and is covered explicitly by tests.

## Queue behavior

Each queued binding reserves a reusable slot. Replacing or canceling a slot invalidates its version. Flush first settles pending graph work, then commits a fixed snapshot. New work is a later wave; graph invalidation must be processed before it can invalidate an already captured binding reading. A setter that explicitly flushes the graph can invalidate both its own committed-cache ticket and a sibling's stale queued write. Snapshot order follows enqueue order; it does not impose an ordering on the core's effect traversal.

Manual flush invalidates older posted tickets. Recursive queue flush throws `InvalidOperationException` in both .NET and Fable; `invalidOp` alone did not preserve that exception type in the generated JavaScript. Disposing the queue during a commit skips the remaining snapshot and makes outstanding posted callbacks inert.

`CollectAndContinue` records failures and attempts later entries. `FailFastCancel` records the first failure and cancels the remaining snapshot **slots**, including replacements enqueued for those slots during that wave. Independently enqueued slots not in the snapshot survive for the next wave. Skipped work is not implicitly retried; a later reader invalidation can enqueue it again. Neither policy is transactional. The supplied `post` callback must defer invocation; `Microtask.create` uses `queueMicrotask`. Tests also use explicit/manual posting to make wave boundaries observable.

## Evidence

The new checks exercise ready/pending/failed recovery and initial pending/error fallbacks in both scheduling modes; committed equality and always-write; source/memo/binding suppression; actual core async pending status; failed-setter recovery; policy callback tracking; callback-triggered disposal; per-run cleanup; throwing reporting; batch flush; manual tickets; wave replacement; fail-fast slot cancellation; reentrant graph flush; and mid-snapshot disposal. Native tests register bindings through `BuildContext` and verify mounted DOM updates and cancellation after disposal. Node and Chromium also exercise real microtasks.

A failing recursive-flush check guided the exception identity correction under Fable. The registration-time disposal guard was added before its test was executed; it is contract coverage rather than a recorded red/green regression. The reentrant test initially assumed core traversal order and was corrected to check next-wave membership and absence of stale writes. Most additional matrix checks were written as contract coverage, not recorded failing regressions.

Final verification:

- `rtk npm --prefix examples/Fable.Ranvier.Playground test`: complete suites passed, 173 Node cases (including 43 new binding cases) and 17 Chromium tests. Chromium executes the 42 synchronous binding cases in the range harness and a separate real microtask check.
- Release `DomTests.fsproj` build: zero warnings/errors. Fresh scoped FCS check: zero errors/warnings.
- SageFs: all 40 pure binding cases passed on .NET after reloading persisted source. Its existing worktree session was refreshed; the user's daemon was not restarted. A successful response was followed by the MCP client's existing SSE `BrokenResourceError` during connection teardown.
- `rtk proxy dotnet fsi build.fsx -- test --quick --configuration Release`: 938 F# and 81 C# tests passed on each of net8.0/net10.0, plus AOT smoke. This canonical runner covers existing core tests separately from the experimental binding module.
- Playground production build passed, 71.70 kB JavaScript / 20.16 kB gzip. The application has not adopted the new binding proof, so these sizes do not measure its cost.
- Fresh review found no important defects under the documented contracts. Its cleanup, fail-fast replacement and reentrant-flush coverage suggestions are included. Staged formatting/comment-hygiene checks run during commit; no XML documentation comments were added.

The whole-solution Release build still reports the same five baseline `Ranvier.Tests.Fable` errors (`IcedTasks`, `cancellableTask`, `Expect.isNotNull`, `testSequenced`, `Expect.hasLength`) and existing docs dependency warnings. Whole-solution acceptance remains open.

## Remaining gates

This is a runtime proof for framework Task 5, not its production completion or a performance claim. Next, carry explicit binding policies through the provisional view representation/backend and migrate one realistic consumer. Decide how initial setup failure should affect mount construction, how status notification should compose with component boundaries, and whether long-lived setter resources need a distinct committed-write owner. Extend callback/error/reentrancy combinations before freezing that API.

Measure the direct synchronous path, shared-graph fan-out, queue throughput, retained allocations and bundle contribution against equivalent legacy bindings. Current costs include the binding root/effect, closures, a committed cache and version checks; queued bindings additionally retain a slot, and each flush materializes a snapshot. These are identified costs, not measured allocation counts. Do not assume the absence of an automatic memo proves the required performance. Optional Feliz/Oxpecker sugar must preserve the measured resource and update-path budgets. Keep scheduling and retention selections explicit.

Broad DOM generation remains a separate catalogue prerequisite; no Xantham dependency was upgraded in this proof.
