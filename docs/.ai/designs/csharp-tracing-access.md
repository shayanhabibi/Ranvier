# C# access to tracing: design

**Status:** implemented. `Ranvier.CSharp.Tracing` (`src/Ranvier.CSharp/Tracing.fs`) and the "Tracing" section of
`guide/csharp.md` mirror the F# queries. `Trace.errorOrigin` (§3) is **not added**: the `ErrorOrigin` property from
[failure provenance](failure-provenance.md) answers "where did this error come from" in every build, traced or not, and
C# reaches it through the property (wave B decision). The stale gap line in `concepts/ecosystem.md` ("has no tracing")
is already gone at `b693a42`. This note maps the three questions from research §8 to the existing surface. Line
references are to `c631f23`, except those marked "at `b693a42`".

## 1. What F# tracing offers, and the C# mirror

| F# (`TraceApi.fs`) | C# (`Tracing.fs`) | Builds |
| --- | --- | --- |
| `Trace.named` :33 | `Named` :33, :37 | all |
| `Trace.label` :15 | `Label` :45-47, `[Conditional("RANVIER_TRACE")]` | all |
| `events` :46 | `Events` :51 | traced |
| `origin` :51 | `Origin` for a node :56 or a path :116 | traced |
| `why`, `whyAt`, `whyDepth` :105-118 | `Why`, `WhyDepth` :61-72, :121-132 | traced |
| `whyNot` :122 | `WhyNot` :75, :136 | traced |
| `history` :126 | `History` :79, :141 | traced |
| `waitingOn` :133 | `WaitingOn` :83, :146 | traced |
| `snapshot`, `snapshotAt` :172-176 | `Snapshot` :87-92 | traced |
| `resolve` :183 | `Resolve`, returning `Nullable<int>` :98 | traced |
| `reconcile` :140 | `Reconcile` :153 | traced |
| `dumpText`, `dump` :237, :245 | `DumpText`, `Dump` :160-166 | traced |

C# receives rendered text from `Trace.render` (`TraceApi.fs:192`), never the option-bearing records (`TraceOrigin`,
`Why`), which carry `string option` and `int list` (`TraceEvents.fs:227-231`). `Events` returns `TraceEvent[]`, an F#
struct record (`TraceEvents.fs:206-217`) whose fields hold no options.

## 2. The three questions

- **What is alive?** `Snapshot` answers it: the owner tree, with each node's status, run count and sources.
- **Why did this run?** `Why`, `History` and `WhyNot` answer it.
- **Where did this error come from?** No trace query answers it. The `ErrorOrigin` property from failure provenance
  answers it in every build (§3).

The gap was measured with an FSI script against a traced Debug build of `c631f23`. The graph was:

- `parse`, a memo that throws while `x > 0`
- `mid`, a memo reading `parse`
- `view`, a memo reading `y` and then `mid`
- an effect reading `view`

The script wrote `x = 1` and then `y = 5`:

- `Why(view)` returns `#70 Write /y`. That is the cause of the run, not the origin of the error.
- `Snapshot` shows `/parse`, `/mid` and `/view` all as `error`, and does not show which of them threw.
- The first event whose payload is `view`'s exception is `#51 Moved /parse`. `Origin("/parse")` returns
  `/parse Memo at err.fsx:10`, which is the answer.
- The .NET `StackTrace` of the exception contains the throw site, err.fsx:10, but it names no node. It also gains a
  `--- End of stack trace from previous location ---` segment for each hop, three segments here. Under Fable the JS
  stack holds only the throw.

The log already holds the answer:

- A failed read propagates the same exception instance. The memo cutoff compares errors by reference
  (`Core.fs:2393`, `4009`), and `captureFailure` reuses the held capture (`Platform.fs:337-349`).
- `Moved` records the exception as its payload (`Core.fs:2405`, `3291`, `4027`).
- An async failure is a `Fail` event whose payload is the exception, recorded by `Tracer.FlightSettled` and
  `Tracer.SourceSettled` (`Trace.fs:1028-1040, 1063-1075` at `b693a42`); `why` reads that payload at
  `TraceModel.fs:375`.
- Effects and projections are outside this: an `Effect` records its failure only through its `RunEnd` status
  (`Core.fs:2876-2879`; `EffectOn` at `3029-3047`), and projections log `Moved` with a null payload
  (`Projections.fs:436, 610, 650`).

## 3. `errorOrigin`: not added

The wave B decision drops this query. The `ErrorOrigin` property proposed in `failure-provenance.md` §3 sits on
`Memo`, `AsyncMemo`, `Boundary`, `Effect`, `Projection` and `AsyncSource`, compiles in untraced builds, and covers the
effects and projections a payload search misses (§2). A traced-only query would repeat it. The proposal below is kept
as the record of what was considered.

```fsharp
// TraceApi.fs, traced builds
/// The earliest event carrying the node's current exception: the Moved of the node whose run threw it,
/// or the Fail of the flight or async source that failed with it.
val errorOrigin : graph: Graph -> node: INode -> TraceErrorOrigin
```

```csharp
#if RANVIER_TRACE
public static string ErrorOrigin(Graph graph, INode node);
public static string ErrorOrigin(Graph graph, string path);
public static string ErrorOrigin(Graph graph, Exception error);
#endif
```

```text
error /view: Exception: bad input
  thrown by /parse Memo at err.fsx:10, #51
  why /parse run 2
    #50 RunStart /parse <- /mid
    #41 Mark /parse <- /x
    #40 Write /x = 1
```

- **Node overload.** It takes the payload of the node's last `Moved` or `Fail` that is an exception, finds the
  earliest event whose payload is the same reference, and appends `whyAt` for the run that contains that event.
- **Exception overload.** This overload serves `ReactiveBindings` users. `BoundValue.Error` (`Bindings.fs:149`) and
  a `catch` block hold an exception, not a node. The overload starts from the earliest event that carries the
  exception.
- **A reused exception instance.** When user code rethrows one cached exception from two nodes, the query reports
  the earlier carrier. The documentation will state this.

## 4. Cost model

- **Untraced builds: nothing.** The query sits under `#if RANVIER_TRACE` (`TraceApi.fs:27`, `Tracing.fs:49`).
  `tools/verify-trace.fsx` asserts that the untraced Release IL equals the merge base's, method by method
  (`guide/tracing.md`, "How the zero-cost claim is checked").
- **Traced builds: recording is unchanged.** The design adds no event kind and no recorder call.
- **Query cost.** O(events) time when called. The query allocates one copy of the events (`Trace.events` copies
  them, `TraceApi.fs:46`), plus the rendered text.
- **Benchmarks.** No hot-path benchmark is needed. The existing trace gates cover the claim.

## 5. Fable, AOT and trimming

- The F# `errorOrigin` compares object payloads by reference. Fable's traced build records payloads: `TraceLog`
  stores `Payload` on every append with no Fable branch (`Trace.fs:105-122` at `b693a42`), including the `Moved`
  value (`Tracer.Moved`, `Trace.fs:975`) and the `Fail` exception (`Tracer.FlightSettled`, `Tracer.SourceSettled`).
  The query and its rendering port without `#if`.
- The C# members are .NET only and use no reflection. Traced builds are development builds, so AOT does not
  target them, and nothing in this design prevents it.

## 6. Breaking?

No. Traced-only members fall outside `public-api-baseline.txt`, which records the untraced surface.

## 7. Documentation changes

- `concepts/ecosystem.md`: the "has no tracing" clause is already gone at `b693a42`; the `ValueOption` gap line stays
  (see `csharp-valueoption-removal.md`).
- `guide/tracing.md` and `guide/csharp.md`: no change here. "Where did this error come from" is documented with the
  `ErrorOrigin` property by failure provenance.

## 8. Alternatives

- **A `Throw` event recorded when a body's exception is not a propagated read.** Queries become cheaper, but every
  node kind's catch path gains a recorder call. Payload identity already answers the question.
- **Make `Why` follow the error for a failed run.** This changes the meaning of existing output that tests and
  users read.
- **Typed C# result objects.** These need C# mirrors of the `Why` and `TraceOrigin` records. Text answers the three
  questions, and `Events` and `DumpText` serve tools.

## 9. Recommendation

Originally: add `Trace.errorOrigin` with the three C# overloads, and fix the stale gap line.

Decided (wave B): `Trace.errorOrigin` is not added; `ErrorOrigin` from failure provenance covers it. The gap line was
already fixed. Nothing else remains in this note.

## 10. Questions for the maintainer

1. Should `Trace.errorOrigin` be added, with its C# mirror? Decided: no.
2. Should `ErrorOrigin(graph, Exception)` be included? Moot.
3. Should the error origin also appear in `History` lines for error runs? Moot for this note; a follow-up could render
   `ErrorOrigin` in `History` once failure provenance lands.


## Reviewer corrections

Applied on `b693a42`:

- Effects and projections carry no exception payload: stated in §2, and covered by the `ErrorOrigin` property that
  replaces the query (§3).
- The Fable payload claim now cites the recording code (`TraceLog.append`, `Tracer.Moved`, `Tracer.FlightSettled`,
  `Tracer.SourceSettled`) instead of `guide/tracing.md` Limits; the Fable build keeps the payload (§5).
- §1 names the option-bearing records and states that `Events` returns `TraceEvent[]`, whose fields hold no options.
- §2 cites `Tracer.FlightSettled` / `Tracer.SourceSettled` as the recorders of `Fail`, with `TraceModel.fs:375` as the
  reader.

Not applied here:

- §5 "Traced builds are development builds, so AOT does not target them" against `aot-trim-analysis.md` §7 (an AOT CI
  publish with `-p:RanvierTrace=true`). The wave B default is to AOT-check the untraced build only; the alignment of the
  two notes is done with the AOT/trim work.
