namespace Ranvier.Docs.Maps

#if RANVIER_TRACE
open System
open System.Threading.Tasks
open Ranvier

/// <summary>A button beneath a map, and the action it runs.</summary>
type Control = { Label: string; Run: unit -> unit }

/// <summary>A pretend remote service whose requests stay pending until a control answers them.</summary>
/// <remarks>
/// A new request cancels the one before it, as a graph that asks again has moved on, so a superseded flight drops.
/// <c>Settle</c> and <c>Fail</c> answer the request still pending.
/// </remarks>
type Desk<'T>() =
    let mutable pending: TaskCompletionSource<'T> option = None

    let answer (complete: TaskCompletionSource<'T> -> unit) =
        match pending with
        | Some request ->
            pending <- None
            complete request
        | None -> ()

    /// <summary>A request that completes when <c>Settle</c> or <c>Fail</c> answers it.</summary>
    /// <remarks>The argument is read for its dependency only.</remarks>
    member _.Quote(_: 'R) : Task<'T> =
        let request = TaskCompletionSource<'T>()
        let older = pending
        pending <- Some request

        older
        |> Option.iter (fun older ->
#if FABLE_COMPILER
            // fable-library's TaskCompletionSource has no TrySetCanceled.
            older.SetException (OperationCanceledException ()))
#else
            older.TrySetCanceled () |> ignore)
#endif

        request.Task

    member _.Settle(value: 'T) =
        answer (fun request -> request.SetResult value)

    member _.Fail(message: string) =
        answer (fun request -> request.SetException (Exception message))

    /// <summary>The requests awaiting an answer: 1 or 0.</summary>
    member _.Pending = if pending.IsSome then 1 else 0

/// <summary>Where a map's events come from.</summary>
type MapSource =
    /// <summary>A scenario run against a live traced graph; it returns the map's controls.</summary>
    | Live of scenario: (Graph -> Control list)
    /// <summary>A recording, played back from graph creation.</summary>
    | Recorded of events: TraceEvent[]

[<AutoOpen>]
module Helpers =

    /// <summary>The controls of a map, from (label, action) pairs, in order.</summary>
    let controls (items: (string * (unit -> unit)) list) : Control list =
        items
        |> List.map (fun (label, run) -> { Label = label; Run = run })
#endif
