# Reactive queries and mutation reconciliation

Status: implemented on `feat/query-mutation` following the user's instruction. See the [execution ledger](../plans/query-mutation-progress.md) for verification and execution decisions. The design below records the intended contracts.

## Purpose

Keep separately fetched page models consistent after a save without rewriting a navigation stack, loading the entire dataset, normalizing every entity, or changing ordinary records into records of signals. The motivating application loads an index summary, one section's word previews, and one word's detail on successive pages. An accepted save must update the retained detail, section preview, and authoritative total together.

The application still supplies business knowledge: the affected section, preview conversion, insertion/order rules, and the authoritative total. The library supplies typed shared query identity, fetch lifecycle, stale-response rejection, ownership, and staged publication. A preview query and a detail query can have different result types and stay independent cached records.

## Evidence and alternatives

See [research](2026-10-04-query-mutation-research.md) for official TanStack Query and Apollo sources and the local Ranvier audit. Query reconciliation is recommended over these alternatives:

1. A few helpers around async memos: low surface area, but callers still own identity, pending-fetch races, cache lifetime, and reconciliation exceptions. It does not remove the difficult work in Kerams's example.
2. Typed query families with explicit reconciliation: preserve ordinary records and partial loading; describe affected queries once beside the mutation. Recommended first release.
3. Automatic normalized entities and relationships: potentially less application patching, but requires identity/merge/schema rules across preview/detail types and does not infer paginated membership or remote counts. Defer.

## Packaging and compatibility

- Add `Ranvier.Query` as a sibling of `Ranvier.Elmish`, with namespace `Ranvier.Query`.
- Target `net10.0;net8.0;netstandard2.1`; retain the repository's `FSharp.Core` floor of `8.0.100`.
- Support actual Fable execution, traced/untraced builds, and the existing NativeAOT compatibility policy before release.
- Use public Ranvier APIs only. Do not add friend-assembly access, change `GraphOptions`, introduce a scheduler node, or change existing signatures in the planned implementation.
- No mandatory HTTP, UI, timer, normalization, or third-party caching dependency.
- Ordinary immutable records remain the query values. Record/list reconstruction costs remain; this layer does not make arbitrary `List.map` or selectors field-incremental.
- No optimistic writes, automatic mutation retries, timer-based expiry, focus/reconnect refetch, persistence, entity normalization, or C# convenience facade in version one.

## Public interface

These are intended F# member shapes. Supporting classes have internal constructors; the implementation may need ordinary F# signature-file ordering or recursive declarations.

```fsharp
type FetchStatus = Idle | Fetching

type QuerySnapshot<'T> = {
    Data: 'T option
    FetchStatus: FetchStatus
    Error: exn option
    IsStale: bool
}

type MutationOutcome<'T> =
    | Applied of 'T
    | RequestFailed of exn
    | ReconciliationFailed of result: 'T * error: exn

// Opaque synchronous edit description, bound to one client.
type QueryEdit

type QueryLease<'T> =
    member Value: 'T
    member State: QuerySnapshot<'T>
    member Ensure: unit -> unit
    member Refresh: unit -> unit
    interface System.IDisposable

type QueryFamily<'Key, 'T> =
    member Acquire: key: 'Key -> QueryLease<'T>
    member UpdateIfLoaded: key: 'Key * update: ('T -> 'T) -> QueryEdit
    member Invalidate: key: 'Key -> QueryEdit
    member InvalidateWhere: predicate: ('Key -> bool) -> QueryEdit

type QueryClient =
    new: graph: Graph -> QueryClient
    member Define<'Key, 'T>:
        keyComparer: System.Collections.Generic.IEqualityComparer<'Key> *
        fetch: ('Key -> System.Threading.CancellationToken -> System.Threading.Tasks.Task<'T>)
            -> QueryFamily<'Key, 'T>
    member Commit: edits: QueryEdit list -> unit
    member Mutate<'Input, 'Result>:
        input: 'Input *
        execute: ('Input -> System.Threading.CancellationToken -> System.Threading.Tasks.Task<'Result>) *
        reconcile: ('Result -> QueryEdit list)
            -> System.Threading.Tasks.Task<MutationOutcome<'Result>>
    interface System.IDisposable
```

`Value` and `State` are tracked reads, not raw writable signals. `State` is always inspectable without suspending: pending is represented by no data plus `Fetching`. `Value` suspends through a real Ranvier async source until initial data exists, throws the initial request failure, and returns retained data during a refresh or refresh failure. Native boundaries therefore work for initial loading; refresh errors are read from `State.Error`.

Definition uses explicit key equality and captures the graph's value comparer for `'T`; it must not use serialized strings or type-name reflection as identity. Two definitions with the same key and fetch function remain different families. Define families once per client/session, outside view recomputation. Keys are immutable and include every request parameter, including filter/page/order; auth context changes require a new client or distinct keys.

Support `unit` keys for singleton queries. Use an internal non-null key wrapper with a comparer delegating to the supplied key comparer; do not put a potentially null-represented F# key directly into a .NET dictionary. Null-like key values are allowed when the supplied comparer supports them. Reject a null comparer or fetch delegate at definition time.

## Demand and retention

Definition does no IO. `Acquire key` creates or reuses a typed entry, takes a lease, and calls `Ensure`. It is an explicit page-load action, not a pure memo body. Register the returned lease with the page owner's cleanup. Read-only getters never launch requests or retry failures.

`Ensure` starts a fetch only if there is no accepted data or the entry is stale, and joins a current fetch rather than duplicating it. Call it on navigation back to a retained page. `Refresh` explicitly supersedes any current fetch and starts a new one, retaining accepted data.

Invalidation only marks an existing entry stale and retires its current fetch; it does not fetch. Call `Ensure` when displaying it again, or `Refresh` when the application wants an immediate refresh. A hidden page with a live lease does not automatically fetch. This explicit policy is a deliberate first-version choice; it does not equate graph observation with UI visibility.

The client owns the family registries. A page owns a lease; the last lease release evicts the entry, retires its generation, and requests cancellation. There is no indefinite session cache of previously visited pages. Retained stack pages retain their leases. Reopening an evicted page loads it again. Disposing the client (Home/logout) invalidates every lease and outstanding completion; releasing one of several leases does not affect the others. Register client disposal with its session/graph owner, with idempotent explicit disposal also available.

Use graph-thread assertions on creation, reads, acquisition, reconciliation, release, and disposal. Async completions marshal through `Graph.Dispatch`. The API does not silently make a guarded graph thread-safe. The existing `Serialised` affinity contract still applies: actions execute within a graph drain, and a manual dispatcher requires pumping.

## Fetch implementation over existing primitives

Each typed entry owns plain bookkeeping (generation, liveness, lease count, cancellation source), accepted-data and metadata signals with library-controlled non-throwing publication comparers, and a replaceable `AsyncSource<'T>` initial-load gate. These are existing source primitives. No owned memo/effect is stored under whichever page happened to acquire first.

`Value` first reads accepted data. When absent it reads the current gate through a tracked gate-reference signal and then the gate's `Value`. `State` reads accepted data and metadata only; it must not read the gate and accidentally suspend. Once accepted data exists, changes to fetch metadata alone must not wake `Value` readers. Callers may create ordinary memos with explicit comparers for record projections.

Start/retry creates a new gate only when data is absent, outside a pure computation. `AsyncSource` has no reset-to-pending operation, so do not attempt to reuse a failed/settled gate as a new pending source. A refresh with data leaves the data channel readable and only changes metadata. Every transition that changes multiple sources is batched. Data equality is evaluated before publication; internal source comparers must not run arbitrary user comparison during commit.

Capture an entry identity and generation before invoking the fetch delegate untracked. On synchronous throw, task failure, cancellation, or success, dispatch a completion to the graph and first check client liveness, entry identity, and generation. Check again at application time, not just on the worker thread. A retired completion never writes data, error, or fetching status. Observe all faults even after retirement/disposal. If cancellation registration throws, record it with the owning cleanup/error mechanism and continue retiring other operations.

Cancellation is best effort; generation is the correctness mechanism. Increment generation before invoking cancellation callbacks. No callback or loader runs inside multi-entry publication. Null tasks are handled as request failures. An unsolicited cancellation from the currently accepted fetch is a request failure; cancellation requested for a retired generation is ignored.

Initial success sets accepted data, clears error/staleness, marks idle, and settles the gate in a batch. Initial failure marks idle/stale and fails its gate; repeated reads do not retry. Refresh failure retains data and marks idle/stale with the new error. `Ensure`/`Refresh` are the explicit retry operations.

This composition is now verified from the separate Query assembly on .NET and Fable, including native boundary wakeup, retries, and release. No core extension was required.

## Reconciliation

Edit constructors do not mutate data or allocate query entries. `Commit` validates all edits belong to this client before evaluating them. Resolve targets and evaluate predicates/updaters/value comparisons synchronously, untracked, against staged per-entry values. Repeated edits for an entry compose in list order. Reentrant query-client actions from a predicate/updater are rejected before mutation. Updaters must be pure and immutable; the library cannot undo in-place changes to user objects.

`UpdateIfLoaded(key, update)` behaves as follows:

- Absent entry: no allocation, no request, no updater call.
- Entry with accepted data: run the updater, accept its result, clear error/staleness, retire any old fetch, and mark idle. Retire the fetch even if the result compares equal.
- Entry without data: skip the updater but retire its old fetch and mark stale/idle. Replace its gate to disconnect the retired request. It needs an explicit `Ensure` to fetch again. This prevents an initial pre-save request from later publishing an obsolete snapshot.

This operation asserts that its resulting complete query value is current. If the save response cannot establish ordering, filtering, pagination, or unrelated concurrent server changes, use `Invalidate` instead. `InvalidateWhere` evaluates only existing keys in its family and supports cases such as all cached pages for one section. No family-wide lookup crosses into another client.

After every stage succeeds, retire affected generations, install all staged data/metadata/gates within one `Graph.Batch`, then request cancellation of retired tasks outside publication. Deferred effects observe final values. Do not promise a database transaction: fatal runtime failure and in-place user mutation cannot be rolled back. The specific guarantee is no partial data publication when an updater, predicate, or value comparer throws during staging.

`Commit` itself throws on staging failure and leaves accepted data and fetch generations unchanged. For a remotely successful mutation whose reconciliation fails, use the additional recovery policy below.

## Mutations

Serialize `Mutate` invocations FIFO per client, including different mutation functions, through remote completion and reconciliation. This deliberately conservative first version prevents local concurrent saves that affect a shared total from racing. Do not infer remote ordering using invocation IDs or apply `KeepLatest` to mutations. External writers still require authoritative server versions or refetch; serialization is not distributed consistency.

An invocation snapshots its immutable input, queues execution, and returns a task. Invoke the next delegate only after the previous reconciliation/failure transition is finished. Mutations are client-owned, not editor-owned: closing an editor does not cancel a save needed by retained pages.

On success run `reconcile result`, stage, and publish on the graph thread before completing `Applied result`. On remote failure return `RequestFailed error` and do not run reconciliation. Never retry a mutation automatically.

If a remote success is followed by a reconciliation callback/staging failure, return `ReconciliationFailed(result, error)`, preserve all accepted data, and invalidate/retire every existing client query conservatively in a separate library-controlled batch. This signals that the server write succeeded but cached views require refresh; the caller must not resubmit the save as if the server failed. A throwing callback may not have returned its affected keys, hence the client-wide recovery. No recovery fetch starts automatically.

Disposing the client cancels queued/not-yet-started operations and completes outstanding callers as cancelled. Request cancellation of in-flight IO, observe its eventual result/fault, and discard any late publication. Cancellation cannot undo a write already accepted by the server. The new Home session is isolated from the old client.

## Dictionary workflow

Illustrative usage with proposed functions; `keyComparer`, API DTOs, `toPreview`, and `upsertById` are application code. Word previews require an ID for replacement.

```fsharp
let client = new QueryClient(graph)
let index = client.Define(keyComparer<unit>, fun () ct -> api.LoadIndex ct)
let sections = client.Define(keyComparer<int>, api.LoadSection)
let words = client.Define(keyComparer<int>, api.LoadWord)

type Page =
    | DictionaryIndex of QueryLease<IndexSummary>
    | Section of QueryLease<Section>
    | WordEdit of WordDraft

let save input =
    client.Mutate(input, api.SaveWord, fun saved -> [
        words.UpdateIfLoaded(saved.Word.Id, fun _ -> saved.Word)
        sections.UpdateIfLoaded(saved.SectionId, fun section ->
            { section with Words = upsertById (toPreview saved.Word) section.Words })
        index.UpdateIfLoaded((), fun summary ->
            { summary with TotalWordCount = saved.TotalWordCount })
    ])
```

The index page acquires only `index.Acquire ()`; a section page acquires only `sections.Acquire sectionId`. An editor loads details using a word lease and keeps an independent record draft (`createDraft` can be used; per-field signals are optional). On accepted save it resets the draft only if the user has not made newer edits; the simplest sample disables editing while saving. Back calls `Ensure` on the returning page and releases the popped page's lease/draft scope. History remains an ordinary navigation model, optionally inside an existing `Mvu` program.

For paginated sections replace the section update with `sectionPages.InvalidateWhere(fun key -> key.SectionId = saved.SectionId)`. The total is server-supplied, never computed from the number of cached words. Preview and detail queries remain different shapes; no fake `OtherFields`, `LoadedWord` union, or automatic preview/detail merge is required.

## Acceptance and limits

The implementation plan assigns tests to each contract: lazy definition, explicit acquisition, deduplication, ownership, initial and refresh status, native suspension, stale success/failure rejection, unloaded pending entry reconciliation, staged exception safety, client isolation, mutation queue/disposal, and the complete dictionary flow.

Selectors still reevaluate when their query record changes. The win is centralizing cache consistency and isolating unrelated queries. Incremental collection adapters can be composed later; no constant-time whole-list update or universal UI speedup is claimed.

The first release is F#-first but must satisfy the repository's .NET/Fable/tracing/AOT gates. A C# convenience facade, timed retention policies, automatic active-query refetch, server-version policies, and a collection-specific query result are follow-on design work, not hidden requirements for this implementation.
