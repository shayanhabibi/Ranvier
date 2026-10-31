namespace Ranvier.Dom

/// <summary>Constructs scroll-driven animation timelines in a borrowed document.</summary>
/// <remarks>
/// A timeline's <c>currentTime</c> and <c>duration</c> are percentages (<c>CSSUnitValue</c>s in <c>percent</c>) of the
/// source's scroll range, or none while the source has no scrollable box. A created timeline is an
/// <see cref="T:Ranvier.Dom.AnimationTimeline"/> for <see cref="T:Ranvier.Dom.AnimationService"/>.CreateAnimation and the
/// <c>timeline</c> option of <c>Element.animate</c>. Engine errors raise <see cref="T:Ranvier.Dom.AnimationException"/>
/// with the WebIDL name. Disposal invalidates the service; created timelines stay valid. Native objects require their
/// live host and owning thread.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type TimelineService =
    inherit System.IDisposable

    /// <summary>
    /// Constructs a scroll timeline over the source's scroll range along the axis, by default block. Without a source it
    /// follows the document's scrolling element.
    /// </summary>
    abstract CreateScrollTimeline: ?source: Element * ?axis: ScrollAxis -> ScrollTimeline

    /// <summary>Constructs a scroll timeline with a null source: it stays inactive, with no current time.</summary>
    abstract CreateScrollTimelineWithoutSource: ?axis: ScrollAxis -> ScrollTimeline
