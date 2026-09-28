[<AutoOpen>]
module Ranvier.Tests.Support

open System.Threading
open System.Threading.Tasks
open Expecto
open Ranvier

#if !FABLE_COMPILER
// .NET only: every caller measures allocations, and Fable.Mocha has no test rewriting.
/// <summary>
/// Runs <c>test</c> in an untraced build only. A traced build reports each of its cases as skipped, with reason
/// "traced build".
/// </summary>
/// <remarks>For allocation assertions: the trace log allocates by design.</remarks>
let untracedOnly (test: Test) : Test =
#if RANVIER_TRACE
    test
    |> Test.replaceTestCode (fun name _ -> TestLabel (name, TestCase (Sync (fun () -> skiptest "traced build"), Normal), Normal))
#else
    test
#endif
#endif

/// <summary>
/// Memo and async memo construction the suite uses on both platforms. On .NET these are the public constructors;
/// Fable drops those constructors, so under Fable they call <c>Create</c> with the same mode.
/// </summary>
type Make =
    static member Memo(graph: Graph, compute: 'T voption -> 'T) : Memo<'T> =
#if FABLE_COMPILER
        Memo<'T>.Create (graph, compute, ScopeMode.Pure)
#else
        Memo (graph, compute)
#endif

    static member Memo(graph: Graph, compute: 'T voption -> 'T, owning: bool) : Memo<'T> =
#if FABLE_COMPILER
        Memo<'T>.Create (graph, compute, (if owning then ScopeMode.Owning else ScopeMode.Pure))
#else
        Memo (graph, compute, owning)
#endif

    static member AsyncMemo<'T>(graph: Graph, compute: Previous<'T> -> CancellationToken -> Task<'T>) : AsyncMemo<'T> =
#if FABLE_COMPILER
        AsyncMemo<'T>.Create (graph, compute, ScopeMode.PureAsync)
#else
        new AsyncMemo<'T> (graph, compute)
#endif

    static member AsyncMemo<'T>
        (graph: Graph, compute: Previous<'T> -> CancellationToken -> Task<'T>, owning: bool)
        : AsyncMemo<'T> =
#if FABLE_COMPILER
        AsyncMemo<'T>.Create (graph, compute, (if owning then ScopeMode.Owning else ScopeMode.PureAsync))
#else
        new AsyncMemo<'T> (graph, compute, owning)
#endif

/// <summary>A task already completed with <c>value</c>. Under Fable's inline delivery it settles synchronously,
/// as <c>Task.FromResult</c> does on .NET.</summary>
let completed<'T> (value: 'T) : Task<'T> =
    let source = TaskCompletionSource<'T>()
    source.SetResult value
    source.Task

/// <summary>A task already faulted with <c>error</c>. Fable's task library omits <c>Task.FromException</c>.</summary>
let faulted<'T> (error: exn) : Task<'T> =
    let source = TaskCompletionSource<'T>()
    source.SetException error
    source.Task

/// <summary>True when <c>error</c> is the failure of a disposed node. Under Fable every failure qualifies:
/// <c>ObjectDisposedException</c> compiles to a plain <c>Exception</c>.</summary>
let isDisposedError (error: exn) =
#if FABLE_COMPILER
    not (isNull error)
#else
    error :? System.ObjectDisposedException
#endif

/// <summary>The value a node holds before its first value: <c>Unchecked.defaultof</c> in generic code, which is
/// <c>null</c> under Fable.</summary>
let unset<'T> : 'T = Unchecked.defaultof<'T>
