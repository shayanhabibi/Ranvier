---
title: Overview
order: 1
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Fine-grained reactive computation for .NET.

Ranvier is a reactive graph for F#. Signals hold values, memos derive values from them, and effects
run when what they read changes. Alongside dirtiness, each node carries a second flag axis, the
**pending channel** from Solid 2.0: a node can be in flight, and the nodes that read it see that it is
pending.

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

- **Signals, memos and effects**, with glitch-free propagation and an equality cutoff at every level.
- **Owners and cleanup**: every memo and effect belongs to a scope, and disposing the scope runs its
  cleanups and disposes its children.
- **The pending channel**: `AsyncSource`, async memos and flight policies mark a node as in flight,
  and its dependents read the pending flag alongside the value.
- **Boundaries**: Suspense and ErrorBoundary computations catch the pending and failed state their body
  reads, directly or through memos. A Suspense boundary shows a fallback while its body is pending;
  an ErrorBoundary substitutes a recovered value when its body fails.
- **Keyed and index projections**: per-row reactive values over a collection, keyed by identity or
  by position.
- **Lookups and selectors**: a pointwise derived value per key, and membership tests that wake the
  readers of the previous and new key.
- **A thread-affinity guard** with pluggable dispatchers for marshalling off-thread work onto the
  graph thread.

## Targets

`net10.0`, `net8.0` and `netstandard2.1`. The engine also compiles to JavaScript with Fable; see [Fable (JavaScript) target](../fable/index.md).

## Guide

- [Installation](installation.md): how to get Ranvier before its first NuGet release.
- [Getting started](getting-started.md): graphs, signals, memos, effects, batching and scopes.
- [Async and pending](async-and-pending.md): in-flight values, async memos, boundaries, and threading.
- [Collections](collections.fsx): keyed and index projections, lookups and selectors.
- [Tracing](tracing.md): why a node ran or did not run, where it was created, and the graph as it stands.
- [Signal maps](signal-maps.md): how to read the live graph maps beneath the examples, and how to write one.
- [Troubleshooting](troubleshooting.md): each exception message and common symptom, with its cause and fix.

## Status

| Area | Status |
| --- | --- |
| Core graph (signal, memo, effect, owners, batching) | Implemented and tested. |
| Async and boundaries | Implemented. A pending `.Value` read throws `NotReadyException`; `TryValue` reads without throwing. |
| Projections, lookups and selectors | Implemented, with factory map semantics. A node created inside a memo pulled by a projection row belongs to that memo. |
| Fable/JavaScript | Implemented and tested under Node.js; no package published yet. See [Fable (JavaScript) target](../fable/index.md). |
| Collection combinators (`Projection.filter`, `choose`, `map`, `mapWith`, `sortBy`, `groupBy`) | Implemented and tested, including pending and error behaviour; see [Collections](collections.fsx#combinator-views). |
| Reusable lens and prism values for deep writes | Not implemented; they wait on a need for reusable focus paths over collections. |

## Origin

Ranvier began as experiments in a repository called [Partas.Signals](https://github.com/shayanhabibi/Partas.Signals).
