# Fable.Ranvier DOM PoC and Node playground

Status: written design approved by the user on 2026-10-02.

## Purpose

Prove that ordinary F# functions can construct browser DOM once and connect individual text,
attribute and property updates to Ranvier. Provide a small runnable Node/Vite playground using
that implementation. The user requested a Solid v2-like approach and approved a function API,
reactive bindings, event handlers, disposable mounting and a counter/input playground.

Success means running the playground locally, editing F# with a watch loop, and demonstrating
targeted updates and deterministic cleanup in automated DOM tests and a real browser.

## Approach and limits

Use Xantham.Fable.Core.TS DOM types and the existing Ranvier dependency graph. Prefer normal F#
functions over a computation-expression DSL or a JSX/template compiler: this keeps the experiment
small and makes its runtime behaviour explicit. No Solid runtime dependency is needed.

The inspiration is Solid 2's separation of tracked computation from side effects. This is not
an implementation of Solid's complete renderer or scheduling contract. Retain Ranvier's own
effect, batching, error and ownership semantics. The adapter must not introduce a second scheduler.

Reference: [Solid 2 reactivity and split effects](https://github.com/solidjs/solid/blob/next/documentation/solid-2.0/01-reactivity-batching-effects.md).

Out of scope: SSR, hydration, JSX, template compilation, SVG namespaces, keyed collection
reconciliation, dynamic subtree replacement, delegated events and a new async DOM boundary system.
The first playground exercises synchronous state; existing core async APIs remain unchanged.

## Library structure

Develop in the existing `src/Fable.Ranvier` scaffold. It is already untracked user work and must
be inspected through the F# semantic tools before changing it. Keep the existing core project
reference and Xantham DOM package. Make this a reusable library, moving any useful executable
demonstration into the playground rather than silently deleting it.

Add a small browser-facing DOM module and document its experimental API. Bind browser globals
with Fable interop and use Xantham's generated types for DOM operations. Importing the module
must not create a graph, mount UI, or eagerly access `document`; access globals when constructing
or mounting nodes so a Node test can establish its DOM first.

The API provides these operations (final names and inferred types may follow repository conventions):

- Element construction from an HTML tag, a list of modifiers and a list of child nodes.
- Static text and reactive text from a `unit -> string` reader.
- Static attributes and reactive optional attributes; `None` removes the attribute.
- DOM property assignment and reactive property binding, keeping property semantics distinct
  from string attributes. Prefer typed setters over a universal untyped property bag.
- Event registration with a supplied event name and handler, with owner-managed removal.
- Mounting into a supplied host using a supplied graph and a `unit -> Node` factory, returning
  an `IDisposable` handle.

Each reactive binding tracks only its reader. Its DOM write runs in the split effect callback.
Element construction itself is not a reactive computation; a changed signal does not rebuild
the component tree. Static values remain static. Reactive getters are explicit in the call site.
Input property updates should skip an assignment when the DOM already holds that value, preserving
the current input/caret when an input event writes the same text back to the signal.

## Ownership and mounting

`mount` activates the caller's graph and creates one owner for its factory and bindings. The
caller retains ownership of the graph and application state; disposing a mount does not dispose
the shared graph. A second mount may use that same graph independently.

The factory creates one root node, which is appended to the host. Existing host children are
preserved. Disposal stops the owner's computations, unregisters event handlers, and removes only
the root node belonging to that mount. Repeated disposal is safe. If the factory throws before
attachment, dispose the created owner and leave the host untouched. Do not clear `innerHTML`.

Event callbacks run as imperative application interactions, rather than reactive readers.
Any restoration of graph/owner context needed by the core must be explicit. Cleanup registers
the same callback reference and event options used by registration. Retained detached nodes must
not keep an active handler after disposal. Reactive node construction requires an active mount
owner or an explicitly established caller scope; explain that requirement in the README.

## Playground

Create `examples/Fable.Ranvier.Playground` as a private Node project with Vite, an F# entry project
referencing the local library, an HTML shell, and a small stylesheet. Keep generated Fable output,
Vite output, dependencies and test artefacts out of Git. Commit a package lockfile and use the
repository's pinned Fable tool where possible.

The page contains a counter with increment/reset buttons, an editable name field, a reactive
greeting, a memo-derived doubled count, a reactive attribute and a reactive DOM property such as
a disabled reset button. Provide unmount/remount controls outside the demo mount so cleanup can
be exercised without losing those controls. Preserve application state across remounting and
avoid accumulating mounts or effects.

Provide commands for install, Fable compilation, development/watch, production build and tests.
The development command must start Fable watch and Vite, and stop both processes on exit. The
production build must compile F# before bundling. Document the SDK/Node prerequisites and the
exact commands from the playground directory. The README explains the API and the PoC limits.

## Verification

Use a Node DOM implementation for deterministic automated tests of compiled F# code. Test actual
browser DOM in one browser smoke run for input, button clicks, disabled state and mount cleanup.
Do not infer DOM correctness from successful .NET compilation alone.

Required DOM behaviours:

1. Static construction produces the expected nesting, text and attributes.
2. Signal changes update reactive text/attributes/properties while element and text node identity
   remain stable; a removed optional attribute is absent.
3. A derived memo updates the displayed result and independent state does not rebuild the tree.
4. Events update state and input binding preserves the existing input node and current value.
5. Disposal removes only the owned root, stops reactive DOM writes and removes event handlers,
   including on a retained detached node; repeated disposal is safe.
6. A failed factory leaves existing host content unchanged and does not leak bindings/listeners.
7. Two mounts sharing one graph remain independent; disposing one leaves the other working.
8. Remounting and development reload cleanup do not accumulate roots or handlers.

Run the repository's applicable complete core test gates, the playground DOM suite and the
production build. Check Release as well as the normal development compilation where relevant.
Record any unrelated baseline failure without changing unrelated user work.

## Development tooling

Use fslangmcp for semantic inspection, running its workspace check before symbol queries and
impact analysis before altering an existing public signature. Its global executable is installed,
but it is not exposed as a tool in this chat; connect to the configured server rather than using
text search to guess F# semantics.

The user explicitly requested SageFs and Fable.SageFs only where they reduce development-loop
time. Use SageFs for quick pure F# type/shape experiments where available. Use Fable.SageFs for
browser/Fable experiments if its setup is present and cheaper than the compiler watch loop.
Compiled browser bindings cannot be meaningfully executed in the .NET REPL. Prefer Fable watch
plus DOM tests for those cases and use the CLI for final verification. Do not stop, restart or
reinstall the user's SageFs daemon without permission. No tool installation is assumed.

## Review

The design deliberately keeps creation, binding and ownership explicit. The most important
implementation risks are binding to the wrong effect phase, registering cleanup against an
effect-run owner rather than the mount owner, swallowing initial construction failures, and
testing only a DOM emulator. The implementation plan must include tests for these behaviours.
