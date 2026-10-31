namespace Ranvier.Dom

/// <summary>Constructs scroll-driven and view-driven animation timelines in a borrowed document.</summary>
/// <remarks>
/// A timeline's <c>currentTime</c> and <c>duration</c> are percentages (<c>CSSUnitValue</c>s in <c>percent</c>) of the
/// source's scroll range, or of a view timeline's cover range, or none while the source has no scrollable box. An
/// invalid inset raises TypeError. A created timeline is an
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

    /// <summary>
    /// Constructs a view timeline over the subject's visibility in its nearest scroll container along the axis, by
    /// default block, with the scroll container's edges inset by the inset string, by default <c>auto</c>.
    /// </summary>
    abstract CreateViewTimeline: subject: Element * ?axis: ScrollAxis * ?inset: string -> ViewTimeline

    /// <summary>
    /// Constructs a view timeline whose start and end insets are the first and second items of insets; a single item
    /// insets both edges. Each item is a length, a percentage or the keyword <c>auto</c>.
    /// </summary>
    abstract CreateViewTimelineWithInsets:
        subject: Element * insets: CSSNumericValueOrDOMStringOrCSSOMKeywordValue array * ?axis: ScrollAxis -> ViewTimeline
