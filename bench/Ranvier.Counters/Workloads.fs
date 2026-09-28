/// <summary>
/// Application-shaped workloads for Ranvier alone, compiled into both the .NET counter bench and its Fable harness, so
/// both targets run identical graphs.
/// </summary>
module CounterBench.Workloads

open System
open Ranvier

/// <summary>State built outside the measured region, as the scenarios' <c>Prepared</c>.</summary>
type Workload =
    {
        Run: unit -> unit
        Teardown: unit -> unit
    }

[<Literal>]
let Rows = 1000

[<Literal>]
let DetailNodes = 20

[<Literal>]
let DiamondWidth = 100

[<Literal>]
let DynamicReaders = 100

[<Literal>]
let Widgets = 10

[<Literal>]
let SourcesPerWidget = 10

/// <summary>Written by every reaction, so no reaction's result is provably unused.</summary>
let mutable private sink = 0

#if RANVIER_TRACE
let private created = ResizeArray<Graph>()

/// <summary>The graphs created by <c>newGraph</c> since the last call, oldest first.</summary>
let takeGraphs () : Graph[] =
    let graphs = created.ToArray ()
    created.Clear ()
    graphs

/// <summary>A new graph, recorded for <c>takeGraphs</c>.</summary>
let newGraph () =
    let graph = new Graph ()
    created.Add graph
    graph
#else
let inline newGraph () =
    new Graph ()
#endif

let private teardown (owner: Owner) (graph: Graph) () =
    owner.Dispose ()
    (graph :> IDisposable).Dispose()

/// <summary>
/// A <c>Rows</c>-row projection through a <c>filter</c> on a query signal and a <c>sortBy</c> on a direction signal.
/// One effect reads the visible keys; one effect per row reads its cell. An operation alternates between a query
/// change and a direction change.
/// </summary>
let table (n: int) =
    let graph = newGraph ()

    let owner, query, descending =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let source = createSignal (Array.init Rows (fun i -> i, i))
            let query = createSignal 0
            let descending = createSignal false
            let rows = createProjection fst snd (fun () -> source.Value)

            let visible =
                rows
                |> Projection.filter (fun v -> v % 10 >= query.Value)
                |> Projection.sortBy (fun v -> if descending.Value then -v else v)

            createEffect (fun () -> sink <- visible.Keys.Length)

            for key in 0 .. Rows - 1 do
                createEffect (fun () -> sink <- rows.Get key)

            owner, query, descending)

    {
        Run =
            fun () ->
                for op in 1..n do
                    if op % 2 = 1 then
                        query.Value <- (query.Peek + 1) % 5
                    else
                        descending.Value <- not descending.Peek
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>Rows</c> rows, each highlighted through a <c>createSelector</c> over the selected index, and a detail effect that
/// builds <c>DetailNodes</c> memo and effect pairs over the selected row. An operation moves the selection.
/// </summary>
let detail (n: int) =
    let graph = newGraph ()

    let owner, selected =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let rows = Array.init Rows createSignal
            let selected = createSignal 0
            let isSelected = createSelector (fun () -> selected.Value)

            for i in 0 .. Rows - 1 do
                createEffect (fun () -> sink <- if isSelected.Get i then rows[i].Value else 0)

            createEffect (fun () ->
                let row = rows[selected.Value]

                for j in 1..DetailNodes do
                    let field = createMemo (fun _ -> row.Value + j)
                    createEffect (fun () -> sink <- field.Value))

            owner, selected)

    {
        Run =
            fun () ->
                for _ in 1..n do
                    selected.Value <- (selected.Peek + 37) % Rows
        Teardown = teardown owner graph
    }

/// <summary>
/// One source read by <c>DiamondWidth</c> memos, joined by one sum memo read by one effect. An operation writes the
/// source.
/// </summary>
let wideDiamond (n: int) =
    let graph = newGraph ()

    let owner, source =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let source = createSignal 0

            let branches =
                Array.init DiamondWidth (fun j -> createMemo (fun _ -> source.Value + j))

            let sum =
                createMemo (fun _ ->
                    let mutable total = 0

                    for branch in branches do
                        total <- total + branch.Value

                    total)

            createEffect (fun () -> sink <- sum.Value)
            owner, source)

    {
        Run =
            fun () ->
                for _ in 1..n do
                    source.Value <- source.Peek + 1
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>DynamicReaders</c> effects, each reading its own <c>a</c> or <c>b</c> source as a shared condition selects. An
/// operation alternates between flipping the condition and writing every active source.
/// </summary>
let dynamicBranches (n: int) =
    let graph = newGraph ()

    let owner, condition, a, b =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()
            let condition = createSignal true
            let a = Array.init DynamicReaders createSignal
            let b = Array.init DynamicReaders createSignal

            for i in 0 .. DynamicReaders - 1 do
                createEffect (fun () -> sink <- if condition.Value then a[i].Value else b[i].Value)

            owner, condition, a, b)

    {
        Run =
            fun () ->
                for op in 1..n do
                    if op % 2 = 1 then
                        condition.Value <- not condition.Peek
                    else
                        let active = if condition.Peek then a else b

                        for source in active do
                            source.Value <- source.Peek + 1
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>Widgets</c> boundaries, each summing <c>SourcesPerWidget</c> async sources held in signals and read by one
/// effect. <c>boundary</c> wraps a body in <c>createSuspense</c> or <c>createBoundary</c>.
/// </summary>
let private widgets (boundary: (unit -> int) -> Boundary<int>) =
    let graph = newGraph ()

    let owner, slots =
        graph.CreateRoot (fun owner ->
            use _ = graph.Activate ()

            let slots =
                Array.init Widgets (fun _ ->
                    Array.init SourcesPerWidget (fun i ->
                        let source = AsyncSource<int> graph
                        source.Settle i
                        createSignal source))

            for widget in slots do
                let shown =
                    boundary (fun () ->
                        let mutable total = 0

                        for slot in widget do
                            total <- total + slot.Value.Value

                        total)

                createEffect (fun () -> sink <- shown.Value)

            owner, slots)

    graph, owner, slots

/// <summary>
/// <c>Widgets</c> suspense boundaries over <c>SourcesPerWidget</c> async sources each. An operation reloads every
/// source in one batch, then settles each one separately.
/// </summary>
let asyncResolve (n: int) =
    let graph, owner, slots =
        widgets (fun body -> createSuspense (fun last -> ValueOption.defaultValue -1 last) body)

    let flat = Array.concat slots
    let fresh = Array.zeroCreate<AsyncSource<int>> flat.Length

    {
        Run =
            fun () ->
                for op in 1..n do
                    graph.Batch (fun () ->
                        for i in 0 .. flat.Length - 1 do
                            fresh[i] <- AsyncSource<int> graph
                            flat[i].Value <- fresh[i])

                    for source in fresh do
                        source.Settle op
        Teardown = teardown owner graph
    }

/// <summary>
/// <c>Widgets</c> error boundaries over <c>SourcesPerWidget</c> async sources each. An operation fails one source of
/// one widget, then replaces it with a settled one.
/// </summary>
let asyncRecover (n: int) =
    let graph, owner, slots =
        widgets (fun body -> createBoundary (fun last -> ValueOption.defaultValue -1 last) (fun _ last -> ValueOption.defaultValue -2 last) body)

    let failure = exn "load failed"

    {
        Run =
            fun () ->
                for op in 1..n do
                    let slot = slots[op % Widgets][0]
                    let failing = AsyncSource<int> graph
                    slot.Value <- failing
                    failing.Fail failure
                    let settled = AsyncSource<int> graph
                    slot.Value <- settled
                    settled.Settle op
        Teardown = teardown owner graph
    }
