# Fable.Ranvier framework baseline

Date: 2026-10-08. Task 1 of the [framework plan](../superpowers/plans/2026-10-06-fable-ranvier-framework.md). Starting checkpoint: `174f07649f38b1ff7a3ebc60c6c3fb9f290d1cb0`.

## Baseline repair

The checkpoint contains the move of browser globals from `Dom` to `Global`. The library type-checks, but the playground test and app still referenced `Dom.document`. The initial complete playground command failed at `DomChecks.fs:272`; after correcting that reference, its browser compilation exposed the same mismatch at `App.fs:31`. Both consumers and the library README example now use `Global.document`. No runtime behavior or public signature changed in this repair.

SageFs `check_fsharp_code` reproduced the missing `Dom.document` diagnostic, then accepted the corresponding `Global.document` snippet. The existing fragment-root regression test supplied the failing compiler feedback; its runtime assertions remain unchanged.

The first Playwright startup then failed because the installed `node_modules` lacked `concurrently/dist/bin/index.js`. Restoring the existing lockfile with `npm ci` repaired the local installation. No package manifest or lockfile change was made.

## Fresh verification

- `rtk proxy dotnet build src/Fable.Ranvier/Fable.Ranvier.fsproj --configuration Release`: passed, zero warnings/errors.
- `rtk proxy dotnet fsi build.fsx -- test --quick --configuration Release`: passed. F# suite: 938 tests on each of net8.0 and net10.0. C# suite: 81 tests on each target. The runner also completed its AOT smoke stage. Existing xUnit analyzer warnings remain.
- `rtk proxy npm --prefix examples/Fable.Ranvier.Playground test`: passed after the repair and dependency restore. 46 Node/jsdom tests and 11 Chromium tests, including the Fable watch/Vite replacement test.
- `rtk proxy npm --prefix examples/Fable.Ranvier.Playground run build`: passed. JavaScript output: 48.90 kB, 14.42 kB gzip. This is a build artifact measurement, not a comparative performance claim.
- fslangmcp scoped checks of `Ranvier.fsproj` and `Fable.Ranvier.fsproj`: clean. Semantic symbol queries were made after these clean scoped checks.
- fslangmcp whole-solution `check`: 14 projects swept; six errors in `Ranvier.Tests.Fable`. The rest had no error diagnostics. No solution-wide absence-of-usages conclusion was taken from this failing check.
- `rtk proxy dotnet build Ranvier.slnx --configuration Release`: failed with five compiler errors in `Ranvier.Tests.Fable`, plus existing docs dependency and xUnit warnings.

The solution-level failures predate the repair. Missing Fable test support includes `IcedTasks`, `cancellableTask`, `Expect.isNotNull`, `testSequenced` and `Expect.hasLength`. The FCS sweep additionally reports `TestCase.replaceTestCode` in linked `Support.fs`. These failures prevent a clean whole-solution/Fable-core acceptance claim; the Fable core runtime suites were not reached.

`npm audit --json` reports two critical package entries: `shell-quote` and its dependent `concurrently`, associated with [GHSA-pqg4-j6r4-53mv](https://github.com/advisories/GHSA-pqg4-j6r4-53mv). These are existing development-tool dependencies; no dependency upgrade or forced audit fix was applied during this baseline task.

## Current API and migration points

The [public API snapshot](2026-10-08-fable-ranvier-api.json) was captured using `fcs_public_api`. It is project-scoped and does not assert external-consumer compatibility.

- Browser globals are `Global.document`, `Global.window` and `Global.globalThis`; `Global.isBrowser` is a lazy value.
- `Dom.Modifier` is currently `HTMLElement -> unit`. `Dom.element` consumes existing `Node` children and returns an immediately created `HTMLElement`.
- `Mount.mount` accepts a caller-owned graph, host node and factory returning one persistent node; its result is `IDisposable`. `mountWith` additionally accepts `DomOptions` and returns `DomMount` with `Flush`, `Errors` and `Dispose`.
- Fragment roots are rejected; reusable definitions and multi-node mounted ranges are not implemented.
- Readers are tracked and writes untracked. Static argument evaluation remains static. Events restore creation context and batch by the selected PoC options.
- Pending/failed bindings retain previous successful output; they do not create loading/error views. Initial and deferred setter errors follow different reporting paths.
- Scheduling uses a per-mount queue and internal ambient `DomContext`. The new framework must make policy selection explicit while documenting any compatibility adapter.
- Current setters are arbitrary browser callbacks. A portable view needs explicit property metadata/server interpretation rather than serializing these callbacks.

## Ownership audit

The core public owner interface and split-effect definition were inspected through fslangmcp. Mount construction, failure cleanup and queue ownership were compared with the PoC documentation. The [core contract](../content/concepts/contracts.md#teardown-order) specifies parent cleanup before child disposal.

A SageFs pure probe created a parent root, registered a parent cleanup, created a child root with a child cleanup, then disposed the parent. Its observed trace was `parent -> child`. This confirms the documented order and the mismatch with the proposed renderer's child-first attached-DOM contract. It does not prove a renderer adapter; that remains Task 2's gate.

## Tooling and cleanup

The SageFs daemon was started by the user. Connected to it through Streamable HTTP; its reported version was `0.6.906+09285e9`. Created project session `9ef3aa4c` only after the library build completed. It loaded the library and core and reached Ready. It reported degraded hot-reload health because its loaded assemblies were built with Release optimizations; only checking and existing-core evaluation were used, not hot-reload patch equivalence.

The temporary MCP client's initial fslangmcp startup against `Ranvier.slnx` caused FSAC's `The element <Solution> is unrecognized, or not supported in this context` diagnostic. Starting against the explicit library project worked; whole-solution FCS checks were then requested through that client with `projectPath` set to the solution.

SageFs MCP `stop_session` from a subsequent connection rejected the request with `the session is owned by another bound MCP connection`. The dashboard's observed session-specific stop operation was used for the session this task created. A subsequent `list_sessions` returned `No active sessions.` The user's daemon was left running.

## Next step

The user selected explicit typed functions for the view DSL. The [draft DSL design](../superpowers/specs/2026-10-08-ranvier-view-dsl-design.md) proposes typed modifier inputs and opaque reusable `View` outputs. Review that shape, then narrow the lifecycle/portability experiment around a handwritten subset. Xantham generation remains follow-on work and does not block those proofs.
