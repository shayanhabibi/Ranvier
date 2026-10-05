---
title: Dictionary navigation
order: 25
---

# Dictionary navigation

The compiled example in `examples/Ranvier.Query.Dictionary/Navigation.fs` implements
the dictionary workflow with an Elmish-style `init`, `update`, and command list.
It fetches the index, visited sections, and individual edited words separately.
Fetched results remain immutable records; the editor keeps an independent draft.

The model has this shape (abbreviated):

```fsharp
type Editor = {
    SectionId: int
    Detail: QueryLease<Word> option // None when adding a word
    Draft: Word option
    Saving: bool
}

type Content =
    | IndexPage of QueryLease<Index>
    | SectionPage of QueryLease<Section>
    | EditorPage of Editor

type Page = { Owner: Owner; Content: Content; Loading: bool; Error: exn option }
type Model = { Session: Session; History: Page list }
```

`Session` owns one client and defines its index, section, and word query families
once. Opening a page creates an owner attached to the session owner, and acquires
its query with `AcquireOwned(key, pageOwner)`. Hidden pages retain their owners.
Back disposes the removed page's owner and calls `EnsureAsync` on the returning
page; fresh cached data completes without another fetch. Home disposes the entire
session and creates a fresh index. Page identity prevents late messages from
modifying a new page that happens to use the same query key.

The loading command awaits `EnsureAsync`, then sends `Loaded` or `LoadFailed`.
For an editor, `Loaded` copies the accepted word into its draft only if it has no
draft yet. `DraftChanged` changes that local record. Loading and save commands
catch `OperationCanceledException`, and discard messages for disposed pages or
sessions. Operations and message delivery are routed through `Graph.Dispatch`.

Saving returns the authoritative word, section ID, and total count from the server:

```fsharp
let reconcile saved = [
    index.UpdateIfLoaded((), fun old ->
        { old with TotalWordCount = saved.TotalWordCount })
    sections.UpdateIfLoaded(saved.SectionId, fun old ->
        { old with Words = upsertById (toPreview saved.Word) old.Words })
    words.SetIfLoaded(saved.Word.Id, saved.Word)
]

let pending = client.Mutate((editor.SectionId, draft), api.SaveWord, reconcile)
```

These are deferred edits, published together after remote success. The client
owns the save, so Back can close its editor while the save still updates the
retained section and index. The callback never traverses History. New words do
not require a full-detail query. Only already acquired entries are considered.
The example assumes complete section preview lists; filtered or paginated lists
should use the invalidation policy in [Queries and mutations](queries.md).

`Applied` accepts the saved draft. `RequestFailed` shows the error without
reconciliation. `ReconciliationFailed` keeps the saved receipt and shows the local
error; Back reloads stale queries. A UI should offer refresh for this outcome,
not label another Save as a cache repair: the remote write has already succeeded.

The example's `Command = (Msg -> unit) -> unit` is the subscription shape used by
Elmish commands, without adding an Elmish or UI dependency. Run `init` and `update`
on the graph thread and hand their commands to your program's dispatch adapter.
The view reads the current page's `State` and editor draft when messages arrive.
Dispose the session on application shutdown as well as Home/logout.

The test suite compiles this exact example and checks retained-page reconciliation,
adding a word without a detail fetch, and ignoring old loads after Home.
