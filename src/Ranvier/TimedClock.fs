namespace Ranvier

open System

/// <summary>A reusable one-shot timer, created disarmed by a monotonic clock.</summary>
/// <remarks>A callback may race with disarm or disposal; consumers must validate their current deadline.</remarks>
[<AbstractClass; AllowNullLiteral>]
type TimedTimer() =
    /// <summary>Replaces the outstanding wait with a nonnegative duration; never invokes the callback synchronously.</summary>
    abstract Arm: TimeSpan -> unit
    /// <summary>Cancels the outstanding wait.</summary>
    abstract Disarm: unit -> unit
    /// <summary>Releases the timer and cancels its outstanding wait. Repeated disposal is harmless.</summary>
    abstract Dispose: unit -> unit

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>A monotonic clock and timer factory for timed reactive values.</summary>
/// <remarks>Clock origins are arbitrary. An adapter with worker-thread callbacks must support concurrent timer operations.</remarks>
[<AbstractClass; AllowNullLiteral>]
type TimedClock() =
    /// <summary>Elapsed milliseconds from an arbitrary origin, advancing monotonically.</summary>
    abstract NowMilliseconds: float
    /// <summary>Creates a disarmed timer that invokes the supplied callback after an armed wait.</summary>
    /// <remarks>Callback exceptions follow the host's dispatcher and timer exception handling.</remarks>
    abstract CreateTimer: Action -> TimedTimer

type private DeadlineTimer(now: unit -> float, create: Action -> (float -> unit) * (unit -> unit) * (unit -> unit), callback: Action) as this =
    inherit TimedTimer()
    let gate = obj ()
    let mutable active = false
    let mutable disposed = false
    let mutable deadline = 0.
    let arm, disarm, dispose = create (Action this.Fire)

    let nativeDue (remaining: float) =
        min 2147483647. (Math.Ceiling remaining)

    member private _.Fire() =
        let deliver =
            lock gate (fun () ->
                if disposed || not active then
                    false
                else
                    let remaining = deadline - now ()

                    if remaining > 0. then
                        arm (nativeDue remaining)
                        false
                    else
                        active <- false
                        true)

        if deliver then
            callback.Invoke ()

    override _.Arm(delay) =
        if delay < TimeSpan.Zero then
            raise (ArgumentOutOfRangeException (nameof delay))

        lock gate (fun () ->
            if disposed then
                raise (ObjectDisposedException (nameof TimedTimer))

            deadline <- now () + delay.TotalMilliseconds
            active <- true
            arm (nativeDue delay.TotalMilliseconds))

    override _.Disarm() =
        lock gate (fun () ->
            if not disposed then
                active <- false
                disarm ())

    override _.Dispose() =
        lock gate (fun () ->
            if not disposed then
                disposed <- true
                active <- false
                dispose ())

/// <summary>System and .NET time-provider adapters for timed values.</summary>
[<RequireQualifiedAccess>]
module TimedClock =
    let private systemClock =
        lazy
            { new TimedClock() with
                member _.NowMilliseconds =
#if FABLE_COMPILER
                    Fable.Core.JsInterop.emitJsExpr () "performance.now()"
#else
                    float (Diagnostics.Stopwatch.GetTimestamp ())
                    * 1000.
                    / float Diagnostics.Stopwatch.Frequency
#endif
                member this.CreateTimer(callback) =
                    if isNull callback then
                        nullArg (nameof callback)

                    let create (fire: Action) =
#if FABLE_COMPILER
                        let mutable handle: obj = null

                        let stop () =
                            if not (isNull handle) then
                                Fable.Core.JsInterop.emitJsExpr handle "clearTimeout($0)"
                                handle <- null

                        let start delay =
                            stop ()
                            handle <- Fable.Core.JsInterop.emitJsExpr (fire, delay) "setTimeout($0, $1)"

                        start, stop, stop
#else
                        let timer =
                            new Threading.Timer (
                                Threading.TimerCallback (fun _ -> fire.Invoke ()),
                                null,
                                Threading.Timeout.Infinite,
                                Threading.Timeout.Infinite
                            )

                        (fun delay ->
                            timer.Change (int delay, Threading.Timeout.Infinite)
                            |> ignore),
                        (fun () ->
                            timer.Change (Threading.Timeout.Infinite, Threading.Timeout.Infinite)
                            |> ignore),
                        (fun () -> timer.Dispose ())
#endif
                    new DeadlineTimer ((fun () -> this.NowMilliseconds), create, callback) :> TimedTimer
            }

    /// <summary>A reusable system clock using monotonic timestamps and one-shot native timers.</summary>
    /// <remarks>Long waits are chunked; positive fractional milliseconds round up.</remarks>
    let system = systemClock.Value

#if NET8_0_OR_GREATER && !FABLE_COMPILER
    /// <summary>Uses a .NET time provider's timestamps and timers with the same deadline and rounding rules as the system clock.</summary>
    /// <exception cref="T:System.ArgumentNullException"><c>provider</c> is null.</exception>
    let ofTimeProvider (provider: TimeProvider) =
        if isNull provider then
            nullArg (nameof provider)

        { new TimedClock() with
            member _.NowMilliseconds =
                float (provider.GetTimestamp ()) * 1000.
                / float provider.TimestampFrequency

            member this.CreateTimer(callback) =
                if isNull callback then
                    nullArg (nameof callback)

                let create (fire: Action) =
                    let timer =
                        provider.CreateTimer (
                            Threading.TimerCallback (fun _ -> fire.Invoke ()),
                            null,
                            Threading.Timeout.InfiniteTimeSpan,
                            Threading.Timeout.InfiniteTimeSpan
                        )

                    (fun (delay: float) ->
                        timer.Change (TimeSpan.FromMilliseconds delay, Threading.Timeout.InfiniteTimeSpan)
                        |> ignore),
                    (fun () ->
                        timer.Change (Threading.Timeout.InfiniteTimeSpan, Threading.Timeout.InfiniteTimeSpan)
                        |> ignore),
                    timer.Dispose

                new DeadlineTimer ((fun () -> this.NowMilliseconds), create, callback) :> TimedTimer
        }
#endif
