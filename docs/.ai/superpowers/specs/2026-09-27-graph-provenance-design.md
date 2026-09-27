# Graph provenance: a traced build with full provenance for development and the REPL

**Status.** Design approved in conversation on 2026-09-27; revised the same day after a three-lens review
(feasibility, fidelity, contract). Basis: [RESEARCH-graph-transparency.md](../../RESEARCH-graph-transparency.md)
and the prototype under the local tag `research/graph-transparency-prototype` (`a36ef9f`, `5584e0a`, based on `b820ad6`).
This spec supersedes the research doc's §8 phased plan and answers its §9 open questions (§9 below). Line numbers
cite `915f139`.

Interpretations chosen where a review finding pressed on a user decision are marked **[interpretation]** and listed
in §11.

## 1. Goal

A development mode in which a person or agent, in tests, samples or a SageFs session, can answer for any node:

| Id | Question | Query | Phase |
| --- | --- | --- | --- |
| A | Why did it run? | `Trace.why` | 1 |
| B | Why did it not run? | `Trace.whyNot` | 1 |
| C | Where did it come from? | `Trace.origin` | 1 |
| D | What was its value on each run? | `Trace.history` | 2 |
| E | What is it waiting on; which flight settled or was dropped? | `Trace.waitingOn` | 2 |
| F | What changed between two runs, or before and after an edit? | `Trace.diff` | 4 |

## 2. Hard rules

1. **Release pays nothing.** No existing Release code path gains a branch, allocation or call. Untraced-build residue
   is exactly: the stub `Tracer` type (§3.2), the `Trace` module (compiled name `TraceModule`) with the inline
   `Trace.named` (§5.1), and the `Trace` class with the `Conditional` `Trace.label` (§5.1). The deterministic preset
   (§6.3) is library behaviour, not residue.
2. **The traced build runs Release's paths.** Tracing may cost any time and memory. The traced build reads, schedules,
   runs, caches and disposes exactly as Release, calls user code only where Release does, and holds no reference to a
   node, owner or graph in its log (§4.1).
3. **Tracing records; it never steers.** Determinism for async comes from opting into `GraphOptions.Deterministic`,
   ordinary library code present in Release (§6.3), never from a trace hook.

## 3. Components

| Unit | File | Role | Untraced build |
| --- | --- | --- | --- |
| Build switch | `Directory.Build.targets`, `src/Ranvier/Ranvier.fsproj` | §3.4. | Symbol undefined |
| Event types | `src/Ranvier/TraceEvents.fs`, before `Core.fs` | `TraceEventKind`, `TraceNodeKind`, `TraceEvent`, `RunStatus`. BCL-only; references no other library file. | Not compiled |
| `Tracer` | `src/Ranvier/Trace.fs`, before `Core.fs` | Internal static class of `[<Conditional("RANVIER_TRACE")>]` hooks; `TraceLog` (per-graph state); `ITraced` (traced state carried by engine objects). | Stub `Tracer` only: same hook signatures, empty bodies; `TraceLog` and `ITraced` not compiled |
| Hook sites | `Core.fs`, `Projections.fs`, `Combinators.fs` | One-line `Tracer.*` statements (§3.3). | Removed with their arguments |
| Traced state | same files | `#if RANVIER_TRACE` declaration blocks: fields and `interface ITraced` implementations on `Graph`, `Owner`, `ObserverSet`, `SourceList` (§3.3). | Not compiled |
| `TraceModel` | `src/Ranvier/TraceModel.fs` | Pure analyser over `TraceEvent[]`: fold to snapshot, cause walks, identity paths, diff, lint, render, JSONL text. Fable-safe: no IO, no `System.Text.Json`. | Not compiled |
| `Trace` API | `src/Ranvier/TraceApi.fs`, after `Combinators.fs`, public | Queries over a live graph, a dump or a checkpoint (§5). File IO sits behind `#if !FABLE_COMPILER`. | The `Trace` module with the inline `Trace.named`; the `Trace` class with the `Conditional` `Trace.label` |
| CLI | `tools/trace.fsx` | `dotnet fsi` front end over JSONL; `#load`s `TraceEvents.fs` and `TraceModel.fs`. | Not part of the library |
| Creation site | `src/Ranvier/TraceSite.fs`, before `Trace.fs` | `TraceSite.capture`: the user's `file:line` from the stack (§4.4). | Not compiled |
| Gates | `tools/verify-trace.fsx` | Runs the gates of §7. | Not part of the library |
| Deterministic preset | `Types.fs`, `Api.fs` | §6.3. | Present |

### 3.1 Boundaries

The engine calls only `Tracer`. `Tracer` writes only a `TraceLog`. `TraceModel` depends exclusively on event arrays;
`trace.fsx` loads it without the library. `TraceApi` connects a live graph to `TraceModel`.

### 3.2 The `Conditional` seam

With `RANVIER_TRACE` undefined, .NET and Fable remove each `Tracer` call together with its argument expressions; the
caller's JS module imports nothing from `Trace.js`. A `Conditional` call must still type-check untraced, so every hook
argument is a value that exists in the untraced build: a local, an existing field, `this`, an id or a constant. A hook
takes engine objects as `obj`; its traced body casts them to `ITraced` to reach the log and trace ids. `Conditional`
requires `unit`-returning methods; the walker stack is a push hook and a pop hook (§4.2).

### 3.3 Hook sites and traced state

- A hook is a single `Tracer.X(...)` statement. `Tracer` is never bound, piped, passed as a value or wrapped.
- No hook site gains `try`, `finally`, `#if` or a rebinding of an engine expression. Hooks that close a run or a walk
  sit where control flow already converges (§4.2); the `Tracer` recovers from exits it does not see.
- `#if RANVIER_TRACE` blocks in engine files hold declarations only: traced-only fields and `interface ITraced with`
  implementations. Every such block is listed in Appendix A and matched by the lint. The graph's log is created by the
  `Tracer.GraphNew(root, guarded)` hook in the `Graph` constructor and stored on the root owner.
- `ITraced` exposes `TraceLog` (get/set) and `TraceId` (get/set). `Graph`, `Owner`, `ObserverSet` and `SourceList`
  implement it. A node's `ObserverSet` and `SourceList` are bound to the graph's log and the node's id by a
  `Tracer.Bind(set, graph, id)` hook in the node constructor.
- A computation's run scope receives the log from `Tracer.ScopeNew(scope, graph, host)` when the scope is created, and
  emits `OwnerNew` then. Any other owner (`new Owner()`, `RootScope`, a key scope) adopts its parent's log through
  `Tracer.OwnerAdopt` in `Owner.SetParent`, `Owner.Attach` or the scope's constructor, and emits `OwnerNew` at adoption.
- A catch binding introduced only for a hook is named `_ex`.

### 3.4 Build switch

- `Directory.Build.targets` (imported after the project body, when `$(Configuration)` is set) holds
  `<RanvierTrace Condition="'$(RanvierTrace)' == '' and '$(Configuration)' == 'Debug'">true</RanvierTrace>` and, when it is
  `true`, appends `RANVIER_TRACE` to `DefineConstants`. The define is repo-wide: tests, samples and benches see the same
  symbol as the library, and `TraceEvents.fs`/`Trace.fs`/`TraceModel.fs`/`TraceApi.fs` item conditions read the final
  property value.
- A plain `dotnet build` or `dotnet test` (Configuration defaults to Debug), and therefore a SageFs project session,
  is traced.
- A traced build emits `[<assembly: AssemblyMetadata("RanvierTrace", "true")>]`.
- A target in `Ranvier.fsproj` with `BeforeTargets="GenerateNuspec"` fails the pack when `RanvierTrace` is `true`
  (this includes `dotnet pack -c Debug` and `dotnet pack --no-build` over a traced build).
- Every untraced gate build, `counters.ps1`, and `bench/*` invocations pass `-p:RanvierTrace=false` as a global property,
  overriding any environment value. `counters.ps1` runs both Fable builds with `--configuration Release`, and its report
  header records `RanvierTrace`. `verify-trace.fsx` clears `RanvierTrace` from every child environment.
- `fable/Ranvier.Counters` and `fable/Ranvier.Bench` set `RanvierTrace=false` in their project files. The
  traced Fable harness takes `RanvierTrace=true` from the environment, as `counters.ps1` does for `RanvierCounters`.

### 3.5 Relation to `RanvierCounters`

`RANVIER_COUNTERS` stays independent. Gate 2 builds without `RanvierCounters`; Gate 3 builds with both.

### 3.6 Threading

A `TraceLog` takes a lock on append when its graph's `ThreadAffinity` is `Unchecked`. Under `Guarded` appends are
single-threaded by the existing affinity check. Unchecked graphs are crash-free when traced; their event order is not
covered by the determinism gates.

## 4. Events

### 4.1 Record

`TraceEvent` is a struct: `Seq: int` (per-graph clock), `Kind: TraceEventKind`, `Node: int`, `Other: int`, `Arg: int`,
`Flag: int`, `Cause: int` (the `Seq` of the causing event, or 0), `Payload: obj`. The clock does not reset at checkpoint;
the log raises `InvalidOperationException` past `Int32.MaxValue` events per graph. Node ids are the graph's existing
ids. Owner ids come from the log's own counter and live in the owner's traced-only `TraceId` field.

The log holds ints and payloads only. A payload is never an `INode`, `Owner`, `Graph` or projection: for a
`NotReadyException` the source id goes in `Other` and `Payload` is null; for a value whose type is a library node type,
the payload is the node's id. Other payloads (phase 2: values, keys, exceptions) are held by reference until the next
checkpoint and are formatted only at dump time (§4.5); a value mutated after recording dumps in its current state, and a
user value that references nodes retains them. Both are documented limits.

### 4.2 Kinds

| Group | Kinds | Serves | Phase |
| --- | --- | --- | --- |
| Lifecycle | `GraphNew`, `NodeNew`, `OwnerNew`, `Dispose`, `OwnerDispose`, `Label` | C | 1 |
| Propagation | `Write`, `Mark`, `MarkSkip`, `Schedule`, `CheckStart`, `CheckResolved`, `EdgeAdd`, `EdgeRemove`, `ObserverAdd`, `ObserverRemove` | A, B | 1 |
| Runs | `RunStart`, `Moved`, `RunEnd`, `WalkAbandoned` | A | 1 |
| Scheduling | `FlushStart`, `FlushEnd`, `BatchEnter`, `BatchExit`, `DischargeStart`, `DischargeEnd` | A, E | 1 |
| Values | `Value` | D | 2 |
| Async | `Suspend`, `FlightStart`, `Settle`, `Fail`, `FlightDrop`, `InboxRun` | E | 2 |
| Errors | `Swallowed`, `ErrorRecorded`, `CleanupThrew` | A | 2 |
| Projections | `Pass`, `Publish` (`Flag`: keys moved), `RowNew`, `RowDispose` | A, B, C | 3 |

Sites are an attribute of `NodeNew`/`OwnerNew` (§4.4), not an event.

#### 4.2.1 Field meanings (phase 1)

| Kind | Node | Other | Arg | Flag | Cause | Payload |
| --- | --- | --- | --- | --- | --- | --- |
| `GraphNew` | 0 | root owner id | 0 | 0 | 0 | null |
| `NodeNew` | node id | owner id, or 0 | `TraceNodeKind` | 0 | `RunStart` seq of the creating run, or 0 | site |
| `OwnerNew` | owner id | parent owner id, or 0 | host node id when it is a node's run scope, else 0 | 1 for a `createRoot` scope | `RunStart` seq of the creating run, or 0 | site |
| `Label` | labelled node or owner id; 0 when unused | 0 | 0 from `Trace.named`, 1 from `Trace.label` | 0 | 0 | label string |
| `Dispose` / `OwnerDispose` | id | 0 | 0 | 0 | 0 | null |
| `Write` | signal | running computation, or 0 | 0 | 1 when the value moved | that computation's `RunStart` seq, or 0 | null |
| `Mark` | target | source | 1 check, 2 dirty | 0 | `Write`, `Moved` or `Publish` seq | null |
| `MarkSkip` | skipped reader | source | as `Mark` | 0 | as `Mark` | null |
| `Schedule` | scheduled node | 0 | queue length ahead | 0 | the `Mark` seq | null |
| `CheckStart` | node | 0 | 0 | 0 | 0 | null |
| `CheckResolved` | node | source that answered dirty, or 0 | 0 | 1 dirty, 0 clean | `CheckStart` seq | null |
| `EdgeAdd` / `EdgeRemove` | computation | source | slot | 0 | 0 | null |
| `ObserverAdd` / `ObserverRemove` | source | observer | 0 | 0 | 0 | null |
| `RunStart` | node | puller (walker top, else the innermost open run in the log, else 0) | run number (1-based) | 0 | first dirty `Mark` since the previous `RunStart`, or 0 | null |
| `Moved` | node | 0 | run number | 0 | `RunStart` seq | null |
| `RunEnd` | node | 0 | `RunStatus` (`Ok`, `Pending`, `Error`, `Abandoned`) | 1 when a `Moved` was recorded in this run | `RunStart` seq | null |
| `WalkAbandoned` | node whose frame was unwound | 0 | 0 | 0 | 0 | null |
| `FlushStart` / `FlushEnd` | 0 | 0 | flush number | 0 | 0 | null |
| `BatchEnter` / `BatchExit` | 0 | 0 | depth after the change | 0 | 0 | null |
| `DischargeStart` / `DischargeEnd` | owner id | host node id | 0 | 0 | 0 | null |

`EdgeAdd`/`EdgeRemove` mirror `SourceList` exactly (`Counters.EdgeAdded`/`EdgeRemoved`). `ObserverAdd`/`ObserverRemove`
mirror `ObserverSet` insertion and removal (`Counters.ObserverInserted`/`ObserverRemoved`) and cover RowWatch
subscriptions (`Projections.fs:377`). `Moved` is a hook in the existing cutoff branch (Memo: beside
`observers.NotifyDirtyExcept`, `Core.fs:1695`); `RunEnd.Flag` is derived from it.

#### 4.2.2 Pairing and the walker stack

- `RunStart` sits beside the existing run counter (Memo: beside `Counters.MemoRecomputed`, `Core.fs:1654`; Effect: in
  `RunBody`, `Core.fs:2079`). `RunEnd` sits after the point where every exit converges: after the Memo cutoff `if`, after
  the `try/with` in `Effect.RunBody`, and the equivalent points in the other node kinds (Appendix A).
- Every loop that calls `UpdateIfNecessary` on a node's sources pushes that node on the log's walker stack before the loop
  and pops it after. Sites at `915f139`: `Memo.ResolveCheck` (`Core.fs:1706`), `Effect.Execute` (2031),
  `AsyncMemo.EnsureCurrent` (2430), `Boundary.EnsureCurrent` (2803), Projection row refresh (`Projections.fs:411`, 967),
  `Projection.ResolveCheck` (583, 597), beacon `Refresh` (656, after its `try/with`), Lookup `Refresh` (1576).
- Recovery: `Pop(node)` unwinds the stack down to `node`, recording `WalkAbandoned` for each frame above it. Runs left
  open close with `RunEnd` status `Abandoned` at: a `RunStart` of the same node (its open run and every run above it), a
  `RunEnd` of an enclosing run (every run above it), `FlushEnd` (every open run), and a dump or checkpoint taken outside
  every run, flush and discharge. `FlushEnd` empties the walker stack.

### 4.3 Identity path

- A path is `/` followed by segments from the graph root owner, which has no segment. Ownerless nodes hang off the graph
  root. A `createRoot` owner is `root#n`, `n` counting earlier roots under the same parent.
- A node that owns a run scope contributes one segment; its scope adds none.
- A segment is the label if one is set, otherwise the site (§4.4), otherwise `<kind>`. It takes `#n` when an earlier
  sibling has the same segment text, with `n` counting such siblings created in the parent's current run. Counters reset
  at every owner run, so a re-created child keeps its path.
- Combinator nodes take structural segments (`filter(Cart.fs:30)`), and rows `row[<key text>]` with the same `#n` rule.
  **[interpretation]** Row key text in paths is canonical text for `int`, `string`, `bool`, `float` and the type name
  otherwise (phase 3).
- An incarnation suffix `@k` (the k-th node to hold the path) disambiguates. A bare path resolves to the live incarnation,
  else the latest.
- `/ # [ ] @ \` in labels and key text are backslash-escaped.

### 4.4 Creation site

- The stack is captured inside the `Tracer` body, never at a hook site. On .NET it is `StackTrace(skipFrames, true)`,
  resolved to `file:line` inside the `Tracer`; a failure records site `?`.
- The site is the innermost frame that has file info, whose assembly is not `Ranvier`, `FSharp.Core` or
  `System.*`, and whose file lies outside `src/Ranvier`.
- A combinator records its site at construction; its rows and internal nodes inherit it.
- A site under JIT inlining can name the caller; `Trace.named` is the stable alternative. .NET traced gates run with
  `DOTNET_TieredCompilation=0`.
- `TraceSite.capture` implements the rules above. In FSI (SageFs) the innermost frame of an `FSI-ASSEMBLY` assembly is
  the site: the script `file:line`, or `stdin:line` for a prompt submission. A submission without line info reports
  `stdin:0`. An FSI assembly name never appears in a path.
- Fable: the traced build sets `Error.stackTraceLimit = Infinity` once. The site is the innermost `new Error().stack`
  frame whose script path lies outside the Ranvier output directory, recorded as JS `file:line:column`.
  `TraceModel` and `trace.fsx` map it to `.fs:line` through Fable `--sourceMaps` output when the map is available and keep
  the JS location otherwise.

### 4.5 JSONL dump

- Line 1, header: `schema` (1), `target` (`net` or `fable`), `graph`, `checkpoint`, `seqFrom`.
- Line 2, `snapshot`: the fold state at `seqFrom` (nodes, owners, edges, paths, statuses, sibling counters, pending mark
  causes, last `RunStart` per node). Each file is self-contained.
- Each further line is one event.
- The canonical dump sorts container-ordered sections (hash-container iteration); raw event order is kept.
- Payloads are formatted at dump time from a copy of the event array, inside `untrack`, with bounded depth; a
  non-materialised `IEnumerable` is written as its type name.

### 4.6 Checkpoint

`Trace.dump`, `Trace.dumpText` and `Trace.checkpoint` run on the graph thread and raise `InvalidOperationException` when
called off it, or while a flush, a discharge or a computation run is in progress; inside `batch` they succeed.
`checkpoint` writes the file, clears in-memory events and keeps the fold state; the clock continues. Queries over a live
graph (`why`, `whyNot`, `origin`, `snapshot`) run at any time on the graph thread.

## 5. Query API

`Trace` is a module of functions over a live graph, and each node-taking function takes the graph first
(`Trace.why graph node`). `Trace.resolve graph path` maps an identity path to a node id; path-taking overloads of the
queries are deferred to phase 4. All queries return F# records or DUs; `Trace.render graph value` turns any of them into
text.

### 5.1 `Trace.named`

```fsharp
#if RANVIER_TRACE
let named (label: string) (f: unit -> 'T) : 'T = ... // push, run, pop in try/finally, inside the library
#else
let inline named (_: string) ([<InlineIfLambda>] f: unit -> 'T) : 'T = f ()
#endif
```

- The label goes to the first `NodeNew` or `OwnerNew` recorded on the thread while the thunk runs; later nodes are
  unaffected. Nested `named` calls label their own first node. A thunk that creates no node records a `Label` event with
  `Node = 0` (a lint warning).
- The pending-label stack is thread-static in the traced `Tracer`.
- Zero Release cost holds for a literal label. A computed label (`$"row {i}"`) goes through `Trace.label (graph,
  node, text)` (§11 decision 1): a `Conditional("RANVIER_TRACE")` member whose call and arguments compile away in a caller
  built without `RANVIER_TRACE`. It records `Label` with `Arg = 1`; the node's path segment becomes the text, and the paths
  beneath it move with it.

### 5.2 Queries

| Member | Returns |
| --- | --- |
| `Trace.events graph` | Copy of the in-memory events. |
| `Trace.why graph node` / `whyAt graph node run` / `whyDepth graph depth node` | Cause chain of the last (or given) run. Walk: `RunStart` → its `Cause` `Mark` → that `Mark`'s `Cause` (`Write`, `Moved`, `Publish`); `Moved`/`RunEnd` → its `RunStart`; a `Write` with `Other ≠ 0` → the writer's `RunStart`. Ends at a `WhyRoot`: `UserWrite` (`Write`, `Other = 0`), `Created` (run 1, `Cause = 0`, rooted at `NodeNew`), `Pulled reader` (`Cause = 0`, `Other ≠ 0`), `Unrecorded seq` (a cause missing from the log), `Settle` (phase 2), `BeforeCheckpoint file`. Each step: seq, kind, path, site; value from phase 2. `render` folds repeated marks ("× 12 memos marked via Cart.fs:30"). |
| `Trace.whyNot graph node` | For the window since the node's last `RunEnd` (or `NodeNew`), the first matching `WhyNotReason`: `Disposed seq`; `Queued` (the last `Schedule` has no later `RunStart` of the node, clean `CheckResolved` of the node or `FlushEnd`: batch open or flush not reached); `Unobserved` (marked, no observer edge, and no `RunStart`, `Schedule`, `CheckStart` or `CheckResolved` of the node in the window); `CheckedClean (resolvedAt, upstream)`; `SkippedAsRunningReader seq`; `NotReached stopAt` (nearest upstream `Write` with `Flag = 0` or `RunEnd` with `Flag = 0`); `Suspended` (phase 2). Values are added in phase 2. |
| `Trace.origin graph node` | Path, site, label, owner chain (innermost first), creating run. |
| `Trace.history graph node` | Every run with value, status, moved and cause (phase 2). |
| `Trace.waitingOn graph node` | Suspension target, in-flight flight, superseded and dropped flights (phase 2). |
| `Trace.snapshot graph` / `snapshotAt graph seq` | Nodes, edges, owners, paths, statuses; latest values from phase 2. |
| `Trace.dump graph path` / `Trace.dumpText graph` | JSONL file (.NET) / JSONL string (both targets). |
| `Trace.checkpoint graph path` | §4.6; returns the file. |
| `Trace.diff a b` | §5.3. |
| `Trace.lint graph` | §5.4. |

A cause older than the current checkpoint is reported as `BeforeCheckpoint`, naming the file that holds it. On Fable,
`dump` and `checkpoint` are absent; `dumpText` returns the JSONL and the harness writes it with node's `fs`.

### 5.3 `diff`

`a`, `b`: graphs, dumps or checkpoints. Matching, in order: exact identity path; then the path with each site segment
replaced by `<kind>#n` (ordinal among same-kind unlabelled siblings), reported as `moved site`; then node id. Output:
added, removed, ran differently (runs, statuses, edges; values from phase 2), and the first seq where the raw sequences
diverge.

### 5.4 `lint`

Rules: a run after the first with `RunStart.Cause = 0` and `Other = 0`; a node marked `Dirty` between its `RunStart` and
`RunEnd` by a source other than itself; a node run twice in one flush, excluding a re-run preceded by a discharge write
(`Core.fs:1611`); a swallowed exception; a flight dropped while a reader waits (phase 2); an unused `Label`; any
`WalkAbandoned` or `Abandoned` run.

### 5.5 CLI and REPL

`tools/trace.fsx` commands: `why | whynot | origin | history | waiting | tree [--up|--down] | snapshot [--at seq] |
lint | diff a b`, printing `render` text, or JSON with `--json`.

REPL flow (SageFs): evaluate, `Trace.checkpoint g "step-3"`, `Trace.resolve g "/total"` and `Trace.render g (Trace.why g
total)`, edit, re-evaluate, `Trace.diff "step-3.jsonl" "step-4.jsonl"`. The session loads the traced Debug build; memory
grows until `Trace.checkpoint`. Script code uses the `Trace` API directly; FSI does not define `RANVIER_TRACE`.

## 6. Memory and async determinism

### 6.1 Retention

The log keeps every event until `Trace.checkpoint`. Only `Trace.checkpoint` removes events from memory.

### 6.2 Remaining nondeterminism

Async completion order under a threaded dispatcher; hash-container iteration order across targets (fenced by the
canonical sort, §4.5); event order on `Unchecked` graphs (§3.6).

### 6.3 Deterministic preset (phase 2)

- `GraphOptions.Deterministic` is `{ GraphOptions.Default with Dispatcher = Some (ManualDispatcher ()) }`, a fresh
  dispatcher per access. It adds no `GraphOptions` field and no engine code path. It fixes when the inbox drains, not
  completion order.
- `DeterministicFlights` is a caller-side helper in `Api.fs`: `Flight<'T> name : Task<'T>` returns a task backed by a
  caller-controlled `TaskCompletionSource`; `Settle(name, value)`, `Fail(name, exn)` and `SettleAll order` complete them
  in the caller's order on the calling thread. Flights created from its tasks reach the inbox in settle order.
- Phase 2 lists this added public API for an API-diff review.

## 7. Proof gates

`tools/verify-trace.fsx` (run with `dotnet fsi`) runs the gates and the lint. The counter comparison needs an elevated
shell for processor counters; the script calls `counters.ps1` as a process and reports a clear failure when not elevated.

Run tiers: per commit, the lint plus the .NET suite traced and untraced; per phase merge, every gate including
`counters.ps1` and Fable.

| Gate | Check |
| --- | --- |
| 1. Zero Release cost | **Primary, deterministic.** Scan the untraced Release `Ranvier.dll` IL (System.Reflection.Metadata): fail on any `call`, `callvirt`, `ldftn` or `newobj` targeting a `Tracer` member, and on any `TraceEvent`/`TraceLog`/`TraceModel` type. Scan Fable Release JS: fail if any module other than `Trace.js` imports `Trace.js`. Positive control: the same scans over the `RanvierTrace=true` build must report hooks, otherwise the gate fails. A sample using `Trace.named "x" (fun () -> createMemo ...)` has Release IL and Fable JS equal to the same sample without `named`. The packed `.nupkg` DLL has no `AssemblyMetadata("RanvierTrace", ...)`, and its public surface equals the committed baseline plus `Trace.named`, `Trace.label` and the §6.3 preset. A sample using `Trace.label` (`samples/label-zero-cost.fsx`) has Release IL and Fable JS equal to the same sample without the call. The untraced Release IL equals the merge-base Release IL method by method (closure line numbers ignored). **Secondary.** Build the merge-base and HEAD untraced and run `counters.ps1` on both in one elevated session: exact equality on bytes/op, objects/op and every library counter; `InstructionRetired` median delta within max(0.5%, the scenario's calibration spread); both report headers show `RanvierTrace=false`. |
| 2. Same behaviour | The .NET Expecto suite passes under Release (`-p:RanvierTrace=false`) and under Release with `-p:RanvierTrace=true`. `fable/Ranvier.Fable` (`Smoke.fs`) passes under Fable untraced and traced. Allocation assertions are the permitted difference: an `untracedOnly` helper marks them skipped with reason "traced build", and the gate fails if the untraced run skips any test. Retention and weak-reference tests (`Retention.fs`, `DischargeReentry.fs:314`) run in both builds. `samples/trace-sample.fsx` runs 5 times per target with byte-identical canonical dumps. The Fable canonical dump equals the .NET one after site mapping, with payload text reduced to canonical form for `int`, `string`, `bool`, `float` (round-trip) and to the type name otherwise. |
| 3. Complete trace | In a build with `RanvierCounters=true` and `RanvierTrace=true`, run each scenario of `bench/Ranvier.Counters/Scenarios.fs` (and `fable/Ranvier.Counters/Scenarios.fs`) sequentially, one graph per scenario: `Counters.Reset()`, run, then compare `Counters.Snapshot()` with the graph's log. `RunStart` where `NodeNew.Arg = Memo` = `MemoRecomputes`; `RunStart` for `Effect` = `EffectRuns`; `EdgeAdd` = `EdgesAdded`; `EdgeRemove` = `EdgesRemoved`; `ObserverAdd`/`ObserverRemove` = observer inserts/removes; `FlushStart` = `Flushes`; `NodeNew` per kind = `SignalsCreated`/`MemosCreated`/`EffectsCreated`; `GraphNew` + `OwnerNew` = `OwnersCreated`. Edge reconciliation: a traced-only internal accessor reached through `TraceApi` walks every live `SourceList` and `ObserverSet` and compares them with the edges and observers folded from the log. |
| Lint | Hooks are single `Tracer.X(...)` statements; `Tracer` is never bound, piped or passed. No `.Value`, `TryValue`, `EnsureCurrent`, `UpdateIfNecessary`, `Track`, `NextId`, `ToString`, `Equals`, `%A`, `sprintf`, `StackTrace`, `Error`, owner or node construction in hook arguments or `Trace.fs` bodies. Every `#if RANVIER_TRACE` block in `Core.fs`, `Projections.fs` and `Combinators.fs` matches Appendix A and holds declarations only. No `Dictionary`/`HashSet` keyed by node or owner objects in `Trace.fs`. Formatting appears only in `TraceModel`, `TraceApi` dump functions and `Trace.render`. |

Performance and allocation measurements are never taken in a SageFs session or a Debug build.

## 8. Phases

Each phase is its own plan and branch, and merges when its gates pass.

| Phase | Work | Done when |
| --- | --- | --- |
| 0. Seam | Build switch and pack guard, `Tracer` stub with one hook (`GraphNew`), `Trace.named`, `untracedOnly`, `counters.ps1` switches, `verify-trace.fsx` with Gate 1 and the lint. | Gate 1 passes on both targets, including its positive control. |
| 1a. Core provenance on .NET (A, B, C) | `TraceEvents.fs`, `TraceLog`, `ITraced`; lifecycle, propagation, run (every node kind, including Projection and Lookup) and scheduling hooks; every walker-stack site; .NET sites and labels; `TraceModel` fold and paths; `why`, `whyNot`, `origin`, `snapshot`, `render`; `dump`, `dumpText`, JSONL schema 1 with the canonical sort; `samples/trace-sample.fsx`. | Gates 2 and 3 pass on .NET. The `b820ad6` check-walk case names the right reader. |
| 1b. Core provenance on Fable | Fable sites and `stackTraceLimit`; source-map mapping in `TraceModel`; `fable/Ranvier.Trace` harness ported from the prototype; traced Smoke run. | Gates 2 and 3 pass on Fable; cross-target canonical dumps are equal. |
| 2. Values and async (D, E) | `Value`, async and error hooks; `history`, `waitingOn`; value columns in `why`/`whyNot`/`snapshot`; deterministic preset. | An async golden trace (under `GraphOptions.Deterministic` and `DeterministicFlights`) is byte-identical over 5 runs per target. |
| 3. Projections and combinators | `Pass`, `Publish`, `RowNew`, `RowDispose`; structural names for `filter`, `sortBy`, `map`, `mapWith`, `groupBy`. | `why` on a `groupBy` row names the upstream write. |
| 4. Files, diff, tooling (F) | `checkpoint`, identity-path `diff`, `lint`, `tools/trace.fsx`, a SageFs walkthrough in `docs/`. | A before/after REPL diff lists added nodes and changed runs. On a scratch branch from the traced tip, `git revert` each of `348c403`, `8f93594`, `b820ad6` and run its regression scenario: `lint` flags it with the revert and is silent without. A revert that does not apply cleanly is noted and skipped. |
| Later | `Ranvier.Traced` NuGet package; the JSONL schema becomes its public contract. Porting the Expecto suite to Fable. | Separate decisions. |

## 9. Answers to the research doc's open questions

1. **Distribution.** Repo first; a `Ranvier.Traced` package later, sharing the JSONL schema.
2. **In-repo Debug.** Yes: Debug maps to `RanvierTrace=true` in this repo (§3.4); the packed library is untraced, enforced
   by the pack guard.
3. **Log bound.** Unbounded, with caller checkpoints (§6.1).
4. **Release snapshot tier.** No: it would add Release surface.
5. **Naming API.** `Trace.named` labels the first node created by its thunk (§5.1); the prototype's
   `Graph.TraceNameNext` slot is dropped.

## 10. Phase 0 and 1 plan

Conventions for every step:

- **SageFs loop** (default): `list_sessions`, then reuse the worktree's session, or create one on
  `tests/Ranvier.Tests/Ranvier.Tests.fsproj` with `working_directory` set to the worktree. RED with
  `send_fsharp_code`, GREEN by redefinition, persist to `.fs`, `hard_reset_fsi_session rebuild=true`, re-verify, commit.
  From step 0.2 on the session loads the Debug build, which is traced.
- **Exception** (named per step): a build SageFs cannot load (Release, `-p:RanvierTrace=false`, `RanvierCounters`), Fable,
  `dotnet pack`, `counters.ps1`, full gates. Take `acquire_full_build_lease` or `acquire_test_suite_lease` first and
  `release_work_lease` after.
- New tests go in `tests/Ranvier.Tests/Tracing.fs` (traced-only cases inside `#if RANVIER_TRACE`) and in
  `tests/Ranvier.Tests/TraceModelTests.fs` (compiled only when `RanvierTrace` is `true`; pure tests over
  hand-written event arrays). Both are added to the test fsproj after `Retention.fs`.
- Per commit: the lint, plus the .NET suite traced (SageFs session or `dotnet test`) and untraced
  (`dotnet test -c Release -p:RanvierTrace=false`, exception: MSBuild define switch). CRLF; no BOM on new files.

### Phase 0: seam

| Step | Work | Files | Tests | Loop |
| --- | --- | --- | --- | --- |
| 0.1 | SageFs session for the worktree; baseline `dotnet test -c Release` green. | none | none | SageFs; exception for the Release baseline run |
| 0.2 | Build switch: Debug mapping and `RANVIER_TRACE` define in `Directory.Build.targets`; `AssemblyMetadata("RanvierTrace","true")` under the define; `BeforeTargets="GenerateNuspec"` guard; `TraceEvents.fs`, `Trace.fs`, `TraceApi.fs` compile items. | `Directory.Build.targets`, `src/Ranvier/Ranvier.fsproj`, `tests/Ranvier.Tests/Ranvier.Tests.fsproj` | `Tracing.fs`: "the assembly is traced exactly when RANVIER_TRACE is defined" (reads `AssemblyMetadataAttribute`, compares with `#if`). | Exception (MSBuild define switch): `dotnet msbuild -getProperty:DefineConstants` with no `-c`, with `-c Release`, and with `-c Release -p:RanvierTrace=true`; `dotnet pack -c Debug` fails; `dotnet pack -c Release -p:RanvierTrace=false` succeeds. Then SageFs hard reset and the test in the session. |
| 0.3 | `Tracer` stub and one hook. `TraceEvents.fs` with `TraceEventKind.GraphNew`/`Label` and `TraceEvent`; `Trace.fs` with `TraceLog` (events, clock), `ITraced`, `Tracer.GraphNew(graph: obj, rootOwnerId)`; the `#if RANVIER_TRACE` log field and `ITraced` on `Graph`, the log created before the root owner (Appendix A row 1). `TraceApi.fs`: `Trace.events`. | `TraceEvents.fs`, `Trace.fs`, `Core.fs`, `TraceApi.fs` | `Tracing.fs`: "a new graph records GraphNew first, naming the root owner". | SageFs |
| 0.4 | `Trace.named` (§5.1): traced non-inline with a thread-static label stack; untraced inline. With no `NodeNew` hook yet, a traced thunk records `Label` with `Node = 0`. | `TraceApi.fs`, `Trace.fs` | `Tracing.fs`: "named returns the thunk's result" (both builds); "named pops its label when the thunk throws" (traced). | SageFs; exception for the untraced run |
| 0.5 | `untracedOnly` helper (skips with reason "traced build"); wrap both `Equality.fs` "allocates nothing" cases. | `tests/Ranvier.Tests/Support.fs` (new, first in compile order), `Equality.fs` | The wrapped cases; the untraced run reports zero skipped. | SageFs (the skip is visible in the session); exception for the untraced run |
| 0.6 | Bench switches: `counters.ps1` passes `-p:RanvierTrace=false` to both .NET builds and `--configuration Release` to both Fable builds; `fable/Ranvier.Counters` and `fable/Ranvier.Bench` set `RanvierTrace=false`; the report header records `RanvierTrace`. | `counters.ps1`, `bench/Ranvier.Counters/Report.fs`, both Fable fsproj | The report header shows `RanvierTrace=false`. | Exception (counter bench, Fable) |
| 0.7 | `tools/verify-trace.fsx`: Gate 1 IL scan over `Ranvier.dll` (System.Reflection.Metadata); Fable import scan; positive control against the traced build; `Trace.named` sample IL/JS equality (`samples/named-zero-cost.fsx` compiled in a throwaway project); nupkg metadata and public-surface check against a committed `docs/.ai/public-api-baseline.txt`; merge-base vs HEAD `counters.ps1` comparison; source lint (hook shape, Appendix A match, forbidden argument calls). | `tools/verify-trace.fsx`, `docs/.ai/public-api-baseline.txt` | The positive control is the script's own test; scan functions are checked against both built DLLs. | SageFs for the scan and lint functions (`send_fsharp_code` over the built DLL paths); exception (full gates, Fable, pack) to run the script |
| 0.8 | Gate 1 on both targets; commit. | none | Gate 1. | Exception (full gates) |

### Phase 1a: core provenance on .NET

| Step | Work | Files | Tests | Loop |
| --- | --- | --- | --- | --- |
| 1.1 | Every phase-1 kind, `TraceNodeKind`, `RunStatus`. `TraceLog` gains the owner counter, walker stack, per-node open run and first pending dirty mark, and a lock for `Unchecked` graphs; `Pop` recovery (§4.2.2). | `TraceEvents.fs`, `Trace.fs` | `Tracing.fs`: "an Unchecked graph creating nodes from two threads records every NodeNew" (count only). | SageFs |
| 1.2 | Owners: traced `TraceLog`/`TraceId` on `Owner` (Appendix A row 2); inherited at construction, adopted in `Append`/`SetParent`; `OwnerNew`, `OwnerDispose`, `DischargeStart`/`DischargeEnd`. | `Core.fs` | "createRoot records OwnerNew with Flag 1 under the graph root"; "new Owner() records OwnerNew when appended"; "a run scope's OwnerNew names its host node"; `Retention.fs` and `DischargeReentry.fs:314` pass traced. | SageFs |
| 1.3 | Nodes: `NodeNew`/`Dispose` for every node kind beside the existing create counters; `Tracer.Bind` for `ObserverSet`/`SourceList` (Appendix A rows 3 and 4); label assignment from `Trace.named`. | `Core.fs`, `Projections.fs` | "each node kind records NodeNew with its TraceNodeKind"; "named labels the first node only"; "nested named labels each first node"; "a node created in a run records that RunStart as Cause"; "a thunk creating no node records an unused Label". | SageFs |
| 1.4 | Edges and observers: `EdgeAdd`/`EdgeRemove` beside `Counters.EdgeAdded`/`EdgeRemoved`; `ObserverAdd`/`ObserverRemove` beside `Counters.ObserverInserted`/`ObserverRemoved`; the traced-only reconciliation accessor. | `Core.fs`, `Trace.fs`, `TraceApi.fs` | "folded edges equal live sources after a diamond, a dynamic dependency switch and a dispose"; "a RowWatch subscription records ObserverAdd". | SageFs |
| 1.5 | Propagation and scheduling: `Write`, `Mark`, `MarkSkip`, `Schedule`, `BatchEnter`/`BatchExit`, `FlushStart`/`FlushEnd`. | `Core.fs` | "a write marks each reader with the Write as Cause"; "a reader writing its own source records MarkSkip"; "a write inside batch schedules with no RunStart until BatchExit"; "a write inside a memo records the memo as Other". | SageFs |
| 1.6 | Runs: `RunStart`/`Moved`/`RunEnd` for Memo, Effect, AsyncMemo, Boundary, Projection and Lookup at the converge points; `Abandoned` closing. | `Core.fs`, `Projections.fs` | "a cutoff run ends with Flag 0"; "a throwing body ends Error"; "a pending body ends Pending"; "RunStart Cause is the first dirty mark"; "a re-run after a discharge write is two RunStarts in one flush". | SageFs |
| 1.7 | Check walks: push/pop at every walker site (§4.2.2), `CheckStart`/`CheckResolved`; Appendix A line numbers. | `Core.fs`, `Projections.fs`, this spec's Appendix A | One test per site kind (memo, effect, async memo, boundary, projection resolve, projection row refresh, beacon, lookup): "RunStart.Other names the walking reader". Also "the b820ad6 check-walk case names the reader" and "an affinity violation inside a walk leaves an empty walker stack after the flush". | SageFs |
| 1.8 | .NET sites (§4.4), captured by `TraceSite.capture` from `Tracer` bodies. | `TraceSite.fs`, `Trace.fs` | "a node reports the test's file:line"; "a node created through an inline combinator reports the user's line". Manual SageFs check: a node created at the prompt reports `stdin:line` or the script line, and no FSI assembly name. | SageFs (the manual check can only run there) |
| 1.9 | `TraceModel` fold and identity paths (§4.3). | `TraceModel.fs` | `TraceModelTests.fs`, one per rule: an ownerless node under the root; `root#n`; a run-scope host adds one segment; `#n` resets on owner re-run; `@k` incarnations and bare-path resolution; escaping of `/ # [ ] @ \`. | SageFs |
| 1.10 | Queries: `why`/`whyAt`/`whyDepth`, `whyNot`, `origin`, `snapshot`/`snapshotAt`, `render` with mark folding. | `TraceModel.fs`, `TraceApi.fs` | One test per `WhyRoot` (`UserWrite`, `Created`, `Pulled`, and `BeforeCheckpoint` from an event array with a snapshot line) and per phase-1 `WhyNotReason`; "render folds 12 marks into one line". | SageFs |
| 1.11 | `dump`/`dumpText`; JSONL schema 1 with header, snapshot line and canonical sort; §4.6 gating. | `TraceModel.fs`, `TraceApi.fs` | "dumpText twice gives the same text"; "dumpText inside an effect run raises"; "dump off the graph thread raises"; "dumpText inside batch succeeds"; "a dump parses back to the same snapshot". | SageFs |
| 1.12 | `samples/trace-sample.fsx`, ported from the prototype onto the §5 API. | `samples/trace-sample.fsx` | Gate 2's 5-run byte identity. | SageFs to develop; exception (`dotnet fsi` against the Release traced build) for the gate |
| 1.13 | Gate 3 runner: `bench/Ranvier.Counters` gains a `--reconcile` mode, compiled under `RANVIER_TRACE`, that runs each scenario on one graph and compares the counter snapshot with the log (§7). | `bench/Ranvier.Counters/Program.fs`, new `Reconcile.fs` | The mode itself. Removing one hook makes it fail once (checked by hand, then restored). | Exception (MSBuild `RanvierCounters=true` and `RanvierTrace=true`) |
| 1.14 | Gates 2 and 3 on .NET, the lint, Gate 1 again; Appendix A final. | `tools/verify-trace.fsx` | All gates. | Exception (full gates) |

### Phase 1b: core provenance on Fable

| Step | Work | Files | Tests | Loop |
| --- | --- | --- | --- | --- |
| 1.15 | Fable sites: `Error.stackTraceLimit = Infinity`, output-directory filter, JS `file:line:column`. | `Trace.fs` | Smoke check: a node's site names `Smoke.fs.js`. | Exception (Fable) |
| 1.16 | Source-map mapping in `TraceModel` (pure over map text; the harness reads the files). | `TraceModel.fs` | `TraceModelTests.fs`: "a JS location maps to its .fs line through a hand-written map"; "an unmapped location stays JS". | SageFs |
| 1.17 | `fable/Ranvier.Trace` harness, ported from the prototype: runs the sample and writes `dumpText` with node's `fs`. | `fable/Ranvier.Trace/*` | Gate 2 Fable legs. | Exception (Fable) |
| 1.18 | Traced Smoke run; cross-target canonical dump equality; Gate 3 over the `fable/Ranvier.Counters` scenarios. | `tools/verify-trace.fsx` | All gates on both targets. | Exception (Fable, full gates) |

## 11. Interpretations pending user decision

| # | Decision pressed | Interpretation in this spec | Alternative |
| --- | --- | --- | --- |
| 1 | Zero Release cost vs `Trace.named` | **Resolved (option C).** `Trace.named` keeps its literal-label contract. A computed label goes through `Trace.label (graph, node, text)`, a `[<Conditional("RANVIER_TRACE")>]` static member: a caller built without `RANVIER_TRACE` drops the call and its arguments. Gate 1 checks this on `samples/label-zero-cost.fsx` (IL, and JS under Fable). | `named` takes `unit -> string`, which costs a closure unless inlined. |
| 2 | Conditional hooks with stub bodies | Hooks stay `Conditional`. Traced state lives in `#if RANVIER_TRACE` declaration blocks on four engine types (Appendix A), because `ObserverSet` and `SourceList` have no graph in scope. | Tracer-side weak tables (`ConditionalWeakTable`) with no engine `#if`, at one table lookup per mark. |
| 3 | Counter equality in Gate 1 | Exact equality on bytes/op, objects/op and library counters. `InstructionRetired` within max(0.5%, calibration spread). Merge-base vs HEAD replaces a committed baseline. The deterministic IL scan is the primary check. | A fixed 0.5% against a committed baseline, which fails on noise. |
| 4 | Diff by identity path falling back to id | One step between them: the path with site segments replaced by `<kind>#n`, reported as `moved site`, so a REPL edit that shifts line numbers still matches (§5.3). | Path, then id only. |
| 5 | Payload fidelity | Payloads other than library nodes are held by reference until checkpoint; a user value that references nodes retains them (§4.1). | Format at record time, which calls user code inside the engine (rule 2). |
| 6 | Gates on both builds | The Fable leg of Gate 2 is `Smoke.fs`, not the Expecto suite, which does not compile under Fable; porting it is a Later row. | Port the suite before phase 1b. |
| 7 | Identity paths | Row key text in paths is canonical for primitive keys and the type name otherwise (§4.3); equal-text keys of other types fall to `#n` ordinals. | Formatted key text at dump time, which differs across targets. |

### 11.1 Open items

- `Lookup.Recompute` runs a cell that received no `Mark` (the `Lookup` receives it), so the cell's `RunStart.Cause` is 0
  and a `why` walk through a `Lookup` cell ends at the cell, short of the upstream write. Planned for phase 3 with the
  other projection hooks.

## Appendix A. `#if RANVIER_TRACE` blocks in engine files

Declaration blocks only; the lint matches this list. Line numbers are the `#if RANVIER_TRACE` lines at commit time.

| File | Type | Contents | Lines |
| --- | --- | --- | --- |
| `Core.fs` | `Graph` | `interface ITraced`, reading and writing the root owner's `TraceLog`. | 1049 |
| `Core.fs` | `Owner` | `TraceLog` and `TraceId` fields; `interface ITraced`. | 547, 753 |
| `Core.fs` | `ObserverSet` | `TraceLog` and owner-id fields; `interface ITraced` and `ITracedEdges`. | 126, 296 |
| `Core.fs` | `SourceList` | `TraceLog` and owner-id fields; `interface ITraced` and `ITracedEdges`. | 366, 465 |
