/// <summary>
/// The workloads, one implementation per engine. In the first five, every
/// engine builds the same shape: a root holds a shared source and its rows, and
/// a row is a source of its own plus one reaction reading the row and the shared
/// source. The cost of a root is constant in the number of roots alive. The
/// projection workloads measure Ranvier alone.
/// </summary>
module CounterBench.Scenarios

open System
open FSharp.Data.Adaptive
open Ranvier

/// <summary>
/// State built outside the measured region. <c>Run</c> performs the operations the
/// state was prepared for; <c>Teardown</c> releases what <c>Run</c> left behind.
/// </summary>
type Prepared =
    {
        Run: unit -> unit
        Teardown: unit -> unit
    }

type Case =
    {
        Scenario: string
        Engine: string
        /// <summary>
        /// What one operation is, as the report prints it.
        /// </summary>
        Unit: string
        /// <summary>
        /// N: the smaller of the two operation counts measured. The larger is
        /// 2N.
        /// </summary>
        Ops: int
        Prepare: int -> Prepared
    }

[<Literal>]
let RowCount = 1000

[<Literal>]
let UpdateStride = 10

[<Literal>]
let ChainDepth = 4

[<Literal>]
let FormFields = 8

/// <summary>
/// Written by every reaction, which keeps every reaction's result live.
/// </summary>
let mutable private sink = 0

/// <summary>
/// Written by the acts of the <c>work-*</c> workloads.
/// </summary>
let mutable private label = ""

let private noTeardown () = ()

let private disposeAll (items: ResizeArray<IDisposable>) =
    for i in 0 .. items.Count - 1 do
        items[i].Dispose ()

module private Ranvier =

    /// <summary>
    /// A root and the sources of its rows. Each row's reaction is a
    /// <c>createEffectOn</c> when <c>split</c>, an <c>Effect</c> otherwise.
    /// </summary>
    let root (split: bool) (graph: Graph) =
        graph.CreateRoot (fun owner ->
            let shared = Signal (graph, 0)
            use _ = if split then graph.Activate () else null

            let rows =
                Array.init RowCount (fun i ->
                    let row = Signal (graph, i)

                    if split then
                        createEffectOn (fun () -> shared.Value + row.Value) (fun v -> sink <- v)
                    else
                        new Effect (graph, (fun () -> sink <- shared.Value + row.Value)) |> ignore

                    row)

            owner, rows)

    let create (split: bool) (n: int) =
        let graph = new Graph ()
        let roots = ResizeArray<Owner> n

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        roots.Add (fst (root split graph))
            Teardown =
                fun () ->
                    for i in 0 .. roots.Count - 1 do
                        (roots[i] :> IDisposable).Dispose ()

                    (graph :> IDisposable).Dispose ()
        }

    let update (split: bool) (n: int) =
        let graph = new Graph ()
        let owner, rowSources = root split graph

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        for i in 0..UpdateStride .. RowCount - 1 do
                            let row = rowSources[i]
                            row.Value <- row.Peek + 1
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    let chain (n: int) =
        let graph = new Graph ()
        let source = Signal (graph, 0)
        let mutable tail = Memo (graph, (fun () -> source.Value + 1))

        for _ in 2..ChainDepth do
            let previous = tail
            tail <- Memo (graph, (fun () -> previous.Value + 1))

        let tail = tail
        sink <- tail.Value
        let next = ref 0

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        next.Value <- next.Value + 1
                        source.Value <- next.Value
                        sink <- tail.Value
            Teardown = fun () -> (graph :> IDisposable).Dispose ()
        }

    let cutoff (n: int) =
        let graph = new Graph ()
        let source = Signal (graph, 1)

        let owner =
            graph.CreateRoot (fun owner ->
                new Effect (graph, (fun () -> sink <- source.Value)) |> ignore
                owner)

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        source.Value <- 1
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    let dispose (split: bool) (n: int) =
        let graph = new Graph ()
        let roots = Array.init n (fun _ -> fst (root split graph))

        {
            Run =
                fun () ->
                    for owner in roots do
                        owner.Dispose ()
            Teardown = fun () -> (graph :> IDisposable).Dispose ()
        }

    /// <summary>
    /// One reaction over a source written with a new value each operation,
    /// whose derived value never changes. <c>split</c> derives it in
    /// <c>createEffectOn</c>'s compute; otherwise an <c>Effect</c> derives it in its body.
    /// </summary>
    let derivedCutoff (split: bool) (n: int) =
        let graph = new Graph ()
        let source = Signal (graph, 1)

        let owner =
            graph.CreateRoot (fun owner ->
                if split then
                    use _ = graph.Activate ()
                    createEffectOn (fun () -> source.Value >>> 30) (fun v -> sink <- v)
                else
                    new Effect (graph, (fun () -> sink <- source.Value >>> 30)) |> ignore

                owner)

        let next = ref 1

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        next.Value <- next.Value + 1
                        source.Value <- next.Value
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    /// <summary>
    /// <c>RowCount</c> rows, each a source and one reaction whose act formats a
    /// label from <c>derive</c> of the row's value. An operation increments every
    /// <c>UpdateStride</c>th row. <c>split</c> builds the reaction with <c>createEffectOn</c>.
    /// </summary>
    let private labelled (derive: int -> int) (split: bool) (n: int) =
        let graph = new Graph ()

        let owner, rowSources =
            graph.CreateRoot (fun owner ->
                use _ = graph.Activate ()

                let rows =
                    Array.init RowCount (fun i ->
                        let row = Signal (graph, i)

                        if split then
                            createEffectOn (fun () -> derive row.Value) (fun v -> label <- String.Concat ("row ", string v))
                        else
                            createEffect (fun () -> label <- String.Concat ("row ", string (derive row.Value)))

                        row)

                owner, rows)

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        for i in 0..UpdateStride .. RowCount - 1 do
                            let row = rowSources[i]
                            row.Value <- row.Peek + 1
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    /// <summary>
    /// A label of the row's tier: one write in ten changes it.
    /// </summary>
    let status = labelled (fun v -> v / 10)

    /// <summary>
    /// A label of the row's value: every write changes it.
    /// </summary>
    let value = labelled id

    /// <summary>
    /// <c>RowCount / FormFields</c> forms of <c>FormFields</c> field sources. Each
    /// form's reaction derives validity from its fields and writes it to a
    /// signal read by one downstream effect. An operation increments one
    /// field of every form. <c>split</c> builds the reaction with <c>createEffectOn</c>.
    /// </summary>
    let form (split: bool) (n: int) =
        let graph = new Graph ()
        let forms = RowCount / FormFields

        let owner, fieldSources =
            graph.CreateRoot (fun owner ->
                use _ = graph.Activate ()

                let fields =
                    Array.init forms (fun f ->
                        let fields = Array.init FormFields (fun i -> Signal (graph, (f + i) % 50))
                        let valid = Signal (graph, false)

                        let validate () =
                            let mutable sum = 0
                            let mutable filled = true

                            for field in fields do
                                let v = field.Value
                                sum <- sum + v
                                filled <- filled && v > 0

                            filled && sum < 300

                        if split then
                            createEffectOn validate (fun v -> valid.Value <- v)
                        else
                            createEffect (fun () -> valid.Value <- validate ())

                        createEffect (fun () -> sink <- if valid.Value then 1 else 0)
                        fields)

                owner, fields)

        let next = ref 0

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        next.Value <- (next.Value + 1) % FormFields

                        for fields in fieldSources do
                            let field = fields[next.Value]
                            field.Value <- (field.Peek + 1) % 50
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    /// <summary>
    /// A projection of <c>RowCount</c> rows keyed by their first component, each row
    /// read by one effect. <c>items</c> is the source's initial value.
    /// </summary>
    let private projection (graph: Graph) (items: (int * int)[]) =
        use _ = graph.Activate ()
        let source = Signal (graph, items)

        let owner =
            graph.CreateRoot (fun owner ->
                let rows = createProjection fst snd (fun () -> source.Value)

                for key in 0 .. RowCount - 1 do
                    new Effect (graph, (fun () -> sink <- rows.Get key)) |> ignore

                owner)

        source, owner

    let projectEdit (n: int) =
        let graph = new Graph ()
        let items = Array.init RowCount (fun i -> i, i)
        let source, owner = projection graph items

        let writes =
            Array.init n (fun op ->
                let next = Array.copy items
                next[0] <- 0, op + 1
                next)

        {
            Run =
                fun () ->
                    for write in writes do
                        source.Value <- write
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    let projectReorder (n: int) =
        let graph = new Graph ()
        let forward = Array.init RowCount (fun i -> i, i)
        let reversed = Array.rev forward
        let source, owner = projection graph forward

        {
            Run =
                fun () ->
                    for op in 1..n do
                        source.Value <- if op % 2 = 1 then reversed else forward
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

    /// <summary>
    /// A <c>RowCount</c>-row projection viewed through <c>filter</c> (even values),
    /// <c>sortBy</c> (descending) and <c>map</c>. One effect reads the tail's keys and
    /// one effect reads each row the filter keeps.
    /// </summary>
    let projectChain (n: int) =
        let graph = new Graph ()
        let items = Array.init RowCount (fun i -> i, i)
        use _ = graph.Activate ()
        let source = Signal (graph, items)

        let owner =
            graph.CreateRoot (fun owner ->
                let tail =
                    createProjection fst snd (fun () -> source.Value)
                    |> Projection.filter (fun v -> v % 2 = 0)
                    |> Projection.sortBy (fun v -> -v)
                    |> Projection.map (fun v -> v + 1)

                new Effect (graph, (fun () -> sink <- tail.Keys.Length)) |> ignore

                for key in 0..2 .. RowCount - 1 do
                    new Effect (graph, (fun () -> sink <- tail.Get key)) |> ignore

                owner)

        // Row 0 alternates between the front and the back of the sort order.
        let writes =
            Array.init n (fun op ->
                let next = Array.copy items
                next[0] <- 0, (if op % 2 = 0 then 2 * (RowCount + op) else -2 * (op + 1))
                next)

        {
            Run =
                fun () ->
                    for write in writes do
                        source.Value <- write
            Teardown =
                fun () ->
                    owner.Dispose ()
                    (graph :> IDisposable).Dispose ()
        }

module private Adaptive =

    /// <summary>
    /// A root is the list of callback subscriptions. Disposing them stops the
    /// rows reacting.
    /// </summary>
    let root () =
        let shared = cval 0
        let subscriptions = ResizeArray<IDisposable> RowCount

        let rowSources =
            Array.init RowCount (fun i ->
                let row = cval i
                let value = AVal.map2 (+) shared row
                subscriptions.Add (value.AddCallback (fun v -> sink <- v))
                row)

        subscriptions, rowSources

    let create (n: int) =
        let roots = ResizeArray<ResizeArray<IDisposable>> n

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        roots.Add (fst (root ()))
            Teardown =
                fun () ->
                    for subscriptions in roots do
                        disposeAll subscriptions
        }

    let update (n: int) =
        let subscriptions, rowSources = root ()
        let values = Array.init RowCount id

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        for i in 0..UpdateStride .. RowCount - 1 do
                            values[i] <- values[i] + 1
                            let v = values[i]
                            transact (fun () -> rowSources[i].Value <- v)
            Teardown = fun () -> disposeAll subscriptions
        }

    let chain (n: int) =
        let source = cval 0
        let mutable tail = AVal.map (fun v -> v + 1) source

        for _ in 2..ChainDepth do
            tail <- AVal.map (fun v -> v + 1) tail

        let tail = tail
        sink <- AVal.force tail
        let next = ref 0

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        next.Value <- next.Value + 1
                        let v = next.Value
                        transact (fun () -> source.Value <- v)
                        sink <- AVal.force tail
            Teardown = noTeardown
        }

    let cutoff (n: int) =
        let source = cval 1
        let subscription = source.AddCallback (fun v -> sink <- v)

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        transact (fun () -> source.Value <- 1)
            Teardown = fun () -> subscription.Dispose ()
        }

    let dispose (n: int) =
        let roots = Array.init n (fun _ -> fst (root ()))

        {
            Run =
                fun () ->
                    for subscriptions in roots do
                        disposeAll subscriptions
            Teardown = noTeardown
        }

module private R3 =

    open R3

    let private subscribe (source: Observable<int>) =
        ObservableSubscribeExtensions.Subscribe (source, (fun v -> sink <- v))

    /// <summary>
    /// A root is the list of subscriptions. Disposing them stops the rows
    /// reacting.
    /// </summary>
    let root () =
        let shared = new ReactiveProperty<int> (0)
        let subscriptions = ResizeArray<IDisposable> RowCount

        let rowSources =
            Array.init RowCount (fun i ->
                let row = new ReactiveProperty<int> (i)

                let value =
                    Observable.CombineLatest (
                        (shared :> Observable<int>),
                        (row :> Observable<int>),
                        (fun a b -> a + b)
                    )

                subscriptions.Add (subscribe value)
                row)

        subscriptions, rowSources

    let create (n: int) =
        let roots = ResizeArray<ResizeArray<IDisposable>> n

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        roots.Add (fst (root ()))
            Teardown =
                fun () ->
                    for subscriptions in roots do
                        disposeAll subscriptions
        }

    let update (n: int) =
        let subscriptions, rowSources = root ()

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        for i in 0..UpdateStride .. RowCount - 1 do
                            let row = rowSources[i]
                            row.Value <- row.CurrentValue + 1
            Teardown = fun () -> disposeAll subscriptions
        }

    let chain (n: int) =
        let source = new ReactiveProperty<int> (0)
        let mutable tail = ObservableExtensions.Select ((source :> Observable<int>), (fun v -> v + 1))

        for _ in 2..ChainDepth do
            tail <- ObservableExtensions.Select (tail, (fun v -> v + 1))

        let latest = ref 0
        let subscription = ObservableSubscribeExtensions.Subscribe (tail, (fun v -> latest.Value <- v))
        let next = ref 0

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        next.Value <- next.Value + 1
                        source.Value <- next.Value
                        sink <- latest.Value
            Teardown = fun () -> subscription.Dispose ()
        }

    let cutoff (n: int) =
        let source = new ReactiveProperty<int> (1)
        let subscription = subscribe source

        {
            Run =
                fun () ->
                    for _ in 1..n do
                        source.Value <- 1
            Teardown = fun () -> subscription.Dispose ()
        }

    let dispose (n: int) =
        let roots = Array.init n (fun _ -> fst (root ()))

        {
            Run =
                fun () ->
                    for subscriptions in roots do
                        disposeAll subscriptions
            Teardown = noTeardown
        }

let private workload (prepare: int -> Workloads.Workload) (n: int) : Prepared =
    let prepared = prepare n
    { Run = prepared.Run; Teardown = prepared.Teardown }

let private scenario name unit ops (engines: (string * (int -> Prepared)) list) =
    engines
    |> List.map (fun (engine, prepare) ->
        {
            Scenario = name
            Engine = engine
            Unit = unit
            Ops = ops
            Prepare = prepare
        })

/// <summary>
/// Every case, in report order. <c>scale</c> multiplies each scenario's N.
/// </summary>
let all (scale: int) : Case list =
    [
        yield!
            scenario "create" $"one root of %d{RowCount} rows" (8 * scale) [
                "Ranvier", Ranvier.create false
                "FSharp.Data.Adaptive", Adaptive.create
                "R3", R3.create
            ]
        yield!
            scenario "update" $"write every %d{UpdateStride}th of %d{RowCount} rows" (50 * scale) [
                "Ranvier", Ranvier.update false
                "FSharp.Data.Adaptive", Adaptive.update
                "R3", R3.update
            ]
        yield!
            scenario "chain" $"write the source of %d{ChainDepth} memos, read the tail" (5000 * scale) [
                "Ranvier", Ranvier.chain
                "FSharp.Data.Adaptive", Adaptive.chain
                "R3", R3.chain
            ]
        yield!
            scenario "cutoff" "write an equal value to an observed source" (50000 * scale) [
                "Ranvier", Ranvier.cutoff
                "FSharp.Data.Adaptive", Adaptive.cutoff
                "R3", R3.cutoff
            ]
        yield!
            scenario "dispose" $"dispose one root of %d{RowCount} rows" (8 * scale) [
                "Ranvier", Ranvier.dispose false
                "FSharp.Data.Adaptive", Adaptive.dispose
                "R3", R3.dispose
            ]
        yield!
            scenario "create-on" $"one root of %d{RowCount} createEffectOn rows" (8 * scale) [
                "Ranvier", Ranvier.create true
            ]
        yield!
            scenario "update-on" $"write every %d{UpdateStride}th of %d{RowCount} createEffectOn rows" (50 * scale) [
                "Ranvier", Ranvier.update true
            ]
        yield!
            scenario "dispose-on" $"dispose one root of %d{RowCount} createEffectOn rows" (8 * scale) [
                "Ranvier", Ranvier.dispose true
            ]
        yield!
            scenario "derive-effect" "write a new value whose derived value is unchanged, Effect" (50000 * scale) [
                "Ranvier", Ranvier.derivedCutoff false
            ]
        yield!
            scenario "derive-on" "write a new value whose derived value is unchanged, createEffectOn" (50000 * scale) [
                "Ranvier", Ranvier.derivedCutoff true
            ]
        yield!
            scenario "work-status" "write every 10th of 1000 rows; the act formats a label, 1 write in 10 changes it" (50 * scale) [
                "createEffect", Ranvier.status false
                "createEffectOn", Ranvier.status true
            ]
        yield!
            scenario "work-value" "write every 10th of 1000 rows; the act formats a label, every write changes it" (50 * scale) [
                "createEffect", Ranvier.value false
                "createEffectOn", Ranvier.value true
            ]
        yield!
            scenario "work-form" "write one field of each of 125 8-field forms; validity feeds a signal and an effect" (50 * scale) [
                "createEffect", Ranvier.form false
                "createEffectOn", Ranvier.form true
            ]
        yield!
            scenario "project-edit" $"change one row of a %d{RowCount}-row projection" (50 * scale) [
                "Ranvier", Ranvier.projectEdit
            ]
        yield!
            scenario "project-reorder" $"reverse a %d{RowCount}-row projection" (50 * scale) [
                "Ranvier", Ranvier.projectReorder
            ]
        yield!
            scenario "project-chain" $"change one row of a %d{RowCount}-row filter, sortBy, map chain" (50 * scale) [
                "Ranvier", Ranvier.projectChain
            ]
        yield!
            scenario "app-table" $"alternate a filter query and a sort direction over %d{Workloads.Rows} rows" (50 * scale) [
                "Ranvier", workload Workloads.table
            ]
        yield!
            scenario "app-detail" $"move the selection over %d{Workloads.Rows} rows; the detail rebuilds %d{Workloads.DetailNodes} memo and effect pairs" (50 * scale) [
                "Ranvier", workload Workloads.detail
            ]
        yield!
            scenario "shape-diamond" $"write a source read by %d{Workloads.DiamondWidth} memos joined by one" (5000 * scale) [
                "Ranvier", workload Workloads.wideDiamond
            ]
        yield!
            scenario "shape-dynamic" $"alternate a branch flip and a write of every active source of %d{Workloads.DynamicReaders} effects" (500 * scale) [
                "Ranvier", workload Workloads.dynamicBranches
            ]
        yield!
            scenario "async-resolve" $"reload %d{Workloads.Widgets} suspense widgets of %d{Workloads.SourcesPerWidget} sources, then settle each" (50 * scale) [
                "Ranvier", workload Workloads.asyncResolve
            ]
        yield!
            scenario "async-recover" "fail one source of one error-boundary widget, then settle a replacement" (500 * scale) [
                "Ranvier", workload Workloads.asyncRecover
            ]
    ]
