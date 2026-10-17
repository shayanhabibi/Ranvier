# Keyed Rows and Arms on Core Primitives Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the view's keyed reconciler, its arm switches and its `Scope` tree with core projections, memos and owners, adding the core options they need.

**Architecture:** The core gains two projection options, a teardown-error slice on `Owner`, and a shared duplicate-key check. Template and authored lists run on `Api.createProjectionOnce ProjectionOptions.CreateFirst`, and the view lays out the projection's rows with a minimal-move step. Arms run on a core memo with a root owner per arm, and view holes mount once. Mount lifetimes wrap core owners in a thin `MountLifetime`, which orders node release after cleanups and groups teardown errors per report.

**Tech Stack:** F# (.NET 8/10 and Fable 5.18), Expecto (core suites on .NET and Fable/Mocha), node `--test` with jsdom and Playwright Chromium (view suites), Vite, Solid 2.0.0-rc.14.

**Spec:** `docs/superpowers/specs/2026-10-14-keyed-rows-on-core-design.md`

## Global Constraints

- Every shell command is prefixed with `rtk`. Bash heredocs that contain backticks or apostrophes break; write scripts to files under `$CLAUDE_JOB_DIR/tmp` or the task's own scratch file.
- Commit messages: one conventional summary line passed with `-m`, no body, no `Co-Authored-By` or `Claude-Session` lines, no `!`. Never push, open a PR or merge.
- Run `rtk dotnet fantomas <file>...` on every changed handwritten `.fs` file before committing. Generated files (`Generated/*.fs`) are not formatted by hand.
- Comments follow `.claude/rules/comments.md`; reviewer-only remarks use `//FOR-REVIEW` or `(*FOR-REVIEW ... *)` and are stripped before the final gate.
- Fable: no interface type tests (`:? IFoo`), and no secondary constructors with a self-identifier.
- After touching `Template.fs`, `TemplateDom.fs`, `Mounting.fs` or `Lifetime.fs`, grep the emitted JavaScript for `curry`, `uncurry` and `partialApply` and compare with the counts recorded in Task 5 Step 1. A new occurrence fails the task.
- Duplicate keys raise the core's `InvalidOperationException` with the message "The projection produced the key <key> twice in one pass. Keys must be unique; check the keyOf function.".
- Keys are compared by the core: `'K: equality`, hashed through `Platform.KeyMap`. The view has no user-supplied or per-region key-identity comparer; arm selection uses an internal `createMemoWithComparer` comparer named `sameArm` over `fst` with `=` (Task 8). The core keeps `createMemoWithComparer` and every other comparer overload.
- Benchmarks compare against Solid 2 (`solid-js`, `@solidjs/web` and `@solidjs/compiler` all 2.0.0-rc.14, native compiler), call `flush()` after each timed write, run paired rounds in a seeded shuffled order (`--shuffle`), and commit raw JSON recording machine, Chromium version, date and commit. Bundle figures are measurements, not gates. Timing targets are reported, not gates.
- Production code carries no `PROTOTYPE (research/keyed-row-cost)` label and no `RANVIER_ROWS_BASELINE` define. Measurement variants live only on the throwaway branch of Task 10.
- Gates: core `rtk dotnet test tests/Ranvier.Tests -c Release -v q` (net8.0 and net10.0); core under Fable `rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release` then `rtk npm test` from the repository root; view from `examples/Fable.Ranvier.Playground`: `rtk npm run test:dom` and `rtk npm run test:browser`; view on .NET `rtk proxy dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj --configuration Release`. A filtered run is never an acceptance check.

## Review Focus

- A template row whose cleanup throws during a list update: the error is reported once, with that update, after the row's nodes leave (Task 7, test "a throwing row cleanup is reported once with its update after the row leaves").
- The mount disposed from inside a row's setup during an update: no further DOM writes, no report, host empty after disposal (Task 7, test "disposing the mount from a row's setup ends the update silently").
- Rows that contain their own lists, reordered and removed: inner rows move with their outer row and leave with it, and disposal leaves the host empty (Task 7, test "rows holding nested lists move and leave whole").
- Rows that render no nodes: they keep their position across moves through their `ranvier:row` marker and leave with it (Task 7, test "zero-node rows keep their place through their marker").
- Keys of different boxed types (`1`, `1L`, `"1"`): rows are kept or replaced exactly as `=` on `obj` decides (Task 7, test "boxed keys of different types keep separate rows").

---

## File structure

| File | Change | Responsibility |
|---|---|---|
| `src/Ranvier/Core.fs` | modify | `Owner.ErrorCount`, `Owner.ErrorsFrom` |
| `src/Ranvier/Types.fs` | modify | `ProjectionOptions` |
| `src/Ranvier/Projections.fs` | modify | create-first passes, memo-free rows, `ProjectionFailures.duplicateKey`, `RequireRowMemos` |
| `src/Ranvier/Combinators.fs` | modify | `Projection.ensureUniqueKeys`, row-memo guards |
| `src/Ranvier/Api.fs` | modify | `createProjectionWithOptions`, `createProjectionOnce` |
| `src/Ranvier/Counters.fs` | modify | `FactoryRowsCreated`, `OnceRowsCreated` tallies |
| `tests/Ranvier.Tests/Owners.fs`, `Projections.fs`; `fable/Ranvier.Tests.Fable/Main.fs` | modify | core tests and their Fable registration |
| `experiments/Ranvier.View.Foundation/Lifetime.fs` | rewrite | `ScopePhase`, `TeardownWindow`, `MountLifetime` over core `Owner` |
| `experiments/Ranvier.View.Foundation/Representation.fs` | modify | comparer-free `Backend`, `Operations`, `Keyed`, `Branch`, `Tree`, `Program`, `Html` |
| `experiments/Ranvier.View.Foundation/Template.fs` | modify | comparer-free `Region`; server duplicate check |
| `experiments/Ranvier.View.Foundation/TemplateDom.fs` | modify | `RegionKind`; region materialisers |
| `experiments/Ranvier.View.Foundation/Mounting.fs` | modify | mounts on `MountLifetime`; `Row`, `RowBuilder`, list, arm and hole regions; removal of `KeyedRegion` |
| `experiments/Ranvier.View.Foundation/Browser.fs`, `Prepared.fs`, `Authoring.fs` | modify | lifetimes and comparer-free signatures |
| `examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs` | modify | comparer-free `Keyed` override; a private copy of the old `Scope` |
| `examples/Fable.Ranvier.Playground/tests/*.fs`, `range.html`, `range.spec.mjs` | modify | updated and new view checks |
| `docs/research/2026-10-14-keyed-rows-on-core.md` and `docs/research/framework-performance/investigation/keyed-rows-on-core/*.json` | create | measurement report and raw data (Task 10) |
| `docs/research/2026-10-14-component-templates.md`, `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` | modify | supersession notes and status (Task 11) |

---

### Task 1: Teardown-error slices on `Owner`

**Files:**
- Modify: `src/Ranvier/Core.fs` (after `member _.Errors`, around line 680)
- Test: `tests/Ranvier.Tests/Owners.fs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Owner.ErrorCount: int`; `Owner.ErrorsFrom(index: int): exn[]` (raises `ArgumentOutOfRangeException` for a negative index).

- [ ] **Step 0: Record the baseline**

Run: `rtk dotnet test tests/Ranvier.Tests -c Release -v q`
Expected: PASS on net8.0 and net10.0. Write both totals into the task report; later tasks compare against them.

- [ ] **Step 1: Write the failing test**

Append inside the `testList "Owners" [ ... ]` of `tests/Ranvier.Tests/Owners.fs`, after the last test:

```fsharp
            test "ErrorsFrom returns the errors recorded at or after an index, in order" {
                let owner = new Owner ()
                owner.OnCleanup (fun () -> failwith "first")
                owner.Dispose ()
                let before = owner.ErrorCount
                // On a disposed scope a cleanup runs immediately, and its failure is recorded.
                owner.OnCleanup (fun () -> failwith "second")
                owner.OnCleanup (fun () -> failwith "third")

                Expect.equal before 1 "one error before the index"
                Expect.equal owner.ErrorCount 3 "every recorded error is counted"

                Expect.sequenceEqual
                    (owner.ErrorsFrom before |> Array.map _.Message)
                    [ "second"; "third" ]
                    "the errors from the index, in recording order"

                Expect.isEmpty (owner.ErrorsFrom 3) "an index at the count gives none"
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> owner.ErrorsFrom -1 |> ignore) "a negative index raises"
            }

            test "ErrorCount of a scope that recorded nothing is zero" {
                let owner = new Owner ()
                owner.Dispose ()
                Expect.equal owner.ErrorCount 0 "no errors"
                Expect.isEmpty (owner.ErrorsFrom 0) "and no slice"
            }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet build tests/Ranvier.Tests -c Release`
Expected: FAIL with `The type 'Owner' does not define the field, constructor or member 'ErrorCount'`.

- [ ] **Step 3: Write minimal implementation**

In `src/Ranvier/Core.fs`, directly after the `member _.Errors = ...` member of `Owner`:

```fsharp
    member _.ErrorCount = if isNull errors then 0 else errors.Count

    /// <summary>
    /// The errors of <c>Errors</c> recorded at or after position <c>index</c>, in recording order.
    /// </summary>
    /// <exception cref="T:System.ArgumentOutOfRangeException"><c>index</c> is negative.</exception>
    member _.ErrorsFrom(index: int) : exn[] =
        if index < 0 then
            raise (ArgumentOutOfRangeException (nameof index))

        if isNull errors || index >= errors.Count then
            [||]
        else
            errors.GetRange(index, errors.Count - index).ToArray ()
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test tests/Ranvier.Tests -c Release -v q`
Expected: PASS, two more tests per framework than Step 0.

Run: `rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release` then `rtk npm test`
Expected: PASS (the Owners list is already registered in `Main.fs`).

- [ ] **Step 5: Format and commit**

```bash
rtk dotnet fantomas src/Ranvier/Core.fs tests/Ranvier.Tests/Owners.fs
rtk git add src/Ranvier/Core.fs tests/Ranvier.Tests/Owners.fs
rtk git commit -m "feat(core): read the errors an owner recorded from a position"
```

---

### Task 2: Create-first projection passes

**Files:**
- Modify: `src/Ranvier/Types.fs` (after `Status`), `src/Ranvier/Projections.fs` (`Projection` members, `ApplyDiff`, `RowsOf`), `src/Ranvier/Api.fs` (after `createProjectionWith`)
- Test: `tests/Ranvier.Tests/Projections.fs`, `fable/Ranvier.Tests.Fable/Main.fs`

**Interfaces:**
- Consumes: nothing.
- Produces: `[<Flags>] type ProjectionOptions = None = 0 | CreateFirst = 1`; `Api.createProjectionWithOptions (options: ProjectionOptions) (keyOf: 'T -> 'K) (factory: (unit -> 'T) -> (unit -> 'V)) (source: unit -> 'T seq) : Projection<'K, 'V>`; internal `Projection.CreateFirst: bool` (settable), `Projection.Discard(key: 'K)`, `Projection.IsDisposedInternal: bool`, `Retire` that skips the memo notification of an entry whose `Row` is null, and `RowsOf.CreateAdded` that stops creating once the projection is disposed.

Ruling recorded for the spec: `ApplyDelta` is left unchanged. It serves only the internal delta-driven projections (`Collections.fs`, `Combinators.fs`), which never receive options, so the spec's `ApplyDelta` paragraph is removed in the plan commit.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Ranvier.Tests/Projections.fs`:

```fsharp
[<Tests>]
let rowOptionTests =
    testList
        "Projection row options"
        [
            test "create-first runs an added row's factory before a removed row's cleanup" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let log = ResizeArray<string>()

                let proj =
                    createProjectionWithOptions
                        ProjectionOptions.CreateFirst
                        id
                        (fun item ->
                            let key = item ()
                            log.Add ("create " + string key)
                            onCleanup (fun () -> log.Add ("retire " + string key))
                            fun () -> item ())
                        (fun () -> items.Value)

                createEffect (fun () -> proj.Keys |> ignore)
                items.Value <- [ 2 ]

                Expect.sequenceEqual log [ "create 1"; "create 2"; "retire 1" ] "the new row exists before the old row retires"
            }

            test "without options a pass retires before it creates" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let log = ResizeArray<string>()

                let proj =
                    createProjectionWithOptions
                        ProjectionOptions.None
                        id
                        (fun item ->
                            let key = item ()
                            log.Add ("create " + string key)
                            onCleanup (fun () -> log.Add ("retire " + string key))
                            fun () -> item ())
                        (fun () -> items.Value)

                createEffect (fun () -> proj.Keys |> ignore)
                items.Value <- [ 2 ]

                Expect.sequenceEqual log [ "create 1"; "retire 1"; "create 2" ] "the default order is unchanged"
            }

            test "a failing create-first factory leaves the previous rows, items and keys" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a") ]
                let created = ResizeArray<int>()
                let retired = ResizeArray<int>()
                let mutable failOn = 0

                let proj =
                    createProjectionWithOptions
                        ProjectionOptions.CreateFirst
                        fst
                        (fun item ->
                            let key = fst (item ())
                            created.Add key
                            onCleanup (fun () -> retired.Add key)

                            if key = failOn then
                                failwith "factory"

                            fun () -> snd (item ()))
                        (fun () -> items.Value)

                createEffect (fun () ->
                    try
                        proj.Keys |> ignore
                    with _ ->
                        ())

                failOn <- 3
                items.Value <- [ (1, "b"); (2, "x"); (3, "y") ]

                Expect.sequenceEqual created [ 1; 2; 3 ] "the pass created rows 2 and 3"
                Expect.sequenceEqual retired [ 3; 2 ] "and disposed them, last first"
                Expect.equal proj.Status Status.Error "the pass failed"

                failOn <- 0
                items.Value <- [ (1, "c"); (2, "z") ]

                Expect.sequenceEqual created [ 1; 2; 3; 2 ] "row 1 survived the failed pass; row 2 is created again"
                Expect.sequenceEqual proj.Keys [ 1; 2 ] "the next pass applies"
                Expect.equal (proj.Get 1) "c" "with the survivor's latest item"
            }

            test "a removed row's cleanup that disposes the projection stops a create-first pass" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let retired = ResizeArray<int>()
                let mutable proj = Unchecked.defaultof<Projection<int, int>>

                proj <-
                    createProjectionWithOptions
                        ProjectionOptions.CreateFirst
                        id
                        (fun item ->
                            let key = item ()

                            onCleanup (fun () ->
                                retired.Add key

                                if key = 2 then
                                    proj.Dispose ())

                            fun () -> item () * 10)
                        (fun () -> items.Value)

                createEffect (fun () -> proj.Keys |> ignore)
                items.Value <- [ 1; 3 ]

                Expect.equal proj.Status Status.None "the pass did not fail"
                Expect.isNull proj.Error "and recorded no error"
                Expect.containsAll retired [ 1; 2; 3 ] "the disposal took the created row with it"
            }
        ]
```

Register the list in `fable/Ranvier.Tests.Fable/Main.fs`, inside `testList "Projections.fs" [ ... ]`, after `Ranvier.Tests.Projections.deltaReaderTests`:

```fsharp
                    Ranvier.Tests.Projections.rowOptionTests
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet build tests/Ranvier.Tests -c Release`
Expected: FAIL with `The value or constructor 'createProjectionWithOptions' is not defined`.

- [ ] **Step 3: Add the options type**

In `src/Ranvier/Types.fs`, directly after the `Status` enum:

```fsharp
/// <summary>
/// Options of a keyed projection.
/// </summary>
[<Flags>]
type ProjectionOptions =
    | None = 0
    /// <summary>
    /// A pass creates its added rows before it retires its removed rows. A factory failure fails the pass, and every
    /// row, item and key stays as the previous pass applied it.
    /// </summary>
    | CreateFirst = 1
```

- [ ] **Step 4: Implement the pass order**

In `src/Ranvier/Projections.fs`, `Projection` type:

1. Next to `member internal _.Seen = seen`, add:

```fsharp
    /// <summary>Whether a pass creates its added rows before it retires its removed ones.</summary>
    member val internal CreateFirst = false with get, set

    member internal _.IsDisposedInternal = disposed

    /// <summary>
    /// Removes the row of <c>key</c> created by a failing create-first pass and disposes it with its scope.
    /// </summary>
    member internal this.Discard(key: 'K) =
        let entry = entries.Find key

        if not (isNull entry) then
            inCleanup <- true

            try
                entries.Remove key
                this.Retire entry
            finally
                inCleanup <- false
```

2. Replace `ApplyDiff` and `ApplyAdditions` with:

```fsharp
    /// <summary>
    /// Disposes removed keys, creates added rows, writes changed items and publishes the key set, in one batch. With
    /// <c>CreateFirst</c> the added rows are created before the removed keys retire. The pass stops after the removals
    /// if a removed key's cleanup disposes the projection.
    /// </summary>
    member private this.ApplyDiff() =
        entries.Iterate (fun key _ ->
            if not (seen.Contains key) then
                removed.Add key)

        if this.CreateFirst then
            this.Pass.CreateAdded ()
            this.RetireRemoved ()

            if not disposed then
                this.SettleAdditions ()
        else
            this.RetireRemoved ()

            if not disposed then
                this.ApplyAdditions ()

    member private this.RetireRemoved() =
        if removed.Count > 0 then
            inCleanup <- true

            try
                for key in removed do
                    let entry = entries.Find key

                    if not (isNull entry) then
                        entries.Remove key
                        this.Retire entry
            finally
                inCleanup <- false

    /// <summary>
    /// The diff after its removals.
    /// </summary>
    member private this.ApplyAdditions() =
        this.Pass.CreateAdded ()
        this.SettleAdditions ()

    /// <summary>
    /// The diff after its creations and removals: hidden keys, item writes and the key set.
    /// </summary>
    member private this.SettleAdditions() =
        if not (isNull (box hidden)) then
            this.SettleHidden ()

        this.Pass.CommitWrites ()
        this.Pass.ClearStaged ()
        publishKeys ()

        this.ObservePass ()
```

3. In `RowsOf.CreateFactored`, replace the `with` branches of the factory `try` with:

```fsharp
                    with
                    | NotReadyException _ as ex ->
                        let message =
                            "The projection's factory for key "
                            + string key
                            + " read a pending source. The factory runs once per key, untracked, and cannot wait for a source to settle. Read the source inside the reader the factory returns."

                        let failure =
#if FABLE_COMPILER
                            let failure = InvalidOperationException message
                            Platform.setInner failure ex
                            failure
#else
                            InvalidOperationException (message, ex)
#endif

                        if this.CreateFirst then
                            raise failure

                        fun () -> raise failure
                    | ex ->
                        if this.CreateFirst then
                            reraise ()

                        let captured = Platform.captureFailure null ex
                        fun () -> Platform.rethrowStored captured
```

4. Replace `RowsOf`'s private `CreateAdded` with:

```fsharp
    member private this.CreateAdded() =
        if adds.Count > 0 then
            if valueForm then
                graph.RunOwned (this.Scope, this.CreateMapped)
            else
                let mutable started = 0

                try
                    while started < adds.Count && not this.IsDisposedInternal do
                        let struct (key, item) = adds[started]
                        started <- started + 1
                        this.CreateFactored (key, item)
                with _ when this.CreateFirst ->
                    for index in started - 1 .. -1 .. 0 do
                        let struct (key, _) = adds[index]
                        this.Discard key

                    reraise ()
```

5. In `Projection.Retire`, replace `entry.Row.NotifyRemoved ()` with the guarded call below. The row whose factory failed under `CreateFirst` has no `Row` when `Discard` retires it:

```fsharp
        if not (isNull (box entry.Row)) then
            entry.Row.NotifyRemoved ()
```

- [ ] **Step 5: Add the API**

In `src/Ranvier/Api.fs`, directly after `createProjectionWith`:

```fsharp
    /// <summary>
    /// <c>createProjectionWith</c> with <c>options</c>.
    /// </summary>
    /// <remarks>
    /// With <c>ProjectionOptions.CreateFirst</c> a pass creates its added rows, then retires its removed rows, then
    /// writes survivors' items and publishes the key set. A factory that raises fails the pass: the rows the pass created
    /// are disposed, last first, and every row, item and key stays as the previous pass applied it. The next change to a
    /// source retries the pass.
    /// </remarks>
    let createProjectionWithOptions
        (options: ProjectionOptions)
        (keyOf: 'T -> 'K)
        (factory: (unit -> 'T) -> (unit -> 'V))
        (source: unit -> 'T seq)
        : Projection<'K, 'V> =
        let rows =
            new KeyedProjection<'T, 'K, 'V> (Graph.Current, keyOf, Unchecked.defaultof<'T -> 'V>, factory, source)

        rows.CreateFirst <- (options &&& ProjectionOptions.CreateFirst) = ProjectionOptions.CreateFirst
        rows :> Projection<'K, 'V>
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `rtk dotnet test tests/Ranvier.Tests -c Release -v q`
Expected: PASS, four more tests per framework than after Task 1.

Run: `rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release` then `rtk npm test`
Expected: PASS, including "Projection row options".

- [ ] **Step 7: Format and commit**

```bash
rtk dotnet fantomas src/Ranvier/Types.fs src/Ranvier/Projections.fs src/Ranvier/Api.fs tests/Ranvier.Tests/Projections.fs fable/Ranvier.Tests.Fable/Main.fs
rtk git add src/Ranvier/Types.fs src/Ranvier/Projections.fs src/Ranvier/Api.fs tests/Ranvier.Tests/Projections.fs fable/Ranvier.Tests.Fable/Main.fs
rtk git commit -m "feat(core): add create-first projection passes that keep the previous rows when a factory fails"
```

---

### Task 3: Memo-free factory rows

**Files:**
- Modify: `src/Ranvier/Projections.fs`, `src/Ranvier/Combinators.fs` (module `Projection`), `src/Ranvier/Api.fs`, `src/Ranvier/Counters.fs`
- Test: `tests/Ranvier.Tests/Projections.fs` (`rowOptionTests`)

**Interfaces:**
- Consumes: `ProjectionOptions`, `Projection.CreateFirst`, `Projection.Discard`, `RowsOf.CreateAdded` (Task 2).
- Produces: `Api.createProjectionOnce (options: ProjectionOptions) (keyOf: 'T -> 'K) (factory: (unit -> 'T) -> 'V) (source: unit -> 'T seq) : Projection<'K, 'V>`; internal `Projection.OnceValue: Func<RowEntry<'K, 'V>, 'V>` (null for memo rows), `Projection.RequireRowMemos(operation: string)`; internal `OnceRow<'T, 'K, 'V>`; private `RowsOf.FactoryFailure(key: 'K, error: exn, advice: string) : unit -> 'V`, shared by `CreateFactored` and `CreateOnce`; counters `FactoryRowsCreated` (index 11) and `OnceRowsCreated` (index 12).

The prototype is `git diff fcda344 aa5b811 -- src/Ranvier` in `C:\Users\shaya\RiderProjects\Ranvier-keyed-rows`. Port its non-baseline branches without the `#if RANVIER_ROWS_BASELINE` blocks or `PROTOTYPE` labels, with the changes below.

- [ ] **Step 1: Write the failing tests**

Append to the `rowOptionTests` list in `tests/Ranvier.Tests/Projections.fs`:

```fsharp
            test "a memo-free row is its factory's result, built once per key" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ (1, "a"); (2, "b") ]
                let mutable builds = 0

                let proj =
                    createProjectionOnce
                        ProjectionOptions.None
                        fst
                        (fun item ->
                            builds <- builds + 1
                            ref (snd (item ())))
                        (fun () -> items.Value)

                let one = proj.Get 1
                items.Value <- [ (1, "c"); (2, "b") ]

                Expect.isTrue (obj.ReferenceEquals (one, proj.Get 1)) "the row value is stored, not recomputed"
                Expect.equal builds 2 "one build per key"
            }

            test "a tracked read of a memo-free row wakes when its key leaves" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1; 2 ]
                let proj = createProjectionOnce ProjectionOptions.None id (fun item -> item () * 10) (fun () -> items.Value)
                let seen = ResizeArray<int option>()
                createEffect (fun () -> seen.Add (proj.TryGet 2))
                items.Value <- [ 1 ]

                Expect.sequenceEqual seen [ Some 20; None ] "the reader re-ran when key 2 left"
            }

            test "a memo-free factory failure is raised by reads of its row" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]

                let proj =
                    createProjectionOnce
                        ProjectionOptions.None
                        id
                        (fun item -> if item () = 1 then failwith "factory" else item ())
                        (fun () -> items.Value)

                Expect.sequenceEqual proj.Keys [ 1 ] "the pass succeeds"
                Expect.throwsC (fun () -> proj.Get 1 |> ignore) (fun error -> Expect.equal error.Message "factory" "the stored failure")
            }

            test "memo-free create-first rows keep the previous rows when a factory fails" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let retired = ResizeArray<int>()
                let mutable failOn = 0

                let proj =
                    createProjectionOnce
                        ProjectionOptions.CreateFirst
                        id
                        (fun item ->
                            let key = item ()
                            onCleanup (fun () -> retired.Add key)

                            if key = failOn then
                                failwith "factory"

                            key * 10)
                        (fun () -> items.Value)

                createEffect (fun () ->
                    try
                        proj.Keys |> ignore
                    with _ ->
                        ())

                failOn <- 3
                items.Value <- [ 2; 3 ]

                Expect.sequenceEqual retired [ 3; 2 ] "this pass's rows retire, last first"
                Expect.equal proj.Status Status.Error "the pass failed"
                failOn <- 0
                items.Value <- [ 1; 4 ]
                Expect.sequenceEqual proj.Keys [ 1; 4 ] "the next pass applies"
                Expect.equal (proj.Get 1) 10 "row 1 survived"
            }

            test "value readers and combinators reject memo-free rows" {
                use g = new Graph ()
                use _ = g.Activate ()
                let items = createSignal [ 1 ]
                let proj = createProjectionOnce ProjectionOptions.None id (fun item -> item ()) (fun () -> items.Value)

                Expect.throwsT<NotSupportedException> (fun () -> proj.NewValueReader () |> ignore) "NewValueReader"
                Expect.throwsT<NotSupportedException> (fun () -> Projection.filter (fun _ -> true) proj |> ignore) "filter"
                Expect.throwsT<NotSupportedException> (fun () -> Projection.map id proj |> ignore) "map"
                Expect.throwsT<NotSupportedException> (fun () -> Projection.sortBy id proj |> ignore) "sortBy"
                Expect.throwsT<NotSupportedException> (fun () -> Projection.fold (+) 0 proj |> ignore) "fold"
            }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet build tests/Ranvier.Tests -c Release`
Expected: FAIL with `The value or constructor 'createProjectionOnce' is not defined`.

- [ ] **Step 3: Implement memo-free rows**

In `src/Ranvier/Projections.fs`:

1. Remove `Sealed` from `ItemRow`'s attributes (`[<AllowNullLiteral>]` stays). After `ItemRow`, add:

```fsharp
/// <summary>
/// A row whose value is its factory's result, held without a row memo. <c>Row</c> is null. <c>Reader</c> is null, or
/// raises the factory's failure.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal OnceRow<'T, 'K, 'V>(key: 'K, item: Signal<'T>) =
    inherit ItemRow<'T, 'K, 'V>(key, item)

    member val Value: 'V = Unchecked.defaultof<'V> with get, set
```

2. In `Projection`, replace `rowObserved` with:

```fsharp
    /// <summary>
    /// Whether anything other than the projection's <c>RowWatch</c> reads the row. A memo-free row is observed through
    /// the key set.
    /// </summary>
    let rowObserved (entry: RowEntry<'K, 'V>) =
        not (isNull (box entry.Row))
        && entry.Row.ObserverCount > (if entry.Watched then 1 else 0)
```

3. Next to `CreateFirst`, add:

```fsharp
    /// <summary>The value of a memo-free row, or null while the projection's rows have memos.</summary>
    member val internal OnceValue: Func<RowEntry<'K, 'V>, 'V> = null with get, set

    /// <summary>Raises <c>NotSupportedException</c> naming <c>operation</c> when the rows have no row memos.</summary>
    member internal this.RequireRowMemos(operation: string) =
        if not (isNull this.OnceValue) then
            raise (
                NotSupportedException (
                    operation
                    + " reads row memos, and a projection from createProjectionOnce has none. Use createProjectionWith."
                )
            )
```

4. `Retire` already guards the memo notification (Task 2 Step 4); leave it unchanged.

5. In `TrackRow`, replace the body's `graph.Track (existing.Row :> ISource)` with:

```fsharp
            if isNull (box existing.Row) then
                graph.Track (keys :> ISource)
            else
                graph.Track (existing.Row :> ISource)
```

6. Replace `ReadRow` with:

```fsharp
    /// <summary>
    /// A tracked read of a row. A pending row raises <c>NotReadyException</c> naming the source its reader awaits, and
    /// links the caller to that source as well as to the row. A memo-free row is a tracked read of the key set.
    /// </summary>
    member private this.ReadRow(entry: RowEntry<'K, 'V>) : 'V =
        if isNull (box entry.Row) then
            graph.Track (keys :> ISource)

            if not observed && keys.ObserverCount > 0 then
                observed <- true

            this.OnceValue.Invoke entry
        else
            try
                try
                    entry.Row.Value
                with NotReadyException _ ->
                    let mutable awaited = entry.Row :> INode
                    let mutable e = entry.Row.PendingSources.GetEnumerator ()

                    if e.MoveNext () then
                        awaited <- e.Current

                    match Awaited.source awaited with
                    | ValueSome source -> graph.Track source
                    | ValueNone -> ()

                    raise (graph.NotReady awaited)
            finally
                if not observed && rowObserved entry then
                    observed <- true
```

7. In `Snapshot` (the loop around `(entry.Row :> ISource).UpdateIfNecessary()`), replace the inner `if not (isNull entry) then ...` body with:

```fsharp
                if not (isNull entry) then
                    if isNull (box entry.Row) then
                        if isNull (box entry.Reader) then
                            copy.Add (key, this.OnceValue.Invoke entry)
                    else
                        (entry.Row :> ISource).UpdateIfNecessary()

                        if entry.Settled then
                            copy.Add (key, entry.Row.Peek)
```

8. First line of `NewValueReader` and of `AsObservableCollection`:

```fsharp
        this.RequireRowMemos "NewValueReader"
```

```fsharp
        this.RequireRowMemos "AsObservableCollection"
```

9. In `RowsOf`, after `let writes = ...`:

```fsharp
    /// <summary>The memo-free factory, or null.</summary>
    let mutable once: (unit -> 'T) -> 'V = Unchecked.defaultof<_>
```

Before `CreateFactored`, add the factory-failure helper that `CreateFactored` and `CreateOnce` share:

```fsharp
    /// <summary>
    /// A reader that raises the failure of the factory for <c>key</c>. Under <c>CreateFirst</c> the failure is raised at
    /// once instead. A pending-source read becomes an <c>InvalidOperationException</c> whose message ends with
    /// <c>advice</c>.
    /// </summary>
    member private this.FactoryFailure(key: 'K, error: exn, advice: string) : unit -> 'V =
        match error with
        | NotReadyException _ ->
            let message =
                "The projection's factory for key "
                + string key
                + " read a pending source. The factory runs once per key, untracked, and cannot wait for a source to settle."
                + advice

            let failure =
#if FABLE_COMPILER
                let failure = InvalidOperationException message
                Platform.setInner failure error
                failure
#else
                InvalidOperationException (message, error)
#endif

            if this.CreateFirst then
                raise failure

            fun () -> raise failure
        | _ ->
            let captured = Platform.captureFailure null error

            if this.CreateFirst then
                Platform.rethrowStored captured

            fun () -> Platform.rethrowStored captured
```

In `CreateFactored`, replace the factory `try` (with the two `with` branches Task 2 wrote) with:

```fsharp
                entry.Reader <-
                    try
                        factory (fun () -> source.Value)
                    with ex ->
                        this.FactoryFailure (key, ex, " Read the source inside the reader the factory returns.")
```

After `CreateFactored`, add `UseOnce` and `CreateOnce`:

```fsharp
    /// <summary>Makes every row a memo-free row: the factory's result, stored once per key.</summary>
    member internal this.UseOnce(factory: (unit -> 'T) -> 'V) =
        once <- factory

        this.OnceValue <-
            Func<RowEntry<'K, 'V>, 'V>(fun entry ->
                let row = entry :?> OnceRow<'T, 'K, 'V>

                if isNull (box row.Reader) then row.Value else row.Reader ())

    member private this.CreateOnce(key: 'K, item: 'T) =
        let entry = OnceRow<'T, 'K, 'V>(key, Signal<'T>(graph, item))
        Tracer.Part (graph, (entry.Item :> INode).Id, (this :> INode).Id, box key)
        this.AddEntry (key, entry)
        let source = entry.Item
        let keyScope = new Owner (graph.Root)
        keyScope.SetParent (this.Scope.AttachLinked keyScope)
        entry.Scope <- keyScope
        Tracer.PartScope (graph, keyScope, (this :> INode).Id, box key)
#if RANVIER_COUNTERS
        Counters.OnceRowCreated ()
#endif

        graph.RunOwned (
            keyScope,
            fun () ->
                try
                    entry.Value <- once (fun () -> source.Value)
                with ex ->
                    entry.Reader <- this.FactoryFailure (key, ex, "")
        )
```

In `CreateFactored`, after `Tracer.PartScope (...)`:

```fsharp
#if RANVIER_COUNTERS
        Counters.FactoryRowCreated ()
#endif
```

In `RowsOf.CreateAdded` (Task 2), replace the `while` loop body's last line `this.CreateFactored (key, item)` and the line before the loop:

```fsharp
                let memoFree = not (isNull (box once))
                let mutable started = 0

                try
                    while started < adds.Count && not this.IsDisposedInternal do
                        let struct (key, item) = adds[started]
                        started <- started + 1

                        if memoFree then
                            this.CreateOnce (key, item)
                        else
                            this.CreateFactored (key, item)
```

10. In `src/Ranvier/Counters.fs`, append `"FactoryRowsCreated"` and `"OnceRowsCreated"` to `Tallies.names`, and after `Flushed`:

```fsharp
    /// <summary>A factory-form projection row, with its row memo.</summary>
    static member internal FactoryRowCreated() =
        Tallies.bump 11

    /// <summary>A memo-free factory row.</summary>
    static member internal OnceRowCreated() =
        Tallies.bump 12
```

11. In `src/Ranvier/Combinators.fs`, module `Projection`, make the first line of `filter`, `choose`, `map`, `mapWith`, `sortBy`, `slice`, `groupBy`, `foldGroup` and `fold` a guard naming the function, for example in `filter`:

```fsharp
        upstream.RequireRowMemos "Projection.filter"
```

(`take`, `skip`, `sub` go through `slice`; `sumBy`, `countBy`, `exists` and `forall` go through `map`.)

12. In `src/Ranvier/Api.fs`, after `createProjectionWithOptions`:

```fsharp
    /// <summary>
    /// A keyed projection whose row value is the factory's result, stored once per key and read without a row memo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>factory</c> runs once per key, untracked, inside a scope that lives until the key is removed, and receives an
    /// accessor for the key's latest item. A tracked read of a row tracks the key set, so the reader wakes when the key
    /// leaves. Without <c>ProjectionOptions.CreateFirst</c> a factory failure is stored and raised by every read of the
    /// row; with it, the failure fails the pass as <c>createProjectionWithOptions</c> describes.
    /// </para>
    /// <para>
    /// <c>NewValueReader</c>, <c>AsObservableCollection</c> and the <c>Projection</c> combinators raise
    /// <c>NotSupportedException</c> on the projection.
    /// </para>
    /// </remarks>
    let createProjectionOnce
        (options: ProjectionOptions)
        (keyOf: 'T -> 'K)
        (factory: (unit -> 'T) -> 'V)
        (source: unit -> 'T seq)
        : Projection<'K, 'V> =
        let rows =
            new KeyedProjection<'T, 'K, 'V> (Graph.Current, keyOf, Unchecked.defaultof<'T -> 'V>, Unchecked.defaultof<_>, source)

        rows.CreateFirst <- (options &&& ProjectionOptions.CreateFirst) = ProjectionOptions.CreateFirst
        rows.UseOnce factory
        rows :> Projection<'K, 'V>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test tests/Ranvier.Tests -c Release -v q`
Expected: PASS, five more tests per framework than after Task 2.

Run: `rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release` then `rtk npm test`
Expected: PASS.

Run: `rtk dotnet build src/Ranvier -c Release -p:RanvierCounters=true`
Expected: build succeeds.

- [ ] **Step 5: Format and commit**

```bash
rtk dotnet fantomas src/Ranvier/Projections.fs src/Ranvier/Combinators.fs src/Ranvier/Api.fs src/Ranvier/Counters.fs tests/Ranvier.Tests/Projections.fs
rtk git add src/Ranvier tests/Ranvier.Tests/Projections.fs
rtk git commit -m "feat(core): add memo-free factory rows through createProjectionOnce"
```

---

### Task 4: A shared duplicate-key check

**Files:**
- Modify: `src/Ranvier/Projections.fs` (before `RowsOf`; `RowsOf.Visit`), `src/Ranvier/Combinators.fs` (module `Projection`)
- Test: `tests/Ranvier.Tests/Projections.fs` (`rowOptionTests`)

**Interfaces:**
- Consumes: the `rowOptionTests` list in `tests/Ranvier.Tests/Projections.fs` (Task 2), already registered in `fable/Ranvier.Tests.Fable/Main.fs`.
- Produces: `Projection.ensureUniqueKeys (keyOf: 'T -> 'K) (items: 'T seq) : unit`; internal `ProjectionFailures.duplicateKey (key: 'K) : InvalidOperationException`.

- [ ] **Step 1: Write the failing tests**

In `tests/Ranvier.Tests/Projections.fs`, directly before `[<Tests>] let rowOptionTests`, add:

```fsharp
/// <summary>The message <c>action</c> raises; the test fails when it raises nothing.</summary>
let private raisedMessage (action: unit -> unit) : string =
    let message = ref ""
    Expect.throwsC action (fun error -> message.Value <- error.Message)
    message.Value

/// <summary>The messages a projection pass over <c>items</c> and <c>ensureUniqueKeys</c> over them raise.</summary>
let private duplicateMessages<'T when 'T: equality> (items: 'T list) : string * string =
    let proj = createProjection id id (fun () -> items)
    raisedMessage (fun () -> proj.Keys |> ignore), raisedMessage (fun () -> Projection.ensureUniqueKeys id items)
```

Append to `rowOptionTests`:

```fsharp
            test "ensureUniqueKeys raises the pass's message for int, string, record and tuple keys" {
                use g = new Graph ()
                use _ = g.Activate ()

                for label, (fromPass, fromCheck) in
                    [
                        "int", duplicateMessages [ 1; 2; 1 ]
                        "string", duplicateMessages [ "a"; "a" ]
                        "record", duplicateMessages [ {| Id = 1 |}; {| Id = 1 |} ]
                        "tuple", duplicateMessages [ (1, "a"); (1, "a") ]
                    ] do
                    Expect.equal fromCheck fromPass label
            }

            test "ensureUniqueKeys keeps boxed keys of different types apart, as = does" {
                Projection.ensureUniqueKeys id [ box 1; box 1L; box "1" ]
            }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `rtk dotnet build tests/Ranvier.Tests -c Release`
Expected: FAIL with `The value, constructor, namespace or type 'ensureUniqueKeys' is not defined`.

- [ ] **Step 3: Implement**

In `src/Ranvier/Projections.fs`, directly before the `RowsOf` type:

```fsharp
/// <summary>Failures shared by projection passes and key checks.</summary>
module internal ProjectionFailures =
    let duplicateKey (key: 'K) =
        InvalidOperationException (
            "The projection produced the key "
            + string key
            + " twice in one pass. Keys must be unique; check the keyOf function."
        )
```

In `RowsOf.Visit`, replace the `raise (InvalidOperationException (...))` expression with:

```fsharp
            raise (ProjectionFailures.duplicateKey key)
```

In `src/Ranvier/Combinators.fs`, module `Projection`, before `filter`:

```fsharp
    /// <summary>Raises the exception a projection pass raises for the first key of <c>items</c> that repeats an earlier key.</summary>
    /// <remarks>Keys are compared as a projection compares them: <c>'K: equality</c>, hashed.</remarks>
    let ensureUniqueKeys (keyOf: 'T -> 'K) (items: 'T seq) : unit =
        let seen = Platform.KeySet<'K>()

        for item in items do
            let key = keyOf item

            if not (seen.Add key) then
                raise (ProjectionFailures.duplicateKey key)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `rtk dotnet test tests/Ranvier.Tests -c Release -v q`, then the Fable suite as in Task 1 Step 4.
Expected: PASS on both, two more tests per framework.

- [ ] **Step 5: Format and commit**

```bash
rtk dotnet fantomas src/Ranvier/Projections.fs src/Ranvier/Combinators.fs tests/Ranvier.Tests/Projections.fs
rtk git add src/Ranvier tests/Ranvier.Tests/Projections.fs
rtk git commit -m "feat(core): share the projection's duplicate-key check through Projection.ensureUniqueKeys"
```

---

### Task 5: Comparer-free keyed and switch surfaces

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Representation.fs`, `Template.fs`, `TemplateDom.fs`, `Mounting.fs`, `Prepared.fs`, `Browser.fs`, `Authoring.fs`, `RepresentationChecks.fsx`; `examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs`; `examples/Fable.Ranvier.Playground/App.fs`
- Test: `examples/Fable.Ranvier.Playground/tests/` `RangeChecks.fs`, `TemplateChecks.fs`, `RepresentationChecks.fs`, `BindingBench.fs`, `BindingIntegrationChecks.fs`, `DelegationChecks.fs`, `FrameworkDiagnostics.fs`, `AuthoringChecks.fs`

**Interfaces:**
- Consumes: `Projection.ensureUniqueKeys` (Task 4).
- Produces: `Backend.Keyed: (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit`; `Backend.Switch` with the same shape (default calls `Keyed`); `Operations.Keyed` and `Operations.Switch: (unit -> (obj * obj) array) -> ((unit -> obj) -> 'view) -> 'view`; `Keyed.list ops key read render`, `Keyed.array ops key read render`, `Branch.select ops key read render`; `Tree.View.Keyed of (unit -> (obj * obj) array) * ((unit -> obj) -> View)`; `Template.Region = { Items; Row }`; `TemplateDom.CompiledRegion` without `Same`; `MountServices.Region: Node * Node * bool * bool * (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit`.

Ruling recorded for the spec: arm identity needs no comparer. A `matchWith` key is the arm's definition object and a `branch` key is the tuple of the user key and the definition, and `=` on `obj` compares definitions by reference on .NET and under Fable.

- [ ] **Step 1: Record the emitted-JavaScript and test baselines**

From `examples/Fable.Ranvier.Playground`:

```bash
rtk npm run test:dom
rtk grep -c "curry[0-9]*(\|uncurry[0-9]*(\|partialApply(" generated-tests/experiments/Ranvier.View.Foundation/Mounting.js generated-tests/experiments/Ranvier.View.Foundation/TemplateDom.js generated-tests/experiments/Ranvier.View.Foundation/Template.js generated-tests/experiments/Ranvier.View.Foundation/Lifetime.js
```

Expected: 314 tests pass. Record each file's count in the task report: later tasks compare against them.

- [ ] **Step 2: Update the tests first**

1. In every test file listed under **Test** and in `examples/Fable.Ranvier.Playground/App.fs`, delete the comparer argument of each `Keyed.list`, `Keyed.array`, `Branch.select`, `keyedArray` and `branch` call: the `(=)` or lambda between `ops`/`Tree.ops`/`Program.ops` (or the function name, for `keyedArray` and `branch`) and the key selector. Find them with `rtk grep -n "Keyed.list\|Keyed.array\|Branch.select\|keyedArray (\|branch (=)" examples/Fable.Ranvier.Playground/tests examples/Fable.Ranvier.Playground/App.fs`. Example: `Keyed.list Tree.ops (=) fst (fun () -> rows.Value) render` becomes `Keyed.list Tree.ops fst (fun () -> rows.Value) render`. The three calls outside the `Keyed`/`Branch` modules:
   - `AuthoringChecks.fs` `portableAuthoring`: `keyedArray fst (fun () -> [| 1, "one"; 2, "two" |]) (fun current -> ...)` and `branch id (fun () -> "selected") (fun current -> text (current ()))`.
   - `App.fs` `foundationRowsView`: `View.keyedArray fst (fun () -> foundationRows.Value) (fun current -> ...)`.
2. `FrameworkDiagnostics.fs`: `Keyed = fun _ _ -> invalidOp "Token extraction requires an element"` and `Switch = fun _ _ -> invalidOp "Token extraction requires an element"`.
3. `RangeChecks.fs`:
   - Delete `arrayComparerMutation` and its case `"array comparers cannot change renderer snapshot slots"`. Ruling recorded for the spec: the test exercised the comparer only; `arrayKeyMutation` keeps the snapshot check.
   - In `keyedDisposal`, delete the comparer lambda `(fun a b -> ... a = b)`; the `id` key selector after it stays. Delete the `comparisonsAfterDisposal` counter and its assertion `equal 0 comparisonsAfterDisposal`. In `expected`, change `if point = "read" || point = "equals" then [ 1; 2 ]` to `if point = "read" then [ 1; 2 ]`. Delete the case `"keyed comparer disposal prevents further reconciliation", fun () -> keyedDisposal "equals"`.
   - In `keyedTracksOnlySnapshot`, replace the comparer lambda by nothing: `Keyed.list Tree.ops id (fun () -> ...) render`. The cleanup half stays.
   - Replace `keyedInitialDisposal` with a reader-only check and delete the case `"initial keyed comparer disposal stops before further callbacks"`:

```fsharp
let private keyedInitialDisposal () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let parent = Scope (ignore, ignore)
    let mutable rendered = 0

    let view =
        Keyed.list
            Tree.ops
            id
            (fun () ->
                parent.Dispose ()
                [ 1; 2; 3 ])
            (fun _ ->
                rendered <- rendered + 1
                Tree.ops.Empty)

    rejected (fun () ->
        mounted graph (Some parent) host None (fun ctx -> Tree.run view ctx.Backend)
        |> ignore)

    equal 0 rendered
    equal 0. host.childNodes.length
    equal 0 parent.ChildCount
```

   with the case line `"initial keyed reader disposal stops before row callbacks", keyedInitialDisposal`.
   - `duplicateKeys`: `with :? InvalidOperationException ->`. `keyedReportsAndRecovery`: `equal true (reports[0] :? InvalidOperationException)`.
4. `TemplateChecks.fs` `eachDuplicates`: both `System.ArgumentException` become `System.InvalidOperationException`.
5. `RepresentationChecks.fs` line 191 and `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx` line 433: `InvalidOperationException` (`System.InvalidOperationException` in the `.fsx`).
6. `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx`, the `Keyed`/`Switch` overrides and the arm-identity assertions. Replace the block from `let mutable regionReads` to `equal false (roleSame adminHome (firstKey readAway))` with:

```fsharp
let mutable regionReads: (unit -> (obj * obj) array) list = []

let capturing =
    { Tree.ops with
        Switch =
            fun read render ->
                regionReads <- regionReads @ [ read ]
                Tree.ops.Switch read render
    }

let renderRoled tab =
    Html.render Omit (Tree.run (Component.renderWith capturing roled tab))

equal "<div><span>user 4</span></div>" (renderRoled "home")
let readHome = regionReads[0]

let firstKey (read: unit -> (obj * obj) array) =
    fst (read ()).[0]

let homeKey () =
    firstKey readHome

let userHome = homeKey ()

// An equal key with the same definition keeps the arm.
equal true (userHome = homeKey ())

roleAdmin <- true
let adminHome = homeKey ()

// An equal key selecting another definition remounts the arm.
equal false (userHome = adminHome)
equal "<div><span>admin home</span></div>" (renderRoled "home")
equal "<div><!--[--><span>admin home</span><!--]--></div>" (render Omit (Component.render roled "home"))

equal "<div><span>admin away</span></div>" (renderRoled "away")
let readAway = regionReads[2]

// A key change under the same definition remounts the arm.
equal false (adminHome = firstKey readAway)
```

   and replace `recording` with:

```fsharp
let recording =
    { Tree.ops with
        Keyed =
            fun read render ->
                regionKinds <- regionKinds @ [ "keyed" ]
                Tree.ops.Keyed read render
        Switch =
            fun read render ->
                regionKinds <- regionKinds @ [ "switch" ]
                Tree.ops.Switch read render
    }
```

   The `=` assertions compare the first keys as the core compares them: a `branch` key is the boxed tuple of the user key and the arm definition, and the definition compares by reference.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `rtk npm run test:dom`
Expected: FAIL at compile with `This expression was expected to have type ... but here has type ...` at the first edited `Keyed.list` call.

- [ ] **Step 4: Remove the comparers from the representation**

`Representation.fs`:

```fsharp
    abstract Keyed: (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit

    abstract Switch: (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit

    default this.Switch(read, render) =
        this.Keyed (read, render)
```

Keep each member's existing doc comment, removing the sentences about the comparer. In `Operations`:

```fsharp
        Keyed: (unit -> (obj * obj) array) -> ((unit -> obj) -> 'view) -> 'view
        Switch: (unit -> (obj * obj) array) -> ((unit -> obj) -> 'view) -> 'view
```

Replace `module Keyed` and `module Branch` with:

```fsharp
module Keyed =
    let private create
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item array)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box read) then
            invalidArg "read" "Keyed item reader cannot be null"

        if isNull (box key) then
            invalidArg "key" "Key selector cannot be null"

        if isNull (box render) then
            invalidArg "render" "Keyed row renderer cannot be null"

        let readObjects () =
            read ()
            |> Array.map (fun item -> box (key item), box item)

        let renderObject (current: unit -> obj) =
            render (fun () -> unbox<'item>(current ()))

        ops.Keyed readObjects renderObject

    /// <summary>Retains keyed rows and supplies their current item values.</summary>
    /// <remarks>The input array's slots are copied before key callbacks run. Keys are compared with <c>=</c>. Duplicate
    /// keys fail before the rows change.</remarks>
    let array<'item, 'key, 'view when 'key: equality>
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item array)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box read) then
            invalidArg "read" "Keyed item reader cannot be null"

        create ops key (fun () -> Array.copy (read ())) render

    /// <summary>Adapts list snapshots to the array-backed keyed renderer.</summary>
    /// <remarks>Equal keys under <c>=</c> retain rows; their item readers return current values.</remarks>
    let list<'item, 'key, 'view when 'key: equality>
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item list)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box read) then
            invalidArg "read" "Keyed item reader cannot be null"

        create ops key (fun () -> List.toArray (read ())) render

module Branch =
    /// <summary>Selects a mounted instance by key and supplies a reader for its current selection.</summary>
    /// <remarks>Equal keys under <c>=</c> retain the instance; a changed key runs the previous instance's cleanups and
    /// removes its nodes before the next instance is set up. Include the branch discriminator in the key when component
    /// shape changes.</remarks>
    let select<'item, 'key, 'view when 'key: equality>
        (ops: Operations<'view>)
        (key: 'item -> 'key)
        (read: unit -> 'item)
        (render: (unit -> 'item) -> 'view)
        =
        if isNull (box key) then
            invalidArg "key" "Key selector cannot be null"

        if isNull (box read) then
            invalidArg "read" "Selection reader cannot be null"

        if isNull (box render) then
            invalidArg "render" "Instance renderer cannot be null"

        let readObjects () =
            let item = read ()
            [| box (key item), box item |]

        ops.Switch readObjects (fun current -> render (fun () -> unbox (current ())))
```

In `Tree`: the case `| Keyed of (unit -> (obj * obj) array) * ((unit -> obj) -> View)`, the run line `| Keyed (read, render) -> backend.Keyed (read, fun current target -> run (render current) target)`, and the ops fields `Keyed = fun read render -> Keyed (read, render)` and `Switch = fun read render -> Embed (fun backend -> backend.Switch (read, fun current target -> run (render current) target))`. In `Program` ops: `Keyed = fun read render -> View (fun backend -> backend.Keyed (read, fun current target -> (render current).Invoke target))` and the same shape for `Switch` with `backend.Switch`. In `Html`:

```fsharp
                member this.Keyed(read, render) =
                    let items = read ()
                    Projection.ensureUniqueKeys fst items

                    for _, item in items do
                        render (fun () -> item) this
```

Delete `Keyed.validateUniqueKeys`.

`Template.fs`: delete `Same` from `Region` and from the four region constructors (`view`, `matchWith`, `branch`, `each`); delete `anySame` and `definitionSame`; in `expandNode` use `ops.Switch (fun () -> region.Items.Invoke instance) (expandRow ops region.Row)` and `ops.Keyed (fun () -> region.Items.Invoke instance) (expandRow ops region.Row)`; in the server branch of the region writer replace `Keyed.validateUniqueKeys (...) items` with `Projection.ensureUniqueKeys fst items`. Change the `matchWith` and `branch` remarks' "compared by definition" sentences to "Arms are compared by key under <c>=</c>; a `matchWith` key is the arm's definition."

`TemplateDom.fs`: delete `Same` from `CompiledRegion` and from `compile`; `MountServices.Region` becomes `abstract Region: Node * Node * bool * bool * (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) -> unit`; `materialise` calls `services.Region (start, finish, region.RootLevel, region.DisposeFirst, read, region.Render)`.

`Mounting.fs`: `override this.Keyed(read, render) = this.AuthoredRegion (false, read, render)`, `override this.Switch(read, render) = this.AuthoredRegion (true, read, render)`; drop `equals` from `AuthoredRegion`, `KeyedRegion` and `MountServices.Region`. Inside `KeyedRegion`, delete `liveEquals`; replace `Keyed.validateUniqueKeys liveEquals items` with `Projection.ensureUniqueKeys fst items`, and `liveEquals entry.Key key` with `entry.Key = key`.

`Prepared.fs` and `Browser.fs`: `member _.Keyed(_, _) =`. `Authoring.fs`: `let inline keyedArray key read render = Keyed.array Program.ops key read render` and `let inline branch key read render = Branch.select Program.ops key read render`, with the remarks' "equal keys" sentences ending "under <c>=</c>". `BindingBaselineMounting.fs`, `member _.Keyed`:
- The member becomes `member _.Keyed(read, render) =`.
- Delete `setupEquals` and `liveEquals`.
- Replace `Keyed.validateUniqueKeys setupEquals initialItems` with `Projection.ensureUniqueKeys fst initialItems`, and `Keyed.validateUniqueKeys liveEquals items` with `Projection.ensureUniqueKeys fst items`.
- Replace `liveEquals entry.Key key` with `entry.Key = key`.

The `ensureSetup ()` and `ensureRegionLive ()` calls around them stay. `Bind(equals, read)` keeps its own `equals`.

`App.fs`: the `View.keyedArray` call loses its `(=)` (Step 2).

- [ ] **Step 5: Run the tests to verify they pass**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: PASS, 311 tests (314 less the three deleted cases).

Run: `rtk npm run test:browser`
Expected: PASS, 22 tests.

Run: `rtk proxy dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj --configuration Release` from the repository root.
Expected: every group prints PASS.

Run the emitted-JavaScript grep from Step 1.
Expected: each count at most its baseline.

- [ ] **Step 6: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Representation.fs experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/Prepared.fs experiments/Ranvier.View.Foundation/Browser.fs experiments/Ranvier.View.Foundation/Authoring.fs examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs examples/Fable.Ranvier.Playground/App.fs examples/Fable.Ranvier.Playground/tests/*.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs examples/Fable.Ranvier.Playground/App.fs examples/Fable.Ranvier.Playground/tests
rtk git commit -m "refactor(view): compare keys with the core's equality and drop the view's key comparers"
```

---

### Task 6: Mount lifetimes on core owners

**Files:**
- Rewrite: `experiments/Ranvier.View.Foundation/Lifetime.fs`
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs`, `Browser.fs`; `examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs` (a private copy of the old `Scope`)
- Delete: `experiments/Ranvier.View.Foundation/LifetimeChecks.fsx` (superseded by `examples/Fable.Ranvier.Playground/tests/LifetimeChecks.fs`)
- Test: `examples/Fable.Ranvier.Playground/tests/LifetimeChecks.fs`, `RangeChecks.fs`, `BindingChecks.fs`, `FrameworkDiagnostics.fs`, and any other file `rtk grep -ln "Scope (" examples/Fable.Ranvier.Playground/tests` lists

**Interfaces:**
- Consumes: `Owner.ErrorCount`, `Owner.ErrorsFrom` (Task 1).
- Produces (all in namespace `Ranvier.View.Foundation`):
  - `type ScopePhase = SettingUp | Attached | Disposed` (unchanged).
  - `type MountLifetime` with `static member internal Create(graph: Graph, owner: Owner, report: exn list -> unit, release: unit -> unit, parent: MountLifetime) : MountLifetime` (call first in the owner's root body), `static member internal Root(graph, report, release) : MountLifetime`, `static member internal Capture(graph: Graph, body: unit -> unit) : exn list * exn option`; members `Owner`, `Parent`, `IsDisposed`, `Phase`, `Failures: exn list`, `OnCleanup(callback: unit -> unit)`, `Child(release: unit -> unit)`, `Child(release: unit -> unit, body: MountLifetime -> unit)`, `MarkAttached()`, `Adopt(adopted: Owner)` (call first in the adopted owner's root body), `Dispose()`, internal `Abandon()`, internal `Route(errors: exn[])`.
  - `Mounting.mount graph (parentOwner: Owner option) report host before build` and `Mounting.mountWith mode graph (parentOwner: Owner option) report host before build`.
  - Internal `MountState(scheduling, graph, parent: MountLifetime, parentOwner: Owner, report, host)`; `MountState.Root: MountLifetime`.

Rulings recorded for the spec:
- `Lifetime.fs` is rewritten rather than removed. `MountLifetime` holds no child list and no cleanup list. It holds a core owner, a parent pointer, an attached flag, a release queue and held errors, which are the release-order and reporting state the spec lists as view-owned.
- Mount-level cleanups now run in core owner order, last registered first. Scope callbacks ran in registration order.
- A teardown driven by the core rather than by `Dispose` (for example `Graph.Dispose`) reports the mount's owner errors, adopted owners' errors and release failures. Errors that computation scopes record into `Graph.Root` are reported only by `Dispose` and list-pass windows.

- [ ] **Step 1: Rewrite the lifetime tests first**

Replace `LifetimeChecks.fs` from `type private Fixture` to the end of the file with:

```fsharp
type private Fixture =
    {
        Parent: HTMLElement
        Child: HTMLElement
        Scope: MountLifetime
        ChildScope: MountLifetime
        Owner: Owner
        Set: int -> unit
        Writes: unit -> int
        Calls: unit -> int
        Reports: ResizeArray<exn list>
        Dispose: unit -> unit
    }

let private expected label =
    [
        label + ":child-clean-attached"
        label + ":parent-clean-attached"
        label + ":remove-child"
        label + ":remove-parent"
    ]

let private createFixture (graph: Graph) (host: HTMLElement) (trace: ResizeArray<string>) label config =
    let parent = Dom.element "div" [] []
    let child = Dom.element "button" [] []
    parent.appendChild (child) |> ignore
    host.appendChild (parent) |> ignore
    let reports = ResizeArray<exn list>()
    let mutable calls = 0
    let mutable writes = 0
    let mutable scope: MountLifetime = null
    let mutable childScope: MountLifetime = null
    let mutable ownerForFailure: Owner option = None
    let signal = graph.Run (fun () -> Api.createSignal 0)

    let report errors =
        reports.Add errors

        if config.ReporterFails then
            failwith "reporter"

    let owner =
        try
            graph.Run (fun () ->
                Api.createRoot (fun owner ->
                    ownerForFailure <- Some owner

                    scope <-
                        MountLifetime.Root (
                            graph,
                            report,
                            fun () ->
                                trace.Add (label + ":remove-parent")
                                remove parent
                        )

                    let unsubscribe = listen child (fun () -> calls <- calls + 1)

                    // Runs after the child lifetime's teardown.
                    scope.OnCleanup (fun () ->
                        same host parent.parentNode.Value
                        same parent child.parentNode.Value
                        trace.Add (label + ":parent-clean-attached"))

                    childScope <-
                        scope.Child (
                            (fun () ->
                                trace.Add (label + ":remove-child")
                                remove child

                                if config.ReleaseFails then
                                    failwith "release"),
                            fun nested ->
                                nested.OnCleanup (fun () ->
                                    same parent child.parentNode.Value
                                    same host parent.parentNode.Value
                                    trace.Add (label + ":child-clean-attached")
                                    unsubscribe ()

                                    if config.Reenter then
                                        scope.Dispose ()

                                    if config.CleanupFails then
                                        failwith "cleanup")

                                Api.createEffect (fun () ->
                                    let value = signal.Value
                                    writes <- writes + 1
                                    child.textContent <- string value)
                        )

                    config.Acquired child (fun () -> calls) (fun value -> signal.Value <- value) (fun () -> writes)

                    match config.SetupFailure with
                    | Some error -> raise error
                    | None -> owner))
        with _ ->
            if not (isNull scope) then
                scope.Dispose ()

            ownerForFailure |> Option.iter _.Dispose()
            reraise ()

    {
        Parent = parent
        Child = child
        Scope = scope
        ChildScope = childScope
        Owner = owner
        Set = fun value -> graph.Run (fun () -> signal.Value <- value)
        Writes = fun () -> writes
        Calls = fun () -> calls
        Reports = reports
        Dispose =
            fun () ->
                scope.Dispose ()
                owner.Dispose ()
    }
```

Keep `withHost`, `disposalPath`, `sharedGraph`, `effectRerun`, `setupFailure` and `teardownFailures` unchanged. Replace `reentrantAndRejected`, `unlinkChildren`, `ancestorReentry`, `attachmentPhases` and `crossTreeDisposal` with:

```fsharp
let private reentrantAndRejected () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let fixture = createFixture graph host trace "root" { normal with Reenter = true }
        let mutable rejected = 0

        fixture.Scope.OnCleanup (fun () ->
            try
                fixture.Scope.Child (ignore) |> ignore
            with :? InvalidOperationException ->
                rejected <- rejected + 1

            try
                fixture.Scope.OnCleanup (ignore)
            with :? InvalidOperationException ->
                rejected <- rejected + 1)

        fixture.Owner.Dispose ()
        equal (expected "root") (List.ofSeq trace)
        equal 2 rejected
        equal true fixture.Scope.IsDisposed
        fixture.Dispose ()
        equal (expected "root") (List.ofSeq trace))

let private childrenReleaseOnce () =
    use graph = new Graph ()
    let mutable released = 0
    let parent = MountLifetime.Root (graph, ignore, ignore)

    graph.Run (fun () ->
        for _ in 1..1000 do
            parent.Child(fun () -> released <- released + 1).Dispose())

    equal 1000 released
    equal false parent.IsDisposed
    parent.Dispose ()
    equal 1000 released

let private ancestorReentry () =
    withHost (fun host _ ->
        use graph = new Graph ()
        let trace = ResizeArray<string>()
        let fixture = createFixture graph host trace "root" normal
        fixture.ChildScope.OnCleanup (fixture.Scope.Dispose)
        fixture.ChildScope.OnCleanup (fun () -> same host fixture.Parent.parentNode.Value)
        fixture.ChildScope.Dispose ()

        // Every cleanup of the tree runs before its first release.
        equal
            [
                "root:parent-clean-attached"
                "root:child-clean-attached"
                "root:remove-child"
                "root:remove-parent"
            ]
            (List.ofSeq trace)

        equal [] fixture.ChildScope.Failures
        equal true fixture.Scope.IsDisposed
        fixture.Dispose ()
        equal 1.0 host.childNodes.length)

let private attachmentPhases () =
    use graph = new Graph ()
    let root = MountLifetime.Root (graph, ignore, ignore)
    let child = graph.Run (fun () -> root.Child ignore)
    equal SettingUp root.Phase
    root.MarkAttached ()
    equal Attached child.Phase
    let later = graph.Run (fun () -> root.Child ignore)
    equal SettingUp later.Phase
    later.MarkAttached ()
    root.Dispose ()
    equal Disposed child.Phase
    let mutable rejected = false

    try
        child.MarkAttached ()
    with :? InvalidOperationException ->
        rejected <- true

    equal true rejected

let private crossTreeDisposal () =
    use graph = new Graph ()
    let trace = ResizeArray<string>()
    let aReports, bReports = ResizeArray<exn list>(), ResizeArray<exn list>()
    let a = MountLifetime.Root (graph, aReports.Add, (fun () -> trace.Add "a:release"))
    // Runs after the teardown of a's child.
    a.OnCleanup (fun () -> trace.Add "a:clean")
    let b = MountLifetime.Root (graph, bReports.Add, (fun () -> trace.Add "b:release"))

    let aChild, bChild =
        graph.Run (fun () -> a.Child (fun () -> trace.Add "a-child:release"), b.Child (fun () -> trace.Add "b-child:release"))

    bChild.OnCleanup (fun () ->
        trace.Add "b-child:clean"
        failwith "b")

    aChild.OnCleanup (fun () ->
        trace.Add "a-child:clean"
        bChild.Dispose ())

    a.Dispose ()

    equal
        [
            "a-child:clean"
            "b-child:clean"
            "b-child:release"
            "a:clean"
            "a-child:release"
            "a:release"
        ]
        (List.ofSeq trace)

    equal 0 aReports.Count

    equal
        [ "b" ]
        (bReports
         |> Seq.concat
         |> Seq.map _.Message
         |> List.ofSeq)

    equal [] a.Failures
    equal false b.IsDisposed
    b.Dispose ()
```

In `cases`, replace `"disposed children unlink in Fable", unlinkChildren` with `"disposed children release once and leave the parent live", childrenReleaseOnce`.

`RangeChecks.fs`: each `let parent = Scope (ignore, ignore)` becomes `let parent = graph.Run (fun () -> Api.createRoot id)`. Delete each `parent.MarkAttached ()`. Replace each `equal 0 parent.ChildCount` with `equal true parent.IsDisposed` when `parent.Dispose ()` precedes it, and delete it otherwise. In `childReporter`, `let parent = graph.Run (fun () -> Api.createRoot id)`, and delete `parentReports` and its assertion.

`BindingChecks.fs`: each `let scope = Scope (ignore, ignore)` becomes `let scope = graph.Run (fun () -> Api.createRoot id)`, `scope.OnCleanup owner.Dispose` becomes `scope.OnCleanup (Action owner.Dispose)`, and each `Binding.startQueued scope.OnCleanup ...` becomes `Binding.startQueued (fun cleanup -> scope.OnCleanup (Action cleanup)) ...`.

`FrameworkDiagnostics.fs` `scopes`:

```fsharp
let scopes width =
    use graph = new Graph ()
    let start = globalThis.performance.now ()
    let root = MountLifetime.Root (graph, ignore, ignore)

    graph.Run (fun () ->
        for _ in 1..width do
            let child = root.Child ignore
            child.OnCleanup ignore)

    let built = globalThis.performance.now ()
    root.Dispose ()
    let disposed = globalThis.performance.now ()
    [| "scope-setup", built - start; "scope-dispose", disposed - built |]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `rtk npm run test:dom`
Expected: FAIL at compile with `The type 'MountLifetime' is not defined`.

- [ ] **Step 3: Write `Lifetime.fs`**

Replace the file's content with:

```fsharp
namespace Ranvier.View.Foundation

open System
open Ranvier

type ScopePhase =
    | SettingUp
    | Attached
    | Disposed

/// <summary>
/// The errors of one view-initiated teardown or list pass: the errors the graph root records while it runs, less those
/// of nested windows, followed by the errors routed to it.
/// </summary>
[<Sealed; AllowNullLiteral>]
type internal TeardownWindow(root: Owner, start: int) =
    let routed = ResizeArray<exn>()
    // Start and end positions in the root's errors of nested windows, in pairs.
    let nested = ResizeArray<int>()

    member _.Root = root
    member _.Start = start

    member _.Add(errors: exn[]) = routed.AddRange errors

    member _.Exclude(first: int, finish: int) =
        nested.Add first
        nested.Add finish

    member _.Errors() : exn list =
        let recorded = root.ErrorsFrom start
        let result = ResizeArray<exn>()

        for offset in 0 .. recorded.Length - 1 do
            let index = start + offset
            let mutable inner = false
            let mutable pair = 0

            while not inner && pair < nested.Count do
                if index >= nested[pair] && index < nested[pair + 1] then
                    inner <- true

                pair <- pair + 2

            if not inner then
                result.Add recorded[offset]

        result.AddRange routed
        List.ofSeq result

/// <summary>
/// The lifetime of a mounted view: a core owner, the release of the view's DOM nodes and the reporter of its teardown
/// errors.
/// </summary>
/// <remarks>
/// <para>
/// The release runs after every cleanup of the owner. While an enclosing lifetime tears down, a release waits in the
/// outermost one's queue and runs before that lifetime's own release, so every cleanup of a disposed tree runs while its
/// nodes are attached. A release that runs while a descendant's <c>Dispose</c> is in progress waits for that call to
/// finish.
/// </para>
/// <para>
/// A teardown's errors reach the reporter once: the errors of the owner and of adopted owners, release failures and,
/// for a teardown started by <c>Dispose</c>, the errors the graph root records meanwhile.
/// </para>
/// </remarks>
[<Sealed; AllowNullLiteral>]
type MountLifetime private (graph: Graph, owner: Owner, report: exn list -> unit, release: unit -> unit, parent: MountLifetime) =
    let inherits = not (isNull parent) && parent.Phase = SettingUp
    // The teardown reports the owner's errors recorded at or after this position.
    let ownerStart = owner.ErrorCount
    let mutable attached = false
    let mutable released = false
    let mutable failures: exn list = []
    let mutable queued: ResizeArray<unit -> unit> = null
    let mutable held: ResizeArray<exn> = null
    let mutable deferred: ResizeArray<unit -> unit> = null

    static member val private Windows = ResizeArray<TeardownWindow>()
    static member val private Frames = ResizeArray<MountLifetime>()

    static member private CurrentWindow: TeardownWindow =
        let windows = MountLifetime.Windows

        if windows.Count = 0 then
            null
        else
            windows[windows.Count - 1]

    /// <summary>
    /// Wraps <c>owner</c> and registers the release as its first cleanup. Call before anything else registers with
    /// <c>owner</c>.
    /// </summary>
    static member internal Create(graph: Graph, owner: Owner, report: exn list -> unit, release: unit -> unit, parent: MountLifetime) =
        let lifetime = MountLifetime (graph, owner, report, release, parent)
        owner.OnCleanup (Action (fun () -> lifetime.Finish ()))
        lifetime

    /// <summary>A lifetime whose owner is a root created under the current owner of <c>graph</c>.</summary>
    static member internal Root(graph: Graph, report: exn list -> unit, release: unit -> unit) =
        graph.Run (fun () -> Api.createRoot (fun owner -> MountLifetime.Create (graph, owner, report, release, null)))

    /// <summary>Runs <c>body</c> in a new teardown window and returns the window's errors with the exception <c>body</c> raised.</summary>
    static member internal Capture(graph: Graph, body: unit -> unit) : exn list * exn option =
        let root = graph.Root
        let windows = MountLifetime.Windows
        let window = TeardownWindow (root, root.ErrorCount)
        windows.Add window
        let mutable failure = None

        try
            try
                body ()
            with error ->
                failure <- Some error
        finally
            windows.RemoveAt (windows.Count - 1)

        let outer = MountLifetime.CurrentWindow

        if not (isNull outer) && obj.ReferenceEquals (outer.Root, root) then
            outer.Exclude (window.Start, root.ErrorCount)

        window.Errors (), failure

    static member private DescendantFrame(lifetime: MountLifetime) : MountLifetime =
        let frames = MountLifetime.Frames
        let mutable result: MountLifetime = null
        let mutable index = frames.Count - 1

        while isNull result && index >= 0 do
            let frame = frames[index]

            if not frame.Released && frame.DescendsFrom lifetime then
                result <- frame

            index <- index - 1

        result

    member _.Owner = owner
    member _.Parent = parent
    member _.IsDisposed = owner.IsDisposed
    member _.Failures = failures
    member internal _.Released = released

    member _.Phase =
        if owner.IsDisposed then Disposed
        elif attached || (inherits && parent.Phase = Attached) then Attached
        else SettingUp

    member private _.EnsureLive() =
        if owner.IsDisposed then
            raise (InvalidOperationException "Renderer scope is disposed")

    member this.OnCleanup(callback: unit -> unit) =
        this.EnsureLive ()
        owner.OnCleanup (Action callback)

    member this.Child(childRelease: unit -> unit) : MountLifetime = this.Child (childRelease, ignore)

    /// <summary>A lifetime nested in this one, its owner a root under this lifetime's owner, with <c>body</c> run inside that root.</summary>
    member this.Child(childRelease: unit -> unit, body: MountLifetime -> unit) : MountLifetime =
        this.EnsureLive ()
        let mutable child: MountLifetime = null

        graph.Run (fun () ->
            Api.runWithOwner owner (fun () ->
                Api.createRoot (fun childOwner ->
                    child <- MountLifetime.Create (graph, childOwner, report, childRelease, this)
                    body child)))

        child

    member this.MarkAttached() =
        this.EnsureLive ()

        if attached then
            raise (InvalidOperationException "Renderer scope is already attached")

        attached <- true

    /// <summary>
    /// Routes the errors recorded by <c>adopted</c> after this call into this lifetime's teardown report.
    /// <c>adopted</c> is a root owner nested in this lifetime. Call before anything else registers with it.
    /// </summary>
    member this.Adopt(adopted: Owner) =
        let start = adopted.ErrorCount
        adopted.OnCleanup (Action (fun () -> this.Route (adopted.ErrorsFrom start)))

    member private this.DescendsFrom(ancestor: MountLifetime) =
        let mutable cursor = parent
        let mutable found = false

        while not found && not (isNull cursor) do
            if obj.ReferenceEquals (cursor, ancestor) then
                found <- true

            cursor <- cursor.Parent

        found

    /// <summary>The highest lifetime, this one included, that is tearing down and has not released its nodes.</summary>
    member internal this.Outermost() : MountLifetime =
        let mutable result = this
        let mutable cursor = parent

        while not (isNull cursor) do
            if cursor.IsDisposed && not cursor.Released then
                result <- cursor

            cursor <- cursor.Parent

        result

    member private _.Hold(errors: exn[]) =
        if isNull held then
            held <- ResizeArray ()

        held.AddRange errors

    member private _.Enqueue(action: unit -> unit) =
        if isNull queued then
            queued <- ResizeArray ()

        queued.Add action

    member private _.Defer(action: unit -> unit) =
        if isNull deferred then
            deferred <- ResizeArray ()

        deferred.Add action

    member private _.RunDeferred() =
        if not (isNull deferred) then
            let pending = deferred
            deferred <- null

            for index in 0 .. pending.Count - 1 do
                pending[index] ()

    /// <summary>
    /// Adds <c>errors</c> to the open teardown window, or to the teardown of the outermost tearing-down lifetime, or
    /// reports them.
    /// </summary>
    member internal this.Route(errors: exn[]) =
        if errors.Length > 0 then
            let window = MountLifetime.CurrentWindow

            if not (isNull window) then
                window.Add errors
            else
                let target = this.Outermost ()

                if target.IsDisposed then
                    target.Hold errors
                else
                    this.Report (List.ofArray errors)

    member private this.Report(errors: exn list) =
        if not errors.IsEmpty then
            failures <- errors

            try
                graph.Run (fun () -> Api.untrack (fun () -> report errors))
            with error ->
                failures <- failures @ [ error ]

    member private this.RunRelease() =
        try
            release ()
        with error ->
            this.Route [| error |]

        released <- true

    member private this.Complete() =
        if not (isNull queued) then
            let pending = queued
            queued <- null

            for index in 0 .. pending.Count - 1 do
                pending[index] ()

        this.RunRelease ()

        if isNull MountLifetime.CurrentWindow && not (isNull held) then
            let errors = List.ofSeq held
            held <- null
            this.Report errors

    member private this.Finish() =
        let outer = this.Outermost ()
        this.Route (owner.ErrorsFrom ownerStart)

        if obj.ReferenceEquals (outer, this) then
            let frame = MountLifetime.DescendantFrame this

            if isNull frame then
                this.Complete ()
            else
                frame.Defer (fun () -> this.Complete ())
        else
            outer.Enqueue (fun () -> this.RunRelease ())

    member private this.TearDown() =
        let frames = MountLifetime.Frames
        frames.Add this

        try
            owner.Dispose ()
        finally
            frames.RemoveAt (frames.Count - 1)

        this.RunDeferred ()

    /// <summary>Tears the lifetime down and reports the teardown's errors once.</summary>
    member this.Dispose() =
        if not owner.IsDisposed then
            let errors, failure = MountLifetime.Capture (graph, (fun () -> this.TearDown ()))
            this.Report errors
            failure |> Option.iter raise

    /// <summary>Tears the lifetime down. Inside another teardown window the errors join that window.</summary>
    member internal this.Abandon() =
        if isNull MountLifetime.CurrentWindow then
            this.Dispose ()
        elif not owner.IsDisposed then
            this.TearDown ()
```

- [ ] **Step 4: Put mounts on lifetimes**

`Mounting.fs`, `MountState`:

1. Constructor: `and internal MountState(scheduling: Scheduling, graph: Graph, parent: MountLifetime, parentOwner: Owner, report: exn list -> unit, host: Node) =`. Rename the existing `let mutable parent: Node` (the element receiving nodes) to `let mutable target: Node` and update its uses; `parent` now names the parent lifetime.
2. Delete `cleanupErrors` and `reporting`. `release` becomes `let release () = range.Release ()`. `ownedCleanup` becomes:

```fsharp
    let ownedCleanup (owner: Owner) (callback: unit -> unit) =
        if owner.IsDisposed then
            invalid "Component cleanup registration requires a live component"

        owner.OnCleanup (Action callback)
```

3. Replace the `let root = match parentScope with ...` binding with `let mutable root: MountLifetime = null`, and `member _.Root: Scope = root` with `member _.Root: MountLifetime = root`.
4. Replace `Run` with:

```fsharp
    /// <summary>Builds the view, attaches it before <c>before</c> in the host, and returns its handle.</summary>
    /// <remarks>A build or attachment failure disposes the mount and rethrows.</remarks>
    member this.Run(before: Node option, build: BuildContext -> unit) =
        let context = BuildContext this

        try
            graph.Run (fun () ->
                let create () =
                    Api.createRoot (fun owner ->
                        root <- MountLifetime.Create (graph, owner, report, release, parent)
                        Api.untrack (fun () -> build context))

                if isNull (box parentOwner) then
                    create ()
                else
                    Api.runWithOwner parentOwner create)

            ensureSetup ()
            range.Attach (host, before)
            root.MarkAttached ()
            delegateAt host

            if not (isNull delegatedNames) then
                root.OnCleanup releaseDelegation

            Mounted this
        with _ ->
            if not (isNull root) then
                root.Abandon ()

            reraise ()
```

5. `Component`, non-naive branch:

```fsharp
        Api.createRoot (fun owner ->
            root.Adopt owner

            Api.untrack (fun () ->
                setup
                    {
                        OnCleanup = fun callback -> ownedCleanup owner callback
                    })

            if owner.IsDisposed then
                invalid "View construction is only valid during live setup")
```

6. `KeyedRegion`: delete `regionScope`. `ensureRegionLive` raises `KeyedDisposed` when `root.IsDisposed || regionOwner |> Option.exists _.IsDisposed`. In `createEntry`, the row is `MountState(scheduling, graph, root, Unchecked.defaultof<_>, reportRow, staging).Run(...)`, still inside `Api.runWithOwner owner`. The region's root body is:

```fsharp
        Api.createRoot (fun owner ->
            regionOwner <- Some owner
            Api.untrack (fun () -> Api.createEffect reconcile))
```

7. `mountWith`:

```fsharp
let mountWith mode graph (parentOwner: Owner option) report host before build =
    let parentOwner =
        match parentOwner with
        | Some owner -> owner
        | None -> Unchecked.defaultof<_>

    match mode with
    | SynchronousCommits -> MountState(Synchronous, graph, null, parentOwner, report, host).Run (before, build)
    | QueuedCommits (failure, post) ->
        let queue = Queue (graph, failure, post)

        try
            let state = MountState (Queued queue, graph, null, parentOwner, report, host)
            let mounted = state.Run (before, build)
            state.Root.OnCleanup queue.Dispose
            mounted
        with _ ->
            queue.Dispose ()
            reraise ()
```

`Browser.fs` `mount`: replace the `Scope` lines with lifetimes:

```fsharp
    let roots = ResizeArray<Node>()

    let release () =
        for node in roots do
            remove (node :?> ChildNode)

        roots.Clear ()

    let mutable parent: Node = host
    let mutable root: MountLifetime = null
    let mutable scope: MountLifetime = null
```

In the backend, `Browser` and `Listen` keep `scope.OnCleanup (...)`. `Component` becomes:

```fsharp
            member _.Component setup =
                let previous = scope

                try
                    scope.Child (
                        ignore,
                        fun child ->
                            scope <- child

                            count (fun c ->
                                c.Owners <- c.Owners + 1
                                c.Setups <- c.Setups + 1)

                            Api.untrack (fun () -> setup { OnCleanup = child.OnCleanup })
                    )
                    |> ignore
                finally
                    scope <- previous
```

and the tail of `mount`:

```fsharp
    try
        graph.Run (fun () ->
            Api.createRoot (fun owner ->
                root <- MountLifetime.Create (graph, owner, report, release, null)
                scope <- root
                count (fun c -> c.Owners <- c.Owners + 1)
                Api.untrack (fun () -> build backend)))
    with _ ->
        if not (isNull root) then
            root.Abandon ()

        reraise ()

    fun () -> root.Dispose ()
```

`examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs` is a frozen benchmark baseline and keeps the old renderer scope. Directly after its `open` lines, add this copy of the old `Scope` (the members the file uses; `ChildCount` is dropped). It is `internal`, not `private`: `mount` and the constructor of `Mounted` name it, and a private type there is error FS0410. Change `let rec mount` to `let rec internal mount`; `BindingBench.fs`, its only caller, is in the same project. Nothing else in the file changes.

```fsharp
/// <summary>The renderer scope tree of the baseline, kept with the baseline it measures.</summary>
type internal Scope private (report: exn list -> unit, release: unit -> unit, parent: Scope option) =
    let children = ResizeArray<Scope>()
    let callbacks = ResizeArray<unit -> unit>()
    let mutable disposed = false
    let mutable attached = false
    let mutable failures: exn list = []
    // The active disposal order of this scope's tree. Set only on the topmost scope; null outside a disposal.
    let mutable draining: ResizeArray<Scope> = null

    new(report, release) = Scope (report, release, None)

    member private this.Top: Scope =
        match parent with
        | Some owner -> owner.Top
        | None -> this

    member private _.Draining
        with get () = draining
        and set value = draining <- value

    member private _.RemoveChild(child) =
        children.Remove child |> ignore

    member private _.EnsureLive() =
        if disposed then
            raise (System.InvalidOperationException "Renderer scope is disposed")

    member this.Child(childReport: exn list -> unit, childRelease: unit -> unit) =
        this.EnsureLive ()
        let child = Scope (childReport, childRelease, Some this)
        children.Add child
        child

    member this.Child(childRelease: unit -> unit) =
        this.Child (report, childRelease)

    member this.OnCleanup(callback: unit -> unit) =
        this.EnsureLive ()
        callbacks.Add callback

    member private this.Collect(order: ResizeArray<Scope>) =
        if not disposed then
            disposed <- true

            for index in 0 .. children.Count - 1 do
                children[index].Collect order

            order.Add this

    member private _.Clean(errors: ResizeArray<exn>) =
        for index in 0 .. callbacks.Count - 1 do
            try
                callbacks[index]()
            with error ->
                errors.Add error

        callbacks.Clear ()

    member private this.Release(errors: ResizeArray<exn>) =
        try
            release ()
        with error ->
            errors.Add error

        children.Clear ()

        match parent with
        | Some owner -> owner.RemoveChild this
        | None -> ()

    member this.Dispose() =
        if not disposed then
            let top = this.Top

            match top.Draining with
            | null ->
                let order = ResizeArray<Scope>()
                let errors = ResizeArray<exn>()
                top.Draining <- order

                try
                    this.Collect order
                    let mutable cleaned = 0
                    let mutable released = 0

                    while released < order.Count do
                        while cleaned < order.Count do
                            order[cleaned].Clean errors
                            cleaned <- cleaned + 1

                        order[released].Release errors
                        released <- released + 1

                    if errors.Count > 0 then
                        failures <- List.ofSeq errors
                finally
                    top.Draining <- null

                if errors.Count > 0 then
                    try
                        report failures
                    with error ->
                        failures <- failures @ [ error ]
            | order -> this.Collect order

    member this.MarkAttached() =
        this.EnsureLive ()

        if attached then
            raise (System.InvalidOperationException "Renderer scope is already attached")

        attached <- true

        for index in 0 .. children.Count - 1 do
            let child = children[index]

            if not child.IsDisposed && child.Phase = SettingUp then
                child.MarkAttached ()

    member _.Phase =
        if disposed then Disposed
        elif attached then Attached
        else SettingUp

    member _.IsDisposed = disposed
    member _.Failures = failures
```

Delete the superseded script:

```bash
rtk git rm experiments/Ranvier.View.Foundation/LifetimeChecks.fsx
```

- [ ] **Step 5: Run the tests to verify they pass**

Run (from `examples/Fable.Ranvier.Playground`): `rtk npm run test:dom`
Expected: 311 pass, with no expected order changed outside `LifetimeChecks.fs`. Use-order flips: none. Only `ctx.OnCleanup`/`root.OnCleanup` on the mount and `Browser.fs` component cleanups change from registration order to core owner order, and no view test asserts the order of two or more of them. The view tests that assert the order of several cleanups already observe core owner order and keep their expected values:

| Test (file) | Expected order, before and after this task |
|---|---|
| "range cleanup runs child first while DOM stays attached" (`RangeChecks.fs`) | `[ "child"; "parent" ]` |
| "template cleanups run in use order without an instance owner" (`TemplateChecks.fs`) | `[ "cleanup 2"; "cleanup 1" ]` |
| "each renders template rows, retains them by key and dispatches their clicks" (`TemplateChecks.fs`) | `[ "setup 1"; "setup 2"; "click 1"; "cleanup 2"; "cleanup 1" ]` |
| "an inlined child shares the clone and keeps its own instance, effect and handlers" (`TemplateChecks.fs`) | `[ "badge 2"; "card"; "cleanup badge"; "cleanup card" ]` |
| "128 components clean up without stopping a sibling mount", "512 components …" (`MountingInvestigationChecks.fs`) | `[| width .. -1 .. 1 |]` |

Every other cleanup assertion in the view tests counts cleanups or sorts them first. A failing order assertion is a defect in this task, not an expected flip.

Run: `rtk npm run test:browser`. Expected: 22 pass.
Run the emitted-JavaScript grep from Task 5 Step 1. Expected: each count at most its baseline.

- [ ] **Step 6: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Lifetime.fs experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/Browser.fs examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs examples/Fable.Ranvier.Playground/tests/*.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs examples/Fable.Ranvier.Playground/tests
rtk git commit -m "refactor(view): build mount lifetimes on core owners in place of the renderer scope tree"
```

---

### Task 7: List regions on the core projection

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs`, `Mounting.fs`
- Test: `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs`, `RangeChecks.fs`, `range.html`, `range.spec.mjs`

**Interfaces:**
- Consumes: `Api.createProjectionOnce`, `ProjectionOptions.CreateFirst` (Tasks 2–3); `MountLifetime.Capture`, `MountState(scheduling, graph, parent, parentOwner, report, host)` (Task 6); comparer-free signatures (Task 5).
- Produces:
  - Public `TemplateDom.RegionKind = ListRows | SelectedArm | ViewHole` (public because the public record `CompiledRegion` holds it).
  - `CompiledRegion = { Marker; Owner; RootLevel; Kind: RegionKind; Items; Render; Row: RegionRow }`.
  - `MountServices.Region: Node * Node * bool * RegionKind * (unit -> (obj * obj) array) * ((unit -> obj) -> Backend -> unit) * Func<(unit -> obj), MountServices, unit> -> unit`, where the materialiser is null for a view hole.
  - In `Mounting.fs`:
    - Private `RegionErrors(report: exn list -> unit)`, with `Initializing`, `SetupFailure`, `Report(errors)` and `Fail(error, errors)`.
    - `Row(first, last)`, with members `First`, `Last`, `Index`, `Removed` and `Mount`, and statics `MoveBefore`, `Remove`, `Validate`, `Relocate` and `Layout`.
    - `RowBuilder(state: MountState)`, with `Staging` and `Build(materialise, current): Row`.
    - The `MountState` members `ListRegion(...)`, `MountRow(current, render): Row`, `RegionOf(...)`, `PlaceRegion(...)` and `RegionParent(...)`.
    - The internal `MountState` members `OwnedCleanup`, `DelegateLate`, `ListenDirect`, `ReportReaderFailure`, `ClearReported` and `NestedRegion`.

Ruling recorded for the spec: the top-level range deletion is replaced. Rows of a top-level region (`NewRegion`, `RootRegion`) remove their own nodes when the region tears down. Rows of a nested region skip their removal while the region tears down, because their nodes leave with the element or row that holds the markers. The range deletion would have deleted foreign nodes inserted inside rows, which "foreign mutations inside keyed rows reject outer movement" keeps.

Ruling recorded for the spec: the minimal-move step matches the common prefix and suffix, then moves only the rows outside one longest increasing run of their previous positions (as `Positional` does in the core, over row objects). When every row between the prefix and suffix is new and the rows are the region's staging content, one insertion places them all.

- [ ] **Step 1: Write the failing tests**

In `TemplateChecks.fs`, after `eachDuplicates`, add:

```fsharp
[<Emit("(() => { let count = 0; const old = $0.insertBefore; $0.insertBefore = function(n, b) { count++; return old.call(this, n, b); }; return () => { $0.insertBefore = old; return count; }; })()")>]
let private countInserts (node: Node) : unit -> int = jsNative

[<Emit("Array.from($0.childNodes).filter(n => n.nodeType === 8 && n.data === 'ranvier:row')")>]
let private rowMarkers (node: Node) : obj array = jsNative

[<Emit("document.activeElement === $0")>]
let private isFocused (node: obj) : bool = jsNative

[<Emit("$0.selectionStart + ',' + $0.selectionEnd")>]
let private selectionOf (node: obj) : string = jsNative

let private reported graph host view =
    let reports = ResizeArray<exn list>()
    let mounted = Mounting.mount graph None reports.Add host None (fun context -> Program.run view context.Backend)
    mounted, reports

let private eachSwapMovesTwo () =
    use graph = new Graph ()
    listedLog.Clear ()

    let items =
        graph.Run (fun () -> Api.createSignal (Array.init 1000 (fun index -> { Id = index; Text = "" })))

    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let next = Array.copy items.Value
    let first = next[1]
    next[1] <- next[998]
    next[998] <- first
    let inserts = countInserts list
    graph.Run (fun () -> items.Value <- next)
    equal 2 (inserts ())
    mounted.Dispose ()

let private eachMatchesReferenceOrder () =
    use graph = new Graph ()
    listedLog.Clear ()
    let mutable state = 7.0

    let next bound =
        state <- (state * 48271.0) % 2147483647.0
        int (state % float bound)

    let items = graph.Run (fun () -> Api.createSignal ([||]: Listed array))
    let host = host ()
    let mounted = mount graph host (Component.render listed items)
    let nodes = System.Collections.Generic.Dictionary<int, obj>()
    let mutable nextId = 1

    for _ in 1..200 do
        let current = items.Value |> Array.map _.Id |> ResizeArray

        for _ in 1 .. next 3 do
            if current.Count > 0 then
                current.RemoveAt (next current.Count)

        for _ in 1 .. next 3 do
            current.Insert (next (current.Count + 1), nextId)
            nextId <- nextId + 1

        for _ in 1 .. next 3 do
            if current.Count > 1 then
                let i, j = next current.Count, next current.Count
                let swapped = current[i]
                current[i] <- current[j]
                current[j] <- swapped

        let order = current.ToArray ()
        graph.Run (fun () -> items.Value <- order |> Array.map (fun id -> { Id = id; Text = string id + "," }))
        equal (order |> Array.map (fun id -> string id + ",") |> String.concat "") host.textContent
        let rendered = host.querySelectorAll "li"

        for index in 0 .. order.Length - 1 do
            let node = box (rendered.item (float index))

            match nodes.TryGetValue order[index] with
            | true, known -> equal true (obj.ReferenceEquals (known, node))
            | _ -> nodes[order[index]] <- node

    mounted.Dispose ()
    equal 0. host.childNodes.length

let private emptyRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.fragment [||])

let private emptyListed =
    Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) emptyRow |])

let private zeroNodeRows () =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = "" } |])
    let host = host ()
    let mounted = mount graph host (Component.render emptyListed items)
    let list = host.querySelector "ul" |> unbox<HTMLElement>
    let markers = rowMarkers list
    equal 3 markers.Length
    graph.Run (fun () -> items.Value <- [| for id in [ 3; 1; 2 ] -> { Id = id; Text = "" } |])
    let moved = rowMarkers list
    equal true (obj.ReferenceEquals (moved[0], markers[2]) && obj.ReferenceEquals (moved[1], markers[0]) && obj.ReferenceEquals (moved[2], markers[1]))
    graph.Run (fun () -> items.Value <- [||])
    equal 0 (rowMarkers list).Length
    mounted.Dispose ()

let private disposalCleansBeforeRelease () =
    use graph = new Graph ()
    let host = host ()
    let counts = ResizeArray<float>()

    let watching =
        Component.define
            (fun setup (item: unit -> Listed) ->
                setup.OnCleanup (fun () -> counts.Add (host.querySelectorAll "li").length)
                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let watched =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) watching |])

    let items = graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = string id } |])
    let mounted = mount graph host (Component.render watched items)
    mounted.Dispose ()
    equal ([ 3.; 3.; 3. ], 0.) (List.ofSeq counts, host.childNodes.length)

let private clicksFollowMovedRows () =
    use graph = new Graph ()
    listedLog.Clear ()
    let items = graph.Run (fun () -> Api.createSignal [| for id in 1..3 -> { Id = id; Text = string id } |])
    let other = host ()
    let host = host ()
    globalThis.document.body.appendChild host |> ignore
    globalThis.document.body.appendChild other |> ignore
    let mounted = mount graph host (Component.render listed items)
    let one = host.querySelector "li" |> unbox<HTMLElement>
    graph.Run (fun () -> items.Value <- [| for id in [ 3; 1; 2 ] -> { Id = id; Text = string id } |])
    one.click ()
    mounted.MoveBefore (other, None)
    one.click ()
    graph.Run (fun () -> items.Value <- [| for id in [ 3; 2 ] -> { Id = id; Text = string id } |])
    one.click ()
    mounted.Dispose ()
    host.remove ()
    other.remove ()
    equal 2 (listedLog |> Seq.filter (fun line -> line = "click 1") |> Seq.length)

let private throwingRowCleanup () =
    use graph = new Graph ()
    let host = host ()
    let seen = ResizeArray<float>()

    let throwing =
        Component.define
            (fun setup (item: unit -> Listed) ->
                setup.OnCleanup (fun () ->
                    seen.Add (host.querySelectorAll "li").length
                    failwith "row cleanup")

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) throwing |])

    let items = graph.Run (fun () -> Api.createSignal [| for id in 1..2 -> { Id = id; Text = string id } |])
    let mutable atReport = -1.
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount
            graph
            None
            (fun errors ->
                atReport <- (host.querySelectorAll "li").length
                reports.Add errors)
            host
            None
            (fun context -> Program.run (Component.render list items) context.Backend)

    graph.Run (fun () -> items.Value <- [| { Id = 2; Text = "2" } |])
    equal ([ 2. ], 1, [ "row cleanup" ], 1.) (List.ofSeq seen, reports.Count, reports[0] |> List.map _.Message, atReport)
    mounted.Dispose ()

let private disposalFromRowSetup () =
    use graph = new Graph ()
    let host = host ()
    let mutable handle: Mounted = Unchecked.defaultof<_>

    let disposing =
        Component.define
            (fun _ (item: unit -> Listed) ->
                if (item ()).Id = 3 then
                    handle.Dispose ()

                item)
            (fun t -> t.element (HtmlTag "li") [||] [| t.text (fun item -> (item ()).Text) |])

    let list =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) disposing |])

    let items = graph.Run (fun () -> Api.createSignal [| { Id = 1; Text = "a" } |])
    let mounted, reports = reported graph host (Component.render list items)
    handle <- mounted
    graph.Run (fun () -> items.Value <- [| { Id = 1; Text = "a" }; { Id = 3; Text = "c" } |])
    equal (Disposed, 0., 0) (handle.Phase, host.childNodes.length, reports.Count)

type Grouped = { Group: int; Members: Listed array }

let private groupRow =
    Component.define (fun _ (item: unit -> Grouped) -> item) (fun t ->
        t.element
            (HtmlTag "li")
            [||]
            [|
                t.text (fun item -> string (item ()).Group + ":")
                t.each (fun (member': Listed) -> member'.Id) (fun item -> (item ()).Members) listedRow
            |])

let private grouped =
    Component.define (fun _ (items: Signal<Grouped array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (group: Grouped) -> group.Group) (fun items -> items.Value) groupRow |])

let private nestedListsMoveWhole () =
    use graph = new Graph ()
    listedLog.Clear ()
    let listedOf texts = texts |> Array.map (fun (id, text) -> { Id = id; Text = text })

    let items =
        graph.Run (fun () ->
            Api.createSignal
                [|
                    { Group = 1; Members = listedOf [| 10, "a"; 11, "b" |] }
                    { Group = 2; Members = listedOf [| 20, "c" |] }
                |])

    let host = host ()
    let mounted = mount graph host (Component.render grouped items)
    equal "1:ab2:c" host.textContent

    graph.Run (fun () ->
        items.Value <-
            [|
                { Group = 2; Members = listedOf [| 20, "c"; 21, "d" |] }
                { Group = 1; Members = listedOf [| 11, "b" |] }
            |])

    equal "2:cd1:b" host.textContent
    graph.Run (fun () -> items.Value <- [| { Group = 1; Members = listedOf [| 11, "b" |] } |])
    equal "1:b" host.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length

let private boxedRow =
    Component.define (fun _ (item: unit -> obj * string) -> item) (fun t ->
        t.element (HtmlTag "li") [||] [| t.text (fun item -> snd (item ())) |])

let private boxedListed =
    Component.define (fun _ (items: Signal<(obj * string) array>) -> items) (fun t ->
        t.element (HtmlTag "ul") [||] [| t.each (fun (item: obj * string) -> fst item) (fun items -> items.Value) boxedRow |])

let private boxedKeys () =
    use graph = new Graph ()
    let items = graph.Run (fun () -> Api.createSignal [| box 1, "int"; box 1L, "long"; box "1", "string" |])
    let host = host ()
    let mounted = mount graph host (Component.render boxedListed items)
    let rendered = host.querySelectorAll "li"
    let long = box (rendered.item 1.)
    graph.Run (fun () -> items.Value <- [| box 1L, "LONG"; box "1", "string" |])
    equal "LONGstring" host.textContent
    equal true (obj.ReferenceEquals (long, box ((host.querySelectorAll "li").item 0.)))
    mounted.Dispose ()

let private focusSurvivesOtherMoves () =
    use graph = new Graph ()

    let field =
        Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
            t.element (HtmlTag "li") [||] [| t.input [| t.Props.Input.value (fun item -> (item ()).Text) |] |])

    let fields =
        Component.define (fun _ (items: Signal<Listed array>) -> items) (fun t ->
            t.element (HtmlTag "ul") [||] [| t.each (fun (item: Listed) -> item.Id) (fun items -> items.Value) field |])

    let items = graph.Run (fun () -> Api.createSignal [| for id in 1..4 -> { Id = id; Text = "value" + string id } |])
    let host = host ()
    globalThis.document.body.appendChild host |> ignore
    let mounted = mount graph host (Component.render fields items)
    let third = (host.querySelectorAll "input").item 2. |> unbox<HTMLInputElement>
    third.focus ()
    third.setSelectionRange (1., 3.)
    graph.Run (fun () -> items.Value <- [| for id in [ 2; 3; 4; 1 ] -> { Id = id; Text = "value" + string id } |])
    let focused = isFocused third
    let selection = selectionOf third
    mounted.Dispose ()
    host.remove ()
    equal (true, "1,3") (focused, selection)

let browserCases: (string * (unit -> unit)) array =
    [| "focus and selection inside a row survive moving another row", focusSurvivesOtherMoves |]
```

Add to `cases`, after the `eachDuplicates` line:

```fsharp
        "a two-row swap of 1,000 rows moves two rows", eachSwapMovesTwo
        "list updates match a reference order and keep node identities", eachMatchesReferenceOrder
        "zero-node rows keep their place through their marker", zeroNodeRows
        "disposing a mount runs every row cleanup before a node leaves", disposalCleansBeforeRelease
        "delegated clicks reach a moved row and stop after its removal", clicksFollowMovedRows
        "a throwing row cleanup is reported once with its update after the row leaves", throwingRowCleanup
        "disposing the mount from a row's setup ends the update silently", disposalFromRowSetup
        "rows holding nested lists move and leave whole", nestedListsMoveWhole
        "boxed keys of different types keep separate rows", boxedKeys
```

`emptyRow` must be zero-node: `t.fragment [||]` with no other form. If `t.fragment [||]` does not compile as a zero-node row template, stop and report NEEDS_CONTEXT; do not substitute another row.

`range.html`: add `import { browserCases as templateBrowser } from "../generated-tests/TemplateChecks.js";` and `window.templateBrowserCases = templateBrowser;`. `range.spec.mjs`: append

```js
test("template rows keep focus and selection across list moves in Chromium", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.templateBrowserCases);
  const results = await page.evaluate(() => window.templateBrowserCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});
```

`RangeChecks.fs`: replace `keyedMoveRecovery` and its two cases with:

```fsharp
let private keyedLayoutFailure () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let reports = ResizeArray<exn>()
    let cleanups = ResizeArray<int>()

    let view =
        Keyed.list Tree.ops id (fun () -> rows.Value) (fun current ->
            Tree.ops.Component (fun ctx ->
                let key = current ()
                ctx.OnCleanup (fun () -> cleanups.Add key)
                Tree.ops.Text (string key)))

    let handle =
        mount graph None (List.iter reports.Add) host None (fun ctx -> Tree.run view ctx.Backend)

    let mutable inserts = 0

    //FOR-REVIEW Only the first insert fails: the old reconciler's rollback then succeeds and restores the order, so this test is red before this task.
    let restore =
        afterAttachment host (fun () ->
            inserts <- inserts + 1

            if inserts = 1 then
                failwith "insert")

    try
        graph.Run (fun () -> rows.Value <- [ 3; 2; 1 ])
    finally
        restore ()

    equal 1 reports.Count
    equal Disposed handle.Phase
    equal 0. host.childNodes.length
    equal [ 1; 2; 3 ] (cleanups |> Seq.sort |> List.ofSeq)
```

with the case `"a failed keyed layout disposes the mount and reports once", keyedLayoutFailure`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `rtk npm run test:dom`
Expected: FAIL. "a two-row swap of 1,000 rows moves two rows" reports `Expected 2; got 997`, and "a failed keyed layout disposes the mount and reports once" reports `Expected Disposed`.

- [ ] **Step 3: Give regions a kind**

`TemplateDom.fs`, before `CompiledRegion`:

```fsharp
/// <summary>How a region mounts its rows.</summary>
type RegionKind =
    /// <summary>One row per keyed item; new rows are created before removed rows retire.</summary>
    | ListRows
    /// <summary>The row of the selected arm; the previous arm is disposed before the next is created.</summary>
    | SelectedArm
    /// <summary>One view, read and mounted once.</summary>
    | ViewHole
```

In `CompiledRegion`, replace `Once` and `DisposeFirst` with `Kind: RegionKind` and add `Row: RegionRow`. In `compile`:

```fsharp
                        Kind =
                            match region.Row with
                            | ItemRow _ -> ListRows
                            | ArmRow -> SelectedArm
                            | ViewRow -> ViewHole
                        Render = rowRenderer region.Row
                        Row = region.Row
```

Delete `readsOnce`. `MountServices.Region`:

```fsharp
    /// <summary>Mounts a region between its start and end markers. The flag marks markers that are roots of the appended
    /// clone; the materialiser is null for a view hole.</summary>
    abstract Region:
        Node *
        Node *
        bool *
        RegionKind *
        (unit -> (obj * obj) array) *
        ((unit -> obj) -> Backend -> unit) *
        Func<(unit -> obj), MountServices, unit> ->
            unit
```

Make `materialise` `let rec internal materialise`, and replace its region loop body with:

```fsharp
        let region = at regions i
        let owner = partInstance state childBase region.Owner
        let start: Node = unbox (at state (StateNodes + region.Marker))
        let finish: Node = unbox start.nextSibling
        let items = region.Items

        let read =
            match region.Kind with
            | ViewHole ->
                let snapshot = items.Invoke owner
                fun () -> snapshot
            | ListRows
            | SelectedArm -> fun () -> items.Invoke owner

        let rows =
            match region.Row with
            | ItemRow (rowDefinition, rowProps) ->
                Func<(unit -> obj), MountServices, unit>(fun current rowServices ->
                    materialise rowDefinition (rowProps.Invoke current) rowServices)
            | ArmRow ->
                Func<(unit -> obj), MountServices, unit>(fun current rowServices ->
                    let arm = unbox<Arm>(current ())
                    materialise arm.Definition (arm.Reader.Invoke current) rowServices)
            | ViewRow -> null

        services.Region (start, finish, region.RootLevel, region.Kind, read, region.Render, rows)
```

- [ ] **Step 4: Add rows, the row builder and the list region**

`Mounting.fs`:

1. After `let private skip`, add:

```fsharp
[<Emit("new Set()")>]
let private nameSet () : obj = jsNative

[<Emit("$0.has($1)")>]
let private setHas (set: obj) (name: string) : bool = jsNative

[<Emit("$0.add($1)")>]
let private setAdd (set: obj) (name: string) : unit = jsNative

[<Emit("$0.forEach($1)")>]
let private setIter (set: obj) (action: string -> unit) : unit = jsNative

/// <summary>For each position of <c>sources</c>, whether it belongs to one longest increasing run of its non-negative values.</summary>
let private increasingRun (sources: int array) : bool array =
    let keep: bool array = Array.zeroCreate sources.Length
    let tails = ResizeArray<int>()
    let previous = Array.create sources.Length -1

    for index in 0 .. sources.Length - 1 do
        let value = sources[index]

        if value >= 0 then
            let mutable low = 0
            let mutable high = tails.Count

            while low < high do
                let middle = (low + high) >>> 1

                if sources[tails[middle]] < value then
                    low <- middle + 1
                else
                    high <- middle

            if low > 0 then
                previous[index] <- tails[low - 1]

            if low = tails.Count then
                tails.Add index
            else
                tails[low] <- index

    if tails.Count > 0 then
        let mutable cursor = tails[tails.Count - 1]

        while cursor >= 0 do
            keep[cursor] <- true
            cursor <- previous[cursor]

    keep

/// <summary>A region's failure state: set up failures recorded during the first pass, reports after it.</summary>
type private RegionErrors(report: exn list -> unit) =
    member val Initializing = true with get, set
    member val SetupFailure: exn option = None with get, set

    member _.Report(errors: exn list) =
        if not errors.IsEmpty then
            try
                Api.untrack (fun () -> report errors)
            with reporterError ->
                raise (AggregateException ("Keyed update and reporter failed", List.toArray (errors @ [ reporterError ])))

    /// <summary>Records <c>errors</c> as the region's setup failure during the first pass; afterwards reports them and raises <c>error</c>.</summary>
    member this.Fail(error: exn, errors: exn list) =
        if this.Initializing then
            this.SetupFailure <-
                Some (
                    match errors with
                    | [ single ] -> single
                    | _ ->
                        AggregateException (
                            "Keyed row setup and cleanup failed: "
                            + String.concat "; " (errors |> List.map _.Message),
                            errors
                        )
                        :> exn
                )
        else
            this.Report errors
            raise error
```

2. In the recursive type group, after `Mounted`, add `Row` and `RowBuilder` (keep `KeyedEntry` until Task 8):

```fsharp
/// <summary>The nodes of a row or arm, from its first node to its last.</summary>
and [<Sealed; AllowNullLiteral>] private Row(first: Node, last: Node) =
    member _.First = first
    member _.Last = last
    /// <summary>The row's position among its region's laid-out rows, or -1 before its first layout.</summary>
    member val Index = -1 with get, set
    member val Removed = false with get, set
    /// <summary>The nested mount of an authored row, or null for a template row.</summary>
    member val Mount: Mounted = Unchecked.defaultof<_> with get, set

    static member MoveBefore(row: Row, parent: Node, anchor: Node) =
        let mutable cursor = row.First
        let mutable going = true

        while going do
            let next = nextOrNull cursor

            if same cursor row.Last then
                going <- false

            parent.insertBefore (cursor, anchor) |> ignore
            cursor <- next

    static member Remove(row: Row) =
        if not row.Removed then
            row.Removed <- true
            let mutable cursor = row.First
            let mutable going = true

            while going do
                let next = nextOrNull cursor

                if same cursor row.Last then
                    going <- false

                remove (cursor :?> ChildNode)
                cursor <- next

    /// <summary>Raises unless the markers share <c>parent</c> and the laid-out rows sit between them in order.</summary>
    static member Validate(regionStart: Node, regionEnd: Node, laidOut: ResizeArray<Row>, parent: Node) =
        if
            not (regionStart.parentNode |> Option.exists (same parent))
            || not (regionEnd.parentNode |> Option.exists (same parent))
        then
            invalid "Keyed-list boundaries were externally mutated"

        let mutable cursor = nextOrNull regionStart

        for index in 0 .. laidOut.Count - 1 do
            let row = laidOut[index]

            if not row.Removed then
                if not (isNull (box row.Mount)) then
                    row.Mount.Validate ()

                if not (same cursor row.First) then
                    invalid "Keyed-list rows or contents were externally mutated"

                cursor <- nextOrNull row.Last

        if not (same cursor regionEnd) then
            invalid "Keyed-list boundaries were externally reordered"

    static member Relocate(laidOut: ResizeArray<Row>) =
        for index in 0 .. laidOut.Count - 1 do
            let row = laidOut[index]

            if not (isNull (box row.Mount)) then
                row.Mount.Relocate ()

    static member private Place(row: Row, parent: Node, anchor: Node) =
        Row.MoveBefore (row, parent, anchor)

        if not (isNull (box row.Mount)) then
            row.Mount.Relocate ()

    /// <summary>
    /// Moves the region's rows into the order of <c>next</c> and stores it as the laid-out rows. Rows outside the
    /// common prefix and suffix move only when they fall outside one longest run of rows already in order. Stops when
    /// <c>live</c> turns false.
    /// </summary>
    static member Layout(live: unit -> bool, laidOut: ResizeArray<Row>, staging: DocumentFragment, parent: Node, regionEnd: Node, next: Row array) =
        let previous = ResizeArray<Row>(laidOut.Count)

        for index in 0 .. laidOut.Count - 1 do
            let row = laidOut[index]

            if not row.Removed then
                row.Index <- previous.Count
                previous.Add row

        let mutable aStart = 0
        let mutable bStart = 0
        let mutable aEnd = previous.Count
        let mutable bEnd = next.Length

        while aStart < aEnd && bStart < bEnd && same previous[aStart] next[bStart] do
            aStart <- aStart + 1
            bStart <- bStart + 1

        while aEnd > aStart && bEnd > bStart && same previous[aEnd - 1] next[bEnd - 1] do
            aEnd <- aEnd - 1
            bEnd <- bEnd - 1

        if bStart < bEnd && live () then
            let anchor = if bEnd < next.Length then next[bEnd].First else regionEnd

            if aStart = aEnd then
                let staged =
                    staging.firstChild |> Option.exists (same next[bStart].First)
                    && staging.lastChild |> Option.exists (same next[bEnd - 1].Last)

                if staged then
                    parent.insertBefore (staging, anchor) |> ignore
                else
                    let mutable index = bStart

                    while index < bEnd && live () do
                        Row.Place (next[index], parent, anchor)
                        index <- index + 1
            else
                let count = bEnd - bStart
                let sources = Array.create count -1

                for offset in 0 .. count - 1 do
                    let index = next[bStart + offset].Index

                    if index >= aStart && index < aEnd && same previous[index] next[bStart + offset] then
                        sources[offset] <- index

                let keep = increasingRun sources
                let mutable before = anchor
                let mutable offset = count - 1

                while offset >= 0 && live () do
                    let row = next[bStart + offset]

                    if not keep[offset] then
                        Row.Place (row, parent, before)

                    before <- row.First
                    offset <- offset - 1

        laidOut.Clear ()

        for index in 0 .. next.Length - 1 do
            next[index].Index <- index
            laidOut.Add next[index]

/// <summary>Materialises the template rows and arms of one region into the region's staging fragment and serves their setup.</summary>
and private RowBuilder(state: MountState) =
    let roots = ResizeArray<Node>()
    let mutable staging: DocumentFragment = Unchecked.defaultof<_>
    let mutable settingUp = false
    let mutable setupFailure: exn option = None

    let setup: Setup =
        {
            OnCleanup = fun callback -> state.OwnedCleanup (Api.getOwner (), callback)
        }

    member _.Staging =
        if isNull (box staging) then
            staging <- fragment ()

        staging

    member this.Build(materialise: Func<(unit -> obj), TemplateDom.MountServices, unit>, current: unit -> obj) : Row =
        roots.Clear ()
        setupFailure <- None
        settingUp <- true

        try
            try
                materialise.Invoke (current, this :> TemplateDom.MountServices)
            with _ ->
                for index in 0 .. roots.Count - 1 do
                    remove (roots[index] :?> ChildNode)

                reraise ()
        finally
            settingUp <- false

        if roots.Count = 0 then
            let marker = comment "ranvier:row"
            this.Staging.appendChild marker |> ignore
            roots.Add marker

        Row (roots[0], roots[roots.Count - 1])

    interface TemplateDom.MountServices with
        member this.Append(node) =
            this.Staging.appendChild node |> ignore
            roots.Add node

        member _.Setup = setup

        member _.SettingUp() = settingUp

        member _.SetupFailed(error) = setupFailure <- Some error

        member _.RaiseSetupFailure() = setupFailure |> Option.iter raise

        member _.IsDelegated(eventName) = isDelegated delegatedEvents eventName

        member _.Delegate(eventName) = state.DelegateLate eventName

        member _.ListenDirect(node, eventName, invoke) = state.ListenDirect (node, eventName, invoke)

        member _.ReportFailure(error) = state.ReportReaderFailure error

        member _.ClearFailure() = state.ClearReported ()

        member _.Region(start, finish, _, kind, read, render, rows) = state.NestedRegion (start, finish, kind, read, render, rows)
```

3. In `MountState`:
   - Replace `delegatedNames: ResizeArray<string>` with `let mutable delegatedNames: obj = null`, and `delegateAt` and `releaseDelegation` with:

```fsharp
    let delegateAt (host: Node) =
        if not (isNull delegatedNames) then
            setIter delegatedNames (fun eventName -> delegateFrom.Invoke (host, eventName))

        delegatedHost <- host

    let releaseDelegation () =
        if not (isNull (box delegatedHost)) && not (isNull delegatedNames) then
            let releasedHost = delegatedHost
            setIter delegatedNames (fun eventName -> undelegateFrom.Invoke (releasedHost, eventName))
            delegatedHost <- Unchecked.defaultof<_>
```

   `registerDelegated` becomes:

```fsharp
    let registerDelegated (eventName: string) =
        if isNull delegatedNames then
            delegatedNames <- nameSet ()

        setAdd delegatedNames eventName

    let mutable delegationReleased = false

    // A row registers delegated events after its mount attached: the name joins the mount's host at once.
    let delegateLate (eventName: string) =
        if isNull delegatedNames || not (setHas delegatedNames eventName) then
            registerDelegated eventName

            if not (isNull (box delegatedHost)) then
                delegateFrom.Invoke (delegatedHost, eventName)

                if not delegationReleased then
                    delegationReleased <- true
                    root.OnCleanup releaseDelegation
```

   - In `Run`, set `delegationReleased <- true` before `root.OnCleanup releaseDelegation`.
   - Add internal members:

```fsharp
    member internal _.OwnedCleanup(owner: Owner, callback: unit -> unit) = ownedCleanup owner callback
    member internal _.DelegateLate(eventName: string) = delegateLate eventName
    member internal _.ListenDirect(node: Node, eventName: string, invoke: obj -> unit) = listenDirect node eventName invoke
    member internal _.ReportReaderFailure(error: exn) = reportReaderFailure error
    member internal _.ClearReported() = clearReported ()

    member internal this.NestedRegion(start, finish, kind, read, render, rows) =
        this.RegionOf (start, finish, NestedRegion, kind, read, render, rows)

    member private _.RegionParent(regionStart: Node, regionEnd: Node) : Node =
        let regionParent =
            regionStart.parentNode
            |> Option.defaultWith (fun () -> invalid "Keyed-list boundaries have no parent")

        if not (regionEnd.parentNode |> Option.exists (same regionParent)) then
            invalid "Keyed-list boundaries were externally mutated"

        regionParent

    member private _.PlaceRegion(regionStart: Node, regionEnd: Node, placement: RegionPlacement, validate: unit -> unit, relocate: unit -> unit) =
        match placement with
        | NestedRegion -> ()
        | NewRegion -> range.AddRegion (regionStart, regionEnd, validate, relocate)
        | RootRegion -> range.SpanRegion (regionStart, regionEnd, validate, relocate)

    /// <summary>Mounts an authored row as a nested mount and returns its nodes.</summary>
    member private _.MountRow(current: unit -> obj, render: (unit -> obj) -> Backend -> unit) : Row =
        let rowHost = fragment ()

        let mounted =
            MountState(scheduling, graph, root, Unchecked.defaultof<_>, report, rowHost)
                .Run (None, (fun context -> render current context.Backend))

        let first =
            rowHost.firstChild
            |> Option.defaultWith (fun () -> invalid "A keyed row has no start boundary")

        let last =
            rowHost.lastChild
            |> Option.defaultWith (fun () -> invalid "A keyed row has no end boundary")

        Row (first, last, Mount = mounted)

    /// <summary>
    /// Mounts one row per keyed item between the region's markers, on a core projection that creates new rows before it
    /// retires removed ones. Template rows (<c>rows</c>) are cloned into the region; authored rows (<c>render</c>) are
    /// nested mounts.
    /// </summary>
    member private this.ListRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            read: unit -> (obj * obj) array,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        let laidOut = ResizeArray<Row>()
        let builder = RowBuilder this
        let errors = RegionErrors report
        let nested = (placement = NestedRegion)
        let mutable regionOwner: Owner = Unchecked.defaultof<_>
        let mutable projection: Projection<obj, Row> = Unchecked.defaultof<_>

        let live () =
            not root.IsDisposed && not regionOwner.IsDisposed

        let regionParent () = this.RegionParent (regionStart, regionEnd)

        this.PlaceRegion (
            regionStart,
            regionEnd,
            placement,
            (fun () -> Row.Validate (regionStart, regionEnd, laidOut, regionParent ())),
            fun () -> Row.Relocate laidOut
        )

        let buildRow (item: unit -> obj * obj) : Row =
            let mutable row: Row = null

            // Runs after the row's own cleanups.
            Api.onCleanup (fun () ->
                if not (isNull row) then
                    if not (isNull (box row.Mount)) then
                        row.Removed <- true
                    elif not (nested && regionOwner.IsDisposed) then
                        Row.Remove row)

            let current () = snd (item ())

            row <-
                if isNull rows then
                    this.MountRow (current, render)
                else
                    builder.Build (rows, current)

            row

        let reconcile () =
            if live () then
                let parent = ref Unchecked.defaultof<Node>
                let keys = ref Unchecked.defaultof<obj array>

                let recorded, failure =
                    MountLifetime.Capture (
                        graph,
                        fun () ->
                            parent.Value <- regionParent ()
                            Row.Validate (regionStart, regionEnd, laidOut, parent.Value)
                            keys.Value <- projection.Keys
                    )

                if live () then
                    match failure with
                    | Some error -> errors.Fail (error, error :: recorded)
                    | None ->
                        let layoutFailure =
                            try
                                Api.untrack (fun () ->
                                    let next = keys.Value |> Array.map (fun key -> projection.Get key)
                                    Row.Layout (live, laidOut, builder.Staging, parent.Value, regionEnd, next))

                                None
                            with error ->
                                Some error

                        match layoutFailure with
                        | Some error ->
                            root.Dispose ()
                            errors.Fail (error, recorded @ [ error ])
                        | None -> errors.Report recorded

        Api.createRoot (fun owner ->
            regionOwner <- owner

            projection <-
                Api.createProjectionOnce
                    ProjectionOptions.CreateFirst
                    (fun (item: obj * obj) -> fst item)
                    buildRow
                    (fun () -> read () :> seq<_>)

            Api.untrack (fun () -> Api.createEffect reconcile))

        errors.Initializing <- false
        errors.SetupFailure |> Option.iter raise

    member private this.RegionOf(start, finish, placement, kind, read, render, rows) =
        match kind with
        | TemplateDom.ListRows -> this.ListRegion (start, finish, placement, read, render, rows)
        | TemplateDom.SelectedArm -> this.KeyedRegion (start, finish, placement, true, read, render)
        | TemplateDom.ViewHole -> this.KeyedRegion (start, finish, placement, false, read, render)
```

   - `override this.Keyed(read, render)` becomes `this.AuthoredRegion (TemplateDom.ListRows, read, render)`, and `override this.Switch(read, render)` becomes `this.AuthoredRegion (TemplateDom.SelectedArm, read, render)`. `AuthoredRegion`:

```fsharp
    member private this.AuthoredRegion(kind, read, render) =
        ensureSetup ()

        let regionStart, regionEnd =
            comment "ranvier:list:start", comment "ranvier:list:end"

        let placement =
            if isNull (box target) then
                NewRegion
            else
                range.AppendGenerated (target, regionStart)
                range.AppendGenerated (target, regionEnd)
                NestedRegion

        this.RegionOf (regionStart, regionEnd, placement, kind, read, render, null)
```

   - Delete `KeyedRegion`'s own `match placement with ... AddRegion/SpanRegion` block, and call `this.PlaceRegion (regionStart, regionEnd, placement, validate, relocate)` in its place.
   - `MountServices.Region` on `MountState`:

```fsharp
        member this.Region(start, finish, rootLevel, kind, read, render, rows) =
            ensureSetup ()

            let placement =
                if rootLevel && isNull (box target) then
                    RootRegion
                else
                    NestedRegion

            this.RegionOf (start, finish, placement, kind, read, render, rows)
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `rtk npm run test:dom`
Expected: PASS, 319 tests: the 311 after Task 5, less `keyedMoveRecovery`'s two cases, plus one replacement and nine new cases.

Run: `rtk npm run test:browser`
Expected: PASS, 23 tests.

Run the emitted-JavaScript grep from Task 5 Step 1.
Expected: each count at most its baseline. `Mounting.js` passes `buildRow` to `createProjectionOnce` without a `curry` or `uncurry` wrapper.

- [ ] **Step 6: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs examples/Fable.Ranvier.Playground/tests/RangeChecks.fs
rtk git add experiments/Ranvier.View.Foundation examples/Fable.Ranvier.Playground/tests
rtk git commit -m "feat(view): mount keyed lists on a create-first core projection with a minimal-move layout"
```

---

### Task 8: Arm regions and view holes on core primitives

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs`
- Test: `examples/Fable.Ranvier.Playground/tests/RangeChecks.fs`

**Interfaces:**
- Consumes: `Row`, `RowBuilder`, `RegionErrors`, `PlaceRegion`, `RegionParent`, `MountRow`, `ListRegion`, `RegionOf` (Task 7); `MountLifetime.Adopt`, `MountLifetime.Capture` (Task 6).
- Produces:
  - Private `sameArm: IEqualityComparer<obj>` (module level): equal when both selections are null, or when their keys (`fst`) are equal under `=`.
  - Private `RegionSite(state: MountState, regionStart: Node, regionEnd: Node, placement: RegionPlacement, report: exn list -> unit)` in the recursive type group, with `Start`, `End`, `LaidOut: ResizeArray<Row>`, `Errors: RegionErrors`, `Owner: Owner`, `Builder: RowBuilder`, `Live(): bool`, `Parent(): Node`, `Validate()`, `ReleaseRow(row: Row)`, `Own(body: unit -> unit)` and `Settle(recorded: exn list, failure: exn option, settle: exn list -> unit)`.
  - `MountState.Site(regionStart, regionEnd, placement): RegionSite`; `ListRegion` rewritten on it; `MountState.ArmRegion(regionStart, regionEnd, placement, read, render, rows)`; `MountState.HoleRegion(regionStart, regionEnd, placement, read, render)`.
  - `MountState.RegionParent` no longer exists (its body is `RegionSite.Parent`). `KeyedRegion`, `KeyedEntry`, `KeyedDisposed` and `KeyedSetupFailed` no longer exist.

- [ ] **Step 1: Write the failing test**

Add to `RangeChecks.fs` and to `allCases` as `"an authored switch disposes the previous arm before the next arm's setup and keeps an equal key", authoredSwitch`:

```fsharp
let private authoredSwitch () =
    use graph = new Graph ()
    let host = Dom.element "section" [] []
    let selected = graph.Run (fun () -> Api.createSignal (1, "a"))
    let log = ResizeArray<string>()

    let view =
        Branch.select Tree.ops fst (fun () -> selected.Value) (fun current ->
            Tree.ops.Component (fun ctx ->
                let key = fst (current ())
                log.Add ("setup " + string key)
                ctx.OnCleanup (fun () -> log.Add ("cleanup " + string key))
                Tree.ops.Bind (=) (fun () -> snd (current ()))))

    let handle = tree graph host view
    graph.Run (fun () -> selected.Value <- (1, "b"))
    equal "b" host.textContent
    graph.Run (fun () -> selected.Value <- (2, "c"))
    equal "c" host.textContent
    equal [ "setup 1"; "cleanup 1"; "setup 2" ] (List.ofSeq log)
    handle.Dispose ()
    equal 0. host.childNodes.length
```

Arm switches go through `KeyedRegion` until this task, which already passes this check. The test pins the behaviour across the replacement.

- [ ] **Step 2: Run the test**

Run: `rtk npm run test:dom`
Expected: PASS (320 tests). The test guards the replacement in Steps 3–4.

- [ ] **Step 3: Share the region site and add the arm and hole regions**

The arm region is the second region kind with markers, laid-out rows, an owner, a row builder and a failure state. That state moves into one private helper, `RegionSite`, which `ListRegion`, `ArmRegion` and `HoleRegion` all use.

`Mounting.fs`, before the recursive type group:

```fsharp
let private sameArm =
    { new System.Collections.Generic.IEqualityComparer<obj> with
        member _.Equals(left, right) =
            if isNull left || isNull right then
                isNull left && isNull right
            else
                fst (unbox<obj * obj> left) = fst (unbox<obj * obj> right)

        member _.GetHashCode _ = 0
    }
```

In the recursive type group, after `RowBuilder`:

```fsharp
/// <summary>
/// The markers and laid-out rows of one region, with the region's owner, row builder and failure state.
/// </summary>
and private RegionSite
    (state: MountState, regionStart: Node, regionEnd: Node, placement: RegionPlacement, report: exn list -> unit) =
    let laidOut = ResizeArray<Row>()
    let errors = RegionErrors report
    let mutable owner: Owner = Unchecked.defaultof<_>
    let mutable builder: RowBuilder = Unchecked.defaultof<_>

    member _.Start = regionStart
    member _.End = regionEnd
    member _.LaidOut = laidOut
    member _.Errors = errors

    /// <summary>The region's owner, set by <c>Own</c>.</summary>
    member _.Owner = owner

    member _.Builder =
        if isNull (box builder) then
            builder <- RowBuilder state

        builder

    /// <summary>Whether the mount and the region's owner are both live.</summary>
    member _.Live() =
        not state.Root.IsDisposed && not owner.IsDisposed

    /// <summary>The markers' shared parent. Raises when the markers have no parent or different parents.</summary>
    member _.Parent() : Node =
        let regionParent =
            regionStart.parentNode
            |> Option.defaultWith (fun () -> invalid "Keyed-list boundaries have no parent")

        if not (regionEnd.parentNode |> Option.exists (same regionParent)) then
            invalid "Keyed-list boundaries were externally mutated"

        regionParent

    member this.Validate() =
        Row.Validate (regionStart, regionEnd, laidOut, this.Parent ())

    /// <summary>
    /// Removes the nodes of a template row, or marks an authored row removed. A template row of a nested region keeps
    /// its nodes while the region's owner tears down; they leave with the element or row holding the markers.
    /// </summary>
    member _.ReleaseRow(row: Row) =
        if not (isNull row) then
            if not (isNull (box row.Mount)) then
                row.Removed <- true
            elif not (placement = NestedRegion && owner.IsDisposed) then
                Row.Remove row

    /// <summary>
    /// Runs <c>body</c> in a new root owner of the region, then raises the setup failure recorded during that first pass.
    /// </summary>
    member _.Own(body: unit -> unit) =
        Api.createRoot (fun created ->
            owner <- created
            body ())

        errors.Initializing <- false
        errors.SetupFailure |> Option.iter raise

    /// <summary>
    /// Ends a pass while the region is live: a failure is recorded or reported with <c>recorded</c>, and a success runs
    /// <c>settle recorded</c>.
    /// </summary>
    member this.Settle(recorded: exn list, failure: exn option, settle: exn list -> unit) =
        if this.Live () then
            match failure with
            | Some error -> errors.Fail (error, error :: recorded)
            | None -> settle recorded
```

In `MountState`, replace `RegionParent` with `Site` (its body is now `RegionSite.Parent`):

```fsharp
    /// <summary>The site of a region between <c>regionStart</c> and <c>regionEnd</c>, placed in the mount's range.</summary>
    member private this.Site(regionStart: Node, regionEnd: Node, placement: RegionPlacement) : RegionSite =
        let site = RegionSite (this, regionStart, regionEnd, placement, report)

        this.PlaceRegion (
            regionStart,
            regionEnd,
            placement,
            (fun () -> site.Validate ()),
            fun () -> Row.Relocate site.LaidOut
        )

        site
```

Replace `ListRegion` (Task 7) with:

```fsharp
    /// <summary>
    /// Mounts one row per keyed item between the region's markers, on a core projection that creates new rows before it
    /// retires removed ones. Template rows (<c>rows</c>) are cloned into the region; authored rows (<c>render</c>) are
    /// nested mounts.
    /// </summary>
    member private this.ListRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            read: unit -> (obj * obj) array,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        let site = this.Site (regionStart, regionEnd, placement)
        let mutable projection: Projection<obj, Row> = Unchecked.defaultof<_>

        let buildRow (item: unit -> obj * obj) : Row =
            let mutable row: Row = null

            // Runs after the row's own cleanups.
            Api.onCleanup (fun () -> site.ReleaseRow row)

            let current () = snd (item ())

            row <-
                if isNull rows then
                    this.MountRow (current, render)
                else
                    site.Builder.Build (rows, current)

            row

        let reconcile () =
            if site.Live () then
                let parent = ref Unchecked.defaultof<Node>
                let keys = ref Unchecked.defaultof<obj array>

                let recorded, failure =
                    MountLifetime.Capture (
                        graph,
                        fun () ->
                            parent.Value <- site.Parent ()
                            Row.Validate (regionStart, regionEnd, site.LaidOut, parent.Value)
                            keys.Value <- projection.Keys
                    )

                site.Settle (
                    recorded,
                    failure,
                    fun recorded ->
                        let layoutFailure =
                            try
                                Api.untrack (fun () ->
                                    let next = keys.Value |> Array.map (fun key -> projection.Get key)

                                    Row.Layout (
                                        (fun () -> site.Live ()),
                                        site.LaidOut,
                                        site.Builder.Staging,
                                        parent.Value,
                                        regionEnd,
                                        next
                                    ))

                                None
                            with error ->
                                Some error

                        match layoutFailure with
                        | Some error ->
                            root.Dispose ()
                            site.Errors.Fail (error, recorded @ [ error ])
                        | None -> site.Errors.Report recorded
                )

        site.Own (fun () ->
            projection <-
                Api.createProjectionOnce
                    ProjectionOptions.CreateFirst
                    (fun (item: obj * obj) -> fst item)
                    buildRow
                    (fun () -> read () :> seq<_>)

            Api.untrack (fun () -> Api.createEffect reconcile))
```

In `MountState`, after `ListRegion`:

```fsharp
    /// <summary>
    /// Mounts the arm selected by the region's first item between its markers. An arm stays while its key is unchanged
    /// under <c>=</c> and receives the item's new value; another key disposes the arm, its cleanups and its nodes, before
    /// the next arm is set up.
    /// </summary>
    member private this.ArmRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            read: unit -> (obj * obj) array,
            render: (unit -> obj) -> Backend -> unit,
            rows: Func<(unit -> obj), TemplateDom.MountServices, unit>
        ) =
        let site = this.Site (regionStart, regionEnd, placement)
        let mutable items: Memo<(obj * obj) array> = Unchecked.defaultof<_>
        let mutable selection: Memo<obj> = Unchecked.defaultof<_>
        let mutable armOwner: Owner = Unchecked.defaultof<_>
        let mutable armItem: Signal<obj> = Unchecked.defaultof<_>
        let mutable armKey: obj = null

        let disposeArm () =
            if not (isNull (box armOwner)) then
                let owner = armOwner
                armOwner <- Unchecked.defaultof<_>
                armItem <- Unchecked.defaultof<_>
                armKey <- null
                site.LaidOut.Clear ()
                Api.batch (fun () -> owner.Dispose ())

        let mountArm (selected: obj) =
            if not (isNull selected) then
                let key, value = unbox<obj * obj> selected
                let item = Api.createSignal value
                let mutable created: Owner = Unchecked.defaultof<_>
                let mutable row: Row = null

                try
                    Api.runWithOwner site.Owner (fun () ->
                        Api.createRoot (fun owner ->
                            created <- owner
                            root.Adopt owner

                            // Runs after the arm's own cleanups.
                            Api.onCleanup (fun () -> site.ReleaseRow row)

                            let current () = item.Value

                            row <-
                                Api.untrack (fun () ->
                                    if isNull rows then
                                        this.MountRow (current, render)
                                    else
                                        site.Builder.Build (rows, current))))
                with _ ->
                    if not (isNull (box created)) then
                        created.Dispose ()

                    reraise ()

                Row.MoveBefore (row, site.Parent (), regionEnd)

                if not (isNull (box row.Mount)) then
                    row.Mount.Relocate ()

                site.LaidOut.Add row
                armOwner <- created
                armItem <- item
                armKey <- key

        let reconcile () =
            if site.Live () then
                let selected = selection.Value

                let recorded, failure =
                    MountLifetime.Capture (
                        graph,
                        fun () ->
                            Api.untrack (fun () ->
                                site.Validate ()
                                disposeArm ()
                                mountArm selected)
                    )

                site.Settle (recorded, failure, (fun recorded -> site.Errors.Report recorded))

        let updatePayload () =
            let current = items.Value

            Api.untrack (fun () ->
                if current.Length > 0 && not (isNull (box armItem)) then
                    let key, value = current[0]

                    if key = armKey then
                        armItem.Value <- value)

        site.Own (fun () ->
            items <- Api.createMemo (fun _ -> read ())

            selection <-
                Api.createMemoWithComparer sameArm (fun _ ->
                    let current = items.Value
                    if current.Length = 0 then null else box current[0])

            Api.untrack (fun () -> Api.createEffect reconcile)
            Api.untrack (fun () -> Api.createEffect updatePayload))

    /// <summary>Mounts the view read once from the region's single item between its markers.</summary>
    member private this.HoleRegion
        (
            regionStart: Node,
            regionEnd: Node,
            placement: RegionPlacement,
            read: unit -> (obj * obj) array,
            render: (unit -> obj) -> Backend -> unit
        ) =
        let site = this.Site (regionStart, regionEnd, placement)
        let items = read ()
        let view = snd items[0]
        let row = this.MountRow ((fun () -> view), render)
        Row.MoveBefore (row, site.Parent (), regionEnd)
        row.Mount.Relocate ()
        site.LaidOut.Add row
```

`RegionOf`:

```fsharp
        | TemplateDom.SelectedArm -> this.ArmRegion (start, finish, placement, read, render, rows)
        | TemplateDom.ViewHole -> this.HoleRegion (start, finish, placement, read, render)
```

- [ ] **Step 4: Delete the old reconciler**

Delete `KeyedRegion`, `KeyedEntry`, `KeyedDisposed`, `KeyedSetupFailed` and every helper used only by them (`moveToOrder`, `createEntry`, `reconcileItems`, `reportRow`). Confirm with `rtk grep -n "Keyed\(Region\|Entry\|Disposed\|SetupFailed\)" experiments/Ranvier.View.Foundation`: no matches.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `rtk npm run test:dom`
Expected: PASS, 320 tests, including every `matchWith`, `branch` and view-hole check in `TemplateChecks.fs`.

Run: `rtk npm run test:browser`. Expected: PASS, 23 tests.
Run the emitted-JavaScript grep from Task 5 Step 1. Expected: each count at most its baseline.

- [ ] **Step 6: Format and commit**

```bash
rtk dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/RangeChecks.fs
rtk git add experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/RangeChecks.fs
rtk git commit -m "feat(view): switch arms on a core memo with a root owner per arm and mount view holes once"
```

---

### Task 9: Branch gate

**Files:**
- Modify: whatever the checks below name.

**Interfaces:**
- Consumes: everything above.
- Produces: a branch on which every gate of Global Constraints passes.

- [ ] **Step 1: Strip review comments**

Run the comment-hygiene skill's `review-comments.ps1` over the branch's changed files in its listing mode, then in strip mode.
Expected: no `FOR-REVIEW` comment remains.

- [ ] **Step 2: Check emitted helpers**

Run the emitted-JavaScript grep from Task 5 Step 1 and record the counts beside the baseline in the task report.
Expected: no file exceeds its baseline.

- [ ] **Step 3: Run every gate unfiltered**

```bash
rtk dotnet test tests/Ranvier.Tests -c Release -v q
rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release
rtk npm test
rtk proxy dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj --configuration Release
```

From `examples/Fable.Ranvier.Playground`: `rtk npm run test:dom` and `rtk npm run test:browser`.
Expected: the core passes with 13 more tests per framework than Task 1 Step 0, `test:dom` passes 320 and `test:browser` passes 23, and every RepresentationChecks group prints PASS.

- [ ] **Step 4: Commit any fixes**

```bash
rtk git add -A experiments src tests fable examples/Fable.Ranvier.Playground/tests
rtk git commit -m "chore(view): strip review comments and pass every gate for keyed rows on the core"
```

Skip the commit when nothing changed.

---

### Task 10: Measurement

**Files:**
- Create (on the PoC branch): `docs/research/2026-10-14-keyed-rows-on-core.md`; `docs/research/framework-performance/investigation/keyed-rows-on-core/{counters,heap,timings,timings-ab-direct,timings-ab-swapped,timings-solid,core,bundle}.json`
- Throwaway branch `measure/keyed-rows-on-core` (never merged): harness files and variant switches

**Interfaces:**
- Consumes: the branch after Task 9.
- Produces: raw JSON and a write-up. Nothing in production code.

Variants:

| | Core | Rows |
|---|---|---|
| **A** | option code compiled out (`RANVIER_ROWS_BASELINE`) | `createProjectionWith`, view wraps `buildRow` as `fun item -> let row = buildRow item in fun () -> row` |
| **B** | options compiled in | `createProjectionWithOptions ProjectionOptions.None`, same wrapper |
| **C1** | options compiled in | `createProjectionWithOptions ProjectionOptions.CreateFirst`, same wrapper |
| **C2** | options compiled in | `createProjectionOnce ProjectionOptions.None` |
| **C3** | options compiled in | `createProjectionOnce ProjectionOptions.CreateFirst` (production) |

- [ ] **Step 1: Create the throwaway branch and port the harness**

```bash
rtk git switch -c measure/keyed-rows-on-core
rtk git checkout research/keyed-row-cost -- bench/Ranvier.RowsBench examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-rows.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-page.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-solid.jsx examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-heap.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-counters.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/rows-bundle.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/vite.config.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/page.mjs examples/Fable.Ranvier.Playground/tests/KeyedRowsPerf.fs examples/Fable.Ranvier.Playground/.gitignore
```

Then make these edits on the throwaway branch only:
- `src/Ranvier/Ranvier.fsproj` and `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`: copy from `research/keyed-row-cost` the `RanvierRowsBaseline` property group (its `PROTOTYPE` comment line, then `<PropertyGroup Condition="'$(RanvierRowsBaseline)' == 'true'">` defining `RANVIER_ROWS_BASELINE`), placed after the `RanvierCounters` property group. In `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, also add `<Compile Include="KeyedRowsPerf.fs" />` directly after `<Compile Include="FrameworkPerf.fs" />`, as on `research/keyed-row-cost`.
- `src/Ranvier`: wrap in `#if !RANVIER_ROWS_BASELINE` the `CreateFirst` branch of `ApplyDiff`, the `CreateFirst` tests in `FactoryFailure` and `CreateAdded`, `Discard`, `OnceRow`, `OnceValue`, `UseOnce`, `CreateOnce`, `RequireRowMemos` and its call sites, `createProjectionWithOptions`, `createProjectionOnce`, and the null tests of `entry.Row` added in Tasks 2 and 3. `ItemRow` stays `Sealed` under the define.
- `Mounting.fs`: `let mutable internal rowForm = 4` (0–4 are A, B, C1, C2, C3), read when a list region is created; `ListRegion` builds the projection matching the table above. Under `RANVIER_ROWS_BASELINE`, forms 1–4 raise `invalidOp`.
- `KeyedRowsPerf.fs`: `mountKeyed (form: int)` sets `Mounting.rowForm` around the mount, in place of `withKeyedPrototype`. `coreBuild` stays.
- `keyed-page.mjs`: adapters `rows-A` (baseline import, form 0), `rows-B`, `rows-C1`, `rows-C2` and `rows-C3` (forms 1–4), and `solid`. Arm adapters `ranvier` (production) and `solid`.
- `bench/Ranvier.RowsBench/Program.fs`: the variants of the table, selected by the same form number.

- [ ] **Step 2: Point the harness at the new output directory**

In `keyed-rows.mjs`, `keyed-counters.mjs`, `keyed-heap.mjs`, `rows-bundle.mjs` and `bench/Ranvier.RowsBench/run.mjs`, change the output directory from `investigation/keyed-rows/` to `investigation/keyed-rows-on-core/`. Every script names its file through `--label`.

- [ ] **Step 3: Build, then op counters**

Run (from `examples/Fable.Ranvier.Playground`): `rtk node benchmarks/frameworks/keyed-rows.mjs --build --ops=create1k --modes=solid --processes=1 --rounds=1 --label=build-check`, then delete that run's JSON. The run compiles `generated-tests` and `generated-tests-baseline`.

Run: `rtk node benchmarks/frameworks/keyed-counters.mjs --build --label=counters`
Expected: B equals A in every tally. C1 equals B, because no measured operation fails. C2 and C3 have zero `FactoryRowsCreated` and one `OnceRowsCreated` per created row.

- [ ] **Step 4: Retained heap by constructor**

Run: `rtk node benchmarks/frameworks/keyed-heap.mjs --counts=1000,10000 --processes=2 --label=heap`
Expected: A and B identical per constructor. C2 and C3 have no `Memo$1` per row.

- [ ] **Step 5: Paired timings, core options**

Run: `rtk node benchmarks/frameworks/keyed-rows.mjs --shuffle --modes=rows-A,rows-B,rows-C1,rows-C2,rows-C3,solid --processes=5 --rounds=21 --label=timings`

Then run the bundle-position control:
- `rtk node benchmarks/frameworks/keyed-rows.mjs --shuffle --modes=rows-A,rows-B --processes=5 --rounds=31 --label=timings-ab-direct`
- The same command with `RANVIER_ROWS_SWAPPED=true` set in the environment and `--label=timings-ab-swapped`.

- [ ] **Step 6: Production against Solid 2, pure core and bundle**

Run: `rtk node benchmarks/frameworks/keyed-rows.mjs --shuffle --modes=rows-C3,solid --processes=5 --rounds=21 --label=timings-solid`. This covers the arm operations through the `ranvier` arm adapter.

From the repository root: `rtk node bench/Ranvier.RowsBench/run.mjs --build --rounds=21 --warmup=10 --samples=21 --runtimes=net,node --label=core`.

From `examples/Fable.Ranvier.Playground`: `rtk node benchmarks/frameworks/rows-bundle.mjs --label=bundle`.

- [ ] **Step 7: Write the report on the PoC branch**

```bash
rtk git add -A
rtk git commit -m "test(bench): measure keyed rows on the core (throwaway)"
rtk git switch poc/fable-ranvier-dom
rtk git checkout measure/keyed-rows-on-core -- docs/research/framework-performance/investigation/keyed-rows-on-core
```

`docs/research/2026-10-14-keyed-rows-on-core.md` opens with machine, Chromium version, Fable version, date and commit. It then has these sections:
- **Core options off and on.** Counter, heap, timing (with the bundle-position control), pure-core and bundle tables for A/B/C1/C2/C3.
- **Production against Solid 2.** Median ratios for every operation, against the spec's targets: `create1k`, `append1k` and `clear1k` at or below 2.0×; `swap1k` and `remove1k` at or below 3×; `clear10k` / `clear1k` within 12×; retained heap at or below 1,300 bytes per row at 1,000 rows; `armsSwitch1k` at or below 2×. Each row is marked met or not met.
- **Unexplained results.** Each one stated as measured.

- [ ] **Step 8: Commit and drop the throwaway branch**

```bash
rtk git add docs/research/2026-10-14-keyed-rows-on-core.md docs/research/framework-performance/investigation/keyed-rows-on-core
rtk git commit -m "docs(research): measure create-first and memo-free rows off and on and the keyed view against Solid 2"
rtk git branch -D measure/keyed-rows-on-core
```

---

### Task 11: Documentation follow-ups

**Files:**
- Modify: `docs/research/2026-10-14-component-templates.md` (section "Dynamic structure"), `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` (status paragraph; Task 6 items)

**Interfaces:**
- Consumes: Tasks 7, 8 and 10.
- Produces: no code.

- [ ] **Step 1: Mark the replaced design**

In `docs/research/2026-10-14-component-templates.md`, section `## Dynamic structure`, add this note as its own paragraph directly above the paragraph that starts "Every region kind (view hole, `matchWith`, `branch` and `each`) mounts through the keyed path.":

```markdown
> Replaced on 2026-10-14 by [keyed rows and arms on core primitives](../superpowers/specs/2026-10-14-keyed-rows-on-core-design.md). Lists now run on a create-first core projection, arms on a core memo with a root owner per arm, and view holes mount once. The paragraph below describes the earlier keyed path.
```

- [ ] **Step 2: Refresh the framework plan**

In the framework plan's status paragraph, replace "Open: keyed-row and list performance," with "Keyed lists and arms run on core projections, memos and owners ([spec](../specs/2026-10-14-keyed-rows-on-core-design.md), [measurements](../../research/2026-10-14-keyed-rows-on-core.md)). Open:". In Task 6:
- Mark "Implement snapshot keyed lists, …" done. Name "zero-node rows keep their place through their marker" and "list updates match a reference order and keep node identities" in `TemplateChecks.fs` as the evidence for empty rows, prepend, reverse and random reorder.
- Mark "Test state and listener identity through moves, and focus/selection in real browsers" done, naming "template rows keep focus and selection across list moves in Chromium" in `range.spec.mjs`.

- [ ] **Step 3: Commit**

```bash
rtk git add docs/research/2026-10-14-component-templates.md docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md
rtk git commit -m "docs(view): mark the keyed path as replaced and record keyed rows on the core in the framework plan"
```
