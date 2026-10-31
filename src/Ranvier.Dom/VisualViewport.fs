namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary>The <c>VisualViewport</c> members of one window, in CSS pixels.</summary>
type VisualViewportState =
    {
        /// <summary>The visual viewport's left edge relative to the layout viewport.</summary>
        OffsetLeft: float
        OffsetTop: float
        /// <summary>The visual viewport's left edge relative to the document's initial containing block.</summary>
        PageLeft: float
        PageTop: float
        /// <summary>The visible width, excluding scrollbars and obscured insets, in CSS pixels at the page scale.</summary>
        Width: float
        Height: float
        /// <summary>The page scale: 1 when unzoomed, 2 at a twofold pinch zoom.</summary>
        Scale: float
    }

/// <summary>The CSS pixel widths of the viewport's edges that host UI covers.</summary>
type VisualViewportInsets =
    {
        Top: float
        Right: float
        Bottom: float
        Left: float
    }

/// <summary>Why a visual viewport read or host input has no result.</summary>
[<RequireQualifiedAccess>]
type VisualViewportUnavailable =
    /// <summary>The window has no frame: its document was detached or its host destroyed.</summary>
    | Detached
    /// <summary>The provider has no such input, with the provider's reason.</summary>
    | Unsupported of reason: string

/// <summary>A visual viewport event, with the members read when it fired.</summary>
[<RequireQualifiedAccess>]
type VisualViewportChange =
    /// <summary>A <c>resize</c> event: the size or the scale changed.</summary>
    | Resized of state: VisualViewportState
    /// <summary>A <c>scroll</c> event: the offsets changed.</summary>
    | Scrolled of state: VisualViewportState

/// <summary>The visual viewport of one window and the host inputs that drive it.</summary>
type VisualViewportProvider =
    {
        /// <summary>The window's <c>visualViewport</c>.</summary>
        Viewport: VisualViewport
        /// <summary>The members, or <c>Detached</c> once the window has no frame.</summary>
        Read: unit -> Result<VisualViewportState, VisualViewportUnavailable>
        /// <summary>Sets the page scale with the scaled document's scroll position as the origin.</summary>
        SetPageScale: float * int * int -> Result<unit, VisualViewportUnavailable>
        /// <summary>Sets the obscured insets.</summary>
        SetObscuredInsets: VisualViewportInsets -> Result<unit, VisualViewportUnavailable>
    }

/// <summary>Visual viewport reads, host inputs and change delivery of one window.</summary>
/// <remarks>
/// Within one rendering update the window's <c>resize</c> and <c>scroll</c> events fire before the visual viewport's,
/// and a visual viewport <c>resize</c> before its <c>scroll</c>. Observers run on the dispatching thread in
/// subscription order. Disposing the service removes its listeners and drops its subscribers.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type VisualViewportService =
    inherit IDisposable
    /// <summary>Reads the members.</summary>
    abstract Current: unit -> Result<VisualViewportState, VisualViewportUnavailable>
    /// <summary>
    /// Sets the page scale, as a pinch zoom does, and scrolls the scaled document to <c>originX</c>, <c>originY</c>.
    /// </summary>
    /// <exception cref="T:System.ArgumentException"><c>scale</c> is not finite and above 0.</exception>
    abstract SetPageScale: scale: float * originX: int * originY: int -> Result<unit, VisualViewportUnavailable>
    /// <summary>Sets the viewport edges that host UI covers.</summary>
    /// <exception cref="T:System.ArgumentException">An inset is not finite and at least 0.</exception>
    abstract SetObscuredInsets: insets: VisualViewportInsets -> Result<unit, VisualViewportUnavailable>
    /// <summary>Adds an observer and returns an idempotent removal handle.</summary>
    abstract Subscribe: observer: (VisualViewportChange -> unit) -> IDisposable

/// <summary>Creates visual viewport services.</summary>
[<RequireQualifiedAccess>]
module VisualViewports =
    let private finite (value: float) =
        not (Double.IsNaN value)
        && not (Double.IsInfinity value)

    /// <summary>A service over <c>provider</c>'s visual viewport.</summary>
    let create (provider: VisualViewportProvider) : VisualViewportService =
        if isNull (box provider) then
            nullArg "provider"

        if isNull (box provider.Viewport) then
            nullArg "provider.Viewport"

        let viewport = provider.Viewport
        let mutable disposed = false
        let observers = List<int * (VisualViewportChange -> unit)>()
        let mutable nextObserver = 0

        let publish (change: VisualViewportState -> VisualViewportChange) =
            match provider.Read () with
            | Ok state ->
                for _, observer in observers.ToArray () do
                    if not disposed then
                        observer (change state)
            | Error _ -> ()

        let listeners: (string * (Event -> unit)) array =
            [|
                "resize", (fun _ -> publish VisualViewportChange.Resized)
                "scroll", (fun _ -> publish VisualViewportChange.Scrolled)
            |]

        for eventType, listener in listeners do
            viewport.addEventListener (eventType, listener)

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof VisualViewportService))

        { new VisualViewportService with
            member _.Current() =
                live ()
                provider.Read ()

            member _.SetPageScale(scale, originX, originY) =
                live ()

                if not (finite scale) || scale <= 0.0 then
                    invalidArg (nameof scale) "The page scale must be finite and above 0"

                provider.SetPageScale (scale, originX, originY)

            member _.SetObscuredInsets insets =
                live ()

                if isNull (box insets) then
                    nullArg (nameof insets)

                for value in [ insets.Top; insets.Right; insets.Bottom; insets.Left ] do
                    if not (finite value) || value < 0.0 then
                        invalidArg (nameof insets) "Each inset must be finite and at least 0"

                provider.SetObscuredInsets insets

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
                            viewport.removeEventListener (eventType, listener)
                    with :? ObjectDisposedException ->
                        ()
        }
