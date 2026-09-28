namespace Ranvier.Docs.Maps

#if RANVIER_TRACE
open System
open System.Threading.Tasks
open Ranvier

/// <summary>The widget a live map renders for a control, and the action its value is written through.</summary>
type Widget =
    | Button of press: (unit -> unit)
    /// <summary>A range over the integers from <c>min</c> to <c>max</c>, inclusive; <c>set</c> runs on every movement.</summary>
    | Slider of min: int * max: int * start: int * set: (int -> unit)
    /// <summary>A number field; <c>set</c> runs when a number is committed.</summary>
    | Number of start: float * set: (float -> unit)
    /// <summary>A text field; <c>set</c> runs when the text is committed.</summary>
    | Text of start: string * set: (string -> unit)
    | Toggle of start: bool * set: (bool -> unit)

/// <summary>One action of a replay, with the log line shown before it.</summary>
type Step =
    {
        Log: string option
        Run: unit -> unit
    }

/// <summary>A control beneath a map: its widget on a live map, its steps on a replay.</summary>
type Control =
    {
        Label: string
        Widget: Widget
        Steps: Step list
    }

/// <summary>A pretend remote service whose requests stay pending until a control answers them.</summary>
/// <remarks>
/// By default a new request cancels the one before it, so a superseded flight drops. A queued desk keeps every
/// request, in the order made: <c>Settle</c> and <c>Fail</c> answer the oldest, <c>SettleNewest</c> and
/// <c>FailNewest</c> the newest.
/// </remarks>
type Desk<'T>(?queued: bool) =
    let queued = defaultArg queued false
    let pending = ResizeArray<TaskCompletionSource<'T>>()

    let answer (index: int) (complete: TaskCompletionSource<'T> -> unit) =
        if pending.Count > 0 then
            let i = if index < 0 then pending.Count - 1 else index
            let request = pending[i]
            pending.RemoveAt i
            complete request

    /// <summary>A request that completes when a control answers it.</summary>
    /// <remarks>The argument is read for its dependency only.</remarks>
    member _.Quote(_: 'R) : Task<'T> =
        let request = TaskCompletionSource<'T>()

        if not queued then
            for older in pending do
#if FABLE_COMPILER
                // fable-library's TaskCompletionSource has no TrySetCanceled.
                older.SetException (OperationCanceledException ())
#else
                older.TrySetCanceled () |> ignore
#endif

            pending.Clear ()

        pending.Add request
        request.Task

    member _.Settle(value: 'T) =
        answer 0 (fun request -> request.SetResult value)

    member _.Fail(message: string) =
        answer 0 (fun request -> request.SetException (Exception message))

    member _.SettleNewest(value: 'T) =
        answer -1 (fun request -> request.SetResult value)

    member _.FailNewest(message: string) =
        answer -1 (fun request -> request.SetException (Exception message))

    /// <summary>The requests awaiting an answer.</summary>
    member _.Pending = pending.Count

/// <summary>Where a map's events come from.</summary>
type MapSource =
    /// <summary>A scenario run against a live traced graph; it returns the map's controls.</summary>
    | Live of scenario: (Graph -> Control list)
    /// <summary>A scenario whose controls each run once, in order, before the map plays it back.</summary>
    | Replayed of scenario: (Graph -> Control list)

[<RequireQualifiedAccess>]
module Controls =

    /// <summary>The number in a field's text, or <c>None</c> for empty or non-numeric text.</summary>
    let parseNumber (text: string) : float option =
        // The empty check guards Fable, where Number("") is 0.
        match text.Trim () with
        | "" -> None
        | trimmed ->
            match Double.TryParse trimmed with
            | true, v -> Some v
            | _ -> None

    let internal steps (label: string) (show: 'V -> string) (values: 'V list) (set: 'V -> unit) : Step list =
        values
        |> List.map (fun v ->
            {
                Log = Some $"set %s{label} = %s{show v}"
                Run = fun () -> set v
            })

[<AutoOpen>]
module Helpers =

    /// <summary>The controls of a map, in order: the row order on a live map and the step order on a replay.</summary>
    let controls (items: Control list) : Control list = items

    let button (label: string) (press: unit -> unit) : Control =
        {
            Label = label
            Widget = Button press
            Steps = [ { Log = None; Run = press } ]
        }

    /// <summary>A slider over <c>min</c> to <c>max</c>, starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let slider (label: string) (min: int, max: int) (start: int) (replay: int list) (set: int -> unit) : Control =
        {
            Label = label
            Widget = Slider (min, max, start, set)
            Steps = Controls.steps label string replay set
        }

    /// <summary>A number field starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let number (label: string) (start: float) (replay: float list) (set: float -> unit) : Control =
        {
            Label = label
            Widget = Number (start, set)
            Steps = Controls.steps label string replay set
        }

    /// <summary>A text field starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let text (label: string) (start: string) (replay: string list) (set: string -> unit) : Control =
        {
            Label = label
            Widget = Text (start, set)
            Steps = Controls.steps label id replay set
        }

    /// <summary>A checkbox starting at <c>start</c>; a replay writes each of <c>replay</c>.</summary>
    let toggle (label: string) (start: bool) (replay: bool list) (set: bool -> unit) : Control =
        let show (b: bool) =
            if b then "true" else "false"

        {
            Label = label
            Widget = Toggle (start, set)
            Steps = Controls.steps label show replay set
        }
#endif
