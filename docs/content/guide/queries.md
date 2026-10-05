---
title: Queries and mutations
order: 24
---

# Queries and mutations

`Ranvier.Query` is a separate preview package for partially loaded remote data. It
adds query identity, page ownership, request retirement, and mutation reconciliation
over Ranvier's existing graph. Results remain ordinary immutable records.

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

This API centralizes cache consistency. It still requires the application to describe
how a saved result affects each query shape. Whole-record selectors still evaluate
when their record source changes, and list updates still reconstruct lists. Use a
memo with an explicit comparer to cut off unchanged projections, or keyed incremental
collections where collection computation itself needs finer granularity.
