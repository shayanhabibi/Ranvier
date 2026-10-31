namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary>Animation frames, timers and microtasks of one host over one millisecond clock.</summary>
/// <remarks>
/// Each scheduling call returns an idempotent cancellation; a callback cancelled before it starts will not run.
/// Callbacks run on the host thread. A callback scheduled from inside a frame callback runs at the next frame.
/// Disposing the scheduler cancels every pending callback and rejects later scheduling with ObjectDisposedException.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type FrameScheduler =
    inherit IDisposable
    /// <summary>The clock frame times are measured on, in milliseconds.</summary>
    abstract Now: unit -> float
    /// <summary>Runs <c>callback</c> with the frame time at the next animation frame.</summary>
    abstract Frame: callback: (float -> unit) -> IDisposable
    /// <summary>Runs <c>callback</c> once <c>milliseconds</c> (clamped to 0) have elapsed.</summary>
    abstract Timeout: milliseconds: float * callback: (unit -> unit) -> IDisposable
    /// <summary>Runs <c>callback</c> at the next microtask checkpoint.</summary>
    abstract Microtask: callback: (unit -> unit) -> IDisposable

/// <summary>The time left in an idle period, passed to each idle callback.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type IdleDeadline =
    /// <summary>Milliseconds left in the idle period, from 0 to 50; 0 once the period ends or when the callback timed
    /// out.</summary>
    abstract TimeRemaining: unit -> float
    /// <summary>True when the callback runs because its timeout elapsed.</summary>
    abstract DidTimeout: bool

/// <summary>Idle-period callbacks of one host.</summary>
/// <remarks>
/// Callbacks run in request order; a callback requested inside an idle callback runs in a later idle period.
/// Each request returns an idempotent cancellation. Disposing the scheduler cancels every pending callback and rejects
/// later requests with ObjectDisposedException.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type IdleScheduler =
    inherit IDisposable
    /// <summary>Runs <c>callback</c> in a later idle period. With <c>timeout</c>, the callback runs no later than
    /// <c>timeout</c> milliseconds from now, with <c>DidTimeout</c> true and no time remaining.</summary>
    abstract Idle: timeout: float option * callback: (IdleDeadline -> unit) -> IDisposable

/// <summary>Idle deadlines over a millisecond clock.</summary>
[<RequireQualifiedAccess>]
module IdleDeadline =
    /// <summary>The longest idle period, in milliseconds.</summary>
    [<Literal>]
    let MaxPeriod = 50.0

    /// <summary>A deadline at <c>deadline</c> on <c>now</c>'s clock.</summary>
    let until (now: unit -> float) (deadline: float) : IdleDeadline =
        { new IdleDeadline with
            member _.TimeRemaining() =
                max 0.0 (deadline - now ())

            member _.DidTimeout = false
        }

    /// <summary>The deadline of a callback that runs because its timeout elapsed.</summary>
    let timedOut: IdleDeadline =
        { new IdleDeadline with
            member _.TimeRemaining() = 0.0
            member _.DidTimeout = true
        }

/// <summary>The idle-callback capability of a host.</summary>
[<RequireQualifiedAccess>]
module IdleCapabilities =
    /// <summary>The <c>PlatformFacility</c> key <c>idle.callbacks</c>.</summary>
    let key = Capabilities.key CapabilityCategory.PlatformFacility "idle.callbacks"

    /// <summary>A provider whose observation of <c>key</c> reports idle callbacks as supported or not, with
    /// <c>reason</c> on each fact.</summary>
    let provider (providerId: string) (mechanism: string) (supported: bool) (reason: string) : CapabilityProvider =
        let fact =
            {
                State =
                    (if supported then
                         CapabilityState.Yes
                     else
                         CapabilityState.No)
                Reason = reason
            }

        let observation =
            { Capabilities.unknown key providerId mechanism "document" reason with
                Exported = fact
                Implemented = fact
                Enabled = fact
                Permitted =
                    {
                        State = CapabilityState.Yes
                        Reason = "No permission gates idle callbacks."
                    }
                Available = fact
                Value = Some (CapabilityValue.Boolean supported)
            }

        {
            Id = providerId
            KnownKeys = [| key |]
            Observe = fun requested _ -> Threading.Tasks.Task.FromResult (if requested = key then Some observation else None)
        }

/// <summary>A deterministic scheduler whose clock, frames and idle periods advance only when its owner calls them.</summary>
[<Sealed>]
type ManualFrameScheduler() =
    let mutable now = 0.0
    let mutable disposed = false
    let mutable next = 0
    let frames = List<int * (float -> unit)>()
    let timers = List<int * float * (unit -> unit)>()
    let microtasks = Queue<int * (unit -> unit)>()
    let idles = List<int * int * (IdleDeadline -> unit)>()
    let idleTimers = HashSet<int>()
    let cancelled = HashSet<int>()

    let live () =
        if disposed then
            raise (ObjectDisposedException (nameof FrameScheduler))

    let handle id =
        { new IDisposable with
            member _.Dispose() =
                cancelled.Add id |> ignore
        }

    let allocate () =
        live ()
        next <- next + 1
        next

    let take id =
        if cancelled.Remove id then false else not disposed

    let nextTimer () =
        timers
        |> Seq.filter (fun (id, _, _) -> not (cancelled.Contains id || idleTimers.Contains id))
        |> Seq.map (fun (_, deadline, _) -> deadline)
        |> Seq.fold min Double.PositiveInfinity

    /// <summary>Runs queued microtasks, and those they queue, until none remain.</summary>
    member _.RunMicrotasks() =
        while microtasks.Count > 0 do
            let id, callback = microtasks.Dequeue ()

            if take id then
                callback ()

    /// <summary>Advances the clock by <c>milliseconds</c> and runs each due timer in deadline order, with a
    /// microtask checkpoint after each.</summary>
    member this.Advance(milliseconds: float) =
        let target = now + max 0.0 milliseconds
        let mutable running = true

        while running do
            let due =
                timers
                |> Seq.filter (fun (_, deadline, _) -> deadline <= target)
                |> Seq.sortBy (fun (id, deadline, _) -> deadline, id)
                |> Seq.tryHead

            match due with
            | Some ((id, deadline, callback) as timer) ->
                timers.Remove timer |> ignore
                now <- max now deadline

                if take id then
                    callback ()

                this.RunMicrotasks ()
            | None -> running <- false

        now <- target

    /// <summary>Runs an idle period of at most <c>milliseconds</c>, also bounded by 50 ms and the next timeout: the idle
    /// callbacks requested before this call run in request order while time remains, each followed by a microtask
    /// checkpoint.</summary>
    member this.RunIdle(milliseconds: float) =
        let deadline =
            min
                (now
                 + min (max 0.0 milliseconds) IdleDeadline.MaxPeriod)
                (nextTimer ())

        let period = IdleDeadline.until (fun () -> now) deadline
        let pending = idles.ToArray ()
        let mutable index = 0

        while index < pending.Length
              && period.TimeRemaining () > 0.0 do
            let (id, timer, callback) as entry = pending[index]
            index <- index + 1
            idles.Remove entry |> ignore

            if timer <> 0 then
                cancelled.Add timer |> ignore
                idleTimers.Remove timer |> ignore

            if take id then
                callback period
                this.RunMicrotasks ()

    /// <summary>Runs the frame callbacks requested before this call with the current time, then a microtask
    /// checkpoint.</summary>
    member this.RunFrame() =
        let batch = frames.ToArray ()
        frames.Clear ()

        for id, callback in batch do
            if take id then
                callback now

        this.RunMicrotasks ()

    interface FrameScheduler with
        member _.Now() = now

        member _.Frame callback =
            let id = allocate ()
            frames.Add ((id, callback))
            handle id

        member _.Timeout(milliseconds, callback) =
            let id = allocate ()
            timers.Add ((id, now + max 0.0 milliseconds, callback))
            handle id

        member _.Microtask callback =
            let id = allocate ()
            microtasks.Enqueue ((id, callback))
            handle id

    interface IdleScheduler with
        member _.Idle(timeout, callback) =
            let id = allocate ()
            let timer = if timeout.IsSome then allocate () else 0

            match timeout with
            | Some milliseconds ->
                let expire () =
                    idleTimers.Remove timer |> ignore

                    match
                        idles
                        |> Seq.tryFind (fun (entry, _, _) -> entry = id)
                    with
                    | Some entry ->
                        idles.Remove entry |> ignore

                        if take id then
                            callback IdleDeadline.timedOut
                    | None -> ()

                timers.Add ((timer, now + max 0.0 milliseconds, expire))
                idleTimers.Add timer |> ignore
            | None -> ()

            idles.Add ((id, timer, callback))

            { new IDisposable with
                member _.Dispose() =
                    cancelled.Add id |> ignore

                    if timer <> 0 then
                        cancelled.Add timer |> ignore
            }

    interface IDisposable with
        member _.Dispose() =
            disposed <- true
            frames.Clear ()
            timers.Clear ()
            microtasks.Clear ()
            idles.Clear ()
            idleTimers.Clear ()
