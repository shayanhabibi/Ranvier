# Fable.Ranvier DOM PoC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task if native execution is chosen, or superpowers:subagent-driven-development if the user chooses delegation. Steps use checkbox syntax for tracking.

**Goal:** Build a small F# DOM library and a runnable Node/Vite playground proving targeted reactive updates and mount cleanup.

**Architecture:** Xantham DOM nodes are created once by ordinary F# functions. Ranvier's existing `createEffectOn` tracks binding readers and performs DOM mutations in its untracked action. A mount supplies the graph and a disposable root owner; a separate playground compiles to JavaScript with Fable.

**Tech Stack:** .NET 10, Fable 5.18.0 (repository manifest), Xantham.Fable.Core.TS 0.1.0, Node 26.7.0 (installed), Vite 8.3.2, jsdom 30.1.1, concurrently 10.0.5, Playwright Test 1.63.0.

**Spec:** [Approved design](../specs/2026-10-02-fable-ranvier-dom-poc-design.md).

## Global Constraints

- Keep Xantham's generated DOM types and the local Ranvier reference; do not add Solid or a second scheduler.
- Preserve Ranvier scheduling, pending/error behaviour and graph ownership.
- Limit this PoC to HTML, static structure, text, attributes, properties, events and mounting.
- Preserve existing user changes, including `Ranvier.slnx` and the untracked `src/Fable.Ranvier` scaffold.
- Use fslangmcp for F# semantics; check before find and impact analysis before existing public signature changes.
- Use SageFs/Fable.SageFs only when they shorten a concrete experiment, as requested by the user.
- Do not restart or reinstall the user's SageFs daemon without permission.
- Prefix shell commands with `rtk`; use `rtk proxy` for commands without a supported filter.
- Avoid new source comments for this small implementation. If XML docs or source-comment review becomes necessary, follow AGENTS.md's skills/plugin instructions first.
- No SSR, hydration, compiler/JSX, SVG, keyed lists, dynamic subtree replacement, delegated events or new async DOM boundaries.

## Review Focus

- Import before DOM globals exist: no eager access to `document` at module initialization (Task 1).
- A factory creates bindings and a listener, then throws: the host stays unchanged and retained nodes are inert (Task 2).
- A host has unrelated children or two independent mounts: disposal removes only the owned root (Task 2).
- The root is manually detached or moved before disposal: cleanup remains safe and handlers stop working (Task 2).
- An input event writes back its current text: retain node identity, focus and caret; test the property setter in a real browser (Task 3).

## Preflight evidence

On 2026-10-02, fslangmcp's fresh workspace check reported zero errors/warnings across 13 projects.
Semantic outline of `src/Fable.Ranvier/Program.fs` found module `Fable.Ranvier.Dom`, `window`,
`document`, and `createElement : string -> HTMLElement`. Preserve these public names/signatures
when moving the file. Impact analysis found only the declaration of `createElement` in this project;
that is a project-scoped result, not proof that external consumers do not exist.

Verified core definitions:

```fsharp
createEffectOn : (unit -> 'T) -> ('T -> unit) -> unit
createRoot : (Owner -> 'T) -> 'T
runWithOwner : Owner -> (unit -> 'T) -> 'T
onCleanup : (unit -> unit) -> unit
```

`createEffectOn` runs its action untracked only after a successful changed result; failed/pending
reads leave the last action in place. Do not advertise automatic DOM loading/error views.

The installed language server is accessible through a temporary MCP stdio client even though its
tools are not exposed directly in this chat. Reuse that route, not textual F# searches. For SageFs,
observe the existing daemon/session state first. A browser DOM experiment belongs in the Fable
watch loop unless Fable.SageFs is available and cheaper. Do not install it merely to satisfy a checkbox.

## File map

- `src/Fable.Ranvier/Fable.Ranvier.fsproj`: reusable library and explicit compile order.
- `src/Fable.Ranvier/Dom.fs`: creation, binding and event modifiers; preserve existing globals/helper.
- `src/Fable.Ranvier/Mount.fs`: owner lifecycle and mounting.
- `src/Fable.Ranvier/README.md`: experimental API, ownership and limits.
- `examples/Fable.Ranvier.Playground/Playground.fsproj`: browser entry.
- `examples/Fable.Ranvier.Playground/App.fs`: demo state, mount factory and external controls.
- `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`: compiled F# DOM checks.
- `examples/Fable.Ranvier.Playground/tests/DomChecks.fs`: behavioural assertions, no framework-dependent .NET browser execution.
- `examples/Fable.Ranvier.Playground/tests/dom.test.mjs`: jsdom initialization and Node test runner.
- `examples/Fable.Ranvier.Playground/tests/playground.spec.mjs`: real Chromium interaction checks.
- `examples/Fable.Ranvier.Playground/package.json`, `package-lock.json`: private Node project and scripts.
- `examples/Fable.Ranvier.Playground/index.html`, `main.js`, `style.css`: browser shell, compiled entry import and styling.
- `examples/Fable.Ranvier.Playground/playwright.config.mjs`: Vite server and Chromium configuration.
- `examples/Fable.Ranvier.Playground/.gitignore`, `README.md`: generated-file exclusions and commands.

### Task 1: DOM construction and reactive bindings, with an executable test loop

**Files:** Library project, `Dom.fs`, test project, `DomChecks.fs`, `dom.test.mjs`, Node manifest/lockfile and playground `.gitignore` from the file map.

**Interfaces:**

```fsharp
type Modifier = HTMLElement -> unit
createElement : string -> HTMLElement
element : string -> Modifier list -> Node list -> HTMLElement
text : string -> Node
reactiveText : (unit -> string) -> Node
attribute : string -> string -> Modifier
bindAttribute : string -> (unit -> string option) -> Modifier
property : (HTMLElement -> 'T -> unit) -> 'T -> Modifier
bindProperty : (unit -> 'T) -> (HTMLElement -> 'T -> unit) -> Modifier
on : string -> (Event -> unit) -> Modifier
```

`element` creates once, applies modifiers and appends children. A `Modifier` is applied only inside
the intended owner scope when it creates bindings or registers cleanup. Property setters are
typed callbacks; the input setter compares current `value` before assigning. Do not serialize
properties into attributes.

- [x] **Step 1: Establish the Node DOM test harness and write failing construction/binding tests.**

Use a library-output `tests/DomTests.fsproj` referencing
`../../../src/Fable.Ranvier/Fable.Ranvier.fsproj`. Compile `DomChecks.fs` only. The Node runner
must import the compiled module before installing globals to exercise import safety:

```javascript
import assert from "node:assert/strict";
import test from "node:test";
import { JSDOM } from "jsdom";
const checks = await import("../generated-tests/DomChecks.js");
const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
test("DOM construction and bindings", () => {
  assert.deepEqual(checks.constructionAndBindings(), []);
});
```

F# exported checks collect assertion labels rather than hiding failures:

```fsharp
let constructionAndBindings () =
    let failures = ResizeArray<string>()
    let check label condition = if not condition then failures.Add label
    use graph = new Graph()
    graph.Run(fun () ->
        let value = createSignal "first"
        let child = Dom.reactiveText (fun () -> value.Value)
        let root = Dom.element "div" [Dom.attribute "id" "sample"] [child]
        check "initial text" (root.textContent = Some "first")
        value.Value <- "second"
        check "updated text" (root.textContent = Some "second")
        check "same text node" (obj.ReferenceEquals(root.firstChild.Value, child)))
    failures.ToArray()
```

Resolve Xantham's precise nullable member types using `fcs_nuget_members`/snippet checking rather
than assuming all text properties are options. The intent of each assertion stays unchanged if
the generated nullable shape needs different syntax. Add assertions for nested static structure,
attribute removal, memo-derived updates, equality cutoff, an unrelated signal write, DOM property
versus attribute state, and a setter reading another signal without adding that signal to tracking.

- [x] **Step 2: Run the new tests and capture the expected missing-API failure.**

From the playground directory, use `rtk proxy npm install`, then `rtk proxy npm run test:dom`.
The script compiles tests before `node --test tests/dom.test.mjs`. Initial failure must be an
unimplemented new API, not a broken relative reference or absent DOM global.

- [x] **Step 3: Implement creation and split bindings; establish event cleanup.**

Move the scaffold's module into `Dom.fs` and set `OutputType` to `Library`. Preserve `window`,
`document` and `createElement` signatures. Use `[<Global("document")>]` for a global binding without
evaluating `window.document` at import time. Add new functions to the same `Fable.Ranvier.Dom` module.

Core binding shape:

```fsharp
let bindProperty read write : Modifier =
    fun element -> createEffectOn read (write element)

let bindAttribute name read : Modifier =
    fun element ->
        createEffectOn read (function
            | Some value -> element.setAttribute(name, value)
            | None -> element.removeAttribute(name))
```

For events, capture `Graph.Current` and its current owner at registration time. Wrap the user
handler in graph/owner activation and `untrack`; register cleanup outside a binding action so it
belongs to the creation scope. Register and remove the identical handler reference with matching
capture options. Resolve Xantham's `EventTarget` overload once; a minimal typed Emit helper is
acceptable for that boundary if generated overloads cannot express the callback simply. Avoid
an untyped DOM object bag.

- [x] **Step 4: Run construction/binding tests and compare the public API.**

Run `rtk proxy npm run test:dom` and fslangmcp `check` for the library. Inspect `fcs_public_api` to
confirm existing helper signatures remain and new functions match the interfaces above. The library
can run in Node after globals are set; compiling its DLL does not prove browser correctness.

- [x] **Step 5: Record the independently passing task.**

Update this plan's checkboxes. Commit only task files, preserving existing unrelated staged or
unstaged changes. Include the pre-existing scaffold files only after comparing their before/after
content and retaining useful code.

### Task 2: Disposable mounting and adversarial cleanup tests

**Files:** `Mount.fs`, library compile order, `DomChecks.fs`, `dom.test.mjs`, library README.

**Interfaces:** Consumes Task 1's `Dom` module; produces:

```fsharp
Mount.mount : Graph -> Node -> (unit -> Node) -> System.IDisposable
```

- [x] **Step 1: Add failing mount lifecycle tests.**

Export `mountLifecycle : unit -> string array` and invoke it in a second Node test. Cover an
existing host child, idempotent disposal, a retained detached button, a reactive text node retained
after disposal, two mounts sharing a graph, externally detached/moved roots and a throwing factory.
Use actual DOM event dispatch to verify listeners stop reacting, not just a cleanup counter.

Essential expected behaviour:

```fsharp
let host = Dom.createElement "div"
let existing = Dom.text "preserved"
host.appendChild existing |> ignore
let mutable retained = Unchecked.defaultof<HTMLElement>
let handle = Mount.mount graph host (fun () ->
    retained <- Dom.element "button" [Dom.on "click" (fun _ -> count.Value <- count.Value + 1)] []
    retained :> Node)
handle.Dispose()
handle.Dispose()
check "existing child remains" (obj.ReferenceEquals(host.firstChild.Value, existing))
let before = count.Peek
retained.click()
check "detached handler removed" (count.Peek = before)
```

For failure cleanup, register the same button/listener and a reactive text binding, then raise
`InvalidOperationException("factory failed")`. Assert the exception escapes, host content is
unchanged, retained text stops changing and clicking the retained button does not update state.
Add a handler that creates a memo and cleanup to verify event ownership is restored and untracked.

- [x] **Step 2: Run the failing mount tests.**

Run `rtk proxy npm run test:dom`; expect missing `Mount.mount` or explicit failed lifecycle assertions.

- [x] **Step 3: Implement scoped mount ownership and cleanup.**

Activate the supplied graph, create a root with `createRoot`, and keep its owner available for
failure cleanup. Execute the factory in that root. Append only its returned node. Register root
removal with the owner, removing from its current parent if attached. The disposal handle activates
the graph and disposes the owner exactly once; it does not dispose the graph. A `try/with` around
construction/attachment disposes the captured owner and rethrows the original failure. If attachment
fails, remove any root already attached by this operation; never remove unrelated host children.

Keep cleanup registration separate from `createEffectOn` actions. Dispose the root if an externally
disposed graph already cleaned it up; do not attempt to activate a dead graph merely to repeat
cleanup. Resolve the core's disposed-owner/graph API semantically before choosing that guard.

- [x] **Step 4: Run the complete DOM tests and document the library contract.**

Run `rtk proxy npm run test:dom`. README includes active-owner requirements, mounting with a shared
graph, property setter equality checks, cleanup, experimental status, synchronous static structure
and the fact that pending/failure retains the last successful binding value rather than providing
a DOM loading/error UI automatically.

- [x] **Step 5: Record and commit this task's passing deliverable.**

Update the plan and commit only the library mounting changes, tests and README.

### Task 3: Browser playground, watch loop and real interaction tests

**Files:** Playground entry project, `App.fs`, HTML/JS/CSS shell, Node scripts, Playwright config/test, playground README and lockfile.

**Interfaces:** Consumes Task 1 `Dom` and Task 2 `Mount.mount`. Browser entry exports
`start : unit -> (unit -> unit)` returning cleanup. `main.js` invokes it and registers that cleanup
with `import.meta.hot.dispose` so hot reload does not accumulate graphs/listeners/mounts.

- [x] **Step 1: Add failing real-browser interaction tests and the private Vite shell.**

Give demo controls stable `data-testid` attributes. Required controls: `increment`, `reset`, `name`,
`count`, `doubled`, `greeting`, `mount-toggle`, `demo-root`. The external toggle lives outside the
demo mount. A Node fixture exposes `start` only; do not expose production globals for tests.

```javascript
import { test, expect } from "@playwright/test";
test("targeted updates and remount", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByTestId("reset")).toBeDisabled();
  await page.getByTestId("increment").click();
  await expect(page.getByTestId("count")).toHaveText("1");
  await expect(page.getByTestId("doubled")).toHaveText("2");
  await page.getByTestId("name").fill("Ada");
  await expect(page.getByTestId("greeting")).toHaveText("Hello, Ada");
  await page.getByTestId("mount-toggle").click();
  await expect(page.getByTestId("demo-root")).toHaveCount(0);
  await page.getByTestId("mount-toggle").click();
  await expect(page.getByTestId("demo-root")).toHaveCount(1);
  await expect(page.getByTestId("count")).toHaveText("1");
});
```

Add a browser assertion retaining the input DOM node through a signal-driven sibling update, and
an input event at a chosen caret position that writes identical text back; assert focus/selection
and identity are unchanged. Repeat unmount/remount five times and ensure one increment causes
exactly one count increment. Collect page errors and fail the smoke test on any unexpected error.

- [x] **Step 2: Run browser tests before the app implementation.**

Run `rtk proxy npm run test:browser`; expect missing demo controls. Playwright may install its
project browser through the normal package setup if needed; do not alter the user's open browser.

- [x] **Step 3: Implement the F# app and development scripts.**

Create application signals in one graph outside the mount factory. Create the derived memo under
the mount owner and bind it to the doubled output. Use `Dom.element` for the tree, `reactiveText`
for outputs, `bindAttribute` for an accessible/current-state attribute, `bindProperty` for reset's
disabled state and the name input's value, and `on` for input/click callbacks. Use a typed input
setter that avoids rewriting an identical value. The toggle disposes/remounts only the demo scope;
its own root and graph are cleaned up by `start`'s cleanup.

Scripts (all Fable projects have explicit output directories):

```json
{
  "compile": "dotnet fable Playground.fsproj --outDir generated",
  "compile:tests": "dotnet fable tests/DomTests.fsproj --outDir generated-tests",
  "dev": "npm run compile && concurrently --kill-others-on-fail --kill-others \"dotnet fable watch Playground.fsproj --outDir generated\" \"vite --host 127.0.0.1\"",
  "build": "npm run compile && vite build",
  "test:dom": "npm run compile:tests && node --test tests/dom.test.mjs",
  "test:browser": "npm run compile && playwright test",
  "test": "npm run test:dom && npm run test:browser"
}
```

Use Vite's default root, import `./generated/App.js` from `main.js`, and set Playwright's web server
to Vite on a fixed localhost port with reuse disabled for CI. Pin the npm versions listed in Tech
Stack and generate the lockfile. Set the documented Node floor to 26.0.0 to match the selected
jsdom version's installed runtime branch. Explain .NET 10/tool restore requirements. Ignore
`generated/`, `generated-tests/`, `dist/`, `node_modules/`, `test-results/` and `playwright-report/`.

- [x] **Step 4: Run DOM/browser tests, production build and watch smoke.**

Run `rtk proxy npm test` and `rtk proxy npm run build` in the playground. Start `rtk proxy npm run dev`,
make one reversible edit to visible F# copy, verify the rebuilt output/browser update, then restore
the edit. Stop only the dev processes started for this task and verify their process tree exits.

- [x] **Step 5: Write run instructions and commit the playground.**

README gives commands from repository root and the playground directory, exact prerequisites,
watch/build/test scripts and limitations. Include a screenshot only if useful; do not commit browser
test artefacts or generated JS. Update the plan and commit task files.

### Task 4: Complete verification and review

**Files:** Implementation plan progress and any task-owned fixes discovered by verification.

**Interfaces:** No new APIs; verify the delivered library and playground against the approved spec.

- [ ] Run fresh fslangmcp workspace/library checks and inspect final public API/compile order.
- [ ] Run Release library/solution builds without modifying pre-existing solution edits.
- [ ] Run the complete unfiltered .NET core suite for `tests/Ranvier.Tests/Ranvier.Tests.fsproj` under net10.0, then the existing complete Fable core suite and the playground's complete DOM/browser suites.
- [ ] Run the production build after any verification fix. Report unrelated baseline failures separately.
- [ ] Review mount failure paths, import safety, event registration/removal identity, current-owner restoration and input setter behaviour against the tests above.
- [ ] Request the single independent final review required by the native execution workflow if that method is chosen; apply only substantiated task-owned fixes and re-run affected gates.
- [ ] Final report links the library and playground, gives the exact development command, states checks actually run and names the PoC limits. Do not claim full Solid 2 parity or production readiness.

CLI acceptance commands from the repository root (playground commands run in its directory):

```text
rtk proxy dotnet build src/Fable.Ranvier/Fable.Ranvier.fsproj -c Release
rtk proxy dotnet build Ranvier.slnx -c Release
rtk proxy dotnet run --project tests/Ranvier.Tests/Ranvier.Tests.fsproj --framework net10.0 -c Release
rtk proxy dotnet fable fable/Ranvier.Tests.Fable/Ranvier.Tests.Fable.fsproj --outDir dist/tests
rtk proxy npm test
```

## Self-review

The four tasks cover every design section. Import timing and binding isolation are in Task 1;
failure cleanup, moved roots, retained handlers and shared graphs in Task 2; caret/focus, actual
browser behaviour, remounting and watch lifecycle in Task 3; full applicable gates in Task 4.
The public helper names are preserved. New interfaces use one argument order consistently.
No source implementation or product dependency installation is authorized by this plan until
the user reviews it and chooses execution.
