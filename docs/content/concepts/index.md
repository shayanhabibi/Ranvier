---
title: Concepts
order: 1
---

:::warning
**Preview.** Ranvier is pre-release; its APIs may change.
:::

These pages cover the design behind Ranvier: the model it uses and the reasons for it, as well as the
alternatives that were considered and rejected. The guide pages cover how to use it.

Ranvier is a fine-grained reactive computation library for .NET, written in F#. It provides a dependency graph
of **signals** (settable sources), **memos** (cached derived values) and **effects** (side effects that re-run
when something they read changes). Dependencies are tracked automatically: a computation depends on whatever
it read during its last run. Derived values update in height order, so a computation does not observe a mix
of old and new inputs. An equality cutoff stops propagation when a recomputed value is equal to the previous
one.

Many libraries already provide that layer. Ranvier adds a **pending channel**: a value that has not arrived
yet is tracked as a status of its own. It is not a special value and not a flag you check by hand. The model
follows Solid 2.0's async-aware signals.

```fsharp
open Ranvier

let user = createAsyncSource<string> ()
let greeting = createMemo (fun _ -> "Hello, " + user.Value)

greeting.TryValue   // Pending
user.Settle "Ada"
greeting.TryValue   // Ready "Hello, Ada"
```

This example, like the others in this section, assumes that a graph is active on the current thread.

`greeting` does not check whether `user` has arrived. It reads `user.Value` like any other value, and the
graph marks it Pending until the source settles.

## Pages in this section

- [Suspension](suspension.md) covers what happens when a computation reads a value that is not ready. It
  explains the pending channel, why a read throws instead of returning a marker, how boundaries stop the
  channel with a fallback, and how errors and recovery use the same path.
- [The async graph on .NET](async-graph.md) covers tasks as computation results, superseded flights, thread
  ownership and dispatch, disposal, and what changes when the same core compiles to JavaScript through
  Fable.
- [Contracts](contracts.md) states which threads may touch a graph, what a failure does to the nodes that
  read it, and who owns a node created inside a computation.
- [Ecosystem](ecosystem.md) covers where Ranvier sits beside FSharp.Data.Adaptive, SignalsDotnet, R3 and
  System.Reactive, Fable.Ripple and Solid, and when one of those is the better choice.

## Implemented and exploratory material

The research behind these pages included designs that were measured and then rejected, as well as ideas
that are still open. Each page labels its material as one of the following:

- **Implemented**: behaviour the current code provides and its tests cover.
- **Considered and rejected**: an alternative that was evaluated, with the reason it was dropped.
- **Exploratory**: a direction under consideration. Nothing in this category is available to use.

Measurements quoted in these pages come from a single machine with a Stopwatch harness. Treat them as the
scale of a cost, not as benchmark results. They do not compare Ranvier with any other library.
