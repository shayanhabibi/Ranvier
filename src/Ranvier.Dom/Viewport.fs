namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary>Window dimensions in CSS pixels and the device pixel ratio.</summary>
type ViewportMetrics =
    {
        /// <summary><c>window.innerWidth</c>: the viewport width including scrollbars.</summary>
        InnerWidth: int
        InnerHeight: int
        /// <summary><c>window.outerWidth</c>; a native host reports its viewport.</summary>
        OuterWidth: int
        OuterHeight: int
        /// <summary><c>window.devicePixelRatio</c>: device pixels per CSS pixel, including page zoom.</summary>
        DevicePixelRatio: float
    }

/// <summary>A viewport change, delivered once to each subscriber in the engine's dispatch order.</summary>
/// <remarks>
/// Within one rendering update the order is <c>Resized</c>, <c>Scrolled</c>, <c>ScrollEnded</c>, then
/// <c>ScaleChanged</c>, as HTML's update-the-rendering steps run resize, scroll and media query evaluation.
/// </remarks>
[<RequireQualifiedAccess>]
type ViewportChange =
    /// <summary>A window <c>resize</c> event, with the metrics read when it fired.</summary>
    | Resized of metrics: ViewportMetrics
    /// <summary>A <c>scroll</c> event of the document's viewport.</summary>
    | Scrolled
    /// <summary>A <c>scrollend</c> event of the document's viewport.</summary>
    | ScrollEnded
    /// <summary>A <c>visibilitychange</c> event, with <c>document.hidden</c> when it fired.</summary>
    | VisibilityChanged of hidden: bool
    /// <summary>A window <c>focus</c> (true) or <c>blur</c> (false) event.</summary>
    | Activation of focused: bool
    /// <summary>A <c>change</c> of the resolution media query for the previous ratio, with the new ratio.</summary>
    | ScaleChanged of devicePixelRatio: float

/// <summary>A <c>MediaQueryList</c> owned by its holder; disposing it releases the engine object.</summary>
type ViewportMediaQuery =
    inherit IDisposable
    abstract Media: string
    abstract Matches: bool
    /// <summary>The list as the EventTarget of its <c>change</c> event.</summary>
    abstract Target: EventTarget

/// <summary>The window of one document and the reads the shared contract has no member for.</summary>
type ViewportProvider =
    {
        Window: EventTarget
        Document: Document
        Metrics: unit -> ViewportMetrics
        /// <summary><c>window.matchMedia</c>.</summary>
        MatchMedia: string -> ViewportMediaQuery
    }

/// <summary>Viewport dimensions, visibility and change delivery of one document.</summary>
/// <remarks>
/// Observers run on the dispatching thread in subscription order. Disposing the service removes its window
/// listeners, releases its media query list and drops its subscribers; after the window's provider is disposed it
/// drops only the subscribers.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ViewportService =
    inherit IDisposable
    /// <summary>Reads the live metrics.</summary>
    abstract Current: unit -> ViewportMetrics
    /// <summary><c>document.hidden</c>.</summary>
    abstract Hidden: unit -> bool
    /// <summary>Adds an observer and returns an idempotent removal handle.</summary>
    abstract Subscribe: observer: (ViewportChange -> unit) -> IDisposable

/// <summary>Creates viewport services over a window EventTarget.</summary>
[<RequireQualifiedAccess>]
module Viewport =
    /// <summary>The media query that matches exactly the device pixel ratio <c>ratio</c>.</summary>
    let resolutionQuery (ratio: float) =
        "(resolution: " + string ratio + "dppx)"

    /// <summary>A service over <c>provider</c>'s window.</summary>
    let create (provider: ViewportProvider) : ViewportService =
        if isNull (box provider) then
            nullArg "provider"

        if isNull (box provider.Window) then
            nullArg "provider.Window"

        if isNull (box provider.Document) then
            nullArg "provider.Document"

        let window = provider.Window
        let document = provider.Document
        let mutable disposed = false
        let observers = List<int * (ViewportChange -> unit)>()
        let mutable nextObserver = 0

        let publish change =
            for _, observer in observers.ToArray () do
                if not disposed then
                    observer change

        let atWindow (event: Event) =
            event.eventPhase = 2us

        // Viewport scroll, scrollend and visibilitychange bubble from the document; element scroll events do not.
        let atDocument (event: Event) =
            event.eventPhase = 3us

        let mutable armed: (ViewportMediaQuery * (Event -> unit)) option = None

        let disarm () =
            match armed with
            | Some (query, listener) ->
                armed <- None
                query.Target.removeEventListener ("change", listener)
                query.Dispose ()
            | None -> ()

        let rec arm () =
            let query =
                provider.MatchMedia (resolutionQuery (provider.Metrics ()).DevicePixelRatio)

            let rec listener (_: Event) =
                if not disposed then
                    disarm ()
                    arm ()
                    publish (ViewportChange.ScaleChanged (provider.Metrics ()).DevicePixelRatio)

            query.Target.addEventListener ("change", listener)
            armed <- Some (query, listener)

        let listeners: (string * (Event -> unit)) array =
            [|
                "resize",
                (fun event ->
                    if atWindow event then
                        publish (ViewportChange.Resized (provider.Metrics ())))
                "scroll",
                (fun event ->
                    if atDocument event then
                        publish ViewportChange.Scrolled)
                "scrollend",
                (fun event ->
                    if atDocument event then
                        publish ViewportChange.ScrollEnded)
                "visibilitychange",
                (fun event ->
                    if atDocument event then
                        publish (ViewportChange.VisibilityChanged document.hidden))
                "focus",
                (fun event ->
                    if atWindow event then
                        publish (ViewportChange.Activation true))
                "blur",
                (fun event ->
                    if atWindow event then
                        publish (ViewportChange.Activation false))
            |]

        for eventType, listener in listeners do
            window.addEventListener (eventType, listener)

        arm ()

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof ViewportService))

        { new ViewportService with
            member _.Current() =
                live ()
                provider.Metrics ()

            member _.Hidden() =
                live ()
                document.hidden

            member _.Subscribe observer =
                live ()

                if isNull (box observer) then
                    nullArg "observer"

                let id = nextObserver
                nextObserver <- nextObserver + 1
                observers.Add ((id, observer))

                { new IDisposable with
                    member _.Dispose() =
                        observers.RemoveAll (fun (key, _) -> key = id)
                        |> ignore
                }

            member _.Dispose() =
                if not disposed then
                    disposed <- true
                    observers.Clear ()

                    try
                        for eventType, listener in listeners do
                            window.removeEventListener (eventType, listener)

                        disarm ()
                    with :? ObjectDisposedException ->
                        armed <- None
        }
