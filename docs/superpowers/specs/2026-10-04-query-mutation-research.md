# Query and mutation layer: external research

Date: 2026-10-04. Scope: primary-source precedents for keeping independently fetched plain-record queries consistent after a save. These findings inform a proposed Ranvier layer; they do not establish what Ranvier currently implements.

## Findings

### Identity belongs to the query, including its shape

TanStack Query identifies cached results by query keys. Its documentation explicitly gives different keys for a full entity and its preview, and requires variables affecting the fetched result to participate in identity. This supports distinct typed families for index, section preview list, and word detail, rather than forcing all partial results into one entity record. [Query keys](https://tanstack.com/query/latest/docs/framework/react/guides/query-keys)

**Recommendation:** identify an entry by client/session identity, query-family identity, and a typed parameter key. Include section, pagination, filter, and ordering parameters. Prefer typed families over heterogeneous string paths; different result types must not collide. Authentication context belongs in the client lifetime or query key.

### Successful mutations can patch cached records directly

TanStack supports using the returned mutation result to update cached data immediately instead of refetching it. It recommends immutable updates and demonstrates packaging reconciliation beside a reusable mutation. [Updates from mutation responses](https://tanstack.com/query/latest/docs/framework/react/guides/updates-from-mutation-responses)

Its multi-query update operation changes only existing entries; its single-query setter can create an entry unless the updater declines the write. These are meaningfully different contracts. [QueryClient: setQueriesData and setQueryData](https://tanstack.com/query/latest/docs/framework/react/reference/classes/QueryClient)

**Recommendation:** expose `UpdateIfLoaded` as the safe first-version operation. Define loaded as having accepted data, not merely having an allocated handle or pending request. An absent result must not allocate an entry, start a fetch, or fabricate partial data. Permit explicitly seeding a complete result only as a separate operation. Keep ordinary immutable records as query values.

### Invalidation remains necessary

TanStack invalidation marks matching queries stale and normally refetches currently observed queries in the background; matching can be exact or broader. This is an alternative to implementing a normalized entity cache. [Query invalidation](https://tanstack.com/query/latest/docs/framework/react/guides/query-invalidation)

**Recommendation:** make invalidation distinct from fetching. Support an explicit demand/refetch policy: a retained hidden page is not necessarily actively displayed. Marking an unobserved retained entry stale should not load data immediately. A paginated or ranked query should invalidate when the mutation result cannot establish its correct membership and order.

### Cancellation and stale-result rejection are different responsibilities

TanStack passes an abort signal to query functions. Unused requests may complete into the cache by default; consuming cancellation changes that behavior. Its client cancellation API specifically addresses outgoing requests overwriting cache updates, and cancellation can restore pre-fetch state. [Query cancellation](https://tanstack.com/query/latest/docs/framework/react/guides/query-cancellation), [QueryClient: cancelQueries](https://tanstack.com/query/latest/docs/framework/react/reference/classes/QueryClient)

**Recommendation (design inference):** use cancellation for resource savings and an entry generation/revision check for correctness. A fetch captures its generation; reconciliation, invalidation, replacement, or disposal retires it. Every completion, including faults, must validate identity and generation on the graph thread before publication. Test loaders that ignore cancellation. Never restore a pre-fetch snapshot over a newer accepted mutation. A local generation protects fetch/write races, but cannot establish the server ordering of two concurrent mutations; serialize conflicting writes initially or require authoritative versions.

### Freshness and retention are separate policies

TanStack separates stale time from inactive-cache garbage collection. Its defaults consider queries stale immediately and retain inactive results for five minutes; those are library defaults rather than universal requirements. [Important defaults](https://tanstack.com/query/latest/docs/framework/react/guides/important-defaults)

**Recommendation:** distinguish query definition, cache entry, and page lease. A session/client owns entries, each page owns its lease and draft. Releasing one lease must not dispose an entry used by another page. For the dictionary workflow, explicit session retention is a simple initial policy: Home disposes the client and its entries. Provide explicit eviction or a bounded retention policy before presenting it as suitable for unlimited navigation. Idle-time collection and automatic focus/reconnect policies can follow later.

### Normalization is a separate, stronger commitment

Apollo normalizes objects by identity (normally type name plus ID), replaces nested objects with references, and merges incoming fields with existing fields. Missing incoming fields preserve previously cached fields. This constructs a partial client graph; it does not require fetching the whole dataset. [Apollo cache overview](https://www.apollographql.com/docs/react/caching/overview)

**Recommendation:** do not make normalization mandatory for the first version. F# preview and detail records may be different types and may not share field names. Automatic normalization would need identity, merge, missing-field, and relationship policies. Explicit typed query reconciliation solves the stated workflow without that schema machinery or per-field signals.

## Local Ranvier evidence

Inspected checkout: `7c672a5` on 2026-10-04. Existing user work in `docs/content/guide/asd.fsx` and `examples/Fable.Ranvier.Playground/` was not changed. No product implementation or benchmark was run for this research.

- `createAsyncSource` is a manually completed source. `Settle` and `Fail` may be called repeatedly, but neither returns it to pending and settlements do not apply equality cutoff. It is therefore suitable as an initial-load suspension gate, not a complete refetching query cache. [Async source contract](../../content/guide/async-sources.md)
- `createAsync` supplies pull-triggered task flights, cancellation policies, and prior settled values. Its graph-wide flight policy governs computation; it does not by itself specify authoritative mutation/cache reconciliation. Pure async bodies cannot be used as a place to mutate cached query data. [Async memo contract](../../content/guide/async-memos.md), [memo purity](../../content/guide/memos.md)
- `Graph.Dispatch`, `Graph.Batch`, `Graph.AssertOnGraphThread`, `Graph.Untrack`, `Graph.OnCleanup`, `Owner.Attach`, and `Owner.OnCleanup` are public members, confirmed by `fcs_public_api` on `src/Ranvier/Ranvier.fsproj`. `AsyncSource<'T>(graph)` and `Signal<'T>(graph, initial)` are public constructors. This gives an external assembly a credible composition path without internal scheduler access. [Threading](../../content/guide/threading.md), [ownership](../../content/guide/roots.md), [cleanup](../../content/guide/cleanup.md)
- Batching defers effects; memos can still observe intermediate writes if explicitly read inside a batch, and throwing does not roll writes back. The layer must stage user updater/comparer work before publication. [Batch contract](../../content/guide/batch.md)
- Whole-record nodes use the graph equality policy; default record equality is reference identity. A derived preview allocating a fresh record needs an explicit comparer or reference preservation to suppress equal output. Key identity needs its own typed equality contract rather than borrowing record reference equality. [Equality](../../content/guide/equality.md)
- Existing editable keyed collections support direct live-key writes, but some membership/order and aggregate paths still scan. Keeping record/list query values does not inherit per-key delta complexity automatically. [Library collection contract](../../../src/Ranvier/README.md)
- The separate Elmish assembly demonstrates an external layer using public graph dispatch, untracking, signals, and memos. Its project targets `net10.0;net8.0;netstandard2.1`; source projects pin FSharp.Core `8.0.100`. [Elmish implementation](../../../src/Ranvier.Elmish/Mvu.fs), [project](../../../src/Ranvier.Elmish/Ranvier.Elmish.fsproj), [source build properties](../../../src/Directory.Build.props)
- Shared test files are imported by both .NET and Fable, but Fable also explicitly registers each test list. Packaging, tracing, actual Fable execution, and AOT each have independent gates. [Shared tests](../../../tests/Ranvier.Tests/TestFiles.props), [Fable registration](../../../fable/Ranvier.Tests.Fable/Main.fs), [CI](../../../.github/workflows/ci.yml), [package consumer gate](../../../tests/check-package-compatibility.ps1)

### Semantic verification and limits

The configured MCP tools were not exposed directly in this session. The installed `fslangmcp 0.16.0` was queried through its normal stdio MCP protocol with a temporary local client; no installation or repository configuration change was needed. A fresh `check(scope=workspace)` reported six errors, all attributed to the Fable test project: missing `replaceTestCode`, `IcedTasks`, `cancellableTask`, `isNotNull`, `testSequenced`, and `hasLength`. The .NET test project and library projects had no errors in that verdict. A subsequent scoped check of `src/Ranvier/Ranvier.fsproj` returned `clean`, with zero errors.

No negative workspace `find` result was used to infer absence of a symbol or usages. Public surfaces were queried with an explicit core project path; the later scoped definition lookup followed the clean core check. The Fable-context diagnostics are an analysis baseline, not evidence that the real Fable CLI suite fails. Actual tests/builds were not run for this documentation-only task. A public-API-only composition proof remains the first implementation milestone.

## Proposed first-version contract

These are recommendations inferred from the precedents, not claims about the cited libraries or existing Ranvier APIs.

- Typed query families own fetch functions and parameter-key equality. Handles expose accepted data plus loading/error/stale status. A refresh retains previous data independently from its pending or failed request state.
- Identical family/key requests within a client share one entry and one in-flight fetch. Handles from different clients never share mutable cache state.
- Mutation success runs a synchronous reconciliation description on the owning graph thread. The library stages all record updates first, validates the operation, then publishes them in one batch. Batching notification alone must not be described as rollback or exception atomicity.
- Reconciliation declares exact/family updates and invalidations. Queries without data can still require invalidation: an initial section fetch already in flight when a word is created must not later publish its old snapshot simply because `UpdateIfLoaded` skipped it.
- A success receipt contains sufficient business context, for example saved word, section ID, authoritative total, and optional server version. The library cannot infer membership or count changes from an arbitrary entity value.
- A mutation that completes after its page is popped can still reconcile into its live client. Client disposal prevents publication; it cannot guarantee cancellation of an already accepted server write.
- Defer optimistic writes, rollback stacks, automatic retries of mutations, offline persistence, and entity normalization. These introduce additional ordering/idempotency contracts not needed for the initial example.

## Acceptance scenarios

1. Opening index fetches no words; opening one section fetches only that section's previews; detail is fetched only on demand.
2. Saving adds/updates the loaded section preview and authoritative count together, without replacing navigation history or fetching an unrelated section.
3. Two handles for one family/key deduplicate requests; disposing either leaves the other functional.
4. A pre-save fetch completing after reconciliation cannot overwrite accepted data, even if cancellation is ignored. Include a pending entry with no prior data.
5. Failed refresh retains the last accepted data; repeated reads do not accidentally create a retry storm.
6. A reconciliation updater throwing produces no partially published multi-query update.
7. Stale inactive queries defer fetch until the documented demand boundary; paginated queries use invalidation instead of guessed insertion.
8. Home disposes the old client; late old-session query and mutation completions cannot affect the new index.
9. Ordinary record selectors suppress downstream changes according to an explicit equality policy; the query API does not claim arbitrary whole-list record updates are incremental collection operations.
