---
title: Queries and mutations
order: 24
---

# Queries and mutations

`Ranvier.Query` is a separate preview package for partially loaded remote data. It
adds query identity, page ownership, request retirement, and mutation reconciliation
over Ranvier's existing graph. Results remain ordinary immutable records.

## Dictionary workflow

```fsharp
open Ranvier.Query
open System.Collections.Generic

type WordPreview = { Id: int; Def1: string; Def2: string }
type SectionPreview = { Id: int; Name: string }
type Section = { Id: int; Name: string; Words: WordPreview list }
type Word = { Id: int; Def1: string; Def2: string; OtherFields: Map<string,string> }
type Index = { TotalWordCount: int; Sections: SectionPreview list }
type Saved = { Word: Word; SectionId: int; TotalWordCount: int }

type Page =
    | DictionaryIndex of QueryLease<Index>
    | Section of QueryLease<Section>
    | WordEdit of Word // independent, unsaved draft

type MainModel = { History: Page list }

let client = new QueryClient(graph)
let index = client.Define(EqualityComparer<unit>.Default, loadIndex)
let sections = client.Define(EqualityComparer<int>.Default, loadSection)
let words = client.Define(EqualityComparer<int>.Default, loadWord)
```

The loaders have shape `key -> CancellationToken -> Task<record>`. `Define` performs
no IO. Opening Home calls `index.Acquire ()`; opening a section calls
`sections.Acquire sectionId`; opening an existing editor acquires only that word's
details and copies the accepted record into a draft. The index loads summaries,
each visited section loads previews, and each visited editor loads one full word.
Neither previews nor details require per-field signals.

```fsharp
let reconcile (saved: Saved) = [
    index.UpdateIfLoaded((), fun old ->
        { old with TotalWordCount = saved.TotalWordCount })
    sections.UpdateIfLoaded(saved.SectionId, fun old ->
        { old with Words = upsertById (toPreview saved.Word) old.Words })
    words.UpdateIfLoaded(saved.Word.Id, fun _ -> saved.Word)
]

let save draft = client.Mutate(draft, saveWord, reconcile)
```

`saveWord` returns the server's authoritative total and saved word. `upsertById`
replaces a matching preview or appends a new one; that policy assumes a complete
section preview list with insertion ordering. The editor accepts the returned word
into its draft. The retained section and index already reflect the save when Back
returns to them. Reconciliation does not inspect navigation history, fetch unvisited
entities, or create absent cache entries. If a targeted existing query is still
loading, its old response is retired and the query becomes stale instead.

For sorted, filtered, or paginated previews, define the complete request key and
invalidate matching cached pages instead of guessing list membership:

```fsharp
type SectionKey = { SectionId: int; Page: int; Filter: string }
// Include ordering and any other request parameters in the key as well.
let reconcilePaged saved = [
    pages.InvalidateWhere(fun key -> key.SectionId = saved.SectionId)
    index.UpdateIfLoaded((), fun old ->
        { old with TotalWordCount = saved.TotalWordCount })
]
```

## Loading and ownership

Invalidation retains existing data and performs no IO. On Back, call the returning
lease's `Ensure()` to load missing or stale data. `Ensure` joins an existing request;
`Refresh()` supersedes it. Reads never start IO. `Value` participates in native
pending/error boundaries during the initial load; `State` always returns a snapshot.
Refresh and refresh failure keep the last accepted `Value`. Metadata-only changes
wake `State` consumers without waking `Value` consumers. Internally, an async source
provides initial-load suspension; it does not replace the identity and reconciliation
layer.

Keep leases for hidden pages in History. Dispose the removed page's leases on Back;
the final lease release evicts that query and requests cancellation. Acquiring under
a Ranvier owner also registers automatic cleanup. Construct the client under the
application owner, and dispose it on Home/logout before creating a fresh session.
The client owns saves, so closing an editor does not cancel its in-flight save.
The traced build also retains published values in diagnostic history; eviction does
not erase that history.
Client disposal cancels queued callers and requests remote cancellation; a server
may already have applied the write. Late results cannot change the new session.

An Elmish program can own the client as an application service and send the result
of `save` back as a message. Handle all three outcomes:

- `Applied saved`: remote success and local publication completed.
- `RequestFailed error`: reconciliation was skipped; no automatic retry occurs.
- `ReconciliationFailed(saved, error)`: the server succeeded, but local staging
  failed. Keep the receipt, show the local failure, and explicitly refresh stale
  queries. Do not resubmit the saved write merely to repair the cache.

The client serializes remote writes through reconciliation, including different
result types. `Commit` also accepts an explicit list of edits. It validates every
updater, predicate, and data comparison before publishing in one graph batch.
Repeated edits compose in order. Callbacks must return immutable values and must
not reenter client operations. All client/lease operations follow the graph's thread
contract; async completions return through `Graph.Dispatch`.

## Performance and measurement

The [local reconciliation benchmark](../benchmarks/queries.md) compares this
three-page workflow with a hand-written update on the same machine. At 10 and
1,000 previews, a three-query `Commit` adds about **0.42–0.53 µs per save** and
**1,856 bytes of temporary allocation**. At 10,000 previews, timings overlap and
list reconstruction dominates. These untraced .NET figures exclude fetching,
acquisition, the `Mutate` task/FIFO wrapper, and rendering.

That cost buys staged atomic publication, shared cached values, and retirement of
superseded requests. The package also centralizes request sharing, cancellation,
and page ownership. Those services avoid duplicating lifecycle logic throughout
the application; the benchmark does not establish an application-wide speedup.

The implementation's costs are:

- A keyed cache lookup uses a dictionary: expected constant lookup work, plus the
  key's hashing and equality costs. Acquiring a lease allocates ownership state;
  creating a new entry also creates reactive and request state.
- A successful patch stages the described edits, compares each affected result,
  then publishes the affected entries in one batch. Updaters and result equality
  can dominate this work. Scanning or rebuilding a list of `n` previews remains
  linear in `n`; replacing a cached record does not make its fields incremental.
- `InvalidateWhere` scans the existing entries in that family. It does not scan
  the remote dictionary or fetch matching data. Recovery after reconciliation
  failure invalidates the client's existing entries.
- Cache data is retained while leases exist. Shared keys share an entry; releasing
  its final lease evicts it. Trace history can retain values independently.
- Remote writes run in one client-wide FIFO, including their reconciliation.
  This preserves ordering but limits concurrent write throughput.

The benchmark measures time and allocated bytes for one-word edits against
increasing preview-list sizes. Further measurements should cover retained memory
after release, request count, downstream computation/render count, cached reads,
shared-key acquisition, metadata-only refresh, and predicate invalidation. Compare
identical payloads, update rules, equality policies, dispatch, and UI observations;
separate local work from network latency and traced from untraced results.

## Comparison with Elmish

In the original independent-page model, `Saved` must find the affected pages and
patch each copy, or mark them dirty and reload on Back. A direct history scan grows
with the navigation stack; the list patch still grows with the preview list. The
query layer addresses loaded entries by key and lets pages acquiring the same key
observe the same accepted result. It owns sharing, request retirement, disposal,
and publication, so those rules are implemented once.

An Elmish model with a shared keyed cache can achieve the same lookup and data
sharing costs. It may use fewer allocations because it does not need this layer's
leases, reactive nodes, staging objects, and task bookkeeping. Ranvier.Query does
not replace Elmish: History and editor drafts can remain Elmish models while the
client manages fetched data.

Reactive consumers of unrelated entries are not notified by a targeted save.
This can reduce downstream work compared with broad subscriptions to a whole
model, but Elmish selectors, equality checks, and UI memoization can also avoid
unnecessary rendering. Consumers of the changed whole record still reevaluate;
a selector exposing one field still runs to determine whether that field changed.

This API centralizes cache consistency. It still requires the application to describe
how a saved result affects each query shape. Whole-record selectors still evaluate
when their record source changes, and list updates still reconstruct lists. Use a
memo with an explicit comparer to cut off unchanged projections, or keyed incremental
collections where collection computation itself needs finer granularity.
