namespace Ranvier.CSharp

open System
open System.Collections.Generic
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Ranvier

/// <summary>
/// Factories for the nodes of the active graph, one per <c>Api</c> function, taking delegates.
/// </summary>
/// <remarks>
/// Every member resolves <c>Graph.Current</c>: call it inside <c>graph.Run</c> or while a
/// <c>graph.Activate ()</c> scope is open. Outside an active graph it raises
/// <c>InvalidOperationException</c>.
/// </remarks>
/// <example>
/// <code lang="csharp">
/// using Ranvier;
/// using Ranvier.CSharp;
/// using static Ranvier.CSharp.Reactive;
///
/// var graph = new Graph();
/// graph.Run(() =>
/// {
///     var count = Signal(1);
///     var doubled = Memo(() => count.Value * 2);
///     Effect(() => Console.WriteLine(doubled.Value));
///     count.Value = 2;
/// });
/// </code>
/// </example>
[<AbstractClass; Sealed>]
type Reactive =
    /// <summary>An empty editable collection keyed by <c>keyOf</c>, with insertion order and reactive rows.</summary>
    static member KeyedCollection<'V, 'K when 'K: equality>(keyOf: Func<'V, 'K>) : KeyedCollection<'K, 'V> =
        if isNull keyOf then nullArg "keyOf"
        Api.createKeyedCollection keyOf.Invoke

    /// <summary>A settable source holding <c>initial</c>.</summary>
    static member Signal<'T>(initial: 'T) : Signal<'T> =
        Api.createSignal initial

    /// <summary>A settable source whose write cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member Signal<'T>(initial: 'T, comparer: IEqualityComparer<'T>) : Signal<'T> =
        Api.createSignalWithComparer comparer initial

    /// <summary>A pure derived value: <c>compute</c> re-runs when a value it read changes.</summary>
    /// <remarks>A node created inside <c>compute</c> raises <c>InvalidOperationException</c>; use <c>OwningMemo</c>.</remarks>
    static member Memo<'T>(compute: Func<'T>) : Memo<'T> =
        Api.createMemo (fun _ -> compute.Invoke ())

    /// <summary>A pure memo whose value cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member Memo<'T>(compute: Func<'T>, comparer: IEqualityComparer<'T>) : Memo<'T> =
        Api.createMemoWithComparer comparer (fun _ -> compute.Invoke ())

    /// <summary>
    /// A pure derived value that receives its previous value, or <c>seed</c> on its first run.
    /// </summary>
    static member Memo<'T>(compute: Func<'T, 'T>, seed: 'T) : Memo<'T> =
        Api.createMemo (fun previous -> compute.Invoke (ValueOption.defaultValue seed previous))

    /// <summary>A seeded pure memo whose value cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member Memo<'T>(compute: Func<'T, 'T>, seed: 'T, comparer: IEqualityComparer<'T>) : Memo<'T> =
        Api.createMemoWithComparer comparer (fun previous -> compute.Invoke (ValueOption.defaultValue seed previous))

    /// <summary>
    /// A value seeded by <c>seed ()</c> that accepts local edits through <c>Value</c>. An edit is dropped once the seed
    /// produces an unequal value.
    /// </summary>
    /// <remarks><c>IsEdited</c> reports whether an edit is in force, and <c>Reset ()</c> drops it.</remarks>
    static member Editable<'T>(seed: Func<'T>) : Editable<'T> =
        Api.createEditable (fun _ -> seed.Invoke ())

    /// <summary>
    /// A value seeded by <c>seed ()</c> that accepts local edits through <c>Value</c>. An edit stays in force until
    /// <c>Reset ()</c>, whatever the seed produces.
    /// </summary>
    /// <remarks><c>Upstream</c> reads the seed's current value while an edit is in force.</remarks>
    static member Draft<'T>(seed: Func<'T>) : Editable<'T> =
        Api.createDraft (fun _ -> seed.Invoke ())

    /// <summary>
    /// A derived value that owns the nodes <c>compute</c> creates. They are disposed before each re-run.
    /// </summary>
    static member OwningMemo<'T>(compute: Func<'T>) : Memo<'T> =
        Api.createMemoWith (fun _ -> compute.Invoke ())

    /// <summary>An owning memo whose value cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member OwningMemo<'T>(compute: Func<'T>, comparer: IEqualityComparer<'T>) : Memo<'T> =
        Api.createOwningMemoWithComparer comparer (fun _ -> compute.Invoke ())

    /// <summary>An effect: <c>body</c> runs after the current flush and again when a value it read changes.</summary>
    /// <remarks>The enclosing scope disposes the effect; dispose the returned handle to stop it earlier.</remarks>
    static member Effect(body: Action) : Effect =
        new Effect (Graph.Current, body)

    /// <summary>
    /// An effect split in two: <c>compute</c> tracks, and <c>act</c> runs untracked with its result.
    /// </summary>
    /// <remarks><c>act</c> runs only when <c>compute</c> settles on a value unequal to the last one acted on.</remarks>
    static member EffectOn<'T>(compute: Func<'T>, act: Action<'T>) : unit =
        Api.createEffectOn compute.Invoke act.Invoke

    /// <summary>A split effect whose action cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member EffectOn<'T>(compute: Func<'T>, act: Action<'T>, comparer: IEqualityComparer<'T>) : unit =
        Api.createEffectOnWithComparer comparer compute.Invoke act.Invoke

    /// <summary>
    /// A derived value that arrives later. Each change to a value read before the first <c>await</c> starts a new
    /// flight, and the token is cancelled when a newer flight supersedes it.
    /// </summary>
    static member Async<'T>(compute: Func<CancellationToken, Task<'T>>) : AsyncMemo<'T> =
        Api.createAsync (fun _ token -> compute.Invoke token)

    /// <summary>
    /// <c>Async</c>, with the value last published: read every input, then await <c>previous.SettledOr (seed)</c> or
    /// <c>previous.TrySettled ()</c>.
    /// </summary>
    static member Async<'T>(compute: Func<Previous<'T>, CancellationToken, Task<'T>>) : AsyncMemo<'T> =
        Api.createAsync (fun previous token -> compute.Invoke (previous, token))

    /// <summary>
    /// <c>Async</c>, owning the nodes <c>compute</c> creates before its first <c>await</c>.
    /// </summary>
    static member OwningAsync<'T>(compute: Func<CancellationToken, Task<'T>>) : AsyncMemo<'T> =
        Api.createAsyncWith (fun _ token -> compute.Invoke token)

    /// <summary>A source that stays pending until <c>Settle</c> or <c>Fail</c> is called on it.</summary>
    static member AsyncSource<'T>() : AsyncSource<'T> =
        Api.createAsyncSource<'T>()

    /// <summary>A value that shows <c>fallback ()</c> while <c>body</c> reads a pending source.</summary>
    /// <remarks>An error in <c>body</c> propagates to the boundary's readers.</remarks>
    static member Suspense<'T>(body: Func<'T>, fallback: Func<'T>) : Boundary<'T> =
        Api.createSuspense (fun _ -> fallback.Invoke ()) body.Invoke

    /// <summary>A suspense boundary whose value cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member Suspense<'T>(body: Func<'T>, fallback: Func<'T>, comparer: IEqualityComparer<'T>) : Boundary<'T> =
        Api.createSuspenseWithComparer comparer (fun _ -> fallback.Invoke ()) body.Invoke

    /// <summary>A value that shows <c>recover error</c> while <c>body</c> fails.</summary>
    /// <remarks>A pending read in <c>body</c> propagates to the boundary's readers.</remarks>
    static member ErrorBoundary<'T>(body: Func<'T>, recover: Func<exn, 'T>) : Boundary<'T> =
        Api.createErrorBoundary (fun ex _ -> recover.Invoke ex) body.Invoke

    /// <summary>An error boundary whose value cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member ErrorBoundary<'T>(body: Func<'T>, recover: Func<exn, 'T>, comparer: IEqualityComparer<'T>) : Boundary<'T> =
        Api.createErrorBoundaryWithComparer comparer (fun ex _ -> recover.Invoke ex) body.Invoke

    /// <summary>A value that shows <c>fallback ()</c> while <c>body</c> is pending and <c>recover error</c> while it fails.</summary>
    static member Boundary<'T>(body: Func<'T>, fallback: Func<'T>, recover: Func<exn, 'T>) : Boundary<'T> =
        Api.createBoundary (fun _ -> fallback.Invoke ()) (fun ex _ -> recover.Invoke ex) body.Invoke

    /// <summary>A pending/error boundary whose value cutoff uses <c>comparer</c> instead of the graph's equality policy.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>comparer</c> is null.</exception>
    static member Boundary<'T>(body: Func<'T>, fallback: Func<'T>, recover: Func<exn, 'T>, comparer: IEqualityComparer<'T>) : Boundary<'T> =
        Api.createBoundaryWithComparer comparer (fun _ -> fallback.Invoke ()) (fun ex _ -> recover.Invoke ex) body.Invoke

    /// <summary>
    /// A command running <c>execute</c>, enabled while <c>canExecute ()</c> is true and <c>policy</c> allows. The command
    /// raises its events from an effect of its own, owned by the current scope.
    /// </summary>
    /// <remarks>
    /// <c>canExecute</c> re-runs when a value it read changes, as a memo's body does; a null <c>canExecute</c> is always
    /// true. <c>execute</c> receives the parameter and a token cancelled by <c>Cancel</c>, <c>Dispose</c> and
    /// <c>CommandPolicy.CancelPrevious</c>.
    /// </remarks>
    /// <exception cref="T:System.ArgumentNullException"><c>execute</c> is null.</exception>
    static member Command
        (
            execute: Func<obj, CancellationToken, Task>,
            [<Optional; DefaultParameterValue(null: Func<bool>)>] canExecute: Func<bool>,
            [<Optional; DefaultParameterValue(CommandPolicy.Disable)>] policy: CommandPolicy
        ) : ReactiveCommand =
        if isNull execute then
            nullArg "execute"

        new ReactiveCommand (Graph.Current, (fun parameter token -> execute.Invoke (parameter, token)), canExecute, policy, false, true)

    /// <summary>
    /// A command running <c>execute</c> synchronously, enabled while <c>canExecute ()</c> is true. The writes
    /// <c>execute</c> makes are batched.
    /// </summary>
    /// <exception cref="T:System.ArgumentNullException"><c>execute</c> is null.</exception>
    static member Command(execute: Action<obj>, [<Optional; DefaultParameterValue(null: Func<bool>)>] canExecute: Func<bool>) : ReactiveCommand =
        if isNull execute then
            nullArg "execute"

        new ReactiveCommand (Graph.Current, ReactiveCommand.Synchronous execute, canExecute, CommandPolicy.Disable, true, true)

    /// <summary>A memo that is true while any of <c>sources</c> is pending.</summary>
    /// <remarks>
    /// The memo reads each source's status with <c>Graph.TrackStatus</c>, so it re-runs when a source it read changes, and
    /// a failed source counts as settled.
    /// </remarks>
    static member AnyPending([<ParamArray>] sources: INode[]) : Memo<bool> =
        let graph = Graph.Current
        let sources = Array.copy sources

        Api.createMemo (fun _ ->
            sources
            |> Array.exists (fun node ->
                graph.TrackStatus node &&& Status.Pending
                <> Status.None))

    /// <summary>
    /// <c>Suspense</c> whose <c>fallback</c> receives the boundary's last value, or <c>seed</c> before its first.
    /// </summary>
    /// <remarks>Returning its argument keeps the last value shown while <c>body</c> reloads.</remarks>
    static member Suspense<'T>(body: Func<'T>, fallback: Func<'T, 'T>, seed: 'T) : Boundary<'T> =
        Api.createSuspense (fun previous -> fallback.Invoke (ValueOption.defaultValue seed previous)) body.Invoke

    /// <summary>
    /// <c>ErrorBoundary</c> whose <c>recover</c> receives the error and the boundary's last value, or <c>seed</c> before
    /// its first.
    /// </summary>
    static member ErrorBoundary<'T>(body: Func<'T>, recover: Func<exn, 'T, 'T>, seed: 'T) : Boundary<'T> =
        Api.createErrorBoundary (fun ex previous -> recover.Invoke (ex, ValueOption.defaultValue seed previous)) body.Invoke

    /// <summary>
    /// <c>Boundary</c> whose handlers receive the boundary's last value, or <c>seed</c> before its first.
    /// </summary>
    static member Boundary<'T>(body: Func<'T>, fallback: Func<'T, 'T>, recover: Func<exn, 'T, 'T>, seed: 'T) : Boundary<'T> =
        Api.createBoundary
            (fun previous -> fallback.Invoke (ValueOption.defaultValue seed previous))
            (fun ex previous -> recover.Invoke (ex, ValueOption.defaultValue seed previous))
            body.Invoke

    /// <summary>Runs <c>body</c> without recording anything it reads.</summary>
    static member Untrack<'T>(body: Func<'T>) : 'T =
        Api.untrack body.Invoke

    /// <summary>Runs <c>body</c> without recording anything it reads.</summary>
    static member Untrack(body: Action) : unit =
        Api.untrack body.Invoke

    /// <summary>Runs <c>body</c> with effects deferred until it returns, so a group of writes runs each effect once.</summary>
    static member Batch<'T>(body: Func<'T>) : 'T =
        Api.batch body.Invoke

    /// <summary>Runs <c>body</c> with effects deferred until it returns, so a group of writes runs each effect once.</summary>
    static member Batch(body: Action) : unit =
        Api.batch body.Invoke

    /// <summary>
    /// Registers <c>cleanup</c> with the innermost enclosing scope. Inside an effect, owning memo or boundary it runs
    /// before the next re-run as well as at disposal.
    /// </summary>
    static member OnCleanup(cleanup: Action) : unit =
        Api.onCleanup cleanup.Invoke

    /// <summary>
    /// The innermost enclosing scope. Captured before an <c>await</c>, it is the owner <c>RunWithOwner</c> needs after it.
    /// </summary>
    static member CurrentOwner: Owner = Api.getOwner ()

    /// <summary>Runs <c>body</c> with <c>owner</c> as the scope new nodes and cleanups attach to.</summary>
    static member RunWithOwner<'T>(owner: Owner, body: Func<'T>) : 'T =
        Api.runWithOwner owner body.Invoke

    /// <summary>Runs <c>body</c> with <c>owner</c> as the scope new nodes and cleanups attach to.</summary>
    static member RunWithOwner(owner: Owner, body: Action) : unit =
        Api.runWithOwner owner body.Invoke

    /// <summary>
    /// Runs <c>body</c> in a nested scope. Disposing the owner it receives disposes every node created inside.
    /// </summary>
    static member Root<'T>(body: Func<Owner, 'T>) : 'T =
        Api.createRoot body.Invoke

    /// <summary>
    /// Runs <c>body</c> in a nested scope. Disposing the owner it receives disposes every node created inside.
    /// </summary>
    static member Root(body: Action<Owner>) : unit =
        Api.createRoot body.Invoke

    /// <summary>Runs the effects already queued on the current graph.</summary>
    static member Flush() : unit =
        Api.flush ()

    /// <summary>
    /// A keyed collection over <c>source ()</c>: one row per key, each separately observable. A row re-runs
    /// <c>map</c> only when its item changes.
    /// </summary>
    /// <remarks>Duplicate keys fail the pass.</remarks>
    static member Projection<'T, 'K, 'V when 'K: equality>
        (source: Func<IEnumerable<'T>>, keyOf: Func<'T, 'K>, map: Func<'T, 'V>)
        : Projection<'K, 'V> =
        Api.createProjection keyOf.Invoke map.Invoke source.Invoke

    /// <summary>A collection over <c>source ()</c> keyed by position.</summary>
    static member IndexProjection<'T, 'V>(source: Func<IEnumerable<'T>>, map: Func<'T, 'V>) : Projection<int, 'V> =
        Api.createIndexProjection map.Invoke source.Invoke

    /// <summary>
    /// A pointwise derived collection: <c>Get key</c> computes <c>f (source (), key)</c>, and a source change
    /// recomputes only the keys returned by <c>affected (previous, next)</c>.
    /// </summary>
    /// <remarks><c>affected</c> must return every key whose value can differ between the two states.</remarks>
    static member Lookup<'S, 'K, 'V when 'K: equality>
        (source: Func<'S>, f: Func<'S, 'K, 'V>, affected: Func<'S, 'S, IEnumerable<'K>>)
        : Lookup<'K, 'V> =
        Api.createLookup (fun state key -> f.Invoke (state, key)) (fun previous next -> affected.Invoke (previous, next)) source.Invoke

    /// <summary>
    /// Membership in a single-valued selection: <c>Get key</c> is true for the selected key only. A selection change
    /// wakes the readers of the previous and the next key.
    /// </summary>
    static member Selector<'K when 'K: equality>(source: Func<'K>) : Lookup<'K, bool> =
        Api.createSelector source.Invoke
