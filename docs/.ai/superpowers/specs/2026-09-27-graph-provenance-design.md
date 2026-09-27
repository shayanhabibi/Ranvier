# Graph provenance: a traced build with full provenance for development and the REPL

**Status.** Design approved in conversation on 2026-09-27. Basis: [RESEARCH-graph-transparency.md](../../RESEARCH-graph-transparency.md)
and the prototype under the local tag `research/graph-transparency-prototype` (`a36ef9f`, `5584e0a`, based on `b820ad6`).
This spec supersedes the research doc's §8 phased plan and answers its §9 open questions (see §9 below).

## 1. Goal

A development mode in which a person or an agent, in tests, samples or a SageFs session, can answer for any node:

| Id | Question | Query |
| --- | --- | --- |
| A | Why did it run? | `Trace.why` |
| B | Why did it not run? | `Trace.whyNot` |
| C | Where did it come from? | `Trace.origin` |
| D | What was its value at each run? | `Trace.history` |
| E | What is it waiting on, and which flight settled or was dropped? | `Trace.waitingOn` |
| F | What changed between two runs, or before and after an edit? | `Trace.diff` |

## 2. Hard rules

1. **Release pays nothing.** No runtime branch, allocation or call is added to a build without the `RANVIER_TRACE`
   symbol. The one accepted residue is the stub `Tracer` type (§3.2) and `Trace.named`.
2. **The traced build runs Release's paths.** Tracing may cost any time or memory. It never changes what the engine
   reads, schedules, runs, caches or disposes, and never calls user code while the engine is running.
3. **Tracing records; it never steers.** Determinism for async comes from opting into `GraphOptions.Deterministic`,
   ordinary library code present in Release (§6.3), never from a trace hook.

## 3. Components

| Unit | File | Role | Untraced build |
| --- | --- | --- | --- |
| Build switch | `src/Ranvier/Ranvier.fsproj`, `Directory.Build.props` | `RanvierTrace=true` defines `RANVIER_TRACE`. Repo builds with `Configuration=Debug` set `RanvierTrace=true` unless set explicitly; `dotnet pack` forces it off. | Symbol undefined |
| `Tracer` | `src/Ranvier/Trace.fs`, before `Core.fs` | Internal type of `[<Conditional("RANVIER_TRACE")>]` static hooks writing to the graph's log. Holds the check-walk walker stack. | Empty stub bodies; call sites and their arguments removed by the compiler on .NET and Fable |
| Hook sites | `Core.fs`, `Projections.fs`, `Combinators.fs` | One-line `Tracer.*` calls. Arguments are fields already in scope. | Removed |
| `TraceModel` | `src/Ranvier/TraceModel.fs` | Pure analyser over events: fold to snapshot, cause walks, identity paths, diff, lint, render. Never touches a live graph. | Not compiled |
| `Trace` API | `src/Ranvier/TraceApi.fs`, after `Combinators.fs`, public | Queries over a live graph, a dump or a checkpoint (§5). | Only `Trace.named` exists |
| CLI | `tools/trace.fsx` | `dotnet fsi` front end over JSONL dumps; loads `TraceModel.fs` as source. | Not part of the library |
| Gates | `tools/verify-trace.fsx` | Runs the gates of §7. | Not part of the library |
| Deterministic preset | `Api.fs` | `GraphOptions.Deterministic`, `DeterministicFlights`. | Present |

### 3.1 Boundaries

The engine calls only `Tracer`. `Tracer` writes only the log. `TraceModel` reads only events. `TraceApi` connects a live
graph to `TraceModel`. `TraceModel` is therefore testable on hand-written event arrays, and `trace.fsx` runs without the
library loaded.

### 3.2 The `Conditional` seam

Verified by a throwaway spike on 2026-09-27 (Fable 5.18.0, .NET 10): with the symbol undefined, a
`[<Conditional("RANVIER_TRACE")>]` static member call is removed together with its argument expressions on both .NET
Release and Fable JS; the caller's module imports nothing from `Tracer`. With the symbol defined, both targets emit the
call. `Tracer` bodies sit in `#if RANVIER_TRACE ... #else` with empty stubs, so the untraced build ships the stub type's
metadata (.NET) and an unimported `Trace.js` (Fable) only.

`Conditional` requires methods returning `unit`. A hook that must return a value (the walker stack push) is split into
a unit push and a unit pop.

### 3.3 Relation to `RanvierCounters`

`RANVIER_COUNTERS` stays independent. Gate 3 (§7) builds the tests with both `RanvierCounters=true` and
`RanvierTrace=true`.

## 4. Events

### 4.1 Record

`TraceEvent` is a struct: `Seq: int64` (per-graph clock), `Kind: TraceEventKind`, `Node: int`, `Other: int`,
`Arg: int`, `Flag: int`, `Cause: int64` (the `Seq` of the causing event, or 0), `Payload: obj` (a value, key or
exception held by reference, never formatted at record time). Node ids are the graph's existing ids; owners use a
tracer-side counter so `NextId` is never read.

### 4.2 Kinds

| Group | Kinds | Serves |
| --- | --- | --- |
| Lifecycle | `GraphNew`, `NodeNew`, `OwnerNew`, `Dispose`, `Label`, `Site` | C |
| Propagation | `Write`, `Mark`, `CheckStart`, `CheckResolved`, `EdgeAdd`, `EdgeRemove` | A, B |
| Runs | `RunStart`, `RunEnd` (`Flag`: moved; `Arg`: status), `Value` | A, D |
| Async | `Suspend`, `FlightStart`, `Settle`, `Fail`, `FlightDrop`, `InboxRun` | E |
| Scheduling | `FlushStart`, `FlushEnd`, `BatchEnter`, `BatchExit`, `DischargeStart`, `DischargeEnd` | A, E |
| Errors | `Swallowed`, `ErrorRecorded`, `CleanupThrew` | A |
| Projections | `Pass`, `Publish` (`Flag`: keys moved), `RowNew`, `RowDispose` | A, B, C |

`RunStart` and `RunEnd` are paired on every exit path (try/finally in the traced build only). `RunStart.Other` is the
reader that pulled the run, taken from the walker stack during check walks (`Projection.ResolveCheck`,
`IBeaconHost.Refresh`) and from `CurrentComputation` elsewhere.

### 4.3 Identity path

A node's path is its owner chain plus its own segment, e.g. `root#0/Cart.fs:12#0/total`. The segment is the label when
one is set (`Trace.named`), otherwise `site#n`, `n` being the creation order among siblings from the same site.
Combinator nodes take structural segments: `filter(Cart.fs:30)/row[k=7]`, the site being the user's call site. Keys are
held by reference and formatted at dump time.

### 4.4 Creation site

The traced build captures a stack at node creation and keeps the first frame outside `Ranvier`. .NET resolves
it to `file:line` in-process. Fable records the JS `file:line:column` from `new Error().stack`; `TraceModel` and
`trace.fsx` map it to `.fs:line` through Fable `--sourceMaps` output when the map is available, and keep the JS location
otherwise.

### 4.5 JSONL dump

Line 1 is a header: `schema` (1), `target` (`net` or `fable`), `graph`, `checkpoint`, `seqFrom`. Each further line is
one event. Payloads are formatted at dump time (`%A` on .NET, Fable's equivalent). `Trace.dump` and `Trace.checkpoint`
raise `InvalidOperationException` when called during a flush, so formatting never runs inside the engine.

A payload is a reference: a value mutated after it was recorded dumps in its current state. This is a documented limit.

## 5. Query API

All queries return F# records; `Trace.render` turns any of them into text.

| Function | Returns |
| --- | --- |
| `Trace.named label (fun () -> ...)` | The created node. Inlines to the call in Release. |
| `Trace.why node` / `whyAt node run` | Cause chain of the last (or given) run back to its root cause: a user write, a settle, or a pulling reader. Each step: seq, path, site, value. `render` folds repeated marks ("× 12 memos marked via Cart.fs:30"); `depth` limits the walk. |
| `Trace.whyNot node` | Most recent reason it did not run: cutoff (both values), check resolved unchanged, no reader, disposed. |
| `Trace.origin node` | Path, site, label, owner chain, creation cause. |
| `Trace.history node` | Every run with value, status, moved and cause. |
| `Trace.waitingOn node` | Suspension target, in-flight flight, superseded and dropped flights. |
| `Trace.snapshot graph` / `snapshotAt graph seq` | Nodes, edges, owners, statuses, latest values. |
| `Trace.checkpoint graph path` | Writes the log to JSONL, clears in-memory events, keeps a snapshot; returns the file. |
| `Trace.dump graph path` | Writes JSONL without clearing. |
| `Trace.diff a b` | Nodes matched by identity path; added, removed, ran differently (runs, values, edges); plus the first seq where the raw sequences diverge. `a`, `b`: graphs, dumps or checkpoints. |
| `Trace.lint graph` | Anomalies: a node run twice in one flush, a run with no mark, a swallowed exception, a flight dropped while a reader waits, a node marked dirty while running. |

A node argument is a node or a path string; path strings also resolve against dumps. A cause older than the current
checkpoint is reported as such, naming the file that holds it.

`tools/trace.fsx` commands: `why | whynot | origin | history | waiting | tree [--up|--down] | snapshot [--at seq] |
lint | diff a b`, printing `render` text, or JSON with `--json`.

REPL flow (SageFs): evaluate, `Trace.checkpoint g "step-3"`, `Trace.why total |> Trace.render`, edit, re-evaluate,
`Trace.diff "step-3.jsonl" "step-4.jsonl"`.

## 6. Memory, async and determinism

### 6.1 Retention

The log keeps every event until `Trace.checkpoint`. There is no automatic checkpoint: file boundaries stay a function
of the caller, and dumps stay byte-identical across runs.

### 6.2 Nondeterminism that remains

Async completion order under a threaded dispatcher, and hash-container iteration order across targets. Canonical dumps
sort container-ordered sections; raw event order is kept.

### 6.3 Deterministic preset

`GraphOptions.Deterministic` selects the `ManualDispatcher`; `DeterministicFlights` settles registered flights in a
caller-given order. Both are ordinary library code, present and testable in Release.

## 7. Proof gates

`tools/verify-trace.fsx` (run with `dotnet fsi`) runs every gate and the lint. It runs before any commit touching
`Trace.fs`, `TraceModel.fs`, `TraceApi.fs` or a hook site. The counter bench needs an elevated shell for processor
counters; the script calls `counters.ps1` as a process and reports a clear failure when not elevated.

| Gate | Check |
| --- | --- |
| 1. Zero Release cost | Untraced counters equal the committed baseline on bytes/op, objects/op and library op counts; instruction counts within 0.5 % on .NET. No `Tracer_` identifier in Fable release JS outside `Trace.js`. The packed `.nupkg` contains no `TraceModel` or query types. |
| 2. Same paths | Full suite passes on .NET Release, .NET traced, Fable Release, Fable traced. Tests asserting zero allocation (e.g. `Equality.fs` "allocates nothing") run untraced only, marked by one shared helper: the one permitted difference. The trace sample runs 5 times per target with byte-identical dumps; the Fable dump equals the .NET dump after site mapping. |
| 3. Complete trace | With `RanvierCounters` and `RanvierTrace`, per-kind event counts equal the counters (memo `RunStart` = `MemoRecomputes`, effect `RunStart` = `EffectRuns`, `EdgeAdd` − `EdgeRemove` = edge totals, `FlushStart` = `Flushes`). An edge reconciliation replays edges against each observer's live source count after every test. |
| Lint | No `.Value`, `TryValue`, `EnsureCurrent`, `UpdateIfNecessary`, `Track`, `NextId`, `ToString`, `Equals`, `%A`, `sprintf`, owner or node construction in `Tracer.*` arguments or `Trace.fs` bodies. Formatting appears only in `TraceModel`, `Trace.dump` and `Trace.render`. |

## 8. Phases

Each phase is its own plan and branch, and merges when its gates pass.

| Phase | Work | Done when |
| --- | --- | --- |
| 0. Seam | Build switch, Debug mapping, `Tracer` stub, `Trace.named`, `verify-trace.fsx` with Gate 1 and the lint. | Gate 1 passes on both targets. |
| 1. Core provenance (A, B, C) | Lifecycle, propagation, run and scheduling hooks mapped against current main; walker stack; creation sites and source maps; `TraceModel` fold; `why`, `whyNot`, `origin`, `snapshot`, `render`. | Gates 2 and 3 pass. The `b820ad6` check-walk case names the right reader. |
| 2. Values and async (D, E) | `Value`, async and error hooks; `history`, `waitingOn`; deterministic preset. | An async golden trace is byte-identical over 5 runs per target; Gate 3 covers async counters. |
| 3. Projections and combinators | `Pass`, `Publish`, `RowNew`, `RowDispose`; structural names for `filter`, `sortBy`, `map`, `mapWith`, `groupBy`. | `why` on a `groupBy` row names the upstream write. |
| 4. Files, diff, tooling (F) | `checkpoint`, `dump`, JSONL schema 1, identity-path `diff`, `lint`, `tools/trace.fsx`, a SageFs walkthrough in `docs/`. | A before/after REPL diff lists added nodes and changed runs. Rebuilt parents of `348c403`, `8f93594`, `b820ad6`: `lint` flags each bug before its fix and is silent after. |
| Later | `Ranvier.Traced` NuGet package; the JSONL schema becomes its public contract. | Separate decision. |

## 9. Answers to the research doc's open questions

1. **Distribution.** Repo first; a `Ranvier.Traced` package later, sharing the JSONL schema.
2. **In-repo Debug.** Yes: Debug maps to `RanvierTrace=true` in this repo; the packed library stays untraced.
3. **Log bound.** Unbounded with caller checkpoints (§6.1).
4. **Release snapshot tier.** No: it would add Release surface.
5. **Naming API.** `Trace.named` scope; the prototype's `Graph.TraceNameNext` slot is dropped.
