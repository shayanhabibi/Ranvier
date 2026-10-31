namespace Ranvier.Dom

/// <summary>A Web Animations engine error, by its WebIDL exception name and message.</summary>
exception AnimationException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>Constructs keyframe effects, animations and document timelines in a borrowed document.</summary>
/// <remarks>
/// Keyframes are the keyframe-sequence form: each <see cref="T:Ranvier.Dom.Keyframe"/> holds an optional offset, easing
/// and composite and its properties, named as IDL attributes (<c>opacity</c>, <c>backgroundColor</c>) or custom
/// properties. <c>Element.animate</c>, <c>getAnimations</c>, <c>Document.timeline</c> and the members of the created
/// objects are the shared DOM interfaces. Engine errors raise <see cref="T:Ranvier.Dom.AnimationException"/> with the
/// WebIDL name: TypeError for unsorted or out-of-range offsets and invalid easing or composite strings. Disposal
/// invalidates the service and cancels its pending <c>Ready</c> and <c>Finished</c> operations; created objects stay
/// valid. Native objects require their live host and owning thread.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type AnimationService =
    inherit System.IDisposable

    /// <summary>Constructs a keyframe effect on the target, or on no target for null, with the timing of options.</summary>
    abstract CreateKeyframeEffect: target: Element * keyframes: Keyframe array * ?options: KeyframeEffectOptions -> KeyframeEffect

    /// <summary>Constructs a keyframe effect on the target, or on no target for null, lasting duration milliseconds.</summary>
    abstract CreateKeyframeEffectFor: target: Element * keyframes: Keyframe array * duration: float -> KeyframeEffect

    /// <summary>Constructs a new effect with the source's target, keyframes, timing and composite operations.</summary>
    abstract CopyKeyframeEffect: source: KeyframeEffect -> KeyframeEffect

    /// <summary>Constructs an idle animation of the effect on the timeline, by default the document timeline.</summary>
    abstract CreateAnimation: ?effect: AnimationEffect * ?timeline: AnimationTimeline -> WebAnimation

    /// <summary>Constructs an idle animation of the effect with no timeline.</summary>
    abstract CreateAnimationWithoutTimeline: ?effect: AnimationEffect -> WebAnimation

    /// <summary>Constructs a document timeline whose zero is originTime milliseconds after the document's, by default 0.</summary>
    abstract CreateDocumentTimeline: ?originTime: float -> DocumentTimeline

    /// <summary><c>Animation.ready</c>: settles with the animation when the ready promise current at the call resolves.</summary>
    /// <remarks>
    /// <c>ResourceFailure.Aborted "AbortError"</c> when the engine rejects that promise (<c>cancel()</c>, or a reset of the
    /// pending tasks); <c>Cancelled</c> on <c>Cancel</c>, <c>Dispose</c> or service disposal; <c>Invalidated</c> when the
    /// animation's document is replaced or destroyed. The terminal callback runs on the service's owner context, never
    /// inside the call. An animation of another document raises <see cref="T:System.ArgumentException"/>.
    /// </remarks>
    abstract Ready: animation: WebAnimation * terminal: (ResourceOutcome<WebAnimation> -> unit) -> ResourceOperation<WebAnimation>

    /// <summary><c>Animation.finished</c>: settles as <c>Ready</c> does, for the finished promise current at the call.</summary>
    abstract Finished: animation: WebAnimation * terminal: (ResourceOutcome<WebAnimation> -> unit) -> ResourceOperation<WebAnimation>
