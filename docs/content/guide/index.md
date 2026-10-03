---
title: Overview
order: 1
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Build reactive state for .NET: update a value, and the computations that depend on it update too.

* **Signals** hold values you can read and write.
* **Memos** derive values from tracked reads.
* **Effects** run when a value they read changes.

:::tip Async state travels through the graph
A node waiting on an async dependency is **pending**. Its readers can report loading, keep a
previous result, or use a boundary's fallback. This pending channel follows Solid 2.0 and is
independent of whether a value is out of date.
:::

## A taste

```fsharp
open Ranvier

let graph = new Graph ()

let doubled =
    graph.Run (fun () ->
        let count = createSignal 1
        let doubled = createMemo (fun _ -> count.Value * 2)
        createEffect (fun () -> printfn "doubled = %d" doubled.Value)
        count.Value <- 5
        doubled)
```

```text
doubled = 2
doubled = 10
```

## What you get

:::note Signals, memos and effects
Readers see consistent values as changes propagate. An equal result stops propagation at that node.
:::

:::note Owners and cleanup
Every memo and effect belongs to a scope. Dispose the scope to release its nodes and run cleanups.
:::

:::tip Pending axis
`AsyncSource` and async memos report pending state alongside their values. Flight policies control
what happens when inputs change during a request.
:::

:::tip Boundaries
A Suspense boundary supplies a fallback while its body is pending. An ErrorBoundary supplies a
recovered value when its body fails. Both handle dependencies read directly or through memos.
:::

:::note Keyed and index projections
Per-row reactive values over a collection, keyed by identity or by position.
:::


:::note Lookups and selectors
Lookups derive a value for each key you read. Selectors report which key is selected, triggering
the readers of the previous and new selection.
:::

:::tip Thread-affinity guard
Dispatchers move work from other threads onto the graph thread.
:::

## Targets

`net10.0`, `net8.0` and `netstandard2.1` and [Fable (JavaScript)](../fable/index.md).

## Guide

- [Installation](installation.md): preview packages, supported targets and Native AOT.
- [Getting started](getting-started.md): a short walkthrough of a reactive graph.
- [Graphs](graph.fsx), [Signals](signals.fsx), [Memos](memos.md) and [Effects](effects.md): the core primitives.
- [Equality](equality.md): graph defaults and typed comparers for individual nodes.
- [Roots and owners](roots.md), [Cleanup](cleanup.md), [Untrack](untrack.md) and [Batch](batch.md): lifetime and tracking controls.
- [Editable values](editable.md) and [Drafts](drafts.md): local edits over upstream state.
- [Async and pending](async-and-pending.md): in-flight values, async memos, boundaries, and threading.
- [Testing async state](testing.md): deterministic tests that decide when each flight lands.
- [Blazor Server](blazor-server.md): one graph per circuit, with `ThreadAffinity.Serialised`.
- [Collections](collections.fsx): projections, editable keyed sources, change readers, lookups and collection views.
- [Aggregates](aggregates.fsx): totals, counts and folds over collection rows.
- [Forms](forms.md): records of reactive fields and batched resets.
- [Tracing](tracing.md): why a node ran or did not run, where it was created, and the graph as it stands.
- [Signal maps](signal-maps.md): how to read the live graph maps beneath the examples, and how to write one.
- [Troubleshooting](troubleshooting.md): each exception message and common symptom, with its cause and fix.
- [Migrating from Elmish](elmish.md): an MVU model read through selector memos, one view at a time.
- [C#](csharp.md): C# factories, XAML bindings and reactive commands.

## Status

:::details Implementation status and planned features

| Area | Status |
| --- | --- |
| Core graph (signal, memo, effect, owners, batching) | Implemented and tested. |
| Per-node typed comparers | Implemented for signals, memos, split effects and boundaries; see [Equality](equality.md). |
| Editable keyed collections and value readers | Implemented, with direct row edits and bounded change cursors; see [Direct edits](collection-updates.fsx#direct-edits) and [Reading changes](projections.fsx#reading-changes). |
| Async and boundaries | Implemented. A pending `.Value` read throws `NotReadyException`; `TryValue` reads without throwing. |
| Projections, lookups and selectors | Implemented, with factory map semantics. A node created inside a memo pulled by a projection row belongs to that memo. |
| Fable/JavaScript | Implemented and tested under Node.js; no package published yet. See [Fable (JavaScript) target](../fable/index.md). |
| Collection combinators (`Projection.filter`, `choose`, `map`, `mapWith`, `sortBy`, `groupBy`) | Implemented and tested, including pending and error behaviour; see [Collections](collection-views.fsx). |
| Editable values (`createEditable`, `createDraft`) and the MVU bridge (`Mvu`, in the `Ranvier.Elmish` package) | Implemented and tested; see [Editable values and forms](forms.md) and [Migrating from Elmish](elmish.md). |
| Reusable lens and prism values for deep writes | Not implemented; they wait on a need for reusable focus paths over collections. |

:::
