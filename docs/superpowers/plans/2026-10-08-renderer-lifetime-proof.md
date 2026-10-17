# Renderer lifetime proof implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** prove a renderer-local child-first cleanup adapter over the current parent-first Ranvier core, before selecting the performant view representation.

**Architecture:** an experimental scope registry owns renderer callbacks and DOM release operations. Register its disposal with the existing core cleanup hook; drain descendant callbacks before parent callbacks, then release DOM. All scopes in a drained subtree become inactive before callbacks run. A shared active drain coordinates overlapping child-to-ancestor disposal requests so reentry cannot detach unfinished cleanup.

**Tech Stack:** F#, Ranvier, Fable Release output, existing Node/jsdom and Chromium harnesses; SageFs for pure logic/type probes, CLI for acceptance.

**Spec:** [approved DSL direction](../specs/2026-10-08-ranvier-view-dsl-design.md), [framework brief C03](../specs/2026-10-06-fable-ranvier-framework-brief.md), and Task 2 of the [framework plan](2026-10-06-fable-ranvier-framework.md).

## Global constraints

- Dispose descendants before parent cleanup, with their DOM still attached during cleanup, then remove the owned range.
- Attempt all cleanup even when one callback fails, aggregate failures through an explicit reporting contract, and make repeated disposal harmless.
- Other operations through a disposed scope report a lifecycle error.
- Do not silently change core semantics.
- Static content creates no reactive observers, memos or scheduler entries.
- Neither sugar nor typed helper dispatch may introduce additional mounted owners, reactive computations, listener registrations or scheduling waves.
- Follow AGENTS.md: prefix shell commands with `rtk`; use a clean fslangmcp check before semantic queries; run refactor impact before changing existing public signatures; read SageFs skill before F# changes; use the configured comment hygiene check.
- Leave the user's SageFs daemon running. Stop sessions created for this work.
- Keep prototype types out of the published library. No production API, portable representation or sugar library is frozen by this experiment.

## Review focus

1. Graph disposal and effect reruns must invoke the same adapter as explicit disposal.
2. Cleanup and reporting failures must not skip siblings or DOM removal.
3. Reentrant disposal and callback attempts to register new work must not double-run or extend teardown.
4. Partial setup failure must release acquired resources while preserving unrelated host content.
5. Independently disposed children must unlink from the registry so a long-lived parent does not retain disposed subtrees.

## Files and interfaces

- Create `experiments/Ranvier.View.Foundation/Lifetime.fs`: experimental scope registry, no browser types or reactive computations.
- Create `experiments/Ranvier.View.Foundation/LifetimeChecks.fsx`: pure .NET executable assertions over that registry and the built Ranvier assembly.
- Create `examples/Fable.Ranvier.Playground/tests/LifetimeChecks.fs`: browser/core integration checks exporting `cases : (string * (unit -> unit)) array`.
- Create `examples/Fable.Ranvier.Playground/tests/lifetime.test.mjs`: jsdom runner matching `dom.test.mjs`.
- Create `examples/Fable.Ranvier.Playground/tests/lifetime.spec.mjs` and `lifetime.html`: Chromium runner matching the existing scheduled browser harness.
- Modify `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`: link the experimental registry before its check module.
- Modify `examples/Fable.Ranvier.Playground/package.json`: include the new Node runner in `test:dom`; retain existing browser/build commands.
- Create `docs/research/2026-10-08-renderer-lifetime-proof.md`: observed traces, resource counts, results and decision.

Prototype interface, not an exported compatibility promise:

```fsharp
namespace Ranvier.View.Foundation

type Scope(report: exn list -> unit, release: unit -> unit) =
    member Child: release:(unit -> unit) -> Scope
    member OnCleanup: callback:(unit -> unit) -> unit
    member Dispose: unit -> unit
    member IsDisposed: bool
    member ChildCount: int
    member Failures: exn list
```

`Child` and `OnCleanup` reject an inactive scope. `ChildCount` counts only live registered children. `Failures` records teardown failures on the scope whose `Dispose` initiated the drain, including reporter failure. Callback order within one scope is registration order for this experiment; sibling order is creation order. These choices do not settle production policy.

## Task 1: Prove the portable teardown registry

**Consumes:** callbacks and release functions only; no Ranvier owner or DOM dependency.
**Produces:** the experimental `Scope` interface above and executable pure traces.

- [x] Read the SageFs skill and create an isolated implementation workspace using the worktree skill. Build the target once, create a SageFs project session and wait until Ready. Do not modify the running daemon.
- [x] Write the pure assertions in `LifetimeChecks.fsx`. The first run must fail because the registry does not exist. Use `#load "Lifetime.fs"`, then include these tests:

```fsharp
open Ranvier.View.Foundation

let equal expected actual =
    if actual <> expected then failwithf "Expected %A; got %A" expected actual

let trace = ResizeArray<string>()
let errors = ResizeArray<exn>()
let parent = Scope(List.iter errors.Add, fun () -> trace.Add "remove-parent")
let child = parent.Child(fun () -> trace.Add "remove-child")
parent.OnCleanup(fun () -> trace.Add "parent")
child.OnCleanup(fun () -> trace.Add "child")
child.OnCleanup(fun () -> failwith "child failure")
child.OnCleanup(fun () -> parent.Dispose(); trace.Add "after-reentry")
parent.Dispose()
parent.Dispose()
child.Dispose()
equal [ "child"; "after-reentry"; "parent"; "remove-child"; "remove-parent" ]
      (List.ofSeq trace)
equal 1 errors.Count
equal 0 parent.ChildCount

let detachedParent = Scope(ignore, ignore)
for _ in 1..1000 do
    let detachedChild = detachedParent.Child(ignore)
    detachedChild.Dispose()
equal 0 detachedParent.ChildCount
detachedParent.Dispose()

let mutable rejected = false
try parent.OnCleanup(ignore)
with :? System.InvalidOperationException -> rejected <- true
equal true rejected

let releases = ResizeArray<string>()
let brokenReporter = Scope((fun _ -> failwith "reporter"), fun () -> releases.Add "released")
brokenReporter.OnCleanup(fun () -> failwith "cleanup")
brokenReporter.Dispose()
equal [ "released" ] (List.ofSeq releases)
equal 2 brokenReporter.Failures.Length
```

Run: `rtk proxy dotnet fsi experiments/Ranvier.View.Foundation/LifetimeChecks.fsx`.

- [x] Implement the registry using this two-phase algorithm. Compiler-check the recursive type in SageFs before saving. This is the candidate to test, not a claim that its signatures have already compiled:

```fsharp
namespace Ranvier.View.Foundation

type Scope private (report: exn list -> unit, release: unit -> unit, parent: Scope option, drain: ResizeArray<Scope> option ref) =
    let children = ResizeArray<Scope>()
    let callbacks = ResizeArray<unit -> unit>()
    let mutable disposed = false
    let mutable failures: exn list = []

    new(report, release) = Scope (report, release, None, ref None)

    member private _.RemoveChild(child) =
        children.Remove child |> ignore

    member private _.EnsureLive() =
        if disposed then
            raise (System.InvalidOperationException "Renderer scope is disposed")

    member this.Child(childRelease: unit -> unit) =
        this.EnsureLive ()
        let child = Scope (report, childRelease, Some this, drain)
        children.Add child
        child

    member this.OnCleanup(callback: unit -> unit) =
        this.EnsureLive ()
        callbacks.Add callback

    member private this.Collect(order: ResizeArray<Scope>) =
        if not disposed then
            disposed <- true

            for child in children do
                child.Collect order

            order.Add this

    member private _.Clean(errors: ResizeArray<exn>) =
        for callback in callbacks do
            try
                callback ()
            with error ->
                errors.Add error

        callbacks.Clear ()

    member private this.Release(errors: ResizeArray<exn>) =
        try
            release ()
        with error ->
            errors.Add error

        children.Clear ()

        match parent with
        | Some owner -> owner.RemoveChild this
        | None -> ()

    member this.Dispose() =
        if not disposed then
            match drain.Value with
            | Some order -> this.Collect order
            | None ->
                let order = ResizeArray<Scope>()
                let errors = ResizeArray<exn>()
                drain.Value <- Some order

                try
                    this.Collect order
                    let mutable cleaned = 0
                    let mutable released = 0

                    while released < order.Count do
                        while cleaned < order.Count do
                            order[cleaned].Clean errors
                            cleaned <- cleaned + 1

                        order[released].Release errors
                        released <- released + 1

                    failures <- List.ofSeq errors
                finally
                    drain.Value <- None

                if errors.Count > 0 then
                    try
                        report failures
                    with error ->
                        failures <- failures @ [ error ]

    member _.IsDisposed = disposed
    member _.ChildCount = children.Count
    member _.Failures = failures
```

- [x] Add these assertions for throwing release, three-level postorder, registration during cleanup and child-only disposal. Run the complete pure file in SageFs and the CLI.

```fsharp
let nestedTrace = ResizeArray<string>()
let nested = Scope(ignore, fun () -> nestedTrace.Add "remove-parent")
let middle = nested.Child(fun () -> nestedTrace.Add "remove-middle")
let leaf = middle.Child(fun () -> nestedTrace.Add "remove-leaf"; failwith "release")
leaf.OnCleanup(fun () -> nestedTrace.Add "leaf")
middle.OnCleanup(fun () -> nestedTrace.Add "middle")
nested.OnCleanup(fun () ->
    nestedTrace.Add "parent"
    let mutable registrationRejected = false
    try nested.Child(ignore) |> ignore
    with :? System.InvalidOperationException -> registrationRejected <- true
    equal true registrationRejected)
nested.Dispose()
equal [ "leaf"; "middle"; "parent"; "remove-leaf"; "remove-middle"; "remove-parent" ]
      (List.ofSeq nestedTrace)
equal 1 nested.Failures.Length

let liveParent = Scope(ignore, ignore)
let removedChild = liveParent.Child(ignore)
removedChild.Dispose()
equal false liveParent.IsDisposed
equal 0 liveParent.ChildCount
let laterChild = liveParent.Child(ignore)
equal false laterChild.IsDisposed
liveParent.Dispose()
equal true laterChild.IsDisposed
```
- [x] Add the registry as a linked file in `DomTests.fsproj` before browser tests; run Release build/compilation to detect differences from the REPL. Do not optimize the two-phase allocation strategy yet; record its O(n) teardown storage as a candidate cost for the next performance experiment.
- [x] Commit only the prototype, pure checks and compile registration: `rtk git commit -m "test: prototype renderer child-first lifetime registry"` after staging the named files and passing the relevant checks.

## Task 2: Exercise every core/browser teardown entry point

**Consumes:** `Scope`, `Ranvier.Api.createRoot`, `onCleanup`, `createEffect`, and caller-owned `Graph`.
**Produces:** passing browser integration traces or a recorded blocking incompatibility. Existing shipping APIs remain unchanged.

- [x] Register the Node runner as `lifetime.test.mjs`:

```javascript
import test, { after } from "node:test";
import { JSDOM } from "jsdom";
const { cases } = await import("../generated-tests/LifetimeChecks.js");
const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
after(() => {
  dom.window.close();
  delete globalThis.window;
  delete globalThis.document;
});
for (const [name, check] of cases) test(name, () => check());
```

Set `test:dom` to `npm run compile:tests && node --test tests/dom.test.mjs tests/scheduled.test.mjs tests/lifetime.test.mjs`. Add these compile items after the existing scheduled checks, with the registry before its consumers:

```xml
<Compile Include="../../../experiments/Ranvier.View.Foundation/Lifetime.fs" Link="Lifetime.fs" />
<Compile Include="LifetimeChecks.fs" />
```

- [x] Create `lifetime.html`:

```html
<!doctype html>
<html lang="en"><head><meta charset="UTF-8"><title>Lifetime checks</title></head>
<body><script type="module">
import { cases } from "../generated-tests/LifetimeChecks.js";
window.lifetimeCases = cases;
</script></body></html>
```

Create `lifetime.spec.mjs`; the existing Chromium configuration discovers it:

```javascript
import { test, expect } from "@playwright/test";
test("lifetime ordering across core entry points", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/lifetime.html");
  await page.waitForFunction(() => window.lifetimeCases);
  const results = await page.evaluate(() => window.lifetimeCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});
```
- [x] Write a browser fixture with an attached parent and child element plus an unrelated host sibling. Its core integration uses this shape:

```fsharp
let owner =
    graph.Run(fun () ->
        Ranvier.Api.createRoot(fun owner ->
            Ranvier.Api.onCleanup scope.Dispose
            owner))
```

Register renderer callbacks through `scope.OnCleanup`, not raw core callbacks. Child callbacks assert both child and parent have their expected parents. Parent cleanup asserts the child remains attached. Release callbacks remove only the fixture's owned nodes. For explicit disposal, call `scope.Dispose()` before `owner.Dispose()`; hook reentry must be harmless.

- [x] Run the fixture independently for explicit disposal, direct `owner.Dispose()`, and `graph.Dispose()`. Exact successful trace: `child-clean-attached`, `parent-clean-attached`, `remove-child`, `remove-parent`. Assert the unrelated sibling survives and a second disposal adds no trace entries.
- [x] Add a second independently owned fixture sharing one graph. Dispose the first explicitly: the second remains attached and reactive. Dispose the graph: only the second contributes new cleanup/release trace entries.
- [x] Add an effect-rerun fixture with a signal read in `Api.createEffect`. Each run creates its own adapter, registers `Api.onCleanup scope.Dispose` directly in the effect scope, and inserts one parent/child pair. Change the signal synchronously: the old pair must produce the exact trace before the new pair is attached. Graph disposal cleans the latest pair once. Use fslangmcp to resolve the current signal-construction signature before writing the fixture; no core signature changes are authorized by this plan.
- [x] Add failed setup: create the adapter and core owner, insert the parent, acquire a child listener, then throw before setup completes. The mount-side `try/with` must call the adapter's disposal and owner disposal before rethrowing the original error. Assert the child cleanup runs, the listener stops firing, owned nodes disappear and unrelated host content survives. Do not assume `createRoot` alone supplies this unwind.
- [x] Add cleanup/release/reporting failure fixtures. Capture reported errors explicitly. All remaining callbacks and releases must run, DOM must disappear, and another disposal must neither retry failures nor duplicate reports.
- [x] Add callback reentry and rejected registration fixtures. Attempt sibling/parent disposal during cleanup; all callbacks still run once while attached. The registry is inactive throughout teardown. Check `ChildCount = 0` after 1000 independently created/disposed children of a retained parent.
- [x] Run full `test:dom`, full `test:browser`, the production playground build and the pure CLI checks. Inspect Release JS to confirm the adapter introduces no observers, memos or scheduler entries. Record the known whole-solution failures separately rather than calling a scoped build a full acceptance gate.

```powershell
rtk proxy dotnet fsi experiments/Ranvier.View.Foundation/LifetimeChecks.fsx
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run test:dom
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run test:browser
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run build
rtk proxy dotnet build Ranvier.slnx --configuration Release
```

- [x] Write the evidence report with each entry point's actual trace and result, callback/error counts, Fable/browser versions, and the two-phase registry's allocation costs. If an entry point bypasses the adapter, stop representation selection and identify the exact failing trace; evaluate a core extension only after `fcs_refactor_impact` and a separate design review.
- [x] Commit the integration checks, runner changes and report: `rtk git commit -m "test: prove renderer lifetime ordering across disposal paths"` after staging the named files and passing the relevant checks.

## Exit and next plan

Execution completed on 2026-10-08. Independent review found an ancestor-reentry defect in the initial candidate; pure and browser regressions reproduced it, and the shared-drain correction passed 8 pure checks, 56 Node checks and 12 Chromium tests. See the [evidence report](../../research/2026-10-08-renderer-lifetime-proof.md). Candidate code above reflects the verified correction.

This plan covers only the lifetime gate within framework Task 2. Do not mark all of Task 2 complete. A passing adapter permits the next focused plan: compare instruction-tree versus build-program representations, prove typed modifiers and escaped .NET output, then measure direct authoring and tiny Oxpecker-style/Feliz-style sugar probes under identical policies. Representation selection must include lifetime bookkeeping costs from this proof.

Plan self-review: C03 and the five review-focus conditions have explicit tasks; portability and performance selection are deliberately deferred to the dependent experiment. Prototype signatures and F# snippets still require compiler proof. No production performance claim follows from this plan.
