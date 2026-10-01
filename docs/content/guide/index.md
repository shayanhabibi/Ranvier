---
title: Overview
order: 1
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Fine-grained reactive computation for .NET.

* **Signals** hold values
* **Memos** derive from Signals
* **Effects** run when one of their Signals/Memos changes

<br/>

Alongside *dirtiness*, each node carries a second flag axis, the
**pending channel** from Solid 2.0.

:::note The pending axis
When a node has an asynchronous dependency that is running, it is considered
*in flight* and **pending**.
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
Glitch-free propagation and an equality cutoff at every level
:::

:::note Owners and cleanup
Every memo and effect belong to a scope which implements `IDisposable`
:::

:::tip Pending axis
`AsyncSource`, async memos and flight policies mark a node as in flight,
and its dependents read the pending flag alongside the value.
:::

:::tip Boundaries
Suspense and ErrorBoundary computations catch the pending and failed state their body
reads, directly or through memos. A Suspense boundary shows a fallback while its body is pending;
an ErrorBoundary substitutes a recovered value when its body fails.
:::

:::note Keyed and index projections
Per-row reactive values over a collection, keyed by identity or by position.
:::


:::note Lookups and selectors
A pointwise derived value per key, and membership tests that wake the
readers of the previous and new key.
:::

:::tip Thread-affinity guard
Pluggable dispatchers for marshalling off-thread work onto the graph thread.
:::

## Targets

`net10.0`, `net8.0` and `netstandard2.1` and [Fable (JavaScript)](../fable/index.md).

## Guide

- [Installation](installation.md): how to get Ranvier before its first NuGet release.
- [Getting started](getting-started.md): graphs, signals, memos, effects, batching and scopes.
- [Async and pending](async-and-pending.md): in-flight values, async memos, boundaries, and threading.
- [Testing async state](testing.md): deterministic tests that decide when each flight lands.
- [Blazor Server](blazor-server.md): one graph per circuit, with `ThreadAffinity.Serialised`.
- [Collections](collections.fsx): keyed and index projections, lookups and selectors.
- [Editable values and forms](forms.md): values seeded from upstream and edited locally, and forms as records of signals.
- [Tracing](tracing.md): why a node ran or did not run, where it was created, and the graph as it stands.
- [Signal maps](signal-maps.md): how to read the live graph maps beneath the examples, and how to write one.
- [Troubleshooting](troubleshooting.md): each exception message and common symptom, with its cause and fix.
- [Migrating from Elmish](elmish.md): an MVU model read through selector memos, one view at a time.

## Status

| Area | Status |
| --- | --- |
| Core graph (signal, memo, effect, owners, batching) | Implemented and tested. |
| Async and boundaries | Implemented. A pending `.Value` read throws `NotReadyException`; `TryValue` reads without throwing. |
| Projections, lookups and selectors | Implemented, with factory map semantics. A node created inside a memo pulled by a projection row belongs to that memo. |
| Fable/JavaScript | Implemented and tested under Node.js; no package published yet. See [Fable (JavaScript) target](../fable/index.md). |
| Collection combinators (`Projection.filter`, `choose`, `map`, `mapWith`, `sortBy`, `groupBy`) | Implemented and tested, including pending and error behaviour; see [Collections](collections.fsx#combinator-views). |
| Editable values (`createEditable`, `createDraft`) and the MVU bridge (`Mvu`, in the `Ranvier.Elmish` package) | Implemented and tested; see [Editable values and forms](forms.md) and [Migrating from Elmish](elmish.md). |
| Reusable lens and prism values for deep writes | Not implemented; they wait on a need for reusable focus paths over collections. |
