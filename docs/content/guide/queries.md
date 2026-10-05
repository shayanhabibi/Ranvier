---
title: Queries and mutations
order: 24
---

# Queries and mutations

`Ranvier.Query` keeps **visited remote data** consistent across pages. Results stay
ordinary records; History and unsaved drafts stay in your Elmish model.

```fsharp
let client = new QueryClient(graph)
let index = client.Define(loadIndex)
let sections = client.Define(loadSection)
let words = client.Define(loadWord)

let page = sections.AcquireOwned(sectionId, pageOwner)
let loading = page.EnsureAsync()
```

Loaders: `key -> CancellationToken -> Task<record>`. Define families once per client;
acquire once per page. Only acquired keys load. Same key, same accepted record/request.
Reads never start IO.

## Loading without losing cached data

**Play** the refresh sequence. These maps use existing signal-map primitives as
query macros: `fetch` represents a request, `cached` an accepted record, and `page`
a consumer. They illustrate query behavior rather than exposing its internal graph.

```fsharp map replay code=collapsed code-max-height=24rem
let desk = Desk<string>()
let demand = createSignal 0
let fetch = createAsync (fun _ _ -> desk.Quote demand.Value)
let cached = createSignal "No data"
let outcome =
    createBoundary
        (fun _ -> None)
        (fun error _ -> Some (Result.Error error.Message))
        (fun () -> Some (Result.Ok fetch.Value))
let publish =
    createEffect (fun () ->
        match outcome.Value with
        | Some (Result.Ok value) -> cached.Value <- value
        | _ -> ())
let page =
    createMemo (fun _ ->
        let value = cached.Value
        match outcome.Value with
        | None -> value + " (loading)"
        | Some (Result.Error error) -> value + " (" + error + ")"
        | Some (Result.Ok _) -> value)
createEffect (fun () -> printfn "%s" page.Value)

controls [
    button "Answer initial load" (fun () -> desk.Settle "Tea")
    |> describe "The accepted record becomes available to the page."
    |> expect "initial value published" (fun () -> cached.Peek = "Tea" && page.Peek = "Tea")
    button "Refresh" (fun () -> demand.Value <- demand.Peek + 1)
    |> describe "Refresh starts a request; the accepted Tea record remains visible."
    |> expect "refresh retains data" (fun () -> cached.Peek = "Tea" && desk.Pending = 1)
    button "Refresh again" (fun () -> demand.Value <- demand.Peek + 1)
    |> describe "The replacement request retires the older flight."
    |> expect "one current request" (fun () -> desk.Pending = 1 && fetch.Runs = 3)
    button "Fail refresh" (fun () -> desk.Fail "offline")
    |> describe "Failure is metadata: the page still has Tea."
    |> expect "failure retains data" (fun () -> cached.Peek = "Tea" && page.Peek.Contains "offline")
    button "Retry and answer" (fun () ->
        demand.Value <- demand.Peek + 1
        desk.Settle "Coffee")
    |> describe "Explicit demand retries; only the new response publishes."
    |> expect "retry publishes" (fun () -> cached.Peek = "Coffee" && page.Peek = "Coffee")
]
```

- `Value`: tracked record; initial loading suspends, initial failure throws.
- `State`: tracked `{ Data; FetchStatus; Error; IsStale }`, including during loading.
  Metadata changes leave `Value` consumers quiet.
- `Ensure[Async]`: load missing/stale data or join the request; fresh data needs no IO.
- `Refresh[Async]`: replace the request; keep accepted data on refresh/failure.
- Async variants return `Task<T>` **after publication**. Failure faults it;
  request retirement or lease/client disposal cancels it. Disposing one shared
  lease cancels only its own waiters.

## Save once, reconcile loaded queries

The server returns the saved word, section ID, and authoritative total count:

```fsharp
let reconcile saved = [
    index.UpdateIfLoaded((), fun old ->
        { old with TotalWordCount = saved.TotalWordCount })
    sections.UpdateIfLoaded(saved.SectionId, fun old ->
        { old with Words = upsertById (toPreview saved.Word) old.Words })
    words.SetIfLoaded(saved.Word.Id, saved.Word)
]

let pending = client.Mutate(draft, saveWord, reconcile)
```

In this macro, each cache node summarizes a whole query record; `commit` represents
staged, batched publication. Edit Tea, then add Coffee: the unvisited Coffee detail
stays absent. Arrows show tracked reads; the commit's writes flash the cache nodes.

```fsharp map replay code=collapsed code-max-height=24rem
let indexQuery = createSignal 1
let sectionQuery = createSignal [(42, "Tea")]
let wordQuery = createSignal "Tea"
let saved = createAsyncSource<int * string * int> ()
let commit =
    createEffect (fun () ->
        let id, definition, total = saved.Value
        let old = sectionQuery.Peek
        let previews =
            if old |> List.exists (fun (key, _) -> key = id) then
                old |> List.map (fun (key, value) -> key, (if key = id then definition else value))
            else old @ [(id, definition)]
        batch (fun () ->
            indexQuery.Value <- total
            sectionQuery.Value <- previews
            if id = 42 then wordQuery.Value <- definition))
let indexPage = createMemo (fun _ -> sprintf "%d words" indexQuery.Value)
let sectionPage = createMemo (fun _ -> sectionQuery.Value |> List.map snd |> String.concat ", ")
let wordPage = createMemo (fun _ -> wordQuery.Value)
createEffect (fun () -> printfn "%s / %s / %s" indexPage.Value sectionPage.Value wordPage.Value)

controls [
    button "Edit Tea: server acknowledges" (fun () -> saved.Settle (42, "Green tea", 1))
    |> describe "One receipt patches the loaded section and detail; the total stays unchanged."
    |> expect "edit reconciles" (fun () -> sectionQuery.Peek = [(42, "Green tea")] && wordQuery.Peek = "Green tea")
    button "Add Coffee: server acknowledges" (fun () -> saved.Settle (99, "Coffee", 2))
    |> describe "The index and section update. No Coffee detail query is created."
    |> expect "addition reconciles" (fun () ->
        indexQuery.Peek = 2 && sectionQuery.Peek = [42, "Green tea"; 99, "Coffee"] && wordQuery.Peek = "Green tea")
]
```

**Edit factories are deferred.** `UpdateIfLoaded`, `SetIfLoaded`, `Invalidate`, and
`InvalidateWhere` return `QueryEdit`; apply through `Commit` or `Mutate` reconciliation.

- Accepted data: patch it and retire any superseded request.
- Existing entry without data: retire its request and mark stale.
- Absent key: stay absent. Invalidation retains data and starts no IO.

`upsertById` assumes a complete preview list. For filtered, sorted, or paginated
results, include every request parameter in the key and invalidate matching entries:

```fsharp
pages.InvalidateWhere(fun key -> key.SectionId = saved.SectionId)
```

## Outcomes and contracts

- `Applied saved`: accept the saved draft.
- `RequestFailed error`: show the error; no reconciliation or automatic retry.
- `ReconciliationFailed(saved, error)`: retain the receipt and refresh stale queries.
  **The server saved it—do not repeat the write to repair the cache.**
- Client disposal cancels the `Mutate` task: catch `OperationCanceledException`;
  the server may already have written. Old replies cannot update a new session.

Writes use one client-wide FIFO. Commit validates before publishing in one batch;
repeated edits compose in order. Updaters must return immutable values and must not
reenter the client. Call operations on the graph thread; completions use
`Graph.Dispatch`. Await without blocking the dispatcher with `.Result`.

## View, ownership, and cost

[Dictionary navigation](query-navigation.md) shows the **view consuming the model**,
page owners, independent drafts, Back/Home, and a save completing after Back.
Use `Acquire` for ambient ownership or `AcquireOwned` for explicit page ownership;
final release evicts the entry. Trace history can retain published values.

[Measured local cost](../benchmarks/queries.md): **0.48–0.69 µs and 1,976 extra bytes**
per three-query commit at 10/1,000 previews, for atomic publication and request
retirement. At 10,000 previews, timing intervals overlap. IO, acquisition, mutation
tasks/queue, and rendering are excluded.

A shared Elmish cache can provide the same data sharing with less bookkeeping.
Query centralizes that lifecycle; it does not make record fields or list patches
incremental. Changed-record selectors still run; list rebuilding and family-wide
predicate invalidation remain linear.
