# Renderer lifetime proof

Date: 2026-10-08. Branch: `experiment/renderer-lifetime`, based on `ed53ab470857f5bc73e6402ad7907cccfbe9ddeb`. Implements the [focused lifetime plan](../superpowers/plans/2026-10-08-renderer-lifetime-proof.md), the first gate within framework Task 2.

## Result and boundaries

A renderer-local registry can deliver child-first attached cleanup over the current parent-first core using existing cleanup hooks. The tested entry points pass without changing a shipping library API or core disposal semantics. The prototype lives in `experiments/Ranvier.View.Foundation/Lifetime.fs`; only the playground test project compiles it. This is evidence for the adapter design, not a production renderer or a performance claim.

The registry marks its entire subtree inactive, executes callbacks in postorder, then releases DOM in a separate postorder pass. A shared drain coordinates nested disposal within one scope tree: an ancestor disposal requested by an independently disposing child joins the active drain, and its cleanup waits for that child's remaining callbacks. Newly queued cleanup completes before further release. Parent cleanup therefore still sees its child DOM attached in the cleanup-reentry regression. Cleanup, release and reporting exceptions are collected; repeated and tested reentrant disposal are harmless. Disposed children unlink from a retained parent's registry.

The browser fixture registers parent and child adapters with their corresponding `Api.onCleanup` hooks. An enclosing effect also registers the instance adapter directly. Explicit disposal drains the adapter before disposing its core owner. Failed setup explicitly drains both in its catch path and rethrows the original exception; it does not assume `createRoot` unwinds arbitrary renderer resources by itself.

## Observed traces and resource assertions

For explicit disposal, direct owner disposal and graph disposal, each mounted fixture produces exactly:

```text
child-clean-attached
parent-clean-attached
remove-child
remove-parent
```

Both cleanup callbacks assert actual parent-node identities. Child listener calls stop after disposal; retained nodes' write counts stop changing after owner disposal. Unrelated host content survives. Repeated disposal adds no trace entries.

Two mounts sharing one graph remain independent: disposing the first leaves the second attached, reactive and listening; graph disposal releases the second. For effect reruns, version 0's complete teardown precedes version 1 attachment; graph disposal later tears down version 1 exactly once. The previous instance's listener stays inert.

Failed setup produces the same teardown trace, removes acquired DOM, stops the acquired listener and observer, preserves the host sibling and rethrows the identical original exception. A throwing child cleanup, throwing child release and throwing reporter produce recorded errors `cleanup`, `release`, `reporter`; sibling cleanup/removal still completes, and reports are not retried.

Reentrant disposal adds no duplicate work. Registration during teardown raises `InvalidOperationException`. A retained parent has zero registered children after 1000 independently created/disposed children, on both .NET and Fable. This registry-count check does not establish heap collection.

The independent review found that the initial algorithm covered root-to-root reentry but failed child-to-live-ancestor reentry: parent removal preceded the child's remaining cleanup. Pure .NET, SageFs and jsdom regressions reproduced it. The shared-drain correction now yields `child-first`, `child-second`, `parent`, `remove-child`, `remove-parent`; the browser regression also asserts native attachment and repeated disposal. No other review findings remained.

Independent core roots do not have a renderer-relative disposal order: the graph ran the later-created fixture first in the failure case. Tests assert each root's exact descendant ordering without promising ordering between independent mounts.

## Test-first evidence and cross-runtime finding

The initial pure CLI run failed because `Lifetime.fs` did not exist; SageFs also rejected the missing prototype type. The candidate then passed all seven pure traces in SageFs before persistence, and the persisted files passed after reload and in the CLI.

Browser wiring tests ran with the core hook deliberately absent. Explicit disposal passed, while six core-driven integration cases failed with missing cleanup traces. Adding the cleanup hooks made these paths run. This guards against proving only the explicit mount path.

Fable's generated `invalidOp` did not create a catchable `InvalidOperationException` for this prototype. The browser rejection test stayed red until the registry explicitly constructed that exception. The same pure .NET tests remain green. No public signature changed.

## Fresh verification

- `rtk proxy dotnet fsi experiments/Ranvier.View.Foundation/LifetimeChecks.fsx`: 8/8 pure checks pass; also evaluated in SageFs.
- `rtk proxy npm --prefix examples/Fable.Ranvier.Playground test`: 56/56 Node/jsdom tests and 12/12 Chromium tests pass. The new Chromium test executes all ten lifetime cases.
- `rtk proxy dotnet build examples/Fable.Ranvier.Playground/tests/DomTests.fsproj --configuration Release`: passes, zero warnings/errors.
- Fresh fslangmcp project check of `DomTests.fsproj`: clean, zero warnings/errors. Earlier semantic queries followed a clean scoped check.
- `rtk proxy npm --prefix examples/Fable.Ranvier.Playground run build`: passes; application bundle remains 48.90 kB, gzip 14.42 kB. The experiment is test-only and is not included in that application bundle.
- `rtk proxy dotnet fsi build.fsx -- test --quick --configuration Release`: passes; F# 938 tests per net8.0/net10.0 target, C# 81 tests per target, AOT smoke passes. CLI help confirms `--quick` skips installs/restores, not tests.
- `rtk proxy dotnet build Ranvier.slnx --configuration Release`: still fails with the same five baseline Fable-core compile errors: missing `IcedTasks`, `cancellableTask`, `Expect.isNotNull`, `testSequenced`, and `Expect.hasLength`. Existing docs dependency and xUnit warnings remain. Whole-solution acceptance is not green; Fable-core runtime tests were not reached by that failed build.

Environment: Node v26.7.0, Fable 5.18.0, Chromium 153.0.8010.12, Vite 8.3.2; locked jsdom 30.1.1 and Playwright 1.63.0. These runs establish behavior, not comparative throughput. Existing npm audit findings were unchanged by locked dependency restoration.

## Allocation and scalability evidence

Inspected the generated Release `Lifetime.js`. A scope allocates an object and two arrays (children/callbacks), with one shared drain reference cell per root tree. Each outermost first disposal allocates postorder and error arrays; nested requests reuse the active postorder storage. Fable emits enumerator helpers for collection and callbacks, collection removal helpers for unlinking, and error-list conversion; the drain itself uses indexed loops. The registry imports no Ranvier graph/effect/scheduler implementation and creates no reactive machinery itself. The fixture's deliberately created observers are separate core resources.

Postorder storage is O(n) in the disposed subtree, with recursion proportional to depth. Unlinking uses linear search/removal. Disposing a wide retained parent currently performs repeated removal/array shifts and can cost O(n²) across siblings. These are known prototype costs to address or reject in the performance experiment, not acceptable evidence of an extremely performant production DSL. No heap allocation byte counts or wall-clock comparison were measured here.

The proof covers renderer callbacks registered through the registry, not arbitrary raw core callbacks, concurrent .NET disposal, hostile external DOM mutation, stack-safe extreme nesting, production range movement, async cancellation or queued DOM writes. Existing shipping tests continue to cover their current PoC behavior. Production scope integration must preserve this ordering while adding those capabilities explicitly.

## Next gate

Proceed to the representation/portability/performance experiment: compare equivalent instruction-tree and build-program views, prove element-specific modifier inference and escaped .NET HTML, then inspect and benchmark direct authoring versus small function/builder sugar probes. Include lifetime bookkeeping and the unlinking cost in the comparison. Framework Task 2 remains open until those proofs pass; the public `View` API remains unfrozen.
