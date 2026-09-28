---
title: Tracing
order: 7
---

:::info
Preview — Ranvier is pre-release; APIs follow Partas.Signals and may change. Tracing is a research
feature: its queries and dump schema may change before the first release.
:::

A traced build records every graph operation in a per-graph event log: node creation, reads, marks,
runs, flushes and disposal, each with the source line that caused it. The `Trace` module answers
questions over that log. An untraced build compiles the log out, and its Ranvier IL matches the IL
built without tracing, method for method.

## Why trace a graph

A reactive graph moves control flow out of the call stack. When an effect runs, its stack shows the
flush loop, not the write that caused it. When an effect does not run, there is no stack at all. The
log keeps the causal chain the stack loses, so these questions have answers:

- **Why did this run?** A banner re-rendered after a cart edit. `Trace.why` walks from the run back
  to the write that started it, one hop per node.
- **Why did this not run?** A total stayed stale after a write. `Trace.whyNot` names the reason: an
  open batch, a node nobody reads, a check that found the upstream unchanged, or disposal.
- **Where did this node come from?** A graph holds a thousand memos. `Trace.origin` gives the file
  and line that created one, the owner chain it lives under, and its label.
- **What does the graph look like now?** `Trace.snapshot` prints the owner tree with every node's
  status, run count and sources.

## Turning it on

The `RanvierTrace` MSBuild property compiles tracing in. Debug builds default to traced; every other
configuration defaults to untraced. Set the property explicitly to override either default:

```bash
dotnet build src/Ranvier -c Release -p:RanvierTrace=true
```

A traced build defines `RANVIER_TRACE` for every project in the repository, so tests and samples see
the same build as the library. A traced library cannot be packed: `dotnet pack` fails with an error
naming the switch.

The query functions exist only in a traced build. Code that calls them compiles in a traced build
alone; guard it with `#if RANVIER_TRACE`, or keep it in scripts that load a traced `Ranvier.dll`.
F# Interactive does not define `RANVIER_TRACE`. A script referencing a traced DLL calls the queries
directly.

## Labels

Every node gets an identity path, built from the owner tree. A segment is the node's label if it
has one, otherwise its creation site. An unlabelled graph reads as file and line:

```text
/Cart.fsx:11 Memo ok runs 3 <- /Cart.fsx:9
```

`Trace.named` labels the first node or owner its function creates:

```fsharp
let lines = Trace.named "lines" (fun () -> createSignal [ { Sku = "tea"; Price = 4m; Qty = 1 } ])
let discount = Trace.named "discount" (fun () -> createSignal 0m)
let subtotal =
    Trace.named "subtotal" (fun () ->
        createMemo (fun () -> lines.Value |> List.sumBy (fun l -> l.Price * decimal l.Qty)))
let total = Trace.named "total" (fun () -> createMemo (fun () -> subtotal.Value - discount.Value))
let banner = Trace.named "banner" (fun () -> new Effect (graph, fun () -> printfn "total %M" total.Value))
```

`Trace.named` is available in every build. Untraced, it inlines to `f ()`, so a literal label costs
nothing.

A computed label, such as one per row, goes through `Trace.label`:

```fsharp
let prices =
    [ for i in 1 .. 3 ->
        let price = createSignal (i * 10)
        Trace.label (graph, price, $"price[{i}]")
        price ]
```

`Trace.label` is a `Conditional("RANVIER_TRACE")` member. A caller built without `RANVIER_TRACE`
drops the call, and the interpolated string is never built. The label takes effect only in callers
compiled with the define; a script run in F# Interactive against a traced DLL keeps the site path.

## Why did it run

After `lines.Value <- [ { Sku = "tea"; Price = 4m; Qty = 2 } ]`, the banner prints `total 8`.
`Trace.why` explains the banner's last run:

```fsharp
Trace.why graph banner |> Trace.render graph |> printfn "%s"
```

```text
why /banner run 2
  #49 RunStart /banner (Cart.fsx:13)
  #46 Mark /banner <- /total (Cart.fsx:13)
  #45 Moved /total (Cart.fsx:12) = 8M
  #44 RunStart /total <- /banner (Cart.fsx:12)
  #41 Mark /total <- /subtotal (Cart.fsx:12)
  #40 Moved /subtotal (Cart.fsx:11) = 8M
  #39 RunStart /subtotal <- /total (Cart.fsx:11)
  #32 Mark /subtotal <- /lines (Cart.fsx:11)
  #31 Write /lines (Cart.fsx:9) = [{ Sku = "tea"; Price = 4M; Qty = 2 }]
  root: user write #31
```

Read it from the bottom: a write to `lines` marked `subtotal`; the banner's check pulled `total`,
which pulled `subtotal`; each value moved, so the banner ran. `#n` is the event's position in the
log, and `= value` is the value a write, move or settle recorded. The chain ends at a root: a user
write, the node's creation, a pull by a reader, an async source's settle, or a cause older than the
log (`unrecorded after #n`).

`Trace.whyAt graph node run` explains an earlier run by number, and `Trace.whyDepth graph depth
node` stops after `depth` steps.

## Why did it not run

Writing a new line list with the same subtotal leaves the banner alone:

```fsharp
lines.Value <- [ { Sku = "tea"; Price = 2m; Qty = 4 } ]
Trace.whyNot graph banner |> Trace.render graph |> printfn "%s"
```

```text
checked clean #63 over /total
```

`subtotal` recomputed to the same value, so `total` did not move, and the check walk resolved the
banner clean. The other reasons:

| Reason | Meaning |
| --- | --- |
| `queued #n` | The node is scheduled and its run has not started: a batch is open, or the flush is not reached. |
| `unobserved since mark #n` | The node was marked, and nothing reads it since. A memo whose last reader was disposed shows this. |
| `checked clean #n over ...` | A check walk found every source unchanged. |
| `skipped as the running reader #n` | The node wrote a source it reads while running. |
| `not reached: propagation stopped at #n` | Propagation stopped upstream: a write that kept its value, or a run that did not move. |
| `disposed #n` | The node was disposed. |
| `no reason recorded` | The node ran after its last mark, or was never marked. |

## What did each run do

The examples in this section add an async shipping quote to the cart. A new subtotal starts a new
quote, and `total` reads both:

```fsharp
let shipping = Trace.named "shipping" (fun () -> createAsync (fun _ -> quote subtotal.Value))
let total = Trace.named "total" (fun () -> createMemo (fun () -> subtotal.Value + shipping.Value))
```

`Trace.history` lists every run of a node, oldest first:

```fsharp
Trace.history graph total |> Trace.render graph |> printfn "%s"
```

```text
history /total
  run 1 #15 pending moved flush 1 root: created #8
  run 2 #55 pending flush 2 root: user write #38
  run 3 #71 ok moved flush 3 = 11M root: user write #38
```

Each line gives the run number, its `RunStart`, how it ended, whether it moved the value, the flush
it started in, the value an ended run left, and the root of its `why` chain. Run 2 read a quote
still in flight and ended pending. Run 3 ran when the quote settled; its root is the write that
started the quote.

## What is it waiting on

`Trace.waitingOn` names the pending sources the node's last run read, and lists the node's flights,
newest first:

```fsharp
Trace.waitingOn graph total |> Trace.render graph |> printfn "%s"
```

```text
waiting /total
  suspended on /shipping
```

For an async memo, it lists each flight with the run that started it and its result:

```text
waiting /shipping
  flight 2 #57 run 2 settled #64
  flight 1 #26 run 1 dropped #63 superseded
```

The first quote settled after the second one started, so its result was dropped. A flight can end
`in flight`, `settled`, `failed`, `cancelled` or `dropped`. A dropped flight is `superseded` by a
newer flight, `disposed` with its node, or `suspended`: a failure that arrived while the newest run
waits on a pending source. A settle marked `held pending` kept its value while a newer run waits.

`Trace.why` follows a settle back to the run that started the flight:

```text
  #65 Moved /shipping (Cart.fsx:10) = 3M
  #64 Settle /shipping (Cart.fsx:10) = 3M
  #57 FlightStart /shipping (Cart.fsx:10)
  #56 RunStart /shipping <- /banner (Cart.fsx:10)
```

An `AsyncSource` settle ends the chain at `settle #n`.

## Where did it come from

```fsharp
Trace.origin graph total |> Trace.render graph |> printfn "%s"
```

```text
/total Memo at Cart.fsx:12 #8
```

The `TraceOrigin` record also carries the owner chain up to the graph root and the run that created
the node, which tells apart a node created at startup from one re-created by each run of its owner.

The site is the innermost stack frame outside Ranvier, FSharp.Core and `System.*`. `Trace.resolve
graph "/total"` goes the other way, from a path to the node id.

## What the graph looks like

```fsharp
Trace.snapshot graph |> Trace.render graph |> printfn "%s"
```

```text
/
  /lines Signal fresh runs 0
  /discount Signal fresh runs 0
  /subtotal Memo ok runs 3 <- /lines
  /total Memo ok runs 2 <- /subtotal, /discount
  /banner Effect ok runs 2 <- /total
```

A `map replay` fence draws a log like this one. The site build runs the example under .NET with tracing on, presses each button once, and embeds the recording; Play, Step and the scrubber move through its events.

```fsharp map replay
let lines = createSignal [ 4m; 6m ]
let discount = createSignal 0m
let subtotal = createMemo (fun () -> List.sum lines.Value)
let total = createMemo (fun () -> subtotal.Value - discount.Value)
createEffect (fun () -> printfn $"total {total.Value}")

controls [
    "Add a line", fun () -> lines.Value <- lines.Value @ [ 5m ]
    "Discount 2", fun () -> discount.Value <- 2m
]
```

`Trace.snapshotAt graph seq` folds the log up to an earlier event, to see the graph as it was.

## Dumps

`Trace.dumpText graph` returns the log as JSON Lines, schema 1: a header, the folded starting state,
then one object per event. `Trace.dump graph path` writes the same text to a file.

```text
{"schema":1,"target":"net","graph":1,"checkpoint":null,"seqFrom":1}
{"snapshot":{"seq":0,"root":0,"nodes":[],"owners":[],"sources":[],"observers":[],"siblings":[],"incarnations":[]}}
{"seq":1,"kind":"GraphNew","node":0,"other":1,"arg":0,"flag":0,"cause":0,"payload":null}
{"seq":2,"kind":"OwnerNew","node":2,"other":1,"arg":0,"flag":1,"cause":0,"payload":"trace-sample.fsx:29"}
{"seq":3,"kind":"NodeNew","node":1,"other":0,"arg":1,"flag":0,"cause":0,"payload":"trace-sample.fsx:31"}
{"seq":4,"kind":"Label","node":1,"other":0,"arg":0,"flag":0,"cause":0,"payload":"count"}
```

The same program produces the same dump, byte for byte, on every run of a single-threaded graph.
`Trace.events graph` returns the raw `TraceEvent[]` for your own analysis.

## Limits

- **Fable records, without dumps.** A Fable build records the log that [signal maps](signal-maps.md)
  draw; `Trace.dump` is absent there.
- **Development builds only.** A traced build is slower and allocates per event, and cannot be
  packed. Ship the untraced build.
- **The log is unbounded.** Every event stays in memory for the graph's lifetime. A long session
  grows without limit; a checkpoint to trim it is planned.
- **No values.** The log records that a node moved, not its old or new value. `history` lists runs
  without their values; value capture is planned.
- **Flights are async memo flights.** `waitingOn` lists the flights of an `AsyncMemo`. An
  `AsyncSource` has no flights: its settles appear as `Settle` and `Fail` events.
- **Combinator nodes carry the site of the combinator call.** Rows and internal nodes of `filter`,
  `sortBy` and the other views share the call's file and line, told apart by `#n`, not by key.
- **Sites follow the JIT.** An inlined method can report its caller's line. `Trace.named` is the
  stable name. The repository's trace gates run with `DOTNET_TieredCompilation=0`.
- **Dumps run between flushes, on the graph thread.** `Trace.dump` and `Trace.dumpText` raise
  `InvalidOperationException` off the graph's thread or inside a flush, a discharge or a
  computation's run. Inside `batch` they succeed. The queries run at any time on the graph thread.
- **Unchecked graphs order events by lock.** A graph with `ThreadAffinity = Unchecked` records
  safely from several threads, but its event order, and so its dump, can differ between runs.
- **Labels are names, not keys.** Nodes sharing a label under one owner are told apart by creation
  order: the second takes `#1`, the third `#2`.

## How the zero-cost claim is checked

`tools/verify-trace.fsx` gates every change to the trace code:

- The untraced Release IL references no trace type and equals the IL of the merge base, method by
  method. The packed DLL's public surface equals a committed baseline plus `Trace.named` and
  `Trace.label`.
- A sample using `Trace.named` or `Trace.label` compiles to the same IL as the sample without it.
- The traced test suite passes, and a sample dump is byte-identical over five runs.
- For each counter-bench scenario, the log's run, edge, observer and flush counts equal the
  library's own counters, and the edges folded from the log equal the live graph.
- With elevated rights, the counter bench shows equal allocations and instruction counts between
  the merge base and the change.

## Next

- [Signal maps](signal-maps.md): the log drawn live, beneath the examples in these docs.
- [Troubleshooting](troubleshooting.md): exception messages and symptoms, with their causes and fixes.
- [Getting started](getting-started.md): the graph, owners and batching the log records.
