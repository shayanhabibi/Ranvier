# Fable.Ranvier DOM PoC

Experimental browser DOM helpers using Ranvier and Xantham.Fable.Core.TS 0.1.0. Construct HTML
nodes with ordinary F# functions; connect individual bindings to Ranvier's split effects.

```fsharp
open Fable.Core.TS.Dom
open global.Ranvier
open Fable.Ranvier

let graph = new Graph()
let count = graph.Run(fun () -> createSignal 0)
let host = Dom.document.getElementById("app").Value

let mounted =
    Mount.mount graph host (fun () ->
        Dom.element "button"
            [Dom.on "click" (fun _ -> count.Value <- count.Value + 1)]
            [Dom.reactiveText (fun () -> $"Count: {count.Value}")]
        :> Node)

mounted.Dispose()
graph.Dispose()
```

`Mount.mount graph host factory` establishes a graph and owner for the factory, appends the
returned root, and returns `IDisposable`. Existing host children stay intact. Disposal removes
the mounted root, stops its reactive bindings and unregisters its event listeners. Repeated
disposal is safe, including after graph disposal. Mounts sharing a graph dispose independently.
If construction throws, its owner is cleaned up and the exception is rethrown.

Create fresh root nodes in the factory. A mount owns the returned node's removal even if that
node is subsequently moved to another parent. It does not own the caller's graph or signals.
Disposing the graph also cleans up its mounts.

Return one persistent node, such as an element or text node. `DocumentFragment` roots are rejected
before attachment because insertion consumes the fragment's children. A failed mount still
disposes bindings and listeners created by its factory. Wrap multiple top-level nodes in an element.

## Functions

- `Dom.createElement tag`: bare `HTMLElement`, preserving the original scaffold helper.
- `Dom.element tag modifiers children`: apply modifiers and append existing child nodes once.
- `Dom.text value`: static text node.
- `Dom.reactiveText reader`: a stable text node updated from a `unit -> string` reader.
- `Dom.attribute name value`: static string attribute.
- `Dom.bindAttribute name reader`: reactive optional string attribute; `None` removes it.
- `Dom.property setter value`: typed DOM property assignment through a supplied setter.
- `Dom.bindProperty reader setter`: reactive typed DOM property assignment.
- `Dom.on name handler`: owner-managed event handler, restoring its graph/owner context and
  running the handler without tracking.

Reactive readers are explicit. Static text/attributes do not become reactive because a signal
was read while computing their argument. `reactiveText`, reactive modifiers and event registration
require an active graph/owner: normally use them inside the mount factory. You can instead
establish your own scope with the core's graph/owner APIs, then dispose that scope yourself.

Only the reader passed to a binding is tracked. The setter runs in `createEffectOn`'s action;
reactive reads in the setter do not subscribe it. Equal computed values skip the action.
Pending or failed readers retain the last successful DOM binding value. These helpers do not
automatically create loading/error views. Effect failures follow Ranvier's core error semantics.

DOM properties and attributes are separate. An input's current `value` is a property, while
`aria-label` is an attribute. Use an appropriate typed setter and skip redundant input assignments
to preserve selection/caret when an input event writes its existing text back to state:

```fsharp
let setInputValue (element: HTMLElement) value =
    let input = unbox<HTMLInputElement> element
    if input.value <> value then input.value <- value

let input =
    Dom.element "input"
        [Dom.bindProperty (fun () -> name.Value) setInputValue
         Dom.on "input" (fun event ->
             name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)]
        []
```

The cast must match the tag you constructed. The DOM globals are Fable `Global` bindings and
are used when functions run; importing the library does not mount UI or eagerly read `document`.

## Optional DOM batching

`Mount.mount` keeps synchronous updates. To coalesce DOM writes within a turn:

```fsharp
let options = { DomOptions.Default with Scheduling = Microtask; BatchEvents = true }
let mounted = Mount.mountWith options graph host factory
mounted.Flush()
let setterErrors = mounted.Errors
```

Each mount owns a queue with one slot per binding. Repeated updates replace that slot's pending
write. Reactive computation still follows Ranvier's core scheduler; DOM updates wait for a
microtask. `BatchEvents = true` additionally batches state writes within each registered event
handler. Both options are disabled by default and can be selected independently.

Each binding's first effect action writes immediately. If mounting inside a core batch, initial
values appear when that batch settles, without waiting for a DOM microtask. Subsequent Pending
or Failed readings cancel older queued writes and retain the last successful DOM value.
Disposing a mount or its graph cancels its queued work.

`Flush()` first settles graph work, then commits the current queue snapshot. Writes triggered
by a setter form a later wave; recursive calls to `Flush()` do not drain that wave. A manual flush
makes the previously scheduled callback inert. Queues remain separate even for mounts sharing
a graph. This is microtask scheduling, with no guarantee of one commit per animation frame.

Deferred setter exceptions are isolated so sibling bindings can still commit. `Errors` returns
the exceptions from the last nonempty DOM flush; an empty flush leaves them intact. Initial
setter errors retain core effect error semantics. Deferred errors are not recorded in the core
effect's error state. Setters remain untracked and run under their effect action's owner, so
resources they register are cleaned up on the next action or disposal.

The scheduled path adds a memo per binding and queue overhead. Tests show 100 source writes
coalescing into one text mutation; they do not establish a net throughput improvement. The
browser checks cover focus/caret and a synthetic composition-input echo, not complete native
IME behavior. Input setters should still compare the actual DOM value before assigning it.

## Scope

This borrows Solid 2's split read/write idea and uses Ranvier's effects with optional DOM batching.
It does not implement Solid scheduling, JSX, templates, SSR, hydration, SVG, keyed list diffing,
dynamic subtree replacement or event delegation. The first playground covers synchronous state.
This is a scaffold for evaluating the API, not a production renderer.

See [the Node/Vite playground](../../examples/Fable.Ranvier.Playground/README.md) for a runnable
counter, input, derived values, bindings, remount controls and DOM/browser tests.
