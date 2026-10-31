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

/// <summary>A deterministic scheduler whose clock and frames advance only when its owner calls them.</summary>
[<Sealed>]
type ManualFrameScheduler() =
    let mutable now = 0.0
    let mutable disposed = false
    let mutable next = 0
    let frames = List<int * (float -> unit)>()
    let timers = List<int * float * (unit -> unit)>()
    let microtasks = Queue<int * (unit -> unit)>()
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

        member _.Dispose() =
            disposed <- true
            frames.Clear ()
            timers.Clear ()
            microtasks.Clear ()
