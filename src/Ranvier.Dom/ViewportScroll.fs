namespace Ranvier.Dom

/// <summary>The viewport's scroll position in CSS pixels: <c>window.scrollX</c> and <c>window.scrollY</c>.</summary>
/// <remarks>
/// Browsers and the native engine keep whole-pixel offsets. The native engine truncates a fractional target toward
/// zero, and Chromium rounds it to the nearest pixel.
/// </remarks>
type ScrollPosition = { X: float; Y: float }

/// <summary>A scroll target or delta in CSS pixels, as <c>ScrollToOptions</c>.</summary>
type ScrollRequest =
    {
        /// <summary>The horizontal coordinate; <c>None</c> keeps the current one.</summary>
        Left: float option
        /// <summary>The vertical coordinate; <c>None</c> keeps the current one.</summary>
        Top: float option
        Behavior: ScrollBehavior
    }

/// <summary>Scroll position reads and programmatic scrolling of one document's viewport.</summary>
/// <remarks>
/// Targets clamp to the scrollable range. A scroll that moves the viewport dispatches <c>scroll</c> and then
/// <c>scrollend</c> in a later rendering update, which <see cref="T:Ranvier.Dom.ViewportService"/> reports as
/// <c>Scrolled</c> and <c>ScrollEnded</c>. After the document leaves its window, reads return the origin and
/// scrolls have no effect.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ViewportScrollService =
    /// <summary>Reads the current position.</summary>
    abstract Position: unit -> ScrollPosition
    /// <summary><c>window.scrollTo(options)</c>.</summary>
    abstract ScrollTo: request: ScrollRequest -> unit
    /// <summary><c>window.scrollBy(options)</c>: the request is a delta from the current position.</summary>
    abstract ScrollBy: request: ScrollRequest -> unit

/// <summary>Builds scroll requests.</summary>
[<RequireQualifiedAccess>]
module ScrollRequest =
    /// <summary>Both coordinates, with the default <c>auto</c> behavior.</summary>
    let point (x: float) (y: float) =
        {
            Left = Some x
            Top = Some y
            Behavior = ScrollBehavior.Auto
        }
