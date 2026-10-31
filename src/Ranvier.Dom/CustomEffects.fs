namespace Ranvier.Dom

open System

/// <summary>An application tick that a custom animation effect drives with its progress.</summary>
/// <remarks>
/// Dispose revokes the tick: no tick runs after Dispose returns, including ticks already scheduled. The effect stays
/// valid with its timing and can still be read through the shared <see cref="T:Ranvier.Dom.AnimationEffect"/> members.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CustomEffectRegistration =
    inherit IDisposable

    /// <summary>The effect, to attach to an animation or read with getTiming and getComputedTiming.</summary>
    abstract Effect: AnimationEffect

/// <summary>A custom effect that DocumentTimeline.animate attached to a new, playing animation.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CustomAnimation =
    inherit CustomEffectRegistration

    /// <summary>The playing animation of the effect on the document timeline.</summary>
    abstract Animation: WebAnimation

/// <summary>Constructs custom animation effects, whose ticks receive the effect's progress, in a borrowed document.</summary>
/// <remarks>
/// A tick receives the effect's progress for each animation frame where that progress is not null: frames in order, and
/// within a frame in animation order. A native host delivers a frame's ticks from its run loop after the rendering update
/// that computed them, so a DOM write in a tick renders in the next frame; the browser provider, whose engine has no
/// CustomEffect, calls them from requestAnimationFrame. A tick may write the DOM, control animations, create effects and
/// dispose effects; an update it causes delivers its ticks later, never inside the tick. An exception a tick raises goes
/// to the host's listener error reporting and later ticks still run. Invalid timing raises
/// <see cref="T:Ranvier.Dom.AnimationException"/> with the WebIDL name (TypeError). Disposing the service revokes every
/// effect it created. Native objects require their live host and owning thread.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CustomEffectService =
    inherit IDisposable

    /// <summary>Constructs an effect with the timing, by default the IDL defaults, that calls tick with its progress.</summary>
    abstract CreateCustomEffect: tick: (float -> unit) * ?timing: EffectTiming -> CustomEffectRegistration

    /// <summary>Constructs an effect lasting duration milliseconds that calls tick with its progress.</summary>
    abstract CreateCustomEffectFor: tick: (float -> unit) * duration: float -> CustomEffectRegistration

    /// <summary>Plays a new custom effect on the document timeline (DocumentTimeline.animate) with the options' timing and id.</summary>
    abstract Animate: tick: (float -> unit) * ?options: CustomAnimationOptions -> CustomAnimation
