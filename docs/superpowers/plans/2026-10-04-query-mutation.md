# Query and Mutation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` if the user chooses delegated execution, or `superpowers:executing-plans` for inline execution. Implement task-by-task using `superpowers:test-driven-development` and verify with `superpowers:verification-before-completion`.

**Goal:** Keep partially loaded, ordinary-record page queries consistent after mutations without traversing navigation history or requiring per-field signals.

**Architecture:** Add a separate `Ranvier.Query` assembly over public Ranvier sources, graph dispatch, batching, and ownership. Typed query families share entries through page-owned leases; a generation-guarded fetch controller and staged mutation reconciliation coordinate cached records. Prove native suspension and portable lifecycle composition before implementing the full layer.

**Tech Stack:** F#, .NET 10/8/netstandard2.1, Fable, Expecto, existing tracing, packaging and NativeAOT tooling.

**Spec:** [Query/mutation design](../specs/2026-10-04-query-mutation-design.md). Evidence: [research and local audit](../specs/2026-10-04-query-mutation-research.md).

Status: executed following the user's implementation instruction. See the [execution ledger](query-mutation-progress.md) for completed deliverables, verification, and deviations. The checklists below retain the original plan; publication and integration remain separate decisions.

## Global Constraints

- Target `net10.0;net8.0;netstandard2.1`; retain the repository's `FSharp.Core` floor of `8.0.100`.
- Support actual Fable execution, traced/untraced builds, and the existing NativeAOT compatibility policy before release.
- Use public Ranvier APIs only. Do not add friend-assembly access, change `GraphOptions`, introduce a scheduler node, or change existing signatures in the planned implementation.
- No mandatory HTTP, UI, timer, normalization, or third-party caching dependency.
- Ordinary immutable records remain the query values. Record/list reconstruction costs remain; this layer does not make arbitrary `List.map` or selectors field-incremental.
- No optimistic writes, automatic mutation retries, timer-based expiry, focus/reconnect refetch, persistence, entity normalization, or C# convenience facade in version one.
- Prefix terminal commands with `rtk`. Read the repository's SageFs playbook before F# changes, use `fslangmcp` for F# semantics, and run `fcs_refactor_impact` before any existing public signature change.
- Before source/XML comments are added, follow `AGENTS.md`: use `comment-hygiene@roboz0r` and the F# XML-doc skill. If required tooling is absent, request setup permission as instructed; do not install silently.
- Preserve the user's existing staged/unstaged documentation and playground work. Establish an isolated checkout for implementation; this planning task changes only its three Markdown artifacts.

## Review Focus

- A pending initial fetch with no data is targeted by reconciliation: its pre-save response must never publish (Tasks 3–4).
- A throwing updater, key predicate, or data comparer, including reentrant client access, must not partially publish accepted data (Task 4).
- A save finishes after its editor closes, or after Home creates a new client: reconcile only into the still-live original client (Task 5).
- Native pending reads retry after an initial failure, while state inspection never suspends and refresh retains data (Tasks 1 and 3).
- Two queued creates affect one authoritative total; deduplication, retirement, and lease release must not reorder mutations or admit late faults (Tasks 2, 3 and 5).

## File responsibilities and order

New product files, compiled in this order:

- `src/Ranvier.Query/Types.fs`: status/outcome records and unions, opaque edit staging protocol, non-throwing internal publication comparers. No IO.
- `src/Ranvier.Query/Entry.fs`: typed entry, fetch generation, data/status/gate sources, lease count, guarded completions, and disposal. Uses public Ranvier only.
- `src/Ranvier.Query/Queries.fs`: typed family registry, lease interface, edit descriptions, typed staged values, exact/predicate target resolution, and `QueryClient` query operations.
- `src/Ranvier.Query/Mutations.fs`: FIFO mutation controller and `QueryClient.Mutate` extension; task outcome and graph-thread reconciliation.
- `src/Ranvier.Query/Ranvier.Query.fsproj` and `README.md`: packaging and user contract.

New shared test files, in test compile order:

- `tests/Ranvier.Tests/QuerySupport.fs`: deterministic completion helpers, manual dispatcher setup, no public library types.
- `tests/Ranvier.Tests/QueryComposition.fs`: public-only native pending and ownership contract tests.
- `tests/Ranvier.Tests/Queries.fs`: family identity, keys, lease lifecycle, query states and fetch races.
- `tests/Ranvier.Tests/QueryReconciliation.fs`: staged multi-entry publication, exceptions and pending-empty cases.
- `tests/Ranvier.Tests/QueryMutations.fs`: serialized requests, outcomes, cancellation and client disposal.
- `tests/Ranvier.Tests/QueryDictionary.fs`: the actual partial-loading/back-navigation acceptance scenario.

Modify `Ranvier.slnx`, both test `.fsproj` files, `tests/Ranvier.Tests/TestFiles.props`, and `fable/Ranvier.Tests.Fable/Main.fs` as each deliverable is added. Final integration also modifies the AOT project/entry point, package-compatibility script, docs project and guide index. Existing engine implementation files are read-only for this plan.

## Task 1: Prove a separate assembly can compose query reads

**Files:** Create project, `Types.fs`, `Entry.fs`, minimal `Queries.fs`, `README.md`, `QuerySupport.fs`, `QueryComposition.fs`. Modify solution and both test project references/registrations. The initial entry is internal; expose only the planned `QueryClient.Define`, family `Acquire`, lease `Value`/`State`/`Refresh`/disposal needed to test composition through the public interface.

**Consumes:** public `Signal`, `AsyncSource`, `Graph.Dispatch`, `Graph.Batch`, `Graph.OnCleanup`, and `createSignalWithComparer`.

**Produces:** internal entry read/publication foundation behind the minimal public facade, plus shared `newGraph` / `drainUntil` test helpers. Shared identity/retention is completed in Task 2; mutation behavior is not introduced here.

- [ ] Establish a clean implementation worktree and baseline. Record the fresh `fslangmcp check` result. Research found six Fable-context errors in workspace analysis but a clean core project; use actual CLI tests to distinguish a project-analysis issue from a build failure. Do not suppress those diagnostics or repair unrelated source as part of this feature.
- [ ] Scaffold the separate project following `Ranvier.Elmish.fsproj`: identical targets, FSharp.Core inheritance, `RanvierAotClean`, matching traced package ID (`Ranvier.Query.Traced`), README/icon packing, and project reference to `Ranvier`. Set initial package version `0.1.0-preview.1`. Add test references and portable registration immediately.
- [ ] Write the composition tests first against the planned public query interface backed by the small internal entry. The tested mechanism must live in the new assembly with no internals access to Ranvier or test-only public exports. The equivalent primitive composition below identifies the native-boundary contract the query test must exercise:

```fsharp
let source = AsyncSource<int>(graph)
let accepted = createSignal<int option> None
let gate = createSignal source
let read () =
    match accepted.Value with
    | Some value -> value
    | None -> gate.Value.Value
let shown = createBoundary (fun _ -> -1) (fun _ _ -> -2) read
Expect.equal shown.Value -1 "initial load suspends"
batch (fun () -> accepted.Value <- Some 42; source.Settle 42)
Expect.equal shown.Value 42 "settlement wakes boundary"
```

Test a failed first gate, replacing it with a new pending gate, settlement of the retired gate, and successful retry. Also assert a state-only effect can render `{ Data = None; FetchStatus = Fetching }` without becoming pending. The sketch above is the composition contract, not the final equality/publication implementation.
- [ ] Implement deterministic helpers: `newGraph () : Graph` uses `ManualDispatcher`; `drainUntil (graph: Graph) (condition: unit -> bool) : Async<unit>` pumps on the test's graph thread, yields asynchronously, and fails with a bounded timeout. Reuse existing suite patterns for Fable delivery and .NET graph affinity rather than running `Pump` on an arbitrary thread-pool continuation. Inspect them with `fslangmcp` first. Use controlled `TaskCompletionSource` values; never time sleeps to order competing results.
- [ ] Run the focused failing test with `rtk proxy dotnet run --project tests/Ranvier.Tests -c Release -f net10.0 -- --filter-test-list QueryComposition`; confirm the named tests actually ran. Implement only enough entry composition for them to pass.
- [ ] Verify disposal of the first page does not own/destroy shared entry state: entry sources are client-controlled; per-page effects/memos belong to page scopes. Register cleanup explicitly and do not assume signals have disposable owner lifetimes. Check traced retention and repeated gate replacement for unintended rooting.
- [ ] Compile/run the composition tests with the actual Fable suite under both delivery modes through the existing report runner. This is the first decision gate: if public source composition or portable cancellation cannot meet the contract, capture the failing case and revise the design before touching engine internals. Do not quietly substitute a custom node.
- [ ] Commit only this deliverable after its targeted tests pass: `feat(query): establish public-source composition`.

## Task 2: Typed identity, leases, and bounded retention

**Files:** Extend product `Queries.fs`, `Types.fs` and `Entry.fs`; create/register `tests/Ranvier.Tests/Queries.fs`.

**Consumes:** Task 1 entry sources and public graph ownership.

**Produces:** `QueryClient(graph)`, `Define(keyComparer, fetch)`, `QueryFamily.Acquire`, `QueryLease.Value`, `State`, `Ensure`, `Refresh`, and idempotent disposal with the exact shapes in the spec. Fetch startup is completed in Task 3; keep lifecycle tests self-contained with completed tasks where possible.

- [ ] Write identity tests around real public calls:

```fsharp
let mutable requests = 0
let family = client.Define(EqualityComparer<int>.Default, fun key _ ->
    requests <- requests + 1
    Task.FromResult (key * 10))
Expect.equal requests 0 "definition performs no IO"
use first = family.Acquire 7
use second = family.Acquire 7
Expect.equal requests 1 "same family and key share a request"
first.Dispose ()
Expect.equal second.Value 70 "one lease cannot dispose the other"
```

Add two same-shaped families with key `7`, two clients with key `7`, custom key equality, and compound page/filter keys. Each distinct identity must remain isolated. Key comparer exceptions during lookup must not leave a partially inserted entry.
- [ ] Run focused `Query` tests and confirm failures before implementation.
- [ ] Implement one typed dictionary per family; family identity is its instance, so no heterogeneous value casts are necessary for query lookup. Use a non-null struct key wrapper and a comparer delegating to `keyComparer`, supporting singleton `unit` queries and other null-like keys supported by that comparer. A non-generic internal entry interface may expose lifecycle/staging operations for client iteration without reflecting over result types. Store entries only after creation succeeds; reject null key comparers/fetch functions with documented argument exceptions. Add a real `Define(EqualityComparer<unit>.Default, ...)` / `Acquire ()` test on .NET and Fable.
- [ ] Implement acquisition as lease increment plus `Ensure`; register lease cleanup under the acquiring page owner, and client cleanup under the construction owner. Use the explicit graph, assert thread affinity, and call user fetch functions untracked. Do not depend on changing `Graph.Current` across async continuations.
- [ ] Test last-release eviction by acquiring/releasing key `7`, acquiring again, and expecting a second fetch. Retained hidden pages keep their leases. Test double release, release after client disposal, and access after disposal (`ObjectDisposedException`). Verify a completed task cannot flush effects against an incompletely initialized lease/entry.
- [ ] Test that `Value` readers do not rerun solely for fetch metadata changes; `State` readers should. Use separate internal channels and non-throwing internal comparers. Default value cutoff uses the captured graph comparer, evaluated before publication, not during commit.
- [ ] Run targeted tests on net10/net8 and Fable, then commit `feat(query): add typed query families and leases`.

## Task 3: Fetch lifecycle, initial suspension, and retirement

**Files:** Extend `Entry.fs`, `Queries.fs`, and `tests/Ranvier.Tests/Queries.fs`.

**Consumes:** family identity and leases.

**Produces:** fully implemented `Ensure`/`Refresh`, `QuerySnapshot`, native initial-load `Value`, and internal generation retirement consumed by Task 4. Internal `Retire` advances generation and collects a cancellation action; publication never invokes that action inline.

- [ ] Add controlled-fetch tests. This is the key refresh race, expressed using `drainUntil` from Task 1:

```fsharp
let oldReply, newReply = TaskCompletionSource<int>(), TaskCompletionSource<int>()
let mutable starts = 0
let family = client.Define(EqualityComparer<int>.Default, fun _ _ ->
    starts <- starts + 1
    if starts = 1 then oldReply.Task else newReply.Task)
use query = family.Acquire 1
query.Refresh ()
newReply.SetResult 2
do! drainUntil graph (fun () -> query.State.Data = Some 2)
oldReply.SetResult 1
do! drainUntil graph (fun () -> oldReply.Task.IsCompleted)
graph.Pump () |> ignore
Expect.equal query.State.Data (Some 2) "retired response cannot overwrite"
```

Run the same scenario with an old fault and a loader that ignores cancellation. Assert completion processing has actually drained, using an explicit continuation acknowledgement in the harness where `Task.IsCompleted` alone is insufficient; never infer publication from task completion alone.
- [ ] Test initial failure -> explicit retry -> success; refresh failure retains data; repeated getter reads never retry; two `Ensure` calls deduplicate; `Refresh` supersedes; a synchronous delegate throw and a null task produce request failures. Add a currently accepted IO cancellation case separate from supersession.
- [ ] Run focused tests red, then implement the state transitions exactly as specified. A fetch captures identity+generation. Its dispatched completion validates both and client liveness before any data, metadata, or gate write. Observe every task exception, including faults after eviction/client disposal.
- [ ] Cover thread-pool completion with manual dispatch (invisible until `Pump`), synchronous completion reentry, and the `Serialised` graph contract. Schedule graph operations through `Dispatch` when outside a serialised drain; no worker mutates the registries directly.
- [ ] Add lifecycle tests: final lease release while pending, reacquire the same key, and complete the old request; it must not touch the new entry. Add cleanup callbacks that throw and confirm other pending operations are still retired.
- [ ] Run the focused suites and actual Fable variants. Commit `feat(query): guard fetch publication across refresh and disposal`.

## Task 4: Staged reconciliation and explicit invalidation

**Files:** Extend `Types.fs`, `Entry.fs`, `Queries.fs`; create/register `QueryReconciliation.fs` tests.

**Consumes:** entry generation retirement and source publication.

**Produces:** opaque `QueryEdit`; `UpdateIfLoaded`, `Invalidate`, `InvalidateWhere`; `QueryClient.Commit`. Internal staging accumulates per-entry typed drafts and returns publication plus post-publication cancellation actions.

- [ ] Add a regression proving updater failure cannot partially change data:

```fsharp
use left = numbers.Acquire 1
use right = numbers.Acquire 2
let beforeLeft, beforeRight = left.Value, right.Value
Expect.throws (fun () ->
    client.Commit [
        numbers.UpdateIfLoaded(1, fun n -> n + 10)
        numbers.UpdateIfLoaded(2, fun _ -> failwith "bad updater")
    ]) "staging fails"
Expect.equal (left.Value, right.Value) (beforeLeft, beforeRight)
    "accepted data is unchanged"
```

`numbers` is a family returning `Task.FromResult key`. Also test throwing key predicates, captured graph value comparers, and edits belonging to another client. A failure must preserve fetch generations for plain `Commit`.
- [ ] Add the pending-empty race: acquire a section with an unresolved task, commit `UpdateIfLoaded` for its key, assert updater was never invoked and the entry is stale/idle, then complete the old task. It must not publish. Call `Ensure`, settle a new reply, and assert recovery. For an absent key, assert zero allocation by showing its first later acquisition still performs exactly one initial fetch and that the updater wasn't called.
- [ ] Add an effect reading two loaded queries and assert it observes only the before and final pairs. Add ordered repeated edits for one key (`+1`, then `*2`) and expect `(old+1)*2`. Do not implement last-edit-wins accidentally.
- [ ] Implement opaque edit descriptions bound to one client. Resolve all matching keys and stage updater/value-comparison results before retiring or writing. Use a staging guard to reject `Acquire`, `Ensure`, `Refresh`, `Commit`, `Mutate`, or disposal reentry from callbacks. Returning immutable records is a caller contract; no deep clone or rollback of in-place mutation.
- [ ] Commit staged state with one `Graph.Batch`, then request retired cancellation. Even equal accepted data retires old requests. `Invalidate` preserves data/error, marks stale/idle, retires in-flight work, and never starts IO. No-data invalidation installs a fresh pending gate. Test that a hidden retained query does not fetch until `Ensure`.
- [ ] Test `InvalidateWhere` only touches existing keys matching a section/page predicate; predicates run once per existing key during staging. This is the pagination escape hatch, not an automatic membership algorithm.
- [ ] Run targeted tests and commit `feat(query): reconcile query records with staged publication`.

## Task 5: Client-owned serialized mutations

**Files:** Create `Mutations.fs`, `QueryMutations.fs`; extend the internal client protocol and registrations.

**Consumes:** `Commit` staging/publication and client liveness.

**Produces:** exact `Mutate` and `MutationOutcome` interface from the spec; a FIFO queue per client spanning execute through reconciliation. Do not allocate a separate generic queue for each result type: heterogeneous queued operations are internal closures with typed completion sources.

- [ ] Add a request-order test:

```fsharp
let firstReply = TaskCompletionSource<int>()
let starts = ResizeArray<int>()
let execute input _ =
    starts.Add input
    if input = 1 then firstReply.Task else Task.FromResult 2
let first = client.Mutate(1, execute, fun _ -> [])
let second = client.Mutate(2, execute, fun _ -> [])
Expect.sequenceEqual starts [1] "second remote write waits"
firstReply.SetResult 1
do! drainUntil graph (fun () -> second.IsCompleted)
Expect.sequenceEqual starts [1; 2] "FIFO spans publication"
```

Repeat with different execute functions and result types, proving the queue belongs to the client. Add an authoritative-total scenario where both mutations update the same index.
- [ ] Test request failure: reconciliation is not called, outcome is `RequestFailed`, and the next queued operation runs. Test synchronous throw/null task and current-request IO cancellation. No implicit retry is permitted.
- [ ] Test remote success followed by throwing reconciliation: outcome contains the remote result in `ReconciliationFailed`; accepted data is unchanged; all existing client queries become stale and their pending generations retire. No fetch starts. Then call `Ensure` explicitly and recover without resubmitting the mutation.
- [ ] Implement graph-thread queue admission, untracked delegates, dispatched completion, and task settlement after reconciliation. Reconciliation failure invokes a non-user-code invalidation path, not the failed callback. Run cancellation callbacks outside publication and observe all eventual faults.
- [ ] Test disposing the editor owner during an in-flight mutation leaves the live client's save/reconciliation intact. Test disposing the client cancels pending caller tasks, prevents queued delegates from starting, and ignores late success/fault. A new client must remain untouched. Document that server-side effects may already have happened.
- [ ] Add a continuation that starts another mutation when an outcome completes; assert it joins the queue safely. Test graph disposal from an effect after a commit: all cached writes were already installed, and queued remote requests do not start after disposal.
- [ ] Run targeted .NET/Fable cases and commit `feat(query): coordinate mutations and reconciliation outcomes`.

## Task 6: Prove Kerams's full workflow with plain records

**Files:** Create/register `QueryDictionary.fs`; add `docs/content/guide/queries.md`; update the query README.

**Consumes:** complete proposed public interface.

**Produces:** an executable acceptance example, without a UI-framework dependency or per-field entity signals.

- [ ] Define the actual fixture shapes in this test file:

```fsharp
type WordPreview = { Id: int; Def1: string; Def2: string }
type SectionPreview = { Id: int; Name: string }
type Section = { Id: int; Name: string; Words: WordPreview list }
type Word = { Id: int; Def1: string; Def2: string; OtherFields: Map<string,string> }
type Index = { TotalWordCount: int; Sections: SectionPreview list }
type Saved = { Word: Word; SectionId: int; TotalWordCount: int }
type Page =
    | IndexPage of QueryLease<Index>
    | SectionPage of QueryLease<Section>
    | EditorPage of Word

let toPreview (word: Word) : WordPreview =
    { Id = word.Id; Def1 = word.Def1; Def2 = word.Def2 }
let upsert (word: Word) (items: WordPreview list) =
    let preview = toPreview word
    if items |> List.exists (fun item -> item.Id = word.Id) then
        items |> List.map (fun item -> if item.Id = word.Id then preview else item)
    else items @ [preview]
```

The fixture uses insertion order and a fully loaded section preview list. Do not silently claim this updater works for an arbitrary sorted/paginated endpoint.
- [ ] Write one end-to-end test that records endpoint calls: acquire index (no words), acquire section 7 (only previews), acquire word 42 (only its details), make a separate record draft, and submit a creation/update receipt through `Mutate`. Reconcile with the three-edit list in the spec. Assert the authoritative total, matching preview, loaded detail, unchanged unrelated section, and unchanged navigation-list identity.
- [ ] Test the add path with no prior detail entry: `UpdateIfLoaded` must not manufacture a detail cache entry. The editor accepts the returned word directly; a later detail-page acquisition legitimately fetches it. Test edit path does not increment totals locally, repeated same receipt upserts by ID, and failed save does not leak draft changes into previews.
- [ ] Test Back disposes the editor lease/draft scope and reads the already-reconciled section, without fetch if fresh. Test invalidated returning page calls `Ensure` and refreshes. Test Home disposes the old client and late old-session completions cannot affect the fresh index.
- [ ] Add a separate pagination case with compound key `{ SectionId; Page; Filter }`: invalidate matching existing pages after creation, do not append blindly, do not fetch unrelated/unvisited pages. Add preview-record selectors using an explicit comparer and verify an `OtherFields`-only edit can cut off preview consumers without promising zero selector evaluations.
- [ ] Write the guide using these ordinary-record types and concise functions. Explain query shapes, explicit demand, page leases, mutation outcomes, partial-loading limits, whole-record costs, and how an existing Elmish program owns the client and handles results. Explain field signals are optional and async sources are internal loading machinery, not a substitute for cache reconciliation.
- [ ] Run the complete dictionary tests and commit `docs(query): demonstrate partial dictionary reconciliation`.

## Task 7: Package, portability, and final acceptance

**Files:** Modify `tests/check-package-compatibility.ps1`, `tests/Ranvier.AotSmoke/Ranvier.AotSmoke.fsproj`, `tests/Ranvier.AotSmoke/Program.fs`, `docs/docs.fsproj`, `docs/content/guide/index.md`; verify all project/test registrations. The AOT project compile list confirms `Program.fs` is its entry point.

**Consumes:** the tested layer and example.

**Produces:** release-ready package wiring, public contract docs, compatibility evidence, and measured scoped work counts. Publishing is outside this plan.

- [ ] Add the Query project reference and `TrimmerRootAssembly Include="Ranvier.Query"` using the existing smoke conventions. Extend `Program.fs` to exercise a query, shared lease, mutation reconciliation, failure, and disposal in the smoke executable. Treat any newly required reflection as a design regression.
- [ ] Add `Ranvier.Query` to both traced/untraced package compatibility loops. Extend the fresh FSharp.Core 8.0.100 package-consumer smoke to instantiate the query layer, rather than merely checking that its package restores. Preserve existing package checks.
- [ ] Add the Query project reference to docs and link the guide. Audit public XML comments under `.claude/skills/fsharp-xml-docs/SKILL.md` and source comments with the required comment-hygiene plugin. Update README package claims only after corresponding gates pass.
- [ ] Use `fcs_public_api` before/after for core and new layer. Existing core public surface must be unchanged. Run `fcs_refactor_impact` before any departure from that constraint and revise the design explicitly.
- [ ] Add deterministic work-count assertions: defining 100 families performs zero fetches; reconciliation touches only declared existing keys; inactive invalidation performs zero fetches; two leases deduplicate; metadata-only transitions do not rerun data readers. Report list-reconstruction and selector costs honestly; this feature makes no latency speedup claim needing speculative benchmarks.
- [ ] Run all verification commands below, checking actual test counts and preserving outputs. Focused suites are not final acceptance. Resolve new failures; record pre-existing tool/context diagnostics separately and do not waive failed CLI gates.
- [ ] Review signatures, docs, state transitions and all five Review Focus items. Confirm no product code was added to the core. Commit the final integration as `build(query): verify portable query package`.

## Verification commands

CI uses direct build/test commands because the build script's timing summary can fail with redirected console output. Execute independent configurations sequentially or with separate output directories to avoid stale traced/untraced binaries.

```powershell
rtk proxy dotnet build src/Ranvier.Query/Ranvier.Query.fsproj -c Release
rtk proxy dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=false
rtk proxy dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=true
rtk proxy dotnet test tests/Ranvier.Tests -c Debug
rtk proxy dotnet test tests/Ranvier.CSharp.Tests -c Release
rtk proxy pwsh -NoProfile -File tests/check-package-compatibility.ps1
rtk proxy dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release
rtk proxy pwsh -NoProfile -Command '$env:RanvierTrace = "true"; rtk proxy dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release'
rtk proxy node fable/Ranvier.Tests.Fable/Report.mjs
rtk proxy pwsh -NoProfile -Command '$queryPlanFiles = @(rtk proxy git ls-files "*.fs"); rtk proxy dotnet fantomas --check $queryPlanFiles'
rtk git diff --check
```

Stage the new `.fs` files before the tracked-file format command, or explicitly include all newly created source paths. The Fable runner must include all newly registered Query lists; a passing report with no query cases is not acceptance.

Linux CI AOT gate:

```bash
rtk proxy dotnet publish tests/Ranvier.AotSmoke -c Release -r linux-x64 -o artifacts/aot
rtk proxy ./artifacts/aot/Ranvier.AotSmoke
```

On Windows use `win-x64` and run the corresponding `.exe`; do not run the Linux binary locally. The existing docs workflow builds with `rtk proxy dotnet fsi build.fsx -- docs`; run it with a PTY if required by the build CLI's console summary, inspect the generated guide/link checks, and do not invoke the deployment job. Stop every SageFs session created for implementation, without restarting/stopping the user's daemon.

## Research verification already performed

- `fslangmcp` workspace check: six diagnostics in the Fable test-project context; no error in core, Elmish or .NET tests. This is not a CLI build verdict.
- Core-project check: clean, zero errors.
- Public API inspection confirmed the graph/source/owner composition points used by this design.
- Official TanStack Query/Apollo sources and local Ranvier docs examined; citations are in research.
- No implementation tests, product build, runtime proof, or performance benchmark has been executed as part of planning.

## Self-review and execution boundary

Every design requirement maps to Tasks 1–7: public composition/native pending (1), identity/lifetime (2), query transitions/races (3), staging/invalidation (4), mutations/outcomes (5), user scenario (6), portability/package guarantees (7). The initial composition milestone is an explicit technical gate, not a claim of a tested implementation. Review the demand policy (`Acquire`/`Ensure`), last-lease eviction, and client-wide mutation serialization before implementation; these are deliberate scope decisions that can be changed together in the design and plan.

Implementation starts after design/plan review. No branch merge, package publication, or deployment is part of this plan.
