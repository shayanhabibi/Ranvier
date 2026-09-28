namespace Ranvier

open System
open System.Threading
open System.Threading.Tasks

/// <summary>Identity tests on generic values.</summary>
module internal Identity =
#if FABLE_COMPILER
    /// <summary>True when <c>a</c> and <c>b</c> are <c>===</c>.</summary>
    let inline same (a: 'A) (b: 'A) : bool =
        obj.ReferenceEquals (a, b)

    /// <summary>True when <c>a</c> and <c>b</c> are <c>===</c> or both NaN.</summary>
    [<Fable.Core.Emit("($0 === $1 || ($0 !== $0 && $1 !== $1))")>]
    let unchanged (a: 'A) (b: 'A) : bool =
        obj.ReferenceEquals (a, b)
#else
    /// <summary>
    /// True when <c>a</c> and <c>b</c> are the same object. Always false for a value type, and allocates no box for one.
    /// </summary>
    let inline same (a: 'A) (b: 'A) : bool =
        not typeof<'A>.IsValueType
        && obj.ReferenceEquals (a, b)

    /// <summary>
    /// True when <c>b</c> is <c>a</c>: the same object, or an equal value for a value type or a string. NaN equals NaN.
    /// </summary>
    let inline unchanged (a: 'A) (b: 'A) : bool =
        if typeof<'A>.IsValueType then
            Collections.Generic.EqualityComparer<'A>.Default.Equals(a, b)
        else
            JsComparer<'A>.Instance.Equals(a, b)
#endif

/// <summary>
/// The functions most code should use. Every node type can be constructed
/// directly against an explicit <c>Graph</c> — that is what the tests and the
/// benchmarks do, because they run several graphs in one process — but ordinary
/// code has exactly one graph per thread and should not thread it through every
/// call.
/// </summary>
/// <remarks>
/// <para>
/// These resolve <c>Graph.Current</c>, so they throw outside an active graph rather
/// than silently creating one nobody disposes.
/// </para>
/// <para>
/// Two departures from Solid's names, both because F# can express what
/// JavaScript could not:
/// </para>
/// <para>
/// - <c>createSignal</c> hands back the signal itself, not a getter/setter pair.
///   <c>count.Value</c> reads and <c>count.Value &lt;- 2</c> writes, which is the same two
///   operations without the two closures, and it keeps <c>Peek</c>, <c>TryValue</c> and
///   <c>Status</c> reachable.
/// - <c>createEffect</c> returns unit. The effect attaches itself to the enclosing
///   scope and is disposed with it, so the handle is noise at the call site.
///   Construct <c>Effect</c> directly on the rare occasion you want to dispose one
///   early.
/// </para>
/// </remarks>
[<AutoOpen>]
module Api =
    /// <summary>
    /// A settable source.
    /// </summary>
    let createSignal (initial: 'T) =
        Signal (Graph.Current, initial)

    /// <summary>
    /// A derived value, recomputed on read once something it read has changed.
    /// </summary>
    /// <remarks>
    /// <c>compute</c> is a pure derivation. Creating an owned node in it (a memo,
    /// effect, async value, boundary, root, projection, lookup or <c>onCleanup</c>),
    /// <c>untrack</c> blocks included, raises <c>InvalidOperationException</c>, and the
    /// run fails even when <c>compute</c> catches the exception. A memo that creates
    /// nodes is <c>createMemoWith</c>.
    /// </remarks>
    let createMemo (compute: unit -> 'T) =
        Memo.Create (Graph.Current, compute, ScopeMode.Pure)

    /// <summary>
    /// A derived value that owns the nodes and cleanups <c>compute</c> creates. A run's nodes and cleanups are disposed
    /// before the next run and with the memo; the cleanups run untracked, and <c>compute</c> runs once per discharge.
    /// </summary>
    /// <remarks>
    /// A read of the memo from one of its cleanups returns the previous value, unless the cleanup first wrote a source of
    /// the memo: that read re-runs <c>compute</c> and its result replaces the pending run. An async value created and read
    /// in <c>compute</c> restarts its flight on every settle and never settles: create it outside and read it in <c>compute</c>.
    /// </remarks>
    let createMemoWith (compute: unit -> 'T) =
        Memo.Create (Graph.Current, compute, ScopeMode.Owning)

    /// <summary>
    /// A side effect, run once now and again whenever something it read changes. Disposed with the enclosing scope.
    /// </summary>
    /// <remarks>
    /// Its cleanups run untracked with the effect's scope as the owner, before a re-run and at disposal; a node created by a
    /// cleanup belongs to that scope, including when a pure memo disposes the effect. An async value created and read in
    /// <c>body</c> restarts its flight on every settle and the effect never runs past the read: create it outside.
    /// </remarks>
    let createEffect (body: unit -> unit) =
        Effect.Create (Graph.Current, body) |> ignore

    /// <summary>
    /// A side effect split in two: <c>compute</c> reads the dependencies and <c>act</c> performs the effect with its
    /// result. Disposed with the enclosing scope.
    /// </summary>
    /// <remarks>
    /// <c>compute</c> is a pure derivation, as <c>createMemo</c>'s is. <c>act</c> runs untracked, owns its cleanups, and
    /// runs only once <c>compute</c> has settled on a value unequal to the last value acted on; a pending read or a
    /// failure in <c>compute</c> leaves the previous action in place.
    /// </remarks>
    let createEffectOn (compute: unit -> 'T) (act: 'T -> unit) =
        EffectOn<'T>.Create(Graph.Current, compute, act)
        |> ignore

    /// <summary>
    /// A derived value computed asynchronously. Reads of it raise <c>NotReadyException</c>, which a boundary catches, until
    /// the first result arrives; a change to something it read starts a new run.
    /// </summary>
    /// <remarks>
    /// <c>compute</c> is a pure derivation, as <c>createMemo</c>'s is; an async value that creates nodes is
    /// <c>createAsyncWith</c>. The purity check covers <c>compute</c> up to its first <c>await</c> that suspends; an
    /// <c>await</c> on an already-completed task does not suspend.
    /// </remarks>
    let createAsync (compute: CancellationToken -> Task<'T>) =
        AsyncMemo<'T>.Create(Graph.Current, compute, ScopeMode.PureAsync)

    /// <summary>
    /// An async value that owns the nodes <c>compute</c> creates up to its first <c>await</c> that suspends, disposed before
    /// the next flight starts and with the async value. An <c>await</c> on an already-completed task does not suspend.
    /// </summary>
    /// <remarks>
    /// A node created after that <c>await</c> by a continuation on the graph thread belongs to the graph's root; to keep it in
    /// the flight, create it inside <c>runWithOwner</c> with <c>getOwner ()</c> captured before the <c>await</c>. An async value
    /// created and read in <c>compute</c> before that <c>await</c> restarts its flight on every settle and never settles.
    /// </remarks>
    let createAsyncWith (compute: CancellationToken -> Task<'T>) =
        AsyncMemo<'T>.Create(Graph.Current, compute, ScopeMode.Owning)

    /// <summary>
    /// A source whose value arrives later, settled by hand rather than computed.
    /// </summary>
    let createAsyncSource<'T> () =
        AsyncSource<'T>(Graph.Current)

    /// <summary>
    /// Substitutes <c>fallback</c> for as long as <c>body</c> is suspended, so the suspension stops here instead of
    /// propagating to everything downstream.
    /// </summary>
    /// <remarks>
    /// <c>fallback</c> receives the boundary's last value, <c>ValueNone</c> before its first; returning it keeps the
    /// last value shown while <c>body</c> reloads. Owns the nodes <c>body</c> creates and replaces them on every re-run.
    /// An async value created and read in <c>body</c> never settles: create it outside and read it in <c>body</c>.
    /// </remarks>
    let createSuspense (fallback: 'T voption -> 'T) (body: unit -> 'T) =
        Boundary.Suspense (Graph.Current, body, fallback)

    /// <summary>Substitutes <c>recover ex last</c> when <c>body</c> throws.</summary>
    /// <remarks>
    /// <c>last</c> is the boundary's last value, <c>ValueNone</c> before its first. Owns the nodes <c>body</c> creates
    /// and replaces them on every re-run. An async value created and read in <c>body</c> never settles: create it
    /// outside and read it in <c>body</c>.
    /// </remarks>
    let createErrorBoundary (recover: exn -> 'T voption -> 'T) (body: unit -> 'T) =
        Boundary.Errors (Graph.Current, body, recover)

    /// <summary>Substitutes <c>fallback last</c> while <c>body</c> is suspended and <c>recover ex last</c> when it throws.</summary>
    /// <remarks>
    /// <c>last</c> is the boundary's last value, <c>ValueNone</c> before its first. Owns the nodes <c>body</c> creates
    /// and replaces them on every re-run. An async value created and read in <c>body</c> never settles: create it
    /// outside and read it in <c>body</c>.
    /// </remarks>
    let createBoundary (fallback: 'T voption -> 'T) (recover: exn -> 'T voption -> 'T) (body: unit -> 'T) =
        Boundary.Catching (Graph.Current, body, fallback, recover)

    /// <summary>
    /// Runs <c>body</c> without recording anything it reads.
    /// </summary>
    let untrack (body: unit -> 'T) =
        Graph.Current.Untrack body

    /// <summary>
    /// Runs <c>body</c> with effects deferred, so a group of writes produces one run
    /// of each effect rather than one per write.
    /// </summary>
    let batch (body: unit -> 'T) =
        Graph.Current.Batch body

    /// <summary>
    /// Registers a cleanup with the innermost enclosing scope. Inside the body
    /// of an effect, owning memo, owning async value or boundary that is the
    /// computation's own scope, so it runs before the next re-run as well as at
    /// disposal. On a disposed scope the cleanup runs immediately, untracked
    /// and with effects deferred until it returns.
    /// </summary>
    /// <remarks>
    /// Raises <c>InvalidOperationException</c> inside a pure body: <c>createMemo</c>,
    /// <c>createAsync</c>, a projection row's reader and a lookup's <c>f</c> or
    /// <c>affected</c>.
    /// </remarks>
    let onCleanup (f: unit -> unit) =
        Graph.Current.OnCleanup f

    /// <summary>
    /// The innermost enclosing scope: the scope <c>onCleanup</c> registers with and
    /// new nodes attach to. Captured before an <c>await</c>, it is the scope
    /// <c>runWithOwner</c> needs after it.
    /// </summary>
    /// <remarks>
    /// Raises <c>InvalidOperationException</c> inside a pure body, as <c>onCleanup</c>
    /// does.
    /// </remarks>
    let getOwner () : Owner =
        Graph.Current.CurrentOwner

    /// <summary>
    /// Runs <c>body</c> with <c>owner</c> as the scope new nodes and cleanups attach to.
    /// Tracking is unchanged. A node created under a disposed owner is disposed
    /// as it attaches.
    /// </summary>
    /// <remarks>
    /// Must run on the graph's thread. Raises <c>InvalidOperationException</c>
    /// inside a pure body, as <c>onCleanup</c> does.
    /// </remarks>
    let runWithOwner (owner: Owner) (body: unit -> 'T) : 'T =
        let graph = Graph.Current
        graph.CurrentOwner |> ignore
        graph.RunOwned (owner, body)

    /// <summary>
    /// Creates a nested scope and runs <c>body</c> in it, handing back the owner so
    /// the whole subtree can be disposed at once.
    /// </summary>
    let createRoot (body: Owner -> 'T) =
        Graph.Current.CreateRoot body

    /// <summary>
    /// Runs the effects already queued on the current graph, including inside a batch.
    /// </summary>
    /// <remarks>
    /// Work posted from another thread stays in the inbox; <see cref="M:Ranvier.Graph.Pump"/>
    /// applies it and then flushes.
    /// </remarks>
    let flush () =
        Graph.Current.Flush ()

    /// <summary>
    /// A keyed collection derived from a source collection: a key set, and one
    /// row per key, separately observable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>keyOf</c> chooses identity. By id gives keyed reuse; by reference gives
    /// Solid's unkeyed semantics. Duplicate keys fail the pass.
    /// </para>
    /// <para>
    /// <c>factory</c> runs once per key, untracked, when the key enters, inside a
    /// scope that lives until the key is removed. It receives an accessor for
    /// the key's latest item and returns the row's reader. The row's value is
    /// the reader's result. The reader runs when the row is read and is stale;
    /// it re-runs when the key's item changes or a value it read changes.
    /// </para>
    /// <para>
    /// A survivor wakes its readers only when its item changes under the
    /// graph's equality policy. A reader that creates an owned node (a memo, effect,
    /// async value, boundary, root, projection, lookup or <c>onCleanup</c>) raises
    /// <c>InvalidOperationException</c>, and the exception becomes the row's error.
    /// Create such nodes in the factory body. A node created inside an owning
    /// memo, owning async value or boundary read by the reader belongs to that
    /// node's scope.
    /// </para>
    /// <para>
    /// The pass that evaluates <c>source</c> and <c>keyOf</c> owns the nodes they
    /// create: they are disposed before the next pass and with the projection.
    /// </para>
    /// </remarks>
    let createProjectionWith (keyOf: 'T -> 'K) (factory: (unit -> 'T) -> (unit -> 'V)) (source: unit -> 'T seq) : Projection<'K, 'V> =
        new KeyedProjection<'T, 'K, 'V> (Graph.Current, keyOf, Unchecked.defaultof<'T -> 'V>, factory, source) :> Projection<'K, 'V>

    /// <summary>
    /// <c>createProjectionWith keyOf (fun item -> fun () -> map (item ())) source</c>,
    /// with one fewer node per key. <c>map</c> re-runs when the key's item changes or
    /// a value it read changes, and raises <c>InvalidOperationException</c> if it
    /// creates an owned node; per-key nodes belong in <c>createProjectionWith</c>'s
    /// factory.
    /// </summary>
    let createProjection (keyOf: 'T -> 'K) (map: 'T -> 'V) (source: unit -> 'T seq) : Projection<'K, 'V> =
        new KeyedProjection<'T, 'K, 'V> (Graph.Current, keyOf, map, Unchecked.defaultof<_>, source) :> Projection<'K, 'V>

    /// <summary>
    /// <c>createProjectionWith</c> keyed by position: Solid's <c>indexArray</c>. The
    /// factory runs once per slot, and the accessor returns the slot's current
    /// item.
    /// </summary>
    let createIndexProjectionWith (factory: (unit -> 'T) -> (unit -> 'V)) (source: unit -> 'T seq) : Projection<int, 'V> =
        new IndexProjection<'T, 'V> (Graph.Current, Unchecked.defaultof<'T -> 'V>, factory, source) :> Projection<int, 'V>

    /// <summary>
    /// <c>createProjection</c> keyed by position: the row at a slot survives its
    /// item changing.
    /// </summary>
    let createIndexProjection (map: 'T -> 'V) (source: unit -> 'T seq) : Projection<int, 'V> =
        new IndexProjection<'T, 'V> (Graph.Current, map, Unchecked.defaultof<_>, source) :> Projection<int, 'V>

    /// <summary>
    /// A pointwise derived collection over an open key domain: a cell is built
    /// for each key read, and a source change recomputes only the keys
    /// returned by <c>affected</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>affected prev next</c> must name every key whose value can differ between
    /// the two states. A key left out keeps its stale value; an extra key costs
    /// one recomputation.
    /// </para>
    /// <para>
    /// <c>f</c> is pure: a key whose <c>f</c> creates an owned node fails with
    /// <c>InvalidOperationException</c>. <c>affected</c> is pure too: a node it creates
    /// fails every live key. <c>source</c> owns the nodes it creates, as
    /// <c>createMemoWith</c> does. Effects woken while a read computes keys run
    /// after the read returns.
    /// </para>
    /// </remarks>
    let createLookup (f: 'S -> 'K -> 'V) (affected: 'S -> 'S -> 'K seq) (source: unit -> 'S) : Lookup<'K, 'V> =
        new LookupOf<'S, 'K, 'V> (Graph.Current, f, affected, source) :> Lookup<'K, 'V>

    /// <summary>
    /// Membership in a single-valued selection: <c>selector.Get k</c> is true for
    /// the selected key and false for every other. A selection change wakes
    /// the readers of the previous and the next key only.
    /// </summary>
    /// <remarks>
    /// Solid's <c>createSelector</c>: <c>createLookup</c> with
    /// <c>affected = fun prev next -> [ prev; next ]</c>. Keys compare structurally,
    /// as the lookup's cell map does.
    /// </remarks>
    let createSelector (source: unit -> 'K) : Lookup<'K, bool> =
        let comparer = HashIdentity.Structural<'K>
        createLookup (fun (s: 'K) (k: 'K) -> comparer.Equals (s, k)) (fun prev next -> [ prev; next ]) source

    /// <summary>
    /// A memo holding <c>select ()</c>. While the inner value stays equal under the graph's equality policy, the memo
    /// keeps its previous <c>Some</c> instance and its dependents stay asleep.
    /// </summary>
    /// <remarks>
    /// A hand-written <c>createMemo</c> returning <c>Some</c> allocates a new wrapper per run and, on .NET, wakes its
    /// dependents on every run. A re-run that selects the same instance performs no inner comparison. Under
    /// <c>StructuralPolicy</c> the memo's own cutoff adds one deep compare of the inner value per re-run.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">Called inside a pure body, such as a <c>createMemo</c> body.</exception>
    /// <example>
    /// <code lang="fsharp">
    /// let todo3 = createOptionMemo (fun () -> state.Value.Todos |> List.tryFind (fun t -> t.Id = 3))
    /// </code>
    /// </example>
    let createOptionMemo (select: unit -> 'A option) : Memo<'A option> =
        let graph = Graph.Current
        let equal = graph.Options.Equality.Comparer<'A>()
        let last = ref None

        let compute () =
            let next = select ()

            match last.Value, next with
            | Some previous, Some current when
                Identity.same previous current
                || equal.Equals (previous, current)
                ->
                last.Value
            | _ ->
                last.Value <- next
                next

        Memo.Create (graph, compute, ScopeMode.Pure)

/// <summary>Writes to a <c>Signal</c> computed from its current value.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Signal =
    /// <summary>
    /// Writes <c>f signal.Peek</c> to <c>signal</c>. Readers wake only when the result differs from the current value
    /// under the graph's equality policy.
    /// </summary>
    /// <remarks>The read is untracked.</remarks>
    /// <exception cref="T:System.InvalidOperationException">Called off the graph's thread under a guarded graph.</exception>
    /// <example>
    /// <code lang="fsharp">
    /// Signal.update store (fun s -> { s with Owner.Home.City = "Oslo" })
    /// </code>
    /// </example>
    let update (signal: Signal<'S>) (f: 'S -> 'S) : unit =
        signal.Value <- f signal.Peek

/// <summary>Keyed copy-and-update over F# lists.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module List =
    /// <summary>Replaces the first element <c>x</c> of <c>xs</c> whose key equals <c>key</c> with <c>f x</c>.</summary>
    /// <returns>
    /// <c>xs</c> itself when no key matches or <c>f x</c> is <c>x</c> (an equal value for a value type or a string, NaN included);
    /// otherwise a new list whose tail after the match is the tail of <c>xs</c>.
    /// </returns>
    /// <remarks>
    /// Keys compare under <c>HashIdentity.Structural</c>, the comparer of projection row identity. Allocates one cell
    /// per element up to and including the match, plus a scratch array of the prefix.
    /// </remarks>
    /// <example>
    /// <code lang="fsharp">
    /// todos |> List.updateBy (fun t -> t.Id) 3 (fun t -> { t with Title = "three" })
    /// </code>
    /// </example>
    let updateBy (keyOf: 'T -> 'K) (key: 'K) (f: 'T -> 'T) (xs: 'T list) : 'T list =
        let keys = HashIdentity.Structural<'K>
        let mutable rest = xs
        let mutable index = 0

        while not rest.IsEmpty
              && not (keys.Equals (keyOf rest.Head, key)) do
            rest <- rest.Tail
            index <- index + 1

        match rest with
        | [] -> xs
        | x :: tail ->
            let y = f x

            if Identity.unchanged x y then
                xs
            else
                let prefix = Array.zeroCreate index
                let mutable walk = xs

                for i in 0 .. index - 1 do
                    prefix[i] <- walk.Head
                    walk <- walk.Tail

                let mutable result = y :: tail

                for i in index - 1 .. -1 .. 0 do
                    result <- prefix[i] :: result

                result

/// <summary>Keyed copy-and-update over arrays.</summary>
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Array =
    /// <summary>Replaces the first element <c>x</c> of <c>xs</c> whose key equals <c>key</c> with <c>f x</c>.</summary>
    /// <returns>
    /// <c>xs</c> itself when no key matches or <c>f x</c> is <c>x</c> (an equal value for a value type or a string, NaN included);
    /// otherwise a copy of <c>xs</c>.
    /// </returns>
    /// <remarks>Keys compare under <c>HashIdentity.Structural</c>, the comparer of projection row identity.</remarks>
    /// <example>
    /// <code lang="fsharp">
    /// todos |> Array.updateBy (fun t -> t.Id) 3 (fun t -> { t with Title = "three" })
    /// </code>
    /// </example>
    let updateBy (keyOf: 'T -> 'K) (key: 'K) (f: 'T -> 'T) (xs: 'T array) : 'T array =
        let keys = HashIdentity.Structural<'K>
        let mutable index = 0

        while index < xs.Length
              && not (keys.Equals (keyOf xs[index], key)) do
            index <- index + 1

        if index = xs.Length then
            xs
        else
            let y = f xs[index]

            if Identity.unchanged xs[index] y then
                xs
            else
                let copy = Array.copy xs
                copy[index] <- y
                copy

[<AutoOpen>]
module GraphExtensions =
    type Graph with

        /// <summary>
        /// Activates the graph, runs <c>body</c>, and restores the previous ambient
        /// graph — the whole of the common case in one call.
        /// </summary>
        /// <remarks>
        /// The graph is not disposed afterwards: it outlives the call, which is
        /// what makes the effects created inside <c>body</c> keep running.
        /// </remarks>
        member this.Run(body: unit -> 'T) : 'T =
            use _ = this.Activate ()
            body ()
