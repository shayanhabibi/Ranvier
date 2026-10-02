# Fable.Ranvier playground

A small browser playground for the [DOM PoC](../../src/Fable.Ranvier/README.md). The UI is created
in F# using Xantham DOM types. Ranvier updates text, attributes and DOM properties without
rebuilding the tree. Try the counter, editable name field and unmount/remount button.

## Run

Prerequisites: .NET 10 SDK, Node.js 26 or newer, npm. The repository pins Fable 5.18.0 in its
local .NET tool manifest.

From the repository root:

```powershell
rtk proxy dotnet tool restore
Set-Location examples/Fable.Ranvier.Playground
rtk proxy npm ci
rtk proxy npm run dev
```

Open the localhost URL printed by Vite (normally `http://127.0.0.1:5173`). Edit `App.fs` to change
the demo. The development command compiles once, then runs Fable watch and Vite together. Ctrl+C
stops both. Vite hot reload disposes the old app's mounts and graph before starting a replacement;
hot reload resets app state. The demo's own unmount/remount control preserves its state.

To launch after installation from the repository root:

```powershell
rtk proxy npm --prefix examples/Fable.Ranvier.Playground run dev
```

RTK is the repository's command convention; without it, run the underlying `dotnet`/`npm` commands.

## Build and test

From this directory:

```powershell
rtk proxy npm run build
rtk proxy npx playwright install chromium
rtk proxy npm test
```

`build` compiles F# before Vite bundles it into `dist/`. `test:dom` compiles the F# behavioural
checks and runs them with Node's test runner against jsdom. The test module is imported before
DOM globals are installed to check import safety. `test:browser` compiles the app and runs real
Chromium interactions against a separate Vite server on port 5178. It checks output updates,
input node identity/focus/caret, the disabled property, and listener cleanup across remounts.
The suites can be run separately with `npm run test:dom` and `npm run test:browser`.

The private npm project pins dependencies in `package-lock.json`. Node 26 is the selected minimum
for the pinned jsdom release; no external server or web API is required.

## Layout

- `App.fs`: application state, static element creation and explicit reactive bindings.
- `main.js`: imports the compiled app and registers disposal with Vite.
- `style.css`: responsive layout and styling.
- `tests/DomChecks.fs`: compiled F# checks of library behaviour.
- `tests/dom.test.mjs`: Node/jsdom setup and test invocation.
- `tests/playground.spec.mjs`: real browser interaction checks.

`generated/`, `generated-tests/` and `dist/` are generated and ignored. The Node project references
the local `src/Fable.Ranvier` project, so library edits are part of the Fable watch loop.

## PoC limits

This covers HTML with a static child structure and synchronous state. There is no JSX, SSR,
hydration, SVG, keyed collection reconciliation, dynamic subtree replacement or event delegation.
The runtime is Ranvier, not Solid; Solid 2's split effects inspired the binding approach.
Pending/error values retain the last successful binding output rather than automatically creating
a loading/error view. See the library README for its ownership and property-setter contracts.

SageFs was used to resolve generated DOM types, and the local Fable.SageFs setup was used for quick
JavaScript-shape experiments. Neither tool is a runtime or build prerequisite for this playground.
