(**
---
title: Signals
---
*)
(*** hide ***)
#load "../../literate.fsx"

open Ranvier
open Ranvier.Docs.Maps

let graph = new Graph ()
let active = graph.Activate ()

(**

A signal holds a value that you can read and write. Use `createSignal initial`{fsharp}
inside an [active graph](graph.fsx), or construct `Signal (graph, initial)`{fsharp}
against an explicit graph.

`createSignal` returns the `Signal<'T>` itself. It is always ready: unlike an
[async source](async-sources.md), a signal cannot be pending or failed.

## Reading and writing

| Member | Meaning |
| --- | --- |
| `.Value` | Reads the value and records a dependency when a memo or effect is running |
| `.Value <- x` | Writes the value |
| `.Peek` | Reads the value without recording a dependency |
| `.TryValue` | Reads the value as a `Reading<'T>` (`Ready`, `Pending` or `Failed`) and records a dependency |

*)

(*** title="createSignal in FSharp" ***)

let count = createSignal 1
// Read
count.Value // 1
// Set
count.Value <- 2
// Read
count.Value // 2
count.Peek // 2
count.TryValue // Ready 2

(**

Reading `.Value` or `.TryValue` inside a [memo](memos.md) or [effect](effects.md)
records a dependency. Reading outside a computation simply returns the value.
`.Peek` never records a dependency.

Use `Signal.update count (fun value -> value + 1)`{fsharp} to transform the current
value. It reads through `.Peek`, so the update itself adds no dependency.
The resulting write uses the same equality comparison as `.Value <- value`.

## Propagation

A changed write marks direct observers **dirty**: they need to run again.
Computations farther downstream are marked **check**: an upstream result might
have changed. They bring their dependencies current before deciding whether their
own bodies need to run. A mark for checking is not itself a re-run.

Effects normally finish before a write returns. [Batch](batch.md) defers effects
until the outermost batch ends; an unobserved memo refreshes only when read.

## Equality

A `Signal` that does not change its value will not mark downstream observers:

*)
(*** map replay code=collapsed ***)
let equalityGraph = Graph.Current
let equalityCount = createSignal 2
let equalityEven = createMemo (fun _ -> equalityCount.Value % 2 = 0)
let mutable equalityEffectRuns = 0

createEffect (fun () ->
    equalityEffectRuns <- equalityEffectRuns + 1
    printfn "even = %b" equalityEven.Value)

let mutable equalityMarks = [||]

controls
    [
        button "Write 2 (equal)" (fun () ->
            let offset = (Trace.events equalityGraph).Length
            equalityCount.Value <- 2
            equalityMarks <- Trace.events equalityGraph |> Array.skip offset)
        |> describe "The equal signal write marks no observers and runs no computation."
        |> expect "The equal signal write marks no observers." (fun () ->
            equalityEven.Runs = 1
            && equalityEffectRuns = 1
            && not (
                equalityMarks
                |> Array.exists (fun event -> event.Kind = TraceEventKind.Mark)
            ))
        button "Write 4" (fun () -> equalityCount.Value <- 4)
        |> describe "The memo re-runs, but equal parity cuts off the effect's re-run."
        |> expect "Equal parity cuts off the effect's re-run." (fun () -> equalityEven.Runs = 2 && equalityEffectRuns = 1)
        button "Write 3" (fun () -> equalityCount.Value <- 3)
        |> describe "Changed parity reaches the effect."
        |> expect "Changed parity reaches the effect." (fun () ->
            equalityEven.Runs = 3
            && equalityEffectRuns = 2
            && not equalityEven.Peek)
    ]

(**
The cutoff happens **before notification**. An equal write leaves the stored value
and every observer unchanged. In comparison, `AsyncSource.Settle` always notifies
its readers, even when the value is equal. A downstream memo may cut off that equal
result after its own re-run, but its readers have already been marked for checking.
See [Async sources](async-sources.md#equality-and-check-marks) for the matching replay.

## Choosing equality

`GraphOptions.Equality` supplies the comparison when each node is created:

- `JsIdentityPolicy` is the default. Primitives and strings compare by value;
  reference types such as records and lists compare by identity on .NET.
- `StructuralPolicy` uses `EqualityComparer<'T>.Default`.

An equal copy of a record therefore propagates under the default policy on .NET.
Mutating an object in place and writing the same reference back is cut off. Prefer
immutable updates that create a new value; see [Deep and keyed updates](collection-updates.fsx).

[Equality](equality.md) covers custom policies, NaN and differences between .NET and Fable.
The policy is graph-wide; individual nodes do not accept their own comparers.

## Lifetime and threads

A signal stores state; it does not own a computation or cleanup. Dispose the
[owner](roots.md) of its readers to stop their work and release dependency edges.

Writes obey the graph's thread-affinity guard, including equal writes. Use
`graph.Dispatch` for writes from another thread; see [Threading and dispatch](threading.md).
*)

(*** hide ***)
active.Dispose ()
graph.Dispose ()
