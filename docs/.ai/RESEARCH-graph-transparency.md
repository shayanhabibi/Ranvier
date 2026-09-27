# Graph transparency: a deterministic, Release-faithful view of the reactive graph

**Status.** Research and direction. The prototype (tracer, analyser, samples: `a36ef9f`, `5584e0a`, based
on `b820ad6`) is not on any branch; the local tag `research/graph-transparency-prototype` keeps it.
Measurements and line numbers below refer to that prototype at `b820ad6` unless marked otherwise.

**Question.** How can a person or an agent see the dependency graph, the owner tree, and the reason
each node ran, in a build that (1) produces the same observation on every run and (2) executes the
same scheduling, propagation, cutoff, disposal and suspension paths as Release?

**Answer.** A compile-time build flavour, `-p:RanvierTrace=true`, defines `RANVIER_TRACE`. Each `Graph`
then keeps an append-only log of fixed-shape integer events stamped by a per-graph sequence clock.
Every question (snapshot, owner tree, "why did X run") is answered offline by a pure analyser over
that log. In a default build the hooks do not exist. In a traced build the hooks write to a side
buffer and read nothing the engine decides on.

**Conventions.** ⚠️ marks a claim not measured or not exercised by the prototype. Everything else is
cited to a file, a commit, a URL, or a command whose output is quoted.

---

## §0. Executive summary

| # | Point |
| --- | --- |
| 1 | Gate: `RanvierTrace` MSBuild property → `RANVIER_TRACE` define, the `RanvierCounters` seam. Independent of `Configuration`; the build to observe is Release + trace. |
| 2 | The `DEBUG` symbol is the wrong gate: Fable compiles library source with the consumer's defines and `fable watch` sets `DEBUG`, and a Debug build swaps the optimiser. |
| 3 | Record: per-graph `TraceEvent` structs (`Seq, Kind, Node, Other, Arg, Cause, Flag, Text`). Node ids are the existing `graph.NextId`; owners use a separate tracer counter. |
| 4 | Causes are recorded at notification time: every `Mark` and `RunStart` carries the seq of the event behind it, so `why` is a pointer walk, not an inference. |
| 5 | Determinism, measured: 5 runs byte-identical on .NET, 5 on Fable/node, and the Fable output is byte-identical to .NET after CR stripping. |
| 6 | Fidelity, measured: 475/475 tests pass with tracing off and on. Two zero-allocation assertions fail when on, because the log allocates. |
| 7 | Cost off, measured: all 7 .NET counter scenarios and all 5 Fable scenarios report identical bytes/op, objects/op and library op counts to the untraced baseline. |
| 8 | Cost on, measured only in two tests: ~123 B per equal-value write, ~516 B per write that marks 3 memos. |
| 9 | Not yet traced: suspension, settles, batches, discharges, inbox drains, and AsyncMemo/Boundary/Lookup runs (§5.3). |
| 10 | Remaining nondeterminism is async completion order under a threaded dispatcher and hash-container order across targets; both are fenced (§6). |

---

## §1. The requirement

### 1.1 Determinism

The same program on the same target yields the same log, byte for byte: the same node ids, owner ids,
event order and dump text. Across .NET and Fable the canonical dumps match; the raw log may differ only
at hash-container iteration sites (§6).

### 1.2 Release fidelity ("~99%")

The traced build runs Release's paths. Concretely, the hooks add:

- no tracked read, no edge, no pull, no recompute (`.Value`, `TryValue`, `EnsureCurrent`,
  `UpdateIfNecessary`, `Track` are forbidden in hook arguments);
- no graph id (`NextId` is forbidden), no owner, no scope, no node;
- no branch the engine reads, no throw, no call into user code (`ToString`, `Equals`, `%A`);
- no weak reference, finalizer, timestamp or hash code.

The residual 1% is timing and layout, never ordering (§6.3).

### 1.3 Who reads it

- **A person at a debugger**: owner tree, who reads whom, freshness and status at a point.
- **An agent**: machine-readable text it can diff between runs and bisect by sequence number, with
  stable names (`Memo#2 "doubled"`) and a direct answer to "why did X run, and why did Y not".

---

## §2. Prior art, graded on fidelity

| System | Mechanism | Gets right | Gets wrong | Source |
| --- | --- | --- | --- | --- |
| SolidJS 1.x `DEV` | Dev bundle; `DEV.hooks` (`afterUpdate`, `afterCreateOwner`); walks stored `owned`/`sources`/`observers` | Reads edges the runtime already keeps; absent from prod bundles | Dev wraps every component in its own `createRoot`, prod does not: cleanup order diverged (#3572, fixed by LIFO in #3591; 1.x #1561 still open). Dev-only guards throw where prod succeeds (#3378/#3397); an effect ran in dev but not prod (#1843). `afterUpdate` names no cause | [dev docs](https://docs.solidjs.com/reference/rendering/dev), [#3572](https://github.com/solidjs/solid/issues/3572), [#3591](https://github.com/solidjs/solid/pull/3591), [#1843](https://github.com/solidjs/solid/issues/1843) |
| Solid 2.0 OBSERVE tier | Three tiers: prod / observe / dev | Separates passive observation from dev checks | The observe tier inherited dev's ownership model, not prod's (#3572) | [#3572](https://github.com/solidjs/solid/issues/3572) |
| solid-devtools | Patches inspected nodes (value getter/setter, wrapped `fn`) ⚠️ from memory | Reports "sources that caused the rerun" | Patching adds work only to observed nodes: an observer effect by construction | [debugger](https://github.com/thetarnav/solid-devtools/tree/main/packages/debugger) |
| MobX `spy` / `trace` / `getDependencyTree` | Global event stream, no-op in prod; `Name@n` debug names | Nested start/end events; separate dependency and observer trees | `trace` logs on possibly-stale; if the dependency cuts off, the log was already written (#2859, #1803) | [analyzing reactivity](https://mobx.js.org/analyzing-reactivity.html), [#2859](https://github.com/mobxjs/mobx/issues/2859) |
| Vue `onTrack` / `onTrigger` | Per-computation callbacks, dev only | Cause reported at the moment of triggering | Callback per effect; `__DEV__`-gated ⚠️ from knowledge | [reactivity core](https://vuejs.org/api/reactivity-core.html) |
| Svelte 5 `$inspect.trace` | Dev-only; highlights signals read during the run that triggered it | Reports cause per run | Cause inferred after the run from reads: nothing highlighted when the trigger is not re-read (#14794); nested effect credits its own derived (#16670) | [#14794](https://github.com/sveltejs/svelte/issues/14794), [#16670](https://github.com/sveltejs/svelte/issues/16670) |
| Angular `ɵgetSignalGraph` | Walks `producers` lists; `debugName` | Reads `consumer.value` without recomputing | Ids are `counter++` at discovery, cached in a WeakMap: not reproducible. Composite nodes leak raw primitives (#63227) | [signal_debug.ts](https://github.com/angular/angular/blob/main/packages/core/src/render3/util/signal_debug.ts), [#63227](https://github.com/angular/angular/issues/63227) |
| Preact signals-debug | Importing it changes signal behaviour globally | Names from option or Babel transform | Observation changes the observed | [npm](https://www.npmjs.com/package/@preact/signals-debug) |
| Jotai devtools | Dev store wraps `sub`/`set`/`get`; snapshot as a Map | Snapshot is a diffable value | Wrapping entry points is an observer effect | [devtools](https://jotai.org/docs/tools/devtools) |
| R3 `ObservableTracker` (.NET) | Runtime opt-in; tracks live subscriptions with `AddTime` and stack traces | Leak hunting on .NET | Timestamps and stacks: nondeterministic, costly | [R3 README](https://github.com/Cysharp/R3/blob/main/README.md) |
| FSharp.Data.Adaptive | Weak `Outputs` set, downstream only; `Consume` is destructive | — | Weak edges tie graph shape to GC timing; a node created in `AVal.custom` is collected and a stale value served (#122) | [#122](https://github.com/fsprojects/FSharp.Data.Adaptive/issues/122) |
| FirsthandJS devtools | Walk existing lists; add names, an entry point and a cause log; `dev.ts` replaced by empty stubs in prod | "The graph is the mechanism": instrument only what the graph discards (the cause). Cause hook moved from `propagate` to `write`, 1.01x | Empty stubs still cost bytes: a bundler cannot prove a call is pure | [PR #24](https://github.com/FirsthandJS/firsthand/pull/24) |
| .NET EventSource / DiagnosticSource | Runtime-gated, async, buffered, timestamped | Near-free when disabled | Events can be dropped; timestamps; runtime check paid in Release | [EventSource guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-instrumentation) |
| Partas `RANVIER_COUNTERS` | `Compile Condition` + `#if` call sites; process-static counters | Zero code when off; side counters change no branch or allocation | Process-static: events from parallel graphs mix | `src/Ranvier/Counters.fs`, `counters.ps1` |

Four lessons carry into the design:

1. Structural divergence is the dominant failure (Solid). The traced build adds no owner, scope or node.
2. The cause must be recorded when the notification happens (Vue, FirsthandJS), not inferred from
   post-run reads (Svelte) or logged at mark time as final (MobX). Mark, check resolution and run are
   separate events.
3. Ids assigned at creation from a per-graph counter are reproducible; ids assigned at discovery are
   not (Angular).
4. `#if` removal leaves zero bytes on Fable; empty stubs do not (FirsthandJS).

---

## §3. Recommended design

### 3.1 Mechanism

**Gate.** Copied from the counters seam in `src/Ranvier/Ranvier.fsproj`:

```xml
<PropertyGroup Condition="'$(RanvierTrace)' == 'true'">
    <DefineConstants>$(DefineConstants);RANVIER_TRACE</DefineConstants>
</PropertyGroup>
```

`Trace.fs` sits before `Core.fs`; its public types are always compiled and the `Tracer` class is inside
`#if RANVIER_TRACE`. `TraceModel.fs` (the analyser) is always compiled and never called by the engine.
Build with `dotnet build -c Release -p:RanvierTrace=true`, or set `$env:RanvierTrace=true` so Fable's
MSBuild cracking sees it, as `counters.ps1` does for `RanvierCounters`.

**Record.** One struct per event, appended to a `ResizeArray` owned by the graph:

```fsharp
[<Struct; NoEquality; NoComparison>]
type TraceEvent =
    { Seq: int; Kind: TraceEventKind; Node: int; Other: int; Arg: int; Cause: int; Flag: int; Text: string }
```

`Text` is null except for names, composite roles, and primitive row-key text. The field meanings per
kind are in the `TraceEventKind` doc comments (`Trace.fs` on the branch). The kinds in the prototype:
`GraphNew NodeNew Name Attach OwnerAttach Scope Compose Row Write Mark MarkSkip Schedule FlushStart
FlushEnd CheckResolved RunStart RunEnd EdgeAdd EdgeRemove Dispose OwnerDispose RowRetire`.

**Hook shape.** Every hook is a block beside an existing statement, as the counters are:

```fsharp
#if RANVIER_TRACE
        graph.Tracer.RunEnd (id, runs, status, moved)
#endif
```

Arguments are locals, ids and booleans the engine already computed. The single rewrite is Memo's
cutoff: under `#if` the predicate is bound once as `let moved = ...` and used by both the engine's `if`
and the hook; the `#else` branch keeps the original expression byte for byte, so user `Equals` runs
exactly as often as in Release.

**Hook sites.** 50 `#if RANVIER_TRACE` blocks in `Core.fs` and 18 in `Projections.fs` (branch
`a36ef9f`): node create and dispose for all ten node kinds; attach for Memo, Effect, AsyncMemo, Boundary
and Projection; owner attach, dispose and run-scope links; projection composite roles and rows; edges in
`SourceList.Add`/`trimFrom`, plus the RowWatch edges that bypass `SourceList` (slot -1); writes; marks
inside the `ObserverSet` notify walks; schedule; flush boundaries; check resolution; run start/end for
Memo, Effect and Projection.

**Surface.** Two always-compiled `Graph` members plus the analyser:

| Member | Default build | Traced build |
| --- | --- | --- |
| `Graph.TryTraceEvents () : TraceEvent[] voption` | `ValueNone` | a copy of the log |
| `Graph.TraceNameNext (name)` | no-op | names the next node created |
| `TraceModel.toJsonl / snapshot / snapshotText / snapshotJson / whyChain / why` | pure functions over `TraceEvent[]` | same |

`TraceNameNext` exists because `createEffect` returns `unit` (`Api.fs:85-86`): a `|> named "x"`
combinator cannot name effects.

### 3.2 Identity

| Thing | Id | Source |
| --- | --- | --- |
| Node | `Kind#Id`, e.g. `Memo#2` | existing `graph.NextId` (`Core.fs:1172-1174`); the tracer never calls it, so traced and untraced ids are identical |
| Name | `"doubled"` | `Name` event from `TraceNameNext`; lives in the log, not on the node |
| Owner | `O#n`, root is `O#0` | the tracer's own counter, assigned at construction |
| Composite part | `Signal#6 \| keys of Projection#5` | `Compose` event with role `keys`, `beacon`, `anyPending`, `inFlightVersion`, `watch` |
| Projection row | `Memo#15 \| row[b] of Projection#5` | `Row` event: row memo, item signal, key text for `string`/`int`/`int64`/`bool` keys only |

Internal nodes keep their `NextId` ids and are annotated rather than hidden, which explains the id gaps
and avoids Angular's raw-primitive leak (#63227). Key text is produced by the tracer from a type test,
never by user `ToString`.

### 3.3 Event log and dump schema (real output)

From `dotnet fsi samples/trace-sample.fsx all` on the branch. The scenario is `count → doubled → log`
under `createRoot`, then an `items → rows` projection of three rows read by effect `sum`; two writes
follow, `count <- 2` and item `b` 2 → 20. The full output is 162 lines; the log is 115 events. The
`count <- 2` segment:

```json
{"seq":77,"kind":"Write","node":1,"other":0,"arg":0,"cause":0,"flag":1,"text":null}
{"seq":78,"kind":"Mark","node":2,"other":1,"arg":2,"cause":77,"flag":0,"text":null}
{"seq":79,"kind":"Mark","node":3,"other":2,"arg":1,"cause":78,"flag":0,"text":null}
{"seq":80,"kind":"Schedule","node":3,"other":0,"arg":0,"cause":0,"flag":0,"text":null}
{"seq":81,"kind":"FlushStart","node":0,"other":0,"arg":3,"cause":0,"flag":0,"text":null}
{"seq":82,"kind":"RunStart","node":2,"other":0,"arg":2,"cause":78,"flag":0,"text":null}
{"seq":83,"kind":"RunEnd","node":2,"other":0,"arg":2,"cause":0,"flag":1,"text":null}
{"seq":84,"kind":"Mark","node":3,"other":2,"arg":2,"cause":83,"flag":0,"text":null}
{"seq":85,"kind":"CheckResolved","node":3,"other":0,"arg":0,"cause":0,"flag":1,"text":null}
{"seq":86,"kind":"RunStart","node":3,"other":0,"arg":2,"cause":84,"flag":0,"text":null}
{"seq":87,"kind":"RunEnd","node":3,"other":0,"arg":2,"cause":0,"flag":0,"text":null}
{"seq":88,"kind":"FlushEnd","node":0,"other":0,"arg":3,"cause":0,"flag":0,"text":null}
```

Read as: the write (77) marks `Memo#2` dirty (78), which marks `Effect#3` for check (79). In flush 3 the
effect's check walk runs the memo (82–83), whose value moved, so the memo marks the effect dirty (84);
the check resolves dirty (85) and the effect runs (86). Under a cutoff, 83 carries `flag:0`, 84 is
absent, 85 carries `flag:0`, and 86 is absent.

Snapshot text (`TraceModel.snapshotText`, end of log):

```
owners
  O#0 root
    O#1
      Memo#2 "doubled"
      Effect#3 "log"
      Projection#5 "rows"
        O#2 scope of Projection#5 "rows"
          Memo#13
          Memo#15
          Memo#17
      Effect#11 "sum"
nodes
  Signal#1 "count" | read by [Memo#2 "doubled"]
  Memo#2 "doubled" | runs=2 ok | reads [Signal#1 "count"] | read by [Effect#3 "log"]
  Effect#3 "log" | runs=2 ok | reads [Memo#2 "doubled"]
  Signal#4 "items" | read by [Projection#5 "rows"]
  Projection#5 "rows" | runs=2 ok | reads [Signal#4 "items"]
  Signal#6 | keys of Projection#5 "rows" | read by [Effect#11 "sum"]
  Beacon#7 | beacon of Projection#5 "rows" | read by [Effect#11 "sum"]
  Signal#8 | anyPending of Projection#5 "rows"
  Signal#9 | inFlightVersion of Projection#5 "rows"
  RowWatch#10 | watch of Projection#5 "rows"
  Effect#11 "sum" | runs=2 ok | reads [Beacon#7, Signal#6, Beacon#7, Memo#13, Beacon#7, Memo#15, Beacon#7, Memo#17]
  Signal#12 | item[a] of Projection#5 "rows" | read by [Memo#13]
  Memo#13 | row[a] of Projection#5 "rows" | runs=2 ok | reads [Signal#12] | read by [Effect#11 "sum"]
  Signal#14 | item[b] of Projection#5 "rows" | read by [Memo#15]
  Memo#15 | row[b] of Projection#5 "rows" | runs=2 ok | reads [Signal#14] | read by [Effect#11 "sum"]
  Signal#16 | item[c] of Projection#5 "rows" | read by [Memo#17]
  Memo#17 | row[c] of Projection#5 "rows" | runs=2 ok | reads [Signal#16] | read by [Effect#11 "sum"]
```

Snapshot JSON (`TraceModel.snapshotJson`, head):

```json
{"owners":[{"id":0,"parent":null,"scopeOf":null,"disposed":false},{"id":1,"parent":0,"scopeOf":null,"disposed":false},{"id":2,"parent":null,"scopeOf":5,"disposed":false}],"nodes":[{"id":1,"kind":"Signal","name":"count","owner":null,"sources":[],"watched":[],"observers":[2],"composite":null,"role":null,"runs":0,"status":"ok","disposed":false},{"id":2,"kind":"Memo","name":"doubled","owner":1,"sources":[1],"watched":[],"observers":[3],"composite":null,"role":null,"runs":2,"status":"ok","disposed":false},...
```

Ordering contract of the snapshot: nodes and owners sorted by id; observers sorted by id (the live order
reflects swap-remove history, `Core.fs:196-199`); sources in slot order, because slot order is the check
walk order; RowWatch edges listed as `watched`, apart from `sources`.

Signals and composite parts are not `IOwned`, so they appear in `nodes` only. A projection's run scope
(`O#2`) has no parent owner; the text dump nests it under its host through the `Scope` link.

### 3.4 Why did it run

`RunStart.Cause` is the seq of the first dirty `Mark` since the node's previous run start; the tracer
clears the entry at each run start. `Mark.Cause` is the seq of the event behind the notification: a
`Write`, or the marking node's `RunEnd`. `whyChain` follows these pointers, stepping from a `RunEnd` to
its own `RunStart`, until it reaches a write or an initial run. Real output:

```
== why log run 2 ==
#86 Effect#3 "log" run 2, because
  #84 Memo#2 "doubled" marked Effect#3 "log", because
    #83 Memo#2 "doubled" run 2 ended ok with a new value, because
      #82 Memo#2 "doubled" run 2, because
        #78 Signal#1 "count" marked Memo#2 "doubled", because
          #77 Signal#1 "count" was written with a new value
== why sum run 2 ==
#111 Effect#11 "sum" run 2, because
  #109 Memo#15 marked Effect#11 "sum", because
    #108 Memo#15 run 2 ended ok with a new value, because
      #107 Memo#15 run 2, because
        #99 Signal#14 marked Memo#15, because
          #98 Signal#14 was written with a new value
```

"Why did it not run" is answered by the same events: a `RunEnd` with `flag:0` (cutoff), a
`CheckResolved` with `flag:0`, or a `MarkSkip` (the `NotifyDirtyExcept` exemption for the running
reader).

Known gap: the `sum` chain stops at `Signal#14 was written`, but that write came from `Projection#5`'s
diff pass. `Write` records no running computation. Adding `Other = CurrentComputation.Id` to `Write`
continues the chain to `items`.

### 3.5 Fable

- The define reaches Fable through MSBuild cracking (`$env:RanvierTrace=true`), or a Fable project
  redefines it as `fable/Ranvier.Counters` redefines `RANVIER_COUNTERS`. The branch adds
  `fable/Ranvier.Trace`.
- Without the define `#if` removes every hook: zero emitted JS.
- Measured: `node output/Main.fs.js` 5 times gives 5 byte-identical outputs, and after CR stripping
  they are byte-identical to the .NET output, seq numbers, ids, snapshots and why chains included.
- The tracer uses one `Dictionary<int,int>` (dirty-cause lookup) and never iterates it, and the analyser
  sorts, so the sample contains no container-order difference.
- Fable cannot type-test interfaces: `Schedule` reads the id as `(box item :?> INode).Id`, and
  `Owner.Append` type-tests the concrete `Owner` class to emit `OwnerAttach`.
- `TraceEvent` becomes one JS object per event; ints are JS numbers, exact to 2^53.

### 3.6 Cost

**Off (measured).** `counters.ps1 -NoPmc` on the branch against the `b820ad6` baseline:

| .NET scenario | bytes/op | objects/op |
| --- | ---: | ---: |
| create | 530,408 | 2,002 |
| update | 0 | 0 |
| chain | 0 | 0 |
| cutoff | 0 | 0 |
| dispose | 56 | 0 |
| project-edit | 80 | 0 |
| project-reorder | 4,104 | 0 |

All 7 rows and every library counter column are identical to the baseline. The Fable/node median
bytes/op are identical in all 5 scenarios (create 1,137,608; update 8,799.52; chain 0.0704; cutoff 0;
dispose 29,234). Instructions retired were not measured (`-NoPmc`) ⚠️; the always-compiled additions are
two cold `Graph` members, the public trace types and `TraceModel`.

**On (partly measured).** From the two zero-allocation tests with tracing on: ~123 B/op for an
equal-value write (one `Write` event, amortised growth of a 40-byte struct array) and ~516 B/op for a
moved write that marks three memos (one `Write` and three `Mark`s). A full counters pass with tracing on
was not run ⚠️. The log is unbounded: memory grows linearly with events for the graph's lifetime.

---

## §4. Fidelity evidence

| Check | Result |
| --- | --- |
| `dotnet fsi build.fsx -- test`, tracing off | 475 passed, 0 failed |
| Same, `RanvierTrace=true` (test bin confirmed to contain `Tracer`) | 475 passed, 0 failed, after the change below |
| First traced run | 473 passed; the 2 failures were zero-allocation assertions in `Equality.fs` (1,229,000 B over 10k writes; 5,161,208 B over 10k writes × 3 observers). Cause: log growth. Both now assert only when `TryTraceEvents ()` is `ValueNone` |
| Default builds | `-c Release` and `-c Debug`: 0 warnings, 0 errors; traced build likewise |
| Counters, off vs baseline | identical (§3.6) |

Every behavioural test passes with tracing on. The zero-allocation tests are the one case where tracing
changes an outcome, and they assert nothing in a traced build.

---

## §5. What the prototype leaves open

### 5.1 Required before merge (from the design review)

1. **Check-walk attribution.** `RunStart.Other` ("pulled by") comes from `graph.CurrentComputation`.
   `Projection.ResolveCheck` (`Projections.fs:511-519`) and `IBeaconHost.Refresh` (`556-570`) run
   outside `RunHosted`, where that field names the wrong node or none; this is the `b820ad6` scenario. A
   trace-side walker stack, pushed in each check walk, supplies the correct reader.
2. **Swallowed exceptions.** `IBeaconHost.Refresh` ends in `with _ -> ()` (`Projections.fs` ~567); a
   `Swallowed(proj, exnType)` event records it.
3. **Bracketed runs.** `RunStart`/`RunEnd` paired on every exit path (try/finally in the traced build
   only), so nesting such as "a projection pass inside `Effect#9` run 3" (the `348c403` bug) is
   visible.
4. **Projection publication.** `RunEnd` for a projection records whether keys, rows or the summary were
   published, which is what wakes readers.

### 5.2 Anomaly queries (agent entry point)

Raw JSONL is too much to read cold. `TraceModel` gains lint queries that name suspicious patterns: a
node running twice in one flush; a run with no dirty mark in its window (pulled); a suspension inside a
run that ends in any status other than pending; a superseded flight dropped; a swallowed exception; a
node marked dirty while running and requeued. Plus focused views: upstream and downstream of one id,
limited to N events.

### 5.3 Hooks not yet written

| Event | Site (at `b820ad6`) |
| --- | --- |
| `Suspend(reader, awaited)` | at the computation's catch of `NotReadyException` and `RunHosted`'s re-raise (`Core.fs:1209-1210`), not in `Graph.NotReady` (`1220`), which also fires for untracked and swallowed reads |
| `PendingReraised`, `ClearRaised`/`CheckRaised` | `Core.fs:1209-1210`, `1228-1236` |
| `Settle(id, gen, accepted)`, `FlightDrop` | inside the dispatched closure: `AsyncSource.Settle/Fail` (`1374-1392`), `AsyncMemo` publish (`2091-2112`) |
| `InboxRun` | `Graph.Pump` (`990-1012`), before each `work ()` |
| `BatchEnter/Exit`, `FlushOwed` | `Graph.Batch` (`1111`), `RequestFlush` (`1068`) |
| `DischargeStart/End(owner, rerun\|retire)`, `CleanupThrew`, `ErrorRecorded` | `Graph.Discharge` (`1128`), `Retire` (`1136`), `Owner.DisposeScope` (`622-671`), `RecordError` (`585`) |
| `RunStart/RunEnd`, `Caught` | AsyncMemo `Launch` (`2158`), Boundary `Recompute` (`2438-2570`), Lookup `Recompute` (`Projections.fs:1222`) |
| `Write.Other = running computation` | Signal setter (`1317-1327`) |
| `NodeNew.Other = creator run` | "recreated by owner re-run", distinct from "woken by source" (Svelte #16670) |
| `GraphNew.Arg = dispatcher kind` | `Graph` constructor (`865-902`) |

---

## §6. What stays nondeterministic or divergent, and how it is fenced

### 6.1 Nondeterministic across runs

| Source | Fence |
| --- | --- |
| Async completion order on .NET: `ContinueWith(ExecuteSynchronously)` → `ConcurrentQueue` inbox | Settles are stamped inside the dispatched closure, so order equals drain order. Deterministic sessions use `ManualDispatcher` with `TaskCompletionSource` flights completed at chosen points and explicit `Pump`. The analyser flags multi-item drains ⚠️ not built |
| Default dispatcher chosen from `SynchronizationContext.Current` at construction (`Core.fs:865-902`) | `GraphNew` records the dispatcher kind ⚠️ not built |
| `CancelPrevious` runs cancellation callbacks inline | Recorded as it happens; reproducible only when user callbacks are |
| User bodies reading clocks, `Random`, I/O | The log shows the divergence (for example a different `moved` flag) |

### 6.2 Divergent across .NET and Fable

| Source | Fence |
| --- | --- |
| `KeyMap`/`KeySet` iteration: .NET `Dictionary` enumerates its entries array (insertion order until a `Remove` frees a slot; null key first; unaffected by string-hash randomisation); Fable iterates JS `Map` insertion order, primitive keys before object keys | Affects projection retire order (`Projections.fs:471`), `Refresh` over `inFlight` (`356`), Lookup `FailAll`/`SuspendAll`/`Invalidate`/`Evict`. The raw log keeps the real order; canonical dumps sort same-pass `RowRetire` and refresh events by row ordinal ⚠️ sort not built |
| `pendingSources` (`HashSet`, reference identity); `ReadRow` reports its first element (`Projections.fs:643`) | Suspension is recorded at the raise, never by enumerating the set. The awaited node named in multi-pending cases may differ between targets |
| Exception messages (localised, path-bearing) | Recorded as type plus message; a type-only normaliser for diffs ⚠️ not built. No stack traces |
| Promise microtask vs Task continuation interleaving | Async traces compare per target unless both use deterministic flights |

### 6.3 The residual 1%: traced build vs Release

- **Layout and inlining.** Hook blocks enlarge hot methods (notify walks, `SourceList.Add`); JIT inlining
  decisions can change. Timing changes; ordering does not. Stack-overflow depth for pathological chains
  moves with frame size.
- **Allocation.** The log allocates on every event, visible to zero-allocation tests (§4) and to GC
  timing. `src` has no finalizers, weak references or timers, so engine order is independent of GC;
  only timer-driven user tasks observe it.
- **Memory.** The log is unbounded.
- **Traced performance is not Release performance.** Trace builds are for semantics, never timing, the
  same split `counters.ps1` makes for PMC runs.
- **Hook drift.** A later engine edit can add a path without a hook, or give a hook an argument with a
  side effect. Fenced by the gates in §8 phase 2.

---

## §7. Rejected alternatives

- **Gate on `DEBUG`.** Fable consumers' `fable watch` would trace every dev build; Debug also changes
  the optimiser.
- **Runtime switch (`AppContext`, nullable tracer).** Puts a check in every hot path of the shipped
  binary; Fable has no JIT constant folding.
- **`[<Conditional>]`.** Inside the library it equals `#if`, and it also elides argument evaluation, so
  a side-effecting argument makes the builds differ.
- **EventSource / DiagnosticSource / ActivitySource.** Asynchronous, buffered, timestamped, lossy: not
  a diffable log.
- **Live inspector with per-node wake fields (`RANVIER_INSPECT`).** Last-writer-wins history loses
  multiple wakes per pass; value formatting by user `ToString` against the live graph pulls memos.
- **Always-shipped `Inspect` snapshot in Release.** Adds an interface to every node in the NuGet binary
  for a view the traced log already gives; kept as open question 4.
- **Runtime patching of inspected nodes (solid-devtools style).** Changes work only for observed nodes.
- **Process-static sink (as `Counters`).** Mixes events from parallel graphs in Expecto.
- **Weak-reference registry.** Makes the dump depend on GC timing (FSharp.Data.Adaptive #122).
- **Timestamps, thread ids, hash codes, stack traces in the canonical log.** Nondeterministic by nature.
- **Cause inferred from post-run reads or logged at mark time.** Svelte #14794/#16670 and MobX #2859.
- **Ring buffer by default.** A truncated log cannot be folded into a snapshot.
- **Caller-info attributes for names.** Module `let` functions cannot take optional arguments; Fable
  support is uneven.

---

## §8. Phased plan

| Phase | Work | Acceptance |
| --- | --- | --- |
| 0 (done) | Gate, tracer, analyser, 68 hook sites, .NET and Fable samples (`a36ef9f`, `5584e0a`) | 5+5 byte-identical runs; Fable = .NET; 475/475 both ways; counters off = baseline |
| 1 | Hooks of §5.1 and §5.3; rebase the `Projections.fs` hooks after `Projection.sortBy` merges | Asymmetric-edge reconciliation in `TraceModel` (folded edges vs observer and source counts) reports zero on the full suite; per-kind event counts equal `RanvierCounters` (edges, memo runs, effect runs, flushes) |
| 2 | Fidelity gates in CI: suite under Release and Release+trace on .NET and Fable; counters with and without `RanvierTrace` on the counters workloads; grep lint over `#if RANVIER_TRACE` blocks for `.Value`, `TryValue`, `EnsureCurrent`, `UpdateIfNecessary`, `Track`, `NextId`, `ToString`, `%A`, owner or node construction | All three gates green; lint has zero hits; a `Trace` solution configuration (Release + define) exists |
| 3 | Agent surface: anomaly queries (§5.2), neighbourhood views, `tools/trace.fsx` (`why`, `snapshot --at flush:N`, `tree --up/--down`, `diff a b` reporting the first divergent seq), golden traces for diamond, cutoff, boundary suspend, projection reorder | Rebuild the parents of `348c403`, `8f93594` and `b820ad6` with the tracer, run the tests those commits added: the matching anomaly fires before the fix and is silent after |
| 4 | Deterministic preset: `GraphOptions.Deterministic` (`ManualDispatcher`) and a `DeterministicFlights` helper; canonical sort for container-ordered events | An async scenario gives byte-identical canonical logs across 5 runs on each target and across targets |
| 5 | Optional: field-only `DebuggerDisplay` under the define; `Checkpoint` events that embed a snapshot, for bounded logs | `DebuggerDisplay` reads no `.Value`; `why` over a truncated log reports truncation |

---

## §9. Open questions for the maintainer

1. **Distribution.** Traced use through ProjectReference and source only, or a second NuGet package
   (same assembly name, built with the define)?
2. **In-repo Debug.** Should `Directory.Build.props` map `Configuration=Debug` to `RanvierTrace=true` for
   tests and samples in this repo? The packed library would stay untraced either way.
3. **Log bound.** Keep the log unbounded, or add a preallocated ring with checkpoints? A ring would also
   make the two zero-allocation tests meaningful in traced builds.
4. **Release snapshot tier.** Is a cold, always-shipped `Inspect.snapshot` (one internal interface per
   node, field reads only) worth adding to the NuGet binary for users without a traced build?
5. **Naming API.** Keep `Graph.TraceNameNext` (pending-name slot), add a scoped
   `Trace.naming "log" (fun () -> createEffect ...)`, or both?

---

## §10. Against the agent-loop claims

[RESEARCH-ecosystem-position.md](RESEARCH-ecosystem-position.md#agentic-development-loops) lists what
makes an agent's edit, observe, correct loop good, and which claims this library can make once they are
built. This section maps the traced build onto that list.

### 10.1 Loop criteria

| Criterion | Effect of the traced build |
| --- | --- |
| State as text | Met: `snapshotText`/`snapshotJson` print nodes, reads, readers, runs and owners (§3.3). |
| Determinism without a host | Met for synchronous graphs: byte-identical logs across runs and targets (§4). Async order equals `Pump` drain order (§6.1). |
| Explicit dependencies | Met after one run: `EdgeAdd`/`EdgeRemove` show every dependency and every change a closure edit makes. The observation is dynamic: an untaken branch leaves no edge. Static coverage stays with the analyser (claim 5). |
| Fast feedback, locality, familiarity | Unchanged. |

The ecosystem doc's case for MVU rests on "signals allow fewer mistakes, but the ones they do allow
are harder to see". `TraceModel.why` names the write, mark and cutoff chain behind each run, and
`CheckResolved`/`Write moved:false` record why a node did not run: the replay MVU gets from its message
list.

### 10.2 SageFs problems

1. **Async memos that look hung.** A trace shows the memo pending and the settle waiting in the inbox
   once `Suspend`, `Settle` and `InboxRun` exist (§5.3). ⚠️ Not built.
2. **Hot reload over a live graph.** `RunStart` names the effect instances still running, which
   identifies closures kept from before the reload. The trace diagnoses; it does not repair.
3. **The graph cannot be inspected.** Met in a traced build. SageFs loads the project's default build,
   where `TryTraceEvents` returns `ValueNone`. The agent loop gets the trace only if in-repo Debug maps to
   `RanvierTrace` (open question 2), and gets a snapshot in untraced builds only with the Release tier
   (open question 4).

### 10.3 Positioning claims

- **Claim 1, `graph.Describe()`.** The Release snapshot tier of open question 4, which §7 rejects on
  binary-size grounds. The agent loop reverses that: `Describe()` gives current state (clean, check,
  dirty, pending) in every build; the trace gives history and cause in a traced build. Recommended: both.
- **Claim 2, deterministic async.** The log makes a stepped async scenario assertable as text: two runs
  diff to their first divergent seq. Phase 4's `GraphOptions.Deterministic` packages the claim.
- **The pitch.** With phases 1, 3 and 4 done: *state you can print, causes you can trace, async you can
  step through*. Before then, the "why" half fails in the `b820ad6` check-walk scenario (§5.1, item 1).

### 10.4 Effect on the plan

Answering open questions 2 and 4 "yes" is the agent-loop default. Sequence item 2 of the ecosystem doc,
`Describe()`, becomes `Describe()` in Release plus `RanvierTrace` in in-repo Debug, landing with phase 1.
