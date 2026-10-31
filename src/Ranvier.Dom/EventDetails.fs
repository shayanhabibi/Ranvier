namespace Ranvier.Dom

open System

/// <summary>The ErrorEventInit fields. <c>error</c> (type any) is outside the shared contract.</summary>
type ErrorFields =
    {
        Message: string
        Filename: string
        Lineno: uint32
        Colno: uint32
    }

    /// <summary>Empty strings and zero positions.</summary>
    static member Default =
        {
            Message = ""
            Filename = ""
            Lineno = 0u
            Colno = 0u
        }

/// <summary>The ProgressEventInit fields.</summary>
type ProgressFields =
    {
        LengthComputable: bool
        Loaded: float
        Total: float
    }

    /// <summary>No computable length and zero progress.</summary>
    static member Default =
        {
            LengthComputable = false
            Loaded = 0.0
            Total = 0.0
        }

/// <summary>The AnimationEventInit or TransitionEventInit fields; <c>Name</c> is the animation or property name.</summary>
/// <remarks><c>animation</c>, a CSSAnimation or CSSTransition, is outside the shared contract.</remarks>
type AnimationFields =
    {
        Name: string
        ElapsedTime: float
        PseudoElement: string
    }

    /// <summary>An empty name, zero elapsed time and no pseudo-element.</summary>
    static member Default =
        {
            Name = ""
            ElapsedTime = 0.0
            PseudoElement = ""
        }

/// <summary>The AnimationPlaybackEventInit fields as numbers; None is null.</summary>
type AnimationPlaybackFields =
    {
        CurrentTime: float option
        TimelineTime: float option
    }

    /// <summary>Null times.</summary>
    static member Default =
        {
            CurrentTime = None
            TimelineTime = None
        }

/// <summary>The HashChangeEventInit fields.</summary>
type HashChangeFields =
    {
        OldURL: string
        NewURL: string
    }

    /// <summary>Empty URLs.</summary>
    static member Default = { OldURL = ""; NewURL = "" }

/// <summary>The MediaQueryListEventInit fields.</summary>
type MediaQueryFields =
    {
        Media: string
        Matches: bool
    }

    /// <summary>An empty, unmatched query.</summary>
    static member Default = { Media = ""; Matches = false }

/// <summary>The CompositionEventInit fields; the view is always null.</summary>
type CompositionFields =
    {
        Data: string
        Detail: int
    }

    /// <summary>Empty data and detail 0.</summary>
    static member Default = { Data = ""; Detail = 0 }

/// <summary>Constructs untrusted event detail records in one document.</summary>
/// <remarks>
/// Each method returns the typed event with the given fields. Dispatch them with <c>EventTarget.dispatchEvent</c>;
/// a dispatched record runs no engine behaviour. Engine-dispatched events of the same interfaces expose the same
/// fields.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type EventDetailService =
    inherit IDisposable
    /// <summary>An untrusted ErrorEvent of <c>eventType</c>.</summary>
    abstract CreateErrorEvent: eventType: string * options: EventOptions * fields: ErrorFields -> ErrorEvent
    /// <summary>An untrusted ProgressEvent of <c>eventType</c>.</summary>
    abstract CreateProgressEvent: eventType: string * options: EventOptions * fields: ProgressFields -> ProgressEvent
    /// <summary>An untrusted AnimationEvent of <c>eventType</c>; <c>Name</c> is its animationName.</summary>
    abstract CreateAnimationEvent: eventType: string * options: EventOptions * fields: AnimationFields -> AnimationEvent
    /// <summary>An untrusted TransitionEvent of <c>eventType</c>; <c>Name</c> is its propertyName.</summary>
    abstract CreateTransitionEvent: eventType: string * options: EventOptions * fields: AnimationFields -> TransitionEvent
    /// <summary>An untrusted ContentVisibilityAutoStateChangeEvent of <c>eventType</c>.</summary>
    abstract CreateContentVisibilityEvent: eventType: string * options: EventOptions * skipped: bool -> ContentVisibilityAutoStateChangeEvent
    /// <summary>An untrusted AnimationPlaybackEvent of <c>eventType</c>.</summary>
    abstract CreateAnimationPlaybackEvent: eventType: string * options: EventOptions * fields: AnimationPlaybackFields -> AnimationPlaybackEvent
    /// <summary>An untrusted PageTransitionEvent of <c>eventType</c>.</summary>
    abstract CreatePageTransitionEvent: eventType: string * options: EventOptions * persisted: bool -> PageTransitionEvent
    /// <summary>An untrusted HashChangeEvent of <c>eventType</c>.</summary>
    abstract CreateHashChangeEvent: eventType: string * options: EventOptions * fields: HashChangeFields -> HashChangeEvent
    /// <summary>An untrusted MediaQueryListEvent of <c>eventType</c>.</summary>
    abstract CreateMediaQueryListEvent: eventType: string * options: EventOptions * fields: MediaQueryFields -> MediaQueryListEvent
    /// <summary>An untrusted PopStateEvent of <c>eventType</c> with a null state.</summary>
    abstract CreatePopStateEvent: eventType: string * options: EventOptions * hasUAVisualTransition: bool -> PopStateEvent
    /// <summary>An untrusted CompositionEvent of <c>eventType</c>.</summary>
    abstract CreateCompositionEvent: eventType: string * options: EventOptions * fields: CompositionFields -> CompositionEvent

/// <summary>Argument validation shared by event detail providers.</summary>
module EventDetailContract =
    /// <summary>Raises ArgumentNullException for a null event type.</summary>
    let eventType (value: string) =
        if isNull value then
            nullArg "eventType"

        value

    /// <summary>Raises ArgumentNullException for a null string field.</summary>
    let text (name: string) (value: string) =
        if isNull value then
            nullArg name

        value

    /// <summary>Raises ArgumentOutOfRangeException for NaN or an infinity, which an IDL double rejects.</summary>
    let finite (name: string) (value: float) =
        if Double.IsNaN value || Double.IsInfinity value then
            raise (ArgumentOutOfRangeException (name, value, "An IDL double is finite"))

        value
