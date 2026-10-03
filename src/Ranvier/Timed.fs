namespace Ranvier

open System
open System.Collections.Generic

/// <summary>The clock and optional capture/publication equality comparer of a timed value.</summary>
type TimedOptions<'T> =
    {
        /// <summary>The monotonic clock used for admission deadlines.</summary>
        Clock: TimedClock
        /// <summary>The comparer, or None to use the graph's equality policy.</summary>
        Comparer: IEqualityComparer<'T> option
    }

/// <summary>Default clock and equality options for timed values.</summary>
[<RequireQualifiedAccess>]
module TimedOptions =
    /// <summary>Uses the system clock and the constructing graph's equality policy.</summary>
    let defaults<'T> : TimedOptions<'T> =
        {
            Clock = TimedClock.system
            Comparer = None
        }

/// <summary>The node's debounce or throttle admission rule.</summary>
type internal TimedMode =
    | Debounce = 0
    | First = 1
    | Last = 2
    | Both = 3

/// <summary>A tracked read-only value that captures source changes eagerly and publishes them under a timing rule.</summary>
/// <remarks>The first ready value is immediate. Pending capture holds the published state; failure publishes immediately.</remarks>
[<Sealed>]
type Timed<'T> internal (graph: Graph, options: TimedOptions<'T>, duration: TimeSpan, mode: TimedMode, read: unit -> 'T) as this =
    let id = graph.NextId ()
    let sources = SourceList ()
    let observers = ObserverSet ()
    do Tracer.Bind (sources, graph, id)
    do Tracer.Bind (observers, graph, id)
    let clock = options.Clock
    let equal = defaultArg options.Comparer (graph.Options.Equality.Comparer<'T>())
    let milliseconds = duration.TotalMilliseconds
    let mutable freshness = Freshness.Clean
    let mutable queued = false
    let mutable disposed = false
    let mutable violated = false
    let mutable link: OwnerLink = null
    let mutable status = Status.Uninitialized
    let mutable captureStatus = Status.Uninitialized
    let mutable failure: Failure = null
    let mutable value = Unchecked.defaultof<'T>
    let mutable captured = Unchecked.defaultof<'T>
    let mutable everReady = false
    let mutable candidate = Unchecked.defaultof<'T>
    let mutable hasCandidate = false
    let mutable deadline = Double.NegativeInfinity
    let mutable timer: TimedTimer = Unchecked.defaultof<_>
    let mutable armed = false
    let mutable wakePosted = 0
    let mutable runs = 0

    let wake () =
#if FABLE_COMPILER
        wakePosted <- 0
#else
        Threading.Interlocked.Exchange (&wakePosted, 0)
        |> ignore
#endif
        if not disposed then
            armed <- false
            this.Enqueue ()
            graph.RequestFlush ()

    let callback =
        Action (fun () ->
#if FABLE_COMPILER
            let previous = wakePosted
            wakePosted <- 1
#else
            let previous = Threading.Interlocked.Exchange (&wakePosted, 1)
#endif
            if previous = 0 then
                graph.Post wake)

    do
        if duration < TimeSpan.Zero then
            raise (ArgumentOutOfRangeException (nameof duration))

        if isNull (box clock) then
            nullArg "options.Clock"

        if isNull (box read) then
            nullArg (nameof read)
#if RANVIER_COUNTERS
        Counters.MemoCreated ()
#endif

    /// <summary>Attaches the node to its owner and captures its initial state.</summary>
    member internal this.Start() =
        link <- graph.CurrentOwner.AttachLinked this
        Tracer.TimedNew (graph, id, link.Owner, clock)
        graph.EnterPull ()

        try
            this.Capture ()
        finally
            graph.ExitPull ()

    member private _.Enqueue() =
        if not queued then
            queued <- true
            graph.Schedule (this :> IScheduled)

    member private _.ClearCandidate(cancelled: bool) =
        Tracer.TimedEventIf (graph, id, cancelled && hasCandidate, 53, 0, 0, null)

        hasCandidate <- false
        candidate <- Unchecked.defaultof<_>

        if armed then
            timer.Disarm ()

        armed <- false

    member private this.Cancel() =
        this.ClearCandidate true

    member private _.Arm() =
        if not armed && hasCandidate then
            if isNull (box timer) then
                timer <- clock.CreateTimer callback

            armed <- true
            timer.Arm (TimeSpan.FromMilliseconds (min 2147483647. (max 0. (deadline - clock.NowMilliseconds))))

    member private _.Notify() =
        Tracer.TimedMoved (graph, id, Failure.Payload (failure, box value))
        observers.NotifyDirtyExcept graph.CurrentComputation
        Tracer.Notified graph

    member private this.Fail(ex: exn) =
        this.Cancel ()
        captureStatus <- Status.Error
        let next = graph.FailureOf (ex, this, failure)

        let moved =
            status <> Status.Error
            || Failure.Moved (next, failure)

        failure <- next
        status <- Status.Error
        Tracer.TimedEvent (graph, id, 50, int (byte captureStatus), 0, box ex)
        Tracer.TimedEvent (graph, id, 54, int (byte status), (if moved then 1 else 0), box ex)

        if moved then
            this.Notify ()

    member private this.Admit(next: 'T) =
        try
            let moved =
                status <> Status.None
                || not (equal.Equals (value, next))

            value <- next
            status <- Status.None
            failure <- null
            everReady <- true
            Tracer.TimedEvent (graph, id, 54, int (byte status), (if moved then 1 else 0), box next)

            if not moved then
                Tracer.TimedEvent (graph, id, 52, 2, 0, null)

            if moved then
                this.Notify ()
        with ex ->
            this.Fail ex

    member private this.Capture() =
        freshness <- Freshness.Clean
        runs <- runs + 1
#if RANVIER_COUNTERS
        Counters.MemoRecomputed ()
#endif
        Tracer.RunStart (graph, id, runs)
        sources.BeginRun ()

        try
            try
                let next = graph.RunHosted (this :> IComputation, read)

                if violated then
                    raise (InvalidOperationException (ScopeMessages.forMode ScopeMode.Pure))

                if not disposed then
                    let changed =
                        captureStatus <> Status.None
                        || not (equal.Equals (captured, next))

                    captured <- next
                    captureStatus <- Status.None

                    if changed then
                        Tracer.TimedEvent (graph, id, 50, int (byte captureStatus), 0, box next)

                        if not everReady || milliseconds = 0. then
                            this.Admit next
                        else
                            let now = clock.NowMilliseconds

                            match mode with
                            | TimedMode.First ->
                                if now >= deadline then
                                    deadline <- now + milliseconds
                                    this.Admit next
                                else
                                    Tracer.TimedEvent (graph, id, 52, 3, 0, null)
                            | TimedMode.Both when now >= deadline ->
                                this.Cancel ()
                                deadline <- now + milliseconds
                                this.Admit next
                            | _ ->
                                Tracer.TimedEvent (graph, id, 51, int mode, (if hasCandidate then 1 else 0), box milliseconds)

                                if
                                    mode = TimedMode.Debounce
                                    || not hasCandidate && mode = TimedMode.Last
                                then
                                    deadline <- now + milliseconds

                                candidate <- next
                                hasCandidate <- true
                                this.Arm ()
                    else
                        Tracer.TimedEvent (graph, id, 52, 1, 0, null)
            finally
                if disposed then
                    sources.Clear (this :> IComputation)
                else
                    sources.EndRun (this :> IComputation)
        with
        | ex when violated ->
            violated <- false
            this.Fail (ScopeMessages.failure ScopeMode.Pure ex)
        | NotReadyException _ ->
            this.Cancel ()
            captureStatus <- Status.Pending
            let moved = not everReady && status <> Status.Pending

            if not everReady then
                status <- Status.Pending
                failure <- null

            Tracer.TimedEvent (graph, id, 50, int (byte captureStatus), 0, null)

            Tracer.TimedEventIf (graph, id, not everReady, 54, int (byte status), (if moved then 1 else 0), Failure.Payload (failure, box value))

            if moved then
                this.Notify ()
        | ex -> this.Fail ex

        Tracer.RunEnd (graph, id, captureStatus)
        Tracer.TimedState (graph, id, clock, this)

    interface INode with
        member _.Id = id
        member _.Status = status

    interface ISource with
        member _.AddObserver observer =
            observers.Add observer

        member _.RemoveObserver observer =
            observers.Remove observer

        member _.UpdateIfNecessary() = ()

    interface IComputation with
        member _.AddSource source =
            sources.Add (this :> IComputation, source)

        member _.MarkDirty() =
            if not disposed then
                freshness <- Freshness.Dirty
                this.Enqueue ()

        member _.MarkCheck() =
            if not disposed then
                if freshness = Freshness.Clean then
                    freshness <- Freshness.Check

                this.Enqueue ()

    interface IScheduled with
        member _.Execute() =
            if disposed then
                queued <- false
            else
                if freshness = Freshness.Check then
                    Tracer.CheckStart (graph, id)
                    let mutable i = 0

                    while freshness = Freshness.Check && i < sources.Count do
                        sources.SourceAt(i).UpdateIfNecessary()
                        i <- i + 1

                    Tracer.CheckResolved (graph, id, (freshness = Freshness.Dirty))

                    if freshness = Freshness.Check then
                        freshness <- Freshness.Clean

                queued <- false

                if freshness = Freshness.Dirty then
                    this.Capture ()

                if hasCandidate then
                    if clock.NowMilliseconds >= deadline then
                        let next = candidate
                        this.ClearCandidate false

                        if mode = TimedMode.Both then
                            deadline <- clock.NowMilliseconds + milliseconds

                        this.Admit next
                        Tracer.TimedState (graph, id, clock, this)
                    else
                        this.Arm ()

    interface IScopeHost with
        member _.Scope =
            violated <- true
            raise (InvalidOperationException (ScopeMessages.forMode ScopeMode.Pure))

    interface IOwned with
        member _.Release() =
            link <- null
            this.Dispose ()

    /// <summary>The graph that owns this value.</summary>
    member _.Graph = graph
    /// <summary>The published state, independent of a held candidate's state.</summary>
    member _.Status = status
    /// <summary>The number of source captures, including failed and pending captures.</summary>
    member _.Runs = runs
    /// <summary>The last published value, read without tracking or raising its pending/error state.</summary>
    member _.Peek = value
    /// <summary>The source responsible for the published failure, or null when no failure is published.</summary>
    member _.ErrorOrigin = Failure.OriginOf failure

    /// <summary>Tracks and reads the published value; raises when that state is pending or failed.</summary>
    member _.Value =
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then
            raise (graph.NotReady (this :> INode))

        if status.HasFlag Status.Error then
            graph.Raise failure

        value

    /// <summary>Tracks and returns the published ready, pending or failed state.</summary>
    member _.TryValue =
        graph.Track (this :> ISource)

        if status.HasFlag Status.Pending then Pending
        elif status.HasFlag Status.Error then Failed failure.Error
        else Ready value

    /// <summary>Detaches dependencies, cancels timing and releases ownership; repeated disposal is harmless.</summary>
    member _.Dispose() =
        graph.Entered (
            "Disposing a node",
            fun () ->
                if not disposed then
                    disposed <- true
                    this.Cancel ()

                    if not (isNull (box timer)) then
                        timer.Dispose ()

                    sources.Clear (this :> IComputation)

                    if not (isNull link) then
                        link.Detach ()
                        link <- null

                    Tracer.NodeDispose (graph, id)
                    Tracer.TimedState (graph, id, clock, this)
        )

    interface IDisposable with
        member _.Dispose() =
            this.Dispose ()

#if RANVIER_TRACE
    interface ITimedTrace with
        member _.TimingGraph = box graph

        member _.Timing =
            let log = ((box graph) :?> ITraced).TraceLog
            let struct (capture, _) = log.TimedCapture id

            let windowOpen =
                not disposed
                && (hasCandidate
                    || (mode = TimedMode.First || mode = TimedMode.Both)
                       && clock.NowMilliseconds < deadline)

            {
                Mode =
                    match mode with
                    | TimedMode.First -> "throttleFirst"
                    | TimedMode.Last -> "throttleLast"
                    | TimedMode.Both -> "throttle"
                    | _ -> "debounce"
                CapturedStatus = captureStatus
                PublishedStatus = status
                WindowOpen = windowOpen
                RemainingMilliseconds =
                    if windowOpen then
                        max 0. (deadline - clock.NowMilliseconds)
                    else
                        0.
                WinningCapture = if hasCandidate then capture else log.TimedWinner id
            }
#endif
