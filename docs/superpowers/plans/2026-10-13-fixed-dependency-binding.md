# Fixed-dependency binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a core computation that writes one fixed source's value to a target on every change, without dependency tracking, and ship it only if a benchmark shows it beats an equivalent effect.

**Architecture:** `Binding<'Target, 'T>` implements the internal `IComputation`, `IScheduled` and `IOwned` interfaces the way `Effect` does, but observes exactly one `ISource` through `AddObserver`, as `RowWatch` and `ValueRow` already do. It is scheduled through `graph.Schedule`, resolves a `Check` with a single `UpdateIfNecessary`, and reads its source untracked. Task 2 is a go/no-go gate: if the benchmark shows no gain, the work stops there and nothing public ships.

**Tech Stack:** F# (.NET 10 and net8.0, Fable 5), Expecto, BenchmarkDotNet.

**Spec:** `docs/superpowers/specs/2026-10-13-component-templates-design.md`, section "Direct bindings". The spec lives on `poc/fable-ranvier-dom`; this work happens on `master`.

## Global Constraints

- Work in a new worktree branched from `origin/master`: `git worktree add ../Ranvier-fixed-binding -b feat/core-fixed-binding origin/master`. This is a core change merged to `master` in its own PR, independent of the PoC.
- Every commit message is one conventional-commit summary line, with no body and no AI co-author trailer.
- Prefix shell commands with `rtk`.
- Comments follow `.claude/rules/comments.md`; `///` docs follow the `fsharp-xml-docs` skill. Run `dotnet fantomas` on changed F# files; the pre-commit hook rejects unformatted files and `FOR-REVIEW` comments.
- Construction must stay Fable-compatible: no secondary constructor with a self-identifier, and no interface type tests (Fable compiles them to false).
- Zero Release cost for tracing: every `Tracer` call is a `[<Conditional("RANVIER_TRACE")>]` hook; `tools/verify-trace.fsx` gate 1 must pass.
- A binding's write must not create reactive nodes; the docs state this as a contract.

## Review Focus

- **A memo that recomputes to an equal value.** Expect no write: the binding receives `MarkCheck`, `UpdateIfNecessary` leaves it `Check`, and it resolves `Clean`. Pinned in Task 1 Step 1 ("a memo recomputing to an equal value does not write").
- **A binding created inside an effect's body.** Expect the enclosing effect not to depend on anything the write reads, and the binding to be disposed when that effect re-runs. Pinned in Task 1 ("a binding created in an effect body is disposed when the effect re-runs").
- **A binding created under an already-disposed owner.** Expect no write and no lingering observer edge. Pinned in Task 1 ("a binding under a disposed owner never writes").
- **A write that throws.** Expect the error recorded on the binding, other queued work still running, and the next change writing again. Pinned in Task 1.
- **Several writes in one batch.** Expect exactly one write, with the final value. Pinned in Task 1.

---

### Task 1: Internal `Binding` with correctness tests

**Files:**
- Modify: `src/Ranvier/Core.fs` (add `ReadUntracked` to `Memo`, next to `TryValue` around line 3100)
- Create: `src/Ranvier/Bindings.fs`
- Modify: `src/Ranvier/Ranvier.fsproj` (compile `Bindings.fs` after `Core.fs`)
- Create: `tests/Ranvier.Tests/Bindings.fs`
- Modify: `tests/Ranvier.Tests/TestFiles.props` (after `Effects.fs`), `fable/Ranvier.Tests.Fable/Main.fs` (after the `Effects.fs` line)

**Interfaces:**
- Produces: `Binding<'Target, 'T>` (public type, internal constructors) with `static member internal OfSignal(graph: Graph, source: Signal<'T>, target: 'Target, write: 'Target -> 'T -> unit) : Binding<'Target, 'T>`, `static member internal OfMemo(graph: Graph, source: Memo<'T>, target: 'Target, write: 'Target -> 'T -> unit) : Binding<'Target, 'T>`, `member Status: Status`, `member Runs: int`, `member Error: exn`, `member Dispose: unit -> unit`.
- Produces: `Memo<'T>.ReadUntracked: unit -> Reading<'T>` (internal).

- [ ] **Step 1: Write the failing tests**

Create `tests/Ranvier.Tests/Bindings.fs`:

```fsharp
module Ranvier.Tests.Bindings

open System
open Expecto
open Ranvier
open Ranvier.Tests.Support

let private record (log: ResizeArray<'T>) = fun (_: obj) (value: 'T) -> log.Add value

[<Tests>]
let tests =
    testList
        "Bindings"
        [
            test "a signal binding writes the current value on creation" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                Binding<obj, int>.OfSignal (g, s, null, record log) |> ignore
                Expect.sequenceEqual log [ 1 ] "initial write"
            }

            test "a signal write writes again" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                Binding<obj, int>.OfSignal (g, s, null, record log) |> ignore
                s.Value <- 2
                Expect.sequenceEqual log [ 1; 2 ] "one write per change"
            }

            test "writes in one batch write once with the final value" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                Binding<obj, int>.OfSignal (g, s, null, record log) |> ignore

                g.Batch (fun () ->
                    s.Value <- 2
                    s.Value <- 3)

                Expect.sequenceEqual log [ 1; 3 ] "batched"
            }

            test "a memo binding writes when the memo moves" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, fun _ -> s.Value * 10)
                let log = ResizeArray ()
                Binding<obj, int>.OfMemo (g, m, null, record log) |> ignore
                s.Value <- 2
                Expect.sequenceEqual log [ 10; 20 ] "memo values"
            }

            test "a memo recomputing to an equal value does not write" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, fun _ -> s.Value % 2)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfMemo (g, m, null, record log)
                s.Value <- 3
                Expect.sequenceEqual log [ 1 ] "no second write"
                Expect.equal b.Runs 1 "resolved Clean without running"
            }

            test "a pending memo leaves the target and writes once settled" {
                let g = new Graph ()
                let a = AsyncSource<int> (g)
                let m = Make.Memo (g, fun _ -> a.Value + 1)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfMemo (g, m, null, record log)
                Expect.isEmpty log "no write while pending"
                Expect.equal b.Status Status.Pending "pending"
                a.Settle 4
                Expect.sequenceEqual log [ 5 ] "written after settle"
                Expect.equal b.Status Status.None "settled"
            }

            test "a failed memo leaves the target and recovers" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let m = Make.Memo (g, fun _ -> if s.Value = 2 then failwith "boom" else s.Value)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfMemo (g, m, null, record log)
                s.Value <- 2
                Expect.equal b.Status Status.Error "failed"
                s.Value <- 3
                Expect.sequenceEqual log [ 1; 3 ] "the failure wrote nothing"
                Expect.equal b.Status Status.None "recovered"
            }

            test "a throwing write is recorded and does not strand other work" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfSignal (g, s, null, fun _ v -> if v = 2 then failwith "write" else log.Add v)
                let e = new Effect (g, fun () -> log.Add (s.Value * 100))
                s.Value <- 2
                Expect.equal b.Status Status.Error "recorded"
                Expect.equal b.Error.Message "write" "the write's exception"
                Expect.contains log 200 "the effect still ran"
                s.Value <- 3
                Expect.contains log 3 "writes again"
                e.Dispose ()
            }

            test "a binding is disposed with its owner" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()

                let owner =
                    g.CreateRoot (fun owner ->
                        Binding<obj, int>.OfSignal (g, s, null, record log) |> ignore
                        owner)

                owner.Dispose ()
                s.Value <- 2
                Expect.sequenceEqual log [ 1 ] "no write after disposal"
            }

            test "disposing a binding stops it" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let b = Binding<obj, int>.OfSignal (g, s, null, record log)
                b.Dispose ()
                b.Dispose ()
                s.Value <- 2
                Expect.sequenceEqual log [ 1 ] "stopped"
            }

            test "a binding under a disposed owner never writes" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let owner = g.CreateRoot (fun owner -> owner)
                owner.Dispose ()
                runWithOwner owner (fun () -> Binding<obj, int>.OfSignal (g, s, null, record log) |> ignore)
                s.Value <- 2
                Expect.isEmpty log "never written"
            }

            test "a binding created in an effect body is disposed when the effect re-runs" {
                let g = new Graph ()
                let trigger = Signal (g, 0)
                let s = Signal (g, 1)
                let log = ResizeArray ()
                let mutable effectRuns = 0

                let e =
                    new Effect (
                        g,
                        fun () ->
                            effectRuns <- effectRuns + 1
                            trigger.Value |> ignore
                            Binding<obj, int>.OfSignal (g, s, null, record log) |> ignore
                    )

                s.Value <- 2
                Expect.equal effectRuns 1 "the write is not a dependency of the effect"
                trigger.Value <- 1
                log.Clear ()
                s.Value <- 3
                Expect.sequenceEqual log [ 3 ] "only the binding from the latest run writes"
                e.Dispose ()
            }

            test "the target is passed to the write" {
                let g = new Graph ()
                let s = Signal (g, "a")
                let target = ResizeArray<string> ()
                Binding<ResizeArray<string>, string>.OfSignal (g, s, target, fun t v -> t.Add v) |> ignore
                s.Value <- "b"
                Expect.sequenceEqual target [ "a"; "b" ] "target received the writes"
            }
        ]
```

Register the file: in `tests/Ranvier.Tests/TestFiles.props`, after the `Effects.fs` line add

```xml
        <Compile Include="$(MSBuildThisFileDirectory)Bindings.fs" Link="Bindings.fs" />
```

and in `fable/Ranvier.Tests.Fable/Main.fs`, after the `Effects.fs` line add

```fsharp
            testList "Bindings.fs" [ Ranvier.Tests.Bindings.tests ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --filter-test-list "Bindings"`
Expected: build failure, `The type 'Binding' is not defined`.

- [ ] **Step 3: Add the untracked memo read**

In `src/Ranvier/Core.fs`, directly after `Memo.TryValue`, add:

```fsharp
    /// <summary>
    /// Non-throwing read that brings the memo up to date without recording a dependency.
    /// </summary>
    member internal this.ReadUntracked() : Reading<'T> =
        this.EnsureCurrent ()

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value
```

- [ ] **Step 4: Implement `Binding`**

Create `src/Ranvier/Bindings.fs`:

```fsharp
namespace Ranvier

open System

/// <summary>
/// Writes one source's value to a target whenever the source changes, without dependency tracking.
/// </summary>
/// <remarks>
/// The source is fixed at creation. Writes run in the flush, untracked, and must not create reactive nodes. A pending
/// or failed memo source leaves the target unchanged. Disposed with the owner current at creation, or by
/// <c>Dispose</c>.
/// </remarks>
[<Sealed>]
type Binding<'Target, 'T>
    private
    (graph: Graph, source: ISource, signal: Signal<'T>, memo: Memo<'T>, target: 'Target, write: 'Target -> 'T -> unit) =
    let id = graph.NextId ()
    let mutable freshness = Freshness.Clean
    let mutable status = Status.Uninitialized
    let mutable failure: Failure = null
    let mutable queued = false
    let mutable disposed = false
    let mutable link: OwnerLink = null
    let mutable runs = 0

    static member internal OfSignal(graph: Graph, source: Signal<'T>, target: 'Target, write: 'Target -> 'T -> unit) =
        let binding =
            new Binding<'Target, 'T> (graph, source :> ISource, source, Unchecked.defaultof<_>, target, write)

        binding.Start ()
        binding

    static member internal OfMemo(graph: Graph, source: Memo<'T>, target: 'Target, write: 'Target -> 'T -> unit) =
        let binding =
            new Binding<'Target, 'T> (graph, source :> ISource, Unchecked.defaultof<_>, source, target, write)

        binding.Start ()
        binding

    member private this.Start() =
        source.AddObserver (this :> IComputation)
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.EffectNew (graph, id, link.Owner)
        (this :> IComputation).MarkDirty ()
        graph.RequestFlush ()

    member private this.Run() =
        runs <- runs + 1
        Tracer.RunStart (graph, id, runs)

        try
            if isNull (box memo) then
                status <- Status.None
                failure <- null
                write target signal.Peek
            else
                match memo.ReadUntracked () with
                | Ready value ->
                    status <- Status.None
                    failure <- null
                    write target value
                | Pending -> status <- Status.Pending
                | Failed _ ->
                    status <- Status.Error
                    failure <- memo.Failure
        with ex ->
            failure <- graph.FailureOf (ex, this, null)
            status <- Status.Error

        Tracer.RunEnd (graph, id, status)

    /// <summary>The state after the latest write attempt.</summary>
    member _.Status = status

    /// <summary>The number of write attempts.</summary>
    member _.Runs = runs

    /// <summary>The exception from the latest failed write or failed source, or null.</summary>
    member _.Error: exn = Failure.ErrorOf failure

    /// <summary>Stops the binding and releases its source edge. Idempotent.</summary>
    member this.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    Tracer.NodeDispose (graph, id)

                    if not (isNull link) then
                        link.Detach ()
                        link <- null

                    source.RemoveObserver (this :> IComputation)
        )

    interface IDisposable with
        member this.Dispose() = this.Dispose ()

    interface IOwned with
        member this.Release() =
            link <- null
            this.Dispose ()

    interface INode with
        member _.Id = id
        member _.Status = status

    interface IComputation with
        member _.AddSource _ = ()

        member this.MarkDirty() =
            if not disposed then
                freshness <- Freshness.Dirty

                if not queued then
                    queued <- true
                    graph.Schedule (this :> IScheduled)

        member this.MarkCheck() =
            if not disposed then
                if freshness = Freshness.Clean then
                    freshness <- Freshness.Check

                if not queued then
                    queued <- true
                    graph.Schedule (this :> IScheduled)

    interface IScheduled with
        member this.Execute() =
            if disposed then
                queued <- false
            else
                // queued stays set during the walk: a source recomputing here marks this binding Dirty again.
                if freshness = Freshness.Check then
                    Tracer.CheckStart (graph, id)
                    source.UpdateIfNecessary ()
                    Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                let shouldRun = freshness = Freshness.Dirty
                queued <- false
                freshness <- Freshness.Clean

                if shouldRun then
                    this.Run ()
```

Add to `src/Ranvier/Ranvier.fsproj`, directly after `<Compile Include="Core.fs" />`:

```xml
        <Compile Include="Bindings.fs" />
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --filter-test-list "Bindings"`
Expected: all 13 Bindings tests pass.

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --summary`
Expected: every test passes (1,022 before this task plus 13).

- [ ] **Step 6: Run the Fable suite**

Run: `rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release` then `RanvierTrace=true rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release` then `rtk node fable/Ranvier.Tests.Fable/Report.mjs`
Expected: exit code 0; `docs/.ai/fable-compat.md` lists the 13 Bindings tests as passing in every build and delivery.

- [ ] **Step 7: Format and commit**

```bash
rtk dotnet fantomas src/Ranvier/Bindings.fs src/Ranvier/Core.fs tests/Ranvier.Tests/Bindings.fs
rtk git add src/Ranvier/Bindings.fs src/Ranvier/Core.fs src/Ranvier/Ranvier.fsproj tests/Ranvier.Tests/Bindings.fs tests/Ranvier.Tests/TestFiles.props fable/Ranvier.Tests.Fable/Main.fs docs/.ai/fable-compat.md
rtk git commit -m "feat(core): add an internal fixed-dependency binding"
```

---

### Task 2: Benchmark gate

**Files:**
- Create: `bench/Ranvier.Benchmarks/Bindings.fs`
- Modify: `bench/Ranvier.Benchmarks/Ranvier.Benchmarks.fsproj` (compile `Bindings.fs` after `Effects.fs`)
- Create: `docs/.ai/fixed-binding-benchmark.md`

**Interfaces:**
- Consumes: `Binding<'Target, 'T>.OfSignal`, `Binding<'Target, 'T>.OfMemo` from Task 1 (visible to `Ranvier.Benchmarks` through `InternalsVisibleTo`, `Types.fs:438-443`).

- [ ] **Step 1: Write the benchmarks**

Create `bench/Ranvier.Benchmarks/Bindings.fs`:

```fsharp
module Ranvier.Benchmarks.Bindings

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// A binding against the effect it replaces: one consumer of one source, writing that source's value.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Binding">]
type BindingLifecycleBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)
    let memo = Memo (graph, fun _ -> source.Value + 1)
    let mutable sink = 0
    let write = fun (_: obj) (value: int) -> sink <- value

    [<Params(1, 64, 1024)>]
    member val Nodes = 1 with get, set

    /// <summary>Create and dispose effects that each write one signal's value. Divide by <c>Nodes</c>.</summary>
    [<Benchmark(Baseline = true)>]
    member this.SignalEffects() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    new Effect (graph, fun () -> sink <- source.Value) |> ignore

                owner)

        owner.Dispose ()

    /// <summary>Create and dispose bindings to one signal. Divide by <c>Nodes</c>.</summary>
    [<Benchmark>]
    member this.SignalBindings() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    Binding<obj, int>.OfSignal (graph, source, null, write) |> ignore

                owner)

        owner.Dispose ()

    /// <summary>Create and dispose effects that each write one memo's value. Divide by <c>Nodes</c>.</summary>
    [<Benchmark>]
    member this.MemoEffects() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    new Effect (graph, fun () -> sink <- memo.Value) |> ignore

                owner)

        owner.Dispose ()

    /// <summary>Create and dispose bindings to one memo. Divide by <c>Nodes</c>.</summary>
    [<Benchmark>]
    member this.MemoBindings() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    Binding<obj, int>.OfMemo (graph, memo, null, write) |> ignore

                owner)

        owner.Dispose ()

/// <summary>
/// One write to a source observed by <c>Nodes</c> consumers, flushed. <c>Kind</c> chooses effects or bindings.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Binding">]
type BindingUpdateBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 0)
    let memo = Memo (graph, fun _ -> source.Value + 1)
    let mutable sink = 0
    let mutable counter = 0
    let write = fun (_: obj) (value: int) -> sink <- value

    [<Params(1, 64, 1024)>]
    member val Nodes = 1 with get, set

    [<Params("signal-effect", "signal-binding", "memo-effect", "memo-binding")>]
    member val Kind = "signal-effect" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        for _ in 1 .. this.Nodes do
            match this.Kind with
            | "signal-effect" -> new Effect (graph, fun () -> sink <- source.Value) |> ignore
            | "signal-binding" -> Binding<obj, int>.OfSignal (graph, source, null, write) |> ignore
            | "memo-effect" -> new Effect (graph, fun () -> sink <- memo.Value) |> ignore
            | _ -> Binding<obj, int>.OfMemo (graph, memo, null, write) |> ignore

    /// <summary>Write, and let the flush run every consumer the write invalidated.</summary>
    [<Benchmark>]
    member _.WriteAndFlush() =
        counter <- counter + 1
        source.Value <- counter
```

Add to `bench/Ranvier.Benchmarks/Ranvier.Benchmarks.fsproj`, directly after the `Effects.fs` compile line:

```xml
        <Compile Include="Bindings.fs" />
```

- [ ] **Step 2: Run the benchmarks**

Run: `rtk dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Binding`
Expected: completes; results under `docs/.ai/benchmarks/results/Ranvier.Benchmarks.Bindings.*-report-github.md`.

- [ ] **Step 3: Apply the gate**

The binding passes when both hold:
- In `BindingLifecycleBenchmarks` at `Nodes` 64 and 1024, `SignalBindings` has a mean at most 0.85× `SignalEffects`, or allocates at most 0.75× its bytes; and `MemoBindings` meets the same bound against `MemoEffects`.
- In `BindingUpdateBenchmarks`, each `-binding` kind's `WriteAndFlush` mean is at most 1.05× its `-effect` kind at every `Nodes`.

Write `docs/.ai/fixed-binding-benchmark.md` with the four lifecycle rows and eight update rows at each `Nodes` (mean, allocated, ratio to the effect), the machine and runtime from the report header, and the verdict sentence: "The binding passes the gate" or "The binding fails the gate: <the failing bound>".

- [ ] **Step 4: Commit the measurement**

```bash
rtk git add bench/Ranvier.Benchmarks/Bindings.fs bench/Ranvier.Benchmarks/Ranvier.Benchmarks.fsproj docs/.ai/fixed-binding-benchmark.md docs/.ai/benchmarks/results
rtk git commit -m "test(bench): compare fixed-dependency bindings with effects"
```

- [ ] **Step 5: Stop on failure**

If the verdict is a failure, stop here and report the note to the user. Do not continue to Task 3. The branch stays unmerged as the record; `T.from` in the templates work remains an alias for a render-effect slot.

---

### Task 3: Public API and contract

**Files:**
- Modify: `src/Ranvier/Api.fs` (after `createEffectOnWithComparer`, around line 317)
- Modify: `docs/content/concepts/contracts.md:215-230` (the table "What each API returns, and when it is disposed")
- Modify: `docs/.ai/public-api-baseline.txt` (regenerated)
- Test: `tests/Ranvier.Tests/Bindings.fs`

**Interfaces:**
- Consumes: `Binding<'Target, 'T>.OfSignal`, `OfMemo` (Task 1).
- Produces: `Api.bindSignal (source: Signal<'T>) (target: 'Target) (write: 'Target -> 'T -> unit) : unit` and `Api.bindMemo (source: Memo<'T>) (target: 'Target) (write: 'Target -> 'T -> unit) : unit`.

- [ ] **Step 1: Write the failing test**

Append to the list in `tests/Ranvier.Tests/Bindings.fs`:

```fsharp
            test "bindSignal and bindMemo bind under the current graph and owner" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 1
                let m = createMemo (fun _ -> s.Value * 2)
                let log = ResizeArray<int> ()

                let owner =
                    createRoot (fun owner ->
                        bindSignal s log (fun t v -> t.Add v)
                        bindMemo m log (fun t v -> t.Add v)
                        owner)

                s.Value <- 2
                owner.Dispose ()
                s.Value <- 3
                Expect.sequenceEqual log [ 1; 2; 2; 4 ] "both bound, both disposed with the owner"
            }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --filter-test-list "Bindings"`
Expected: build failure, `The value or constructor 'bindSignal' is not defined`.

- [ ] **Step 3: Add the API**

In `src/Ranvier/Api.fs`, after `createEffectOnWithComparer`:

```fsharp
    /// <summary>
    /// Writes a signal's value to <c>target</c> now and after every change, without dependency tracking. Disposed
    /// with the enclosing scope.
    /// </summary>
    /// <remarks>
    /// The signal is the only dependency. <c>write</c> runs in the flush, untracked, and must not create reactive
    /// nodes. A throwing <c>write</c> is recorded on the binding and leaves other queued work running.
    /// </remarks>
    let bindSignal (source: Signal<'T>) (target: 'Target) (write: 'Target -> 'T -> unit) =
        Binding<'Target, 'T>.OfSignal (Graph.Current, source, target, write) |> ignore

    /// <summary>
    /// Writes a memo's value to <c>target</c> now and after every change, without dependency tracking. Disposed
    /// with the enclosing scope.
    /// </summary>
    /// <remarks>
    /// The memo is the only dependency; a recomputation to an equal value writes nothing. While the memo is pending
    /// or failed, <c>target</c> keeps its last value. <c>write</c> runs in the flush, untracked, and must not create
    /// reactive nodes.
    /// </remarks>
    let bindMemo (source: Memo<'T>) (target: 'Target) (write: 'Target -> 'T -> unit) =
        Binding<'Target, 'T>.OfMemo (Graph.Current, source, target, write) |> ignore
```

- [ ] **Step 4: Run the tests**

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --summary`
Expected: every test passes.

- [ ] **Step 5: Document the contract**

In `docs/content/concepts/contracts.md`, in the table under "What each API returns, and when it is disposed", after the `createEffect`, `createEffectOn` row add:

```markdown
| `bindSignal`, `bindMemo` | Current owner | With the owner. The source is the only dependency; `write` runs untracked in the flush. |
```

Run: `rtk dotnet fsi tools/verify-trace.fsx --baseline`
Expected: `docs/.ai/public-api-baseline.txt` gains the `Binding` type and the two `Api` members.

- [ ] **Step 6: Commit**

```bash
rtk dotnet fantomas src/Ranvier/Api.fs tests/Ranvier.Tests/Bindings.fs
rtk git add src/Ranvier/Api.fs tests/Ranvier.Tests/Bindings.fs docs/content/concepts/contracts.md docs/.ai/public-api-baseline.txt
rtk git commit -m "feat(core): add bindSignal and bindMemo"
```

---

### Task 4: Tracing node kind

**Files:**
- Modify: `src/Ranvier/TraceEvents.fs:116-127` (enum) and `:232-249` (`nodeKind`)
- Modify: `src/Ranvier/Trace.fs` (a `BindingNew` hook beside `EffectNew`, line 723)
- Modify: `src/Ranvier/TraceModel.fs:1124-1136` (`kindPlural`)
- Modify: `src/Ranvier/Bindings.fs` (`Start` calls `Tracer.BindingNew`)
- Test: `tests/Ranvier.Tests/Tracing.fs`

**Interfaces:**
- Produces: `TraceNodeKind.Binding = 12`, `Tracer.BindingNew(graph: obj, id: int, owner: obj)`.

- [ ] **Step 1: Write the failing test**

Append to the list in `tests/Ranvier.Tests/Tracing.fs`, following the style of the test at line 940 that uses `idsOf TraceNodeKind.Effect g`:

```fsharp
            test "a binding is traced as a binding" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = Signal (g, 1)
                bindSignal s null (fun (_: obj) (_: int) -> ())
                Expect.equal (idsOf TraceNodeKind.Binding g |> Array.length) 1 "one binding node"
            }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -p:RanvierTrace=true -- --filter-test-list "Tracing"`
Expected: build failure, `The field, constructor or member 'Binding' is not defined`.

- [ ] **Step 3: Add the kind and the hook**

In `src/Ranvier/TraceEvents.fs`, add to `TraceNodeKind` after `Timed = 11`:

```fsharp
    | Binding = 12
```

and in `nodeKind`, after the `TraceNodeKind.Timed -> "Timed"` case:

```fsharp
        | TraceNodeKind.Binding -> "Binding"
```

In `src/Ranvier/Trace.fs`, after `EffectNew`:

```fsharp
    [<Conditional("RANVIER_TRACE")>]
    static member BindingNew(graph: obj, id: int, owner: obj) =
#if RANVIER_TRACE
        Tracer.Node (graph, id, TraceNodeKind.Binding, owner)
#else
        ()
#endif
```

Copy the attributes exactly as `EffectNew` carries them.

In `src/Ranvier/TraceModel.fs`, in `kindPlural`, before the `| _ -> "nodes"` case:

```fsharp
        | TraceNodeKind.Binding -> "bindings"
```

In `src/Ranvier/Bindings.fs`, replace `Tracer.EffectNew (graph, id, link.Owner)` with `Tracer.BindingNew (graph, id, link.Owner)`.

- [ ] **Step 4: Run the traced and untraced suites and the trace gate**

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -p:RanvierTrace=true -- --summary`
Expected: every test passes.

Run: `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --summary`
Expected: every test passes.

Run: `rtk dotnet fsi tools/verify-trace.fsx --no-counters`
Expected: exit code 0 (gate 1, zero Release cost, passes).

- [ ] **Step 5: Commit**

```bash
rtk dotnet fantomas src/Ranvier/Trace.fs src/Ranvier/TraceEvents.fs src/Ranvier/TraceModel.fs src/Ranvier/Bindings.fs tests/Ranvier.Tests/Tracing.fs
rtk git add src/Ranvier tests/Ranvier.Tests/Tracing.fs
rtk git commit -m "feat(core): trace fixed-dependency bindings"
```

---

### Task 5: Full verification and pull request

**Files:**
- Modify: `docs/.ai/fable-compat.md` (regenerated)

- [ ] **Step 1: Run every suite**

Run each and confirm the expected result:
- `rtk dotnet run --project tests/Ranvier.Tests -f net10.0 -c Release -- --summary`: all pass.
- `rtk dotnet run --project tests/Ranvier.Tests -f net8.0 -c Release -- --summary`: all pass.
- `rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release`, `RanvierTrace=true rtk dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release`, `rtk node fable/Ranvier.Tests.Fable/Report.mjs`: exit code 0.
- `rtk dotnet fsi tools/verify-trace.fsx --no-counters`: exit code 0.

- [ ] **Step 2: Commit the regenerated report**

```bash
rtk git add docs/.ai/fable-compat.md
rtk git commit -m "docs: regenerate the Fable compatibility report"
```

Skip this step if the report is unchanged.

- [ ] **Step 3: Ask before pushing**

Report the verification results to the user and ask whether to push `feat/core-fixed-binding` and open a pull request to `master`. The pull request title is `feat(core): add fixed-dependency bindings`, and its body summarises the gate results from `docs/.ai/fixed-binding-benchmark.md`.
