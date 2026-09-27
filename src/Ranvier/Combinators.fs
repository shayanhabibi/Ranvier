namespace Ranvier

/// <summary>A view's pending keys absent from its <c>Keys</c>, written by the view's pass.</summary>
type internal HeldOut<'K when 'K: equality>(graph: Graph) =
    let cell = Signal<'K[]> (graph, Array.empty)
    let pass = ResizeArray<'K> ()

    /// <summary>The keys the last pass held out, in upstream order. A tracked read.</summary>
    member _.Keys = cell.Value

    /// <summary>The cell behind <c>Keys</c>.</summary>
    member _.Cell = cell

    /// <summary>Discards the keys a failed pass staged.</summary>
    member _.Begin() = pass.Clear ()

    /// <summary>Stages <c>key</c> for the pass in progress.</summary>
    member _.Add(key: 'K) = pass.Add key

    /// <summary>
    /// Stages the keys <c>upstream</c> holds out of its <c>Keys</c>, after those already staged, then writes the staged
    /// keys when they differ from the last pass.
    /// </summary>
    member _.Publish(upstream: Projection<'K, 'V>) =
        let inherited = upstream.PendingExtra

        if not (isNull (box inherited)) then
            pass.AddRange (inherited ())

        let current = cell.Peek

        if current.Length <> pass.Count || not (Seq.forall2 (=) current pass) then
            cell.Value <- pass.ToArray ()

        pass.Clear ()

/// <summary>
/// The keys of <c>upstream</c>, in upstream order, with rows from exactly one of <c>map</c> and <c>factory</c>, as on
/// <c>KeyedProjection</c>.
/// </summary>
type internal MapView<'K, 'V, 'U when 'K: equality>
    (graph: Graph, upstream: Projection<'K, 'V>, map: 'K -> 'U, factory: (unit -> 'K) -> (unit -> 'U)) =
    inherit KeyedProjection<'K, 'K, 'U>(graph, id, map, factory, (fun () -> upstream.Keys))

    let heldOut = HeldOut<'K> graph

    /// <summary>The keys <c>upstream</c> held out of its <c>Keys</c> at the last pass. A tracked read.</summary>
    member _.HeldOut = heldOut.Keys

    override this.Enumerate() =
        heldOut.Begin ()
        base.Enumerate ()
        heldOut.Publish upstream

/// <summary>
/// The keys of <c>inclusion</c> whose predicate row holds <c>true</c>, each with the value of <c>read</c> at the key.
/// </summary>
/// <remarks>
/// A pending predicate keeps the key's last settled membership. A failed predicate leaves the key out of <c>Keys</c> and
/// keeps its row, which raises the failure. <c>read</c> raises when the key's predicate row is pending or failed.
/// </remarks>
type internal FilterView<'K, 'V, 'U when 'K: equality>
    (graph: Graph, upstream: Projection<'K, 'V>, inclusion: Projection<'K, bool> ref, read: 'K -> 'U) =
    inherit RowsOf<'K, 'K, 'U>(graph, read, Unchecked.defaultof<_>)

    let heldOut = HeldOut<'K> graph

    /// <summary>
    /// The upstream keys whose predicate has never settled, in upstream order, followed by the keys <c>upstream</c> holds
    /// out of its <c>Keys</c>. A tracked read.
    /// </summary>
    member _.HeldOut = heldOut.Keys

    override this.Enumerate() =
        heldOut.Begin ()
        let rows = inclusion.Value

        for key in rows.Keys do
            let entry = rows.Entries.Find key

            if not (isNull entry) then
                match entry.Row.TryValue with
                | Ready true -> this.Visit (key, key)
                | Ready false -> ()
                | Pending ->
                    if not entry.Settled then heldOut.Add key
                    elif entry.Row.Peek then this.Visit (key, key)
                | Failed _ ->
                    this.Visit (key, key)
                    this.PassKeys.RemoveAt (this.PassKeys.Count - 1)

        heldOut.Publish upstream

/// <summary>
/// The keys of <c>sortKeys</c> whose sort key has settled, ascending by sort key and then by upstream position, each with
/// a row reading the upstream value.
/// </summary>
/// <remarks>
/// A pending sort key keeps the key's last settled sort key. A failed sort key leaves the key out of <c>Keys</c> and keeps
/// its row, which raises the failure. A <c>float</c> or <c>float32</c> NaN sort key orders after every other sort key.
/// </remarks>
type internal SortView<'K, 'V, 'S when 'K: equality and 'S: comparison>
    (graph: Graph, upstream: Projection<'K, 'V>, sortKeys: Projection<'K, 'S> ref) =
    inherit
        RowsOf<'K, 'K, 'V>(
            graph,
            (fun key ->
                sortKeys.Value.Get key |> ignore
                upstream.Get key),
            Unchecked.defaultof<_>
        )

    let heldOut = HeldOut<'K> graph

    // The placed keys and their sort keys in upstream order, for the current and the last pass.
    let mutable placed = ResizeArray<'K> ()
    let mutable ranks = ResizeArray<'S> ()
    let mutable lastPlaced = ResizeArray<'K> ()
    let mutable lastRanks = ResizeArray<'S> ()

    /// <summary>Positions into <c>lastPlaced</c>, in output order.</summary>
    let mutable order: int[] = Array.empty

    /// <summary>Whether <c>order</c> holds the result of a completed sort.</summary>
    let mutable sorted = false

    let isNaN (rank: 'S) =
        match box rank with
        | :? float as f -> System.Double.IsNaN f
        | :? float32 as f -> System.Single.IsNaN f
        | _ -> false

    /// <summary><c>compare</c>, with NaN equal to NaN and after every other value.</summary>
    let compareRanks (a: 'S) (b: 'S) =
        match isNaN a, isNaN b with
        | true, true -> 0
        | true, false -> 1
        | false, true -> -1
        | false, false -> compare a b

    /// <summary>
    /// Whether <c>order</c> applies to this pass: the last sort completed, and this pass placed the keys and sort keys of the
    /// last pass in the same upstream order.
    /// </summary>
    let unchanged () =
        let equalKeys = System.Collections.Generic.EqualityComparer<'K>.Default
        let mutable same = sorted && placed.Count = lastPlaced.Count
        let mutable i = 0

        while same && i < placed.Count do
            same <- equalKeys.Equals (placed[i], lastPlaced[i]) && compareRanks ranks[i] lastRanks[i] = 0
            i <- i + 1

        same

    let sort () =
        if order.Length <> placed.Count then
            order <- Array.zeroCreate placed.Count

        for i in 0 .. order.Length - 1 do
            order[i] <- i

        order
        |> Array.sortInPlaceWith (fun i j ->
            let byRank = compareRanks ranks[i] ranks[j]
            if byRank <> 0 then byRank else compare i j)

    /// <summary>
    /// The upstream keys whose sort key has never settled, in upstream order, followed by the keys <c>upstream</c> holds
    /// out of its <c>Keys</c>. A tracked read.
    /// </summary>
    member _.HeldOut = heldOut.Keys

    override this.Enumerate() =
        heldOut.Begin ()
        let rows = sortKeys.Value
        placed.Clear ()
        ranks.Clear ()

        for key in rows.Keys do
            let entry = rows.Entries.Find key

            if not (isNull entry) then
                match entry.Row.TryValue with
                | Ready rank ->
                    placed.Add key
                    ranks.Add rank
                | Pending ->
                    if not entry.Settled then
                        heldOut.Add key
                    else
                        placed.Add key
                        ranks.Add entry.Row.Peek
                | Failed _ ->
                    this.Visit (key, key)
                    this.PassKeys.RemoveAt (this.PassKeys.Count - 1)

        if not (unchanged ()) then
            sorted <- false
            sort ()
            sorted <- true

        for i in order do
            this.Visit (placed[i], placed[i])

        let swapKeys = lastPlaced
        lastPlaced <- placed
        placed <- swapKeys
        let swapRanks = lastRanks
        lastRanks <- ranks
        ranks <- swapRanks
        heldOut.Publish upstream

/// <summary>
/// A group of a <c>Grouping</c>: its members in upstream order and the inner view over them. The inner view's pass reads
/// <c>outerKeys</c> first, so it follows the upstream without a reader of the outer view.
/// </summary>
type internal Group<'K, 'V when 'K: equality>(graph: Graph, upstream: Projection<'K, 'V>, outerKeys: unit -> unit) =
    let members = Signal<'K[]> (graph, Array.empty)

    let view =
        new KeyedProjection<'K, 'K, 'V> (
            graph,
            id,
            (fun key -> upstream.Get key),
            Unchecked.defaultof<_>,
            fun () ->
                outerKeys ()
                members.Value
        )

    do view.GetRaisesDisposed <- true

    /// <summary>The members placed by the pass in progress, in upstream order.</summary>
    member val Pass = ResizeArray<'K> ()

    member _.View = view :> Projection<'K, 'V>

    /// <summary>Writes the staged members when they differ from the last pass, then clears them.</summary>
    member this.Publish() =
        let current = members.Peek

        if current.Length <> this.Pass.Count || not (Seq.forall2 (=) current this.Pass) then
            members.Value <- this.Pass.ToArray ()

        this.Pass.Clear ()

/// <summary>
/// A projection keyed by group key, ordered by the upstream position of each group's first member, whose row is the inner
/// view of the group's keys in upstream order. Returned by <c>Projection.groupBy</c>.
/// </summary>
/// <remarks>
/// A pending group key keeps the key's last settled group. A key whose group key failed or has never settled is in
/// <c>UngroupedKeys</c> and in no group. An inner view is disposed once its group is empty.
/// </remarks>
type Grouping<'G, 'K, 'V when 'G: equality and 'K: equality>
    internal (graph: Graph, upstream: Projection<'K, 'V>, groupKeys: Projection<'K, 'G> ref) as this =
    inherit Projection<'G, Projection<'K, 'V>>(graph)

    let groups = Platform.KeyMap<'G, Group<'K, 'V>> ()
    let order = ResizeArray<'G> ()
    let emptied = ResizeArray<'G> ()
    let heldOut = HeldOut<'K> graph
    let adds = ResizeArray<struct ('G * Group<'K, 'V>)> ()
    let writes = ResizeArray<struct (Signal<Projection<'K, 'V>> * Projection<'K, 'V>)> ()

    do this.ExtraObserved <- fun () -> heldOut.Cell.ObserverCount > 0

    member private this.Place(key: 'K, groupKey: 'G) =
        let mutable group = groups.Find groupKey

        if isNull (box group) then
            group <- graph.RunOwned (this.Scope, fun () -> Group<'K, 'V> (graph, upstream, (fun () -> this.Keys |> ignore)))
            groups.Set (groupKey, group)

        if group.Pass.Count = 0 then
            order.Add groupKey

        group.Pass.Add key

    /// <summary>Stages the row of <c>groupKey</c>: a write to a survivor's item source, or a row to create.</summary>
    member private this.Visit(groupKey: 'G, group: Group<'K, 'V>) =
        this.Seen.Add groupKey |> ignore
        this.PassKeys.Add groupKey
        let entry = this.Entries.Find groupKey

        if isNull entry then
            adds.Add (struct (groupKey, group))
        else
            writes.Add (struct ((entry :?> ItemRow<Projection<'K, 'V>, 'G, Projection<'K, 'V>>).Item, group.View))

    member private this.CreateAdded() =
        for struct (groupKey, group) in adds do
            let source = Signal<Projection<'K, 'V>> (graph, group.View)
            let entry = ItemRow<Projection<'K, 'V>, 'G, Projection<'K, 'V>> (groupKey, source)
            this.Entries.Set (groupKey, entry)
            entry.Reader <- fun () -> source.Value
            entry.Row <- Memo<Projection<'K, 'V>>.Create (graph, (fun () -> this.RunRow entry), ScopeMode.ValueRow)

    member private this.Enumerate() =
        heldOut.Begin ()
        let rows = groupKeys.Value
        order.Clear ()

        for key in rows.Keys do
            let entry = rows.Entries.Find key

            if not (isNull entry) then
                match entry.Row.TryValue with
                | Ready groupKey -> this.Place (key, groupKey)
                | Pending -> if entry.Settled then this.Place (key, entry.Row.Peek) else heldOut.Add key
                | Failed _ -> heldOut.Add key

        emptied.Clear ()
        groups.Iterate (fun groupKey group -> if group.Pass.Count = 0 then emptied.Add groupKey)

        for groupKey in order do
            let group = groups.Find groupKey
            group.Publish ()
            this.Visit (groupKey, group)

        for groupKey in emptied do
            let group = groups.Find groupKey
            groups.Remove groupKey
            group.View.Dispose ()

        heldOut.Publish upstream

    /// <summary>
    /// The upstream keys without a group: keys whose group key has never settled or has raised, in upstream order, then the
    /// pending keys held out of the upstream view's <c>Keys</c>.
    /// </summary>
    /// <remarks>
    /// Tracked; wakes its reader when the list changes. A read raises what the pass raised, as <c>Keys</c> does.
    /// </remarks>
    member this.UngroupedKeys: 'K[] = this.ReadAfterPass heldOut.Cell

    /// <summary>The group of <c>key</c>, as a tracked read of the key's group key.</summary>
    /// <remarks>
    /// While the key's group key is pending, returns the key's last settled group, or raises <c>NotReadyException</c> if it
    /// has never settled. A raising group key re-raises its exception, including for a key that settled before. Raises
    /// <c>KeyNotFoundException</c> for a key absent from the upstream <c>Keys</c>, including a pending key held out by the
    /// upstream view, as the upstream <c>Get</c> does.
    /// </remarks>
    member this.GroupOf(key: 'K) : 'G =
        groupKeys.Value.GetSettled key

    interface IProjectionPass with
        member this.Enumerate() = this.Enumerate ()

        member this.CreateAdded() =
            if adds.Count > 0 then
                graph.RunOwned (this.Scope, this.CreateAdded)

        member _.CommitWrites() =
            for struct (source, item) in writes do
                source.Value <- item

        member _.ClearStaged() =
            adds.Clear ()
            writes.Clear ()

/// <summary>Views over a <c>Projection</c> that re-run the user function only for keys whose upstream row changed.</summary>
/// <remarks>
/// A view is a <c>Projection</c> owned by the scope that creates it. A membership or order change costs O(N) per view.
/// A pending key a view holds out of <c>Keys</c> is in the <c>PendingKeys</c> of every view built on that view. A key excluded on failure
/// is absent from those views, and only <c>Get</c> and <c>TryGet</c> of the excluding view raise its error.
/// </remarks>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Projection =
    /// <summary>The keys of <c>upstream</c> whose value satisfies <c>predicate</c>, in upstream order, with their values.</summary>
    /// <remarks>
    /// <c>predicate</c> re-runs for a key when its upstream row changes. A pending predicate keeps the key's last membership;
    /// a key whose predicate has never settled is absent from <c>Keys</c> and present in <c>PendingKeys</c>, while
    /// <c>AnyPending</c> counts only rows of keys in <c>Keys</c>. A throwing predicate excludes the key, and <c>Get</c> and
    /// <c>TryGet</c> of the key raise its exception.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// let rows = createProjection (fun t -> t.Id) id (fun () -> todos.Value)
    /// let active = rows |> Projection.filter (fun t -> not t.Done)
    /// </code>
    /// </example>
    let filter (predicate: 'V -> bool) (upstream: Projection<'K, 'V>) : Projection<'K, 'V> =
        let graph = upstream.Graph
        let inclusion = ref Unchecked.defaultof<Projection<'K, bool>>

        let read key =
            inclusion.Value.Get key |> ignore
            upstream.Get key

        let view = new FilterView<'K, 'V, 'V> (graph, upstream, inclusion, read)

        inclusion.Value <-
            graph.RunOwned (
                view.Scope,
                fun () ->
                    new KeyedProjection<'K, 'K, bool> (
                        graph,
                        id,
                        (fun key -> predicate (upstream.Get key)),
                        Unchecked.defaultof<_>,
                        fun () -> upstream.Keys
                    )
                    :> Projection<'K, bool>
            )

        view.PendingExtra <- fun () -> view.HeldOut
        view :> Projection<'K, 'V>

    /// <summary>
    /// The keys of <c>upstream</c> whose value <c>chooser</c> maps to <c>Some</c>, in upstream order, each with the value
    /// inside the <c>Some</c>.
    /// </summary>
    /// <remarks>
    /// <c>chooser</c> runs once per key when its upstream row changes, and a change between two <c>Some</c> values wakes only
    /// readers of the key's row. A pending <c>chooser</c> keeps the key's last membership; a key whose <c>chooser</c> has
    /// never settled is absent from <c>Keys</c> and present in <c>PendingKeys</c>, while <c>AnyPending</c> counts only rows
    /// of keys in <c>Keys</c>. A throwing <c>chooser</c> excludes the key, and <c>Get</c> and <c>TryGet</c> of the key raise
    /// its exception.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// let rows = createProjection (fun t -> t.Id) id (fun () -> todos.Value)
    /// let due = rows |> Projection.choose (fun t -> t.Due)
    /// </code>
    /// </example>
    let choose (chooser: 'V -> 'U option) (upstream: Projection<'K, 'V>) : Projection<'K, 'U> =
        let graph = upstream.Graph
        let choices = ref Unchecked.defaultof<Projection<'K, 'U option>>
        let inclusion = ref Unchecked.defaultof<Projection<'K, bool>>

        let read key =
            match choices.Value.Get key with
            | Some value -> value
            | None -> raise (System.Collections.Generic.KeyNotFoundException $"The projection has no key %A{key}.")

        let view = new FilterView<'K, 'V, 'U> (graph, upstream, inclusion, read)

        graph.RunOwned (
            view.Scope,
            fun () ->
                choices.Value <-
                    new KeyedProjection<'K, 'K, 'U option> (
                        graph,
                        id,
                        (fun key -> chooser (upstream.Get key)),
                        Unchecked.defaultof<_>,
                        fun () -> upstream.Keys
                    )

                inclusion.Value <-
                    new KeyedProjection<'K, 'K, bool> (
                        graph,
                        id,
                        (fun key -> Option.isSome (choices.Value.Get key)),
                        Unchecked.defaultof<_>,
                        fun () -> upstream.Keys
                    )
        )

        view.PendingExtra <- fun () -> view.HeldOut
        view :> Projection<'K, 'U>

    /// <summary>The keys of <c>upstream</c>, in upstream order, each with <c>mapping</c> of its value.</summary>
    /// <remarks>
    /// <c>mapping</c> re-runs for a key when its upstream row changes. A throwing <c>mapping</c> keeps the key, and <c>Get</c>
    /// and <c>TryGet</c> of the key raise its exception. <c>PendingKeys</c> includes the pending keys <c>upstream</c> holds
    /// outside its <c>Keys</c>.
    /// </remarks>
    let map (mapping: 'V -> 'U) (upstream: Projection<'K, 'V>) : Projection<'K, 'U> =
        if isNull (box upstream.PendingExtra) then
            new KeyedProjection<'K, 'K, 'U> (
                upstream.Graph,
                id,
                (fun key -> mapping (upstream.Get key)),
                Unchecked.defaultof<_>,
                fun () -> upstream.Keys
            )
            :> Projection<'K, 'U>
        else
            let view =
                new MapView<'K, 'V, 'U> (
                    upstream.Graph,
                    upstream,
                    (fun key -> mapping (upstream.Get key)),
                    Unchecked.defaultof<_>
                )

            view.PendingExtra <- fun () -> view.HeldOut
            view :> Projection<'K, 'U>

    /// <summary>
    /// The keys of <c>upstream</c>, in upstream order, each row the reader <c>mapping</c> returns for the key and a
    /// tracked read of its upstream value.
    /// </summary>
    /// <remarks>
    /// <c>mapping</c> runs once per key, untracked, in a scope that owns the nodes it creates and is disposed with the key.
    /// The reader re-runs when a value it read changes. <c>PendingKeys</c> includes the pending keys <c>upstream</c> holds
    /// outside its <c>Keys</c>.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// let rows = createProjection (fun t -> t.Id) id (fun () -> todos.Value)
    /// let labels = rows |> Projection.mapWith (fun id todo -> fun () -> $"{id}: {(todo ()).Title}")
    /// </code>
    /// </example>
    let mapWith (mapping: 'K -> (unit -> 'V) -> (unit -> 'U)) (upstream: Projection<'K, 'V>) : Projection<'K, 'U> =
        let factory (key: unit -> 'K) =
            let k = key ()
            mapping k (fun () -> upstream.Get k)

        if isNull (box upstream.PendingExtra) then
            new KeyedProjection<'K, 'K, 'U> (upstream.Graph, id, Unchecked.defaultof<_>, factory, (fun () -> upstream.Keys))
            :> Projection<'K, 'U>
        else
            let view = new MapView<'K, 'V, 'U> (upstream.Graph, upstream, Unchecked.defaultof<_>, factory)
            view.PendingExtra <- fun () -> view.HeldOut
            view :> Projection<'K, 'U>

    /// <summary>
    /// The keys of <c>upstream</c> with their values, ascending by <c>projection</c> of the value; equal sort keys keep upstream
    /// order.
    /// </summary>
    /// <remarks>
    /// A <c>float</c> or <c>float32</c> NaN sort key orders last; <c>None</c> orders first. A pending sort key keeps the key's last
    /// settled sort key; a key whose sort key has never settled is only in <c>PendingKeys</c>. A throwing sort key excludes the
    /// key, and <c>Get</c> and <c>TryGet</c> of the key raise its exception.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// let rows = createProjection (fun t -> t.Id) id (fun () -> todos.Value)
    /// let byDue = rows |> Projection.sortBy (fun t -> t.Due)
    /// </code>
    /// </example>
    let sortBy (projection: 'V -> 'S) (upstream: Projection<'K, 'V>) : Projection<'K, 'V> =
        let graph = upstream.Graph
        let sortKeys = ref Unchecked.defaultof<Projection<'K, 'S>>
        let view = new SortView<'K, 'V, 'S> (graph, upstream, sortKeys)

        sortKeys.Value <-
            graph.RunOwned (
                view.Scope,
                fun () ->
                    new KeyedProjection<'K, 'K, 'S> (
                        graph,
                        id,
                        (fun key -> projection (upstream.Get key)),
                        Unchecked.defaultof<_>,
                        fun () -> upstream.Keys
                    )
                    :> Projection<'K, 'S>
            )

        view.PendingExtra <- fun () -> view.HeldOut
        view :> Projection<'K, 'V>

    /// <summary>
    /// The groups of <c>upstream</c> by <c>projection</c> of each value, ordered by the upstream position of each group's
    /// first member, each an inner view of the group's keys in upstream order.
    /// </summary>
    /// <remarks>
    /// A pending group key keeps the key's last settled group. A key whose group key throws or has never settled is in
    /// <c>UngroupedKeys</c> and in no group. A moved key leaves its old group and joins its new one in the same pass. An
    /// emptied group's inner view is disposed: its <c>Keys</c> is empty and <c>Get</c> raises
    /// <c>ObjectDisposedException</c>.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// let rows = createProjection (fun t -> t.Id) id (fun () -> todos.Value)
    /// let byOwner = rows |> Projection.groupBy (fun t -> t.Owner)
    /// </code>
    /// </example>
    let groupBy (projection: 'V -> 'G) (upstream: Projection<'K, 'V>) : Grouping<'G, 'K, 'V> =
        let graph = upstream.Graph
        let groupKeys = ref Unchecked.defaultof<Projection<'K, 'G>>
        let view = new Grouping<'G, 'K, 'V> (graph, upstream, groupKeys)

        groupKeys.Value <-
            graph.RunOwned (
                view.Scope,
                fun () ->
                    new KeyedProjection<'K, 'K, 'G> (
                        graph,
                        id,
                        (fun key -> projection (upstream.Get key)),
                        Unchecked.defaultof<_>,
                        fun () -> upstream.Keys
                    )
                    :> Projection<'K, 'G>
            )

        view
