namespace Ranvier.CSharp

open System
open System.Runtime.CompilerServices
open System.Threading.Tasks
open Ranvier

/// <summary>Extension methods on graphs and signals.</summary>
[<Extension; AbstractClass; Sealed>]
type GraphExtensions =

    /// <summary>Activates the graph, runs <c>body</c>, and restores the previous ambient graph.</summary>
    /// <remarks>The graph outlives the call, so the effects created inside <c>body</c> keep running.</remarks>
    [<Extension>]
    static member Run<'T>(graph: Graph, body: Func<'T>) : 'T =
        use _ = graph.Activate ()
        body.Invoke ()

    /// <summary>Activates the graph, runs <c>body</c>, and restores the previous ambient graph.</summary>
    /// <remarks>The graph outlives the call, so the effects created inside <c>body</c> keep running.</remarks>
    [<Extension>]
    static member Run(graph: Graph, body: Action) : unit =
        use _ = graph.Activate ()
        body.Invoke ()

    /// <summary>Writes <c>update</c> applied to the signal's current value, read untracked.</summary>
    [<Extension>]
    static member Update<'T>(signal: Signal<'T>, update: Func<'T, 'T>) : unit =
        Signal.update signal update.Invoke

/// <summary>
/// Awaitable reads of <c>Previous.Settled</c> for C#: the value or a seed, or a <c>(HasValue, Value)</c> pair.
/// </summary>
/// <remarks>
/// Each completes when <c>Settled</c> completes. A completed <c>Settled</c> yields a completed <c>ValueTask</c> without
/// allocating; a pending one allocates one continuation. Read every input, then await, as with <c>Settled</c>.
/// </remarks>
[<Extension; AbstractClass; Sealed>]
type PreviousExtensions =

    /// <summary>The value last published, or <c>seed</c> before the first.</summary>
    [<Extension>]
    static member SettledOr<'T>(previous: Previous<'T>, seed: 'T) : ValueTask<'T> =
        let settled = previous.Settled

        if settled.IsCompletedSuccessfully then
            ValueTask<'T>(ValueOption.defaultValue seed settled.Result)
        else
            ValueTask<'T>(
                task {
                    let! last = settled
                    return ValueOption.defaultValue seed last
                }
            )

    /// <summary>
    /// <c>(true, value)</c> with the value last published, or <c>(false, default)</c> before the first.
    /// </summary>
    [<Extension>]
    static member TrySettled<'T>(previous: Previous<'T>) : [<TupleElementNames([| "HasValue"; "Value" |])>] ValueTask<struct (bool * 'T)> =
        let settled = previous.Settled

        if settled.IsCompletedSuccessfully then
            ValueTask<struct (bool * 'T)>(PreviousExtensions.Unpack settled.Result)
        else
            ValueTask<struct (bool * 'T)>(
                task {
                    let! last = settled
                    return PreviousExtensions.Unpack last
                }
            )

    static member private Unpack(last: 'T voption) : struct (bool * 'T) =
        match last with
        | ValueSome value -> struct (true, value)
        | ValueNone -> struct (false, Unchecked.defaultof<'T>)

/// <summary>
/// Live operators on projections. Each returns a node that updates per changed row rather than recomputing the whole
/// collection.
/// </summary>
/// <remarks>The result belongs to the scope active when the operator is called.</remarks>
[<Extension; AbstractClass; Sealed>]
type ProjectionExtensions =

    /// <summary>The rows whose value satisfies <c>predicate</c>, in upstream order.</summary>
    [<Extension>]
    static member Where<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, predicate: Func<'V, bool>) =
        Projection.filter predicate.Invoke upstream

    /// <summary>Each row's value mapped by <c>selector</c>, under the same key.</summary>
    [<Extension>]
    static member Select<'K, 'V, 'U when 'K: equality>(upstream: Projection<'K, 'V>, selector: Func<'V, 'U>) =
        Projection.map selector.Invoke upstream

    /// <summary>The rows ordered by <c>keySelector</c>, ascending and stable.</summary>
    [<Extension>]
    static member OrderBy<'K, 'V, 'S when 'K: equality and 'S: comparison>(upstream: Projection<'K, 'V>, keySelector: Func<'V, 'S>) =
        Projection.sortBy keySelector.Invoke upstream

    /// <summary>The rows grouped by <c>keySelector</c>, one inner projection per group.</summary>
    [<Extension>]
    static member GroupBy<'K, 'V, 'G when 'K: equality and 'G: equality>(upstream: Projection<'K, 'V>, keySelector: Func<'V, 'G>) =
        Projection.groupBy keySelector.Invoke upstream

    /// <summary>The first <c>count</c> rows.</summary>
    [<Extension>]
    static member Take<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, count: int) =
        Projection.take (fun () -> count) upstream

    /// <summary>The first <c>count ()</c> rows; the view moves when a value <c>count</c> read changes.</summary>
    [<Extension>]
    static member Take<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, count: Func<int>) =
        Projection.take count.Invoke upstream

    /// <summary>The rows after the first <c>count</c>.</summary>
    [<Extension>]
    static member Skip<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, count: int) =
        Projection.skip (fun () -> count) upstream

    /// <summary>The rows after the first <c>count ()</c>; the view moves when a value <c>count</c> read changes.</summary>
    [<Extension>]
    static member Skip<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, count: Func<int>) =
        Projection.skip count.Invoke upstream

    /// <summary>A window of <c>count ()</c> rows starting at <c>offset ()</c>.</summary>
    [<Extension>]
    static member Slice<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, offset: Func<int>, count: Func<int>) =
        Projection.sub offset.Invoke count.Invoke upstream

    /// <summary>A memo of <c>seed</c> folded over every row with <c>folder</c>. A row change re-folds every row.</summary>
    [<Extension>]
    static member Aggregate<'K, 'V, 'S when 'K: equality>(upstream: Projection<'K, 'V>, seed: 'S, folder: Func<'S, 'V, 'S>) =
        Projection.fold (fun state value -> folder.Invoke (state, value)) seed upstream

    /// <summary>
    /// A memo of <c>zero</c> folded over every row with <c>add</c>. A row change subtracts the old value and adds the
    /// new one; a throwing <c>subtract</c> re-folds every row.
    /// </summary>
    [<Extension>]
    static member Aggregate<'K, 'V, 'S when 'K: equality>(upstream: Projection<'K, 'V>, zero: 'S, add: Func<'S, 'V, 'S>, subtract: Func<'S, 'V, 'S>) =
        Projection.foldGroup (fun state value -> add.Invoke (state, value)) (fun state value -> subtract.Invoke (state, value)) zero upstream

    /// <summary>A memo of the sum of <c>selector</c> over every row, kept current per row change.</summary>
    [<Extension>]
    static member Sum<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, selector: Func<'V, int>) : Memo<int> =
        Projection.sumBy selector.Invoke upstream

    /// <summary>A memo of the sum of <c>selector</c> over every row, kept current per row change.</summary>
    [<Extension>]
    static member Sum<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, selector: Func<'V, int64>) : Memo<int64> =
        Projection.sumBy selector.Invoke upstream

    /// <summary>A memo of the sum of <c>selector</c> over every row, kept current per row change.</summary>
    [<Extension>]
    static member Sum<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, selector: Func<'V, decimal>) : Memo<decimal> =
        Projection.sumBy selector.Invoke upstream

    /// <summary>A memo of the sum of <c>selector</c> over every row, kept current per row change.</summary>
    /// <remarks>While the sum is infinite or NaN, each row change re-adds every row.</remarks>
    [<Extension>]
    static member Sum<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, selector: Func<'V, float>) : Memo<float> =
        Projection.sumBy selector.Invoke upstream

    /// <summary>A memo of how many rows satisfy <c>predicate</c>.</summary>
    [<Extension>]
    static member Count<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, predicate: Func<'V, bool>) : Memo<int> =
        Projection.countBy predicate.Invoke upstream

    /// <summary>A memo of whether any row satisfies <c>predicate</c>.</summary>
    [<Extension>]
    static member Any<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, predicate: Func<'V, bool>) : Memo<bool> =
        Projection.exists predicate.Invoke upstream

    /// <summary>A memo of whether every row satisfies <c>predicate</c>.</summary>
    [<Extension>]
    static member All<'K, 'V when 'K: equality>(upstream: Projection<'K, 'V>, predicate: Func<'V, bool>) : Memo<bool> =
        Projection.forall predicate.Invoke upstream
